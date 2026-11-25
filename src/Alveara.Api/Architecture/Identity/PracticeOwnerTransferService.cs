using System.Security.Cryptography;
using System.Text;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Configuration;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Alveara.Api.Architecture.Identity;

public sealed record PracticeOwnerTransferRequest(Guid ExpectedDesignationId, Guid NewOwnerStaffProfileId, string? Reason, bool Confirm, string? CurrentPassword,
    string? MfaCode, bool DisableFormerOwner);

public sealed record PracticeOwnerTransferResult(Guid DesignationId, Guid StaffProfileId, DateTimeOffset DesignatedAtUtc, bool FormerOwnerDisabled, bool Replayed);

/// <summary>
/// ALV-N030-C01 (brief 18.5, approved R10): moves the Practice Owner designation to another person. The only route that ends a designation.
/// <list type="bullet">
/// <item>Actor: the enabled Deployment Administrator only (an account-kind check, not a permission), re-checked under its row lock. Step-up: the current password, plus a
/// current authenticator code when the actor has MFA (Q5); both before the transaction opens, so a failed step-up locks nothing.</item>
/// <item>Stale intent (R09-2): the request names the active designation the operator saw; any other active designation → 409 <c>owner_changed</c>, nothing changes.</item>
/// <item>One serializable transaction, 18.3a lock order: guard, active designation, accounts by id, staff profiles by id. The old row gets <c>EndedAtUtc</c> and the
/// new row is inserted in the same transaction, so readers see exactly one owner. The former owner's sessions end; with <c>DisableFormerOwner</c> their login is also
/// disabled (role unchanged, Q8).</item>
/// <item>Audit: one <c>PracticeOwnerTransferred</c> row with ids only; the reason is in the row's reason field.</item>
/// <item>Idempotency: the key is bound to (actor, key). The new designation's id and the audit row's id are derived from them; the audit row stores the request
/// fingerprint (expected designation, new owner, reason, disable flag - never the password or code). The same key and fingerprint replays the original result;
/// the same key with a different fingerprint → 409 <c>idempotency_key_reused</c>.</item>
/// </list>
/// Failure behaviour: every refusal changes nothing; any failure inside the transaction rolls back everything, including the disable and the session revocation.
/// </summary>
public class PracticeOwnerTransferService(AlveraDbContext db, AccountService accounts, IPracticeClock clock)
{
    public const int ReasonMin = 10, ReasonMax = 500;

    /// <summary>Test-only: runs after the new designation is saved and before the former owner's login is touched, inside the transaction (S34 fault injection).</summary>
    internal Func<Task>? TestHook_AfterDesignationInsert { get; set; }

    public async Task<PracticeOwnerTransferResult> TransferAsync(PracticeOwnerTransferRequest request, string? idempotencyKey, Guid actor, CancellationToken ct)
    {
        var key = idempotencyKey?.Trim() ?? "";
        if (key.Length is 0 or > 200) throw Fail("idempotency_key_required", "An Idempotency-Key header (1 to 200 characters) is required.", 400);

        var actorRow = await db.UserAccounts.AsNoTracking().Where(u => u.Id == actor)
            .Select(u => new LockedAccount(u.Id, (int)u.Role, u.IsDisabled, (byte)u.AccountKind)).SingleOrDefaultAsync(ct);
        if (!IsDeploymentAdministrator(actorRow)) await RefuseActorAsync(actor, actorRow, ct);

        var reason = request.Reason?.Trim() ?? "";
        var fingerprint = Fingerprint(request.ExpectedDesignationId, request.NewOwnerStaffProfileId, reason, request.DisableFormerOwner);
        var auditId = DerivedId("audit", actor, key);
        if (await ReplayAsync(auditId, fingerprint, ct) is { } replay) return replay;

        if (!request.Confirm) throw Fail("confirmation_required", "Confirm that you are transferring the Practice Owner designation to this person.", 400);
        if (reason.Length is < ReasonMin or > ReasonMax) throw Fail("validation_failed", $"Give a reason of {ReasonMin} to {ReasonMax} characters.", 400);

        await StepUpAsync(actor, request.CurrentPassword, request.MfaCode, ct);

        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        try
        {
            // 18.3a: guard, then the active designation, then accounts by id, then staff profiles by id
            await AdminAvailabilityGuard.AcquireAsync(db, ct);
            var current = await db.PracticeOwnerDesignations.FromSqlRaw(
                    "SELECT * FROM [PracticeOwnerDesignations] WITH (UPDLOCK, HOLDLOCK) WHERE [EndedAtUtc] IS NULL")
                .AsNoTracking().SingleOrDefaultAsync(ct)
                ?? throw Fail("no_active_owner", "There is no Practice Owner to transfer from. Designate the first owner instead.", 409);
            if (current.Id != request.ExpectedDesignationId)
                throw Fail("owner_changed", "The Practice Owner changed since this page was loaded. Reload it and confirm again.", 409);
            if (current.StaffProfileId == request.NewOwnerStaffProfileId)
                throw Fail("transfer_same_owner", "This person is already the Practice Owner.", 400);

            var formerAccountId = await db.StaffProfiles.AsNoTracking().Where(s => s.Id == current.StaffProfileId).Select(s => s.UserAccountId).SingleOrDefaultAsync(ct);
            var destinationAccountId = await db.StaffProfiles.AsNoTracking().Where(s => s.Id == request.NewOwnerStaffProfileId).Select(s => s.UserAccountId).SingleOrDefaultAsync(ct);
            var ids = new List<Guid> { actor };
            if (formerAccountId is { } f) ids.Add(f);
            if (destinationAccountId is { } d) ids.Add(d);
            var locked = await PracticeOwnerGuard.LockAccountsAsync(db, ids, ct);

            locked.TryGetValue(actor, out var lockedActor);
            if (!IsDeploymentAdministrator(lockedActor))
            {
                await tx.RollbackAsync(ct);
                db.ChangeTracker.Clear();
                await RefuseActorAsync(actor, lockedActor, ct);
            }

            var destination = await LockStaffAsync(current.StaffProfileId, request.NewOwnerStaffProfileId, ct);
            var destinationAccount = destination?.UserAccountId is { } da && locked.TryGetValue(da, out var acc) ? acc : null;
            if (destination is null || !destination.IsActive || destinationAccount is null || destinationAccount.IsDisabled
                || destinationAccount.AccountKind != (byte)AccountKind.Staff || destinationAccount.Role != (int)Role.Admin)
                throw Fail(PracticeOwnerGuard.TargetMustBeAdministrator,
                    "The new Practice Owner must be an active staff member whose login is an enabled Administrator.", 409);

            var now = clock.UtcNow;
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [PracticeOwnerDesignations] SET [EndedAtUtc] = {now} WHERE [Id] = {current.Id} AND [EndedAtUtc] IS NULL", ct);
            var designation = new PracticeOwnerDesignation
            {
                Id = DerivedId("designation", actor, key), StaffProfileId = destination.Id, DesignatedAtUtc = now, DesignatedByAccountId = actor,
                Method = PracticeOwnerDesignationMethod.Transfer,
            };
            db.PracticeOwnerDesignations.Add(designation);
            await db.SaveChangesAsync(ct);   // the old row ended and the new row inserted: per statement the filtered unique index sees one active owner
            if (TestHook_AfterDesignationInsert is { } hook) await hook();

            var formerDisabled = false;
            if (formerAccountId is { } former)
            {
                if (request.DisableFormerOwner)
                    formerDisabled = await accounts.DisableInTransactionAsync(former, actor, "because the Practice Owner designation was transferred", ct);
                if (!formerDisabled)   // the disable already rotated the stamp and closed the sessions
                {
                    await db.UserAccounts.Where(u => u.Id == former).ExecuteUpdateAsync(s => s.SetProperty(u => u.SecurityStamp, Guid.NewGuid()), ct);
                    await AuditSessionStore.RevokeOpenAsync(db, former, actor, DateTimeOffset.UtcNow, ct);
                }
            }

            db.AuditLogEntries.Add(new AuditLogEntry
            {
                Id = auditId, EventType = AuditEventTypes.PracticeOwnerTransferred, EntityType = nameof(StaffProfile), TargetUserAccountId = destination.Id,
                PerformedByUserAccountId = actor, Reason = reason, TimestampUtc = now,
                Details = $"formerOwnerStaffProfileId={current.StaffProfileId}; newOwnerStaffProfileId={destination.Id}; expectedDesignationId={current.Id}; "
                    + $"newDesignationId={designation.Id}; formerOwnerDisabled={(formerDisabled ? "true" : "false")}; requestHash={fingerprint}",
            });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return new PracticeOwnerTransferResult(designation.Id, destination.Id, now, formerDisabled, Replayed: false);
        }
        catch (AdminGuardBusyException)
        {
            await tx.RollbackAsync(CancellationToken.None);
            throw Fail("admin_guard_busy", "Another administrator change is in progress. Try again in a moment.", 409);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2627 or 2601 })
        {
            await tx.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            if (await ReplayAsync(auditId, fingerprint, ct) is { } concurrent) return concurrent;   // the same key won a race
            throw Fail("owner_changed", "The Practice Owner changed since this page was loaded. Reload it and confirm again.", 409);
        }
    }

    private async Task StepUpAsync(Guid actor, string? password, string? mfaCode, CancellationToken ct)
    {
        try
        {
            await accounts.ReauthenticateAsync(actor, password, ct);
            await accounts.ReauthenticateMfaAsync(actor, mfaCode, ct);
        }
        catch (MfaStepUpRequiredException) { throw Fail("mfa_required", "Enter the current code from your authenticator app.", 403); }
        catch (AccountLockedOutException) { throw Fail("account_locked", "Too many failed attempts. The account is locked for now; try again later.", 403); }
        catch (Exception ex) when (ex is InvalidCurrentPasswordException or InvalidMfaCodeException)
        {
            throw Fail("reauthentication_failed", "The password or authenticator code is not correct.", 403);
        }
    }

    /// <summary>Locks the former and the new owner's staff rows in ascending id order (18.3a) and returns the new owner's row (null if it does not exist).</summary>
    private async Task<StaffProfile?> LockStaffAsync(Guid formerStaffId, Guid newStaffId, CancellationToken ct)
    {
        StaffProfile? destination = null;
        foreach (var id in new[] { formerStaffId, newStaffId }.Distinct().OrderBy(i => new System.Data.SqlTypes.SqlGuid(i)))
        {
            var row = await db.StaffProfiles.FromSqlInterpolated($"SELECT * FROM [StaffProfiles] WITH (UPDLOCK, ROWLOCK) WHERE [Id] = {id}").AsNoTracking().SingleOrDefaultAsync(ct);
            if (id == newStaffId) destination = row;
        }
        return destination;
    }

    private async Task<PracticeOwnerTransferResult?> ReplayAsync(Guid auditId, string fingerprint, CancellationToken ct)
    {
        var row = await db.AuditLogEntries.AsNoTracking().Where(a => a.Id == auditId).Select(a => new { a.Details, a.TimestampUtc, a.TargetUserAccountId }).SingleOrDefaultAsync(ct);
        if (row is null) return null;
        if (Field(row.Details, "requestHash") != fingerprint)
            throw Fail("idempotency_key_reused", "This Idempotency-Key was already used for a different transfer.", 409);
        return new PracticeOwnerTransferResult(Guid.Parse(Field(row.Details, "newDesignationId")!), row.TargetUserAccountId, row.TimestampUtc,
            Field(row.Details, "formerOwnerDisabled") == "true", Replayed: true);
    }

    /// <summary>Reads one <c>key=value</c> field of the fixed-format details.</summary>
    internal static string? Field(string details, string name) =>
        details.Split(';', StringSplitOptions.TrimEntries).Select(p => p.Split('=', 2)).FirstOrDefault(p => p.Length == 2 && p[0] == name)?[1];

    private static bool IsDeploymentAdministrator(LockedAccount? a) =>
        a is not null && a.AccountKind == (byte)AccountKind.DeploymentAdministrator && !a.IsDisabled;

    private async Task RefuseActorAsync(Guid actor, LockedAccount? actorRow, CancellationToken ct)
    {
        await AccountTargetRules.RecordRefusalAsync(db, actor, actorRow is null ? Role.Unassigned : (Role)actorRow.Role, actor, "transfer_not_permitted", ct);
        throw Fail("transfer_not_permitted", "Only the Deployment Administrator can transfer the Practice Owner designation.", 403);
    }

    private static string Fingerprint(Guid expected, Guid newOwner, string reason, bool disable) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"v1|{expected:N}|{newOwner:N}|{disable}|{reason}"))).ToLowerInvariant();

    private static Guid DerivedId(string purpose, Guid actor, string key) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes($"alveara.owner-transfer.{purpose}:{actor:N}:{key}")).AsSpan(0, 16));

    private static ConfigurationException Fail(string code, string message, int status) => new(code, message, status);
}
