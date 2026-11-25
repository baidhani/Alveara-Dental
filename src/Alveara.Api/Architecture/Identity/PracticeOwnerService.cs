using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Configuration;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Alveara.Api.Architecture.Identity;

/// <summary>The active designation. <see cref="DesignationId"/> (ALV-N030-C01, R09-2) is what a transfer sends back as its expected designation.</summary>
public sealed record PracticeOwnerView(Guid StaffProfileId, string DisplayName, DateTimeOffset DesignatedAtUtc, string Method, Guid DesignationId);

/// <summary>
/// ALV-N030 (brief sections 4 and 5, D-3, D-7) and ALV-N030-C01 (brief 18.3, 18.3a): the Practice Owner is a human staff member - an active staff profile with a linked,
/// enabled <see cref="AccountKind.Staff"/> account whose role is <see cref="Role.Admin"/> (owner invariant, OI). The designation carries no permission of its own.
/// <see cref="DesignateInitialAsync"/> succeeds only while no designation row has EVER existed (a second call gets 409 <c>owner_already_designated</c>); the filtered
/// unique index and the protection trigger (51311) are the database backstops. Method: <c>InitialByDeploymentAdministrator</c> when the actor is the Deployment
/// Administrator, otherwise <c>UpgradeTransition</c>.
/// <para>E1a (R09-1): the ACTOR must itself be an enabled Administrator (staff or Deployment Administrator). A check before the transaction refuses early; the
/// authoritative check runs inside the serializable transaction on the actor's row locked in the 18.3a order, so a concurrent downgrade or disable of the actor either
/// commits first (and the designation is refused) or waits for the designation to commit.</para>
/// </summary>
public class PracticeOwnerService(AlveraDbContext db, IPracticeClock clock)
{
    /// <summary>Test-only: runs between the early actor check and the transaction (the gap the in-transaction check closes, S11b).</summary>
    internal Func<Task>? TestHook_AfterPreliminaryActorCheck { get; set; }

    public async Task<PracticeOwnerView?> CurrentAsync(CancellationToken ct) =>
        await db.PracticeOwnerDesignations.AsNoTracking().Where(d => d.EndedAtUtc == null)
            .Select(d => new PracticeOwnerView(d.StaffProfileId, d.StaffProfile!.DisplayName, d.DesignatedAtUtc, d.Method.ToString(), d.Id))
            .SingleOrDefaultAsync(ct);

    public async Task<PracticeOwnerView> DesignateInitialAsync(Guid staffProfileId, bool confirm, Guid actor, CancellationToken ct)
    {
        if (!confirm) throw new ConfigurationException("confirmation_required", "Confirm that this person is the Practice Owner.");

        // Early refusal only - not the control (the in-transaction check below is).
        var early = await db.UserAccounts.AsNoTracking().Where(u => u.Id == actor)
            .Select(u => new LockedAccount(u.Id, (int)u.Role, u.IsDisabled, (byte)u.AccountKind)).SingleOrDefaultAsync(ct);
        if (!PracticeOwnerGuard.IsEligibleDesignator(early)) await RefuseActorAsync(db, actor, early, ct);
        if (await db.PracticeOwnerDesignations.AnyAsync(ct)) throw AlreadyDesignated();
        if (TestHook_AfterPreliminaryActorCheck is { } hook) await hook();

        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        PracticeOwnerDesignation row;
        string displayName;
        try
        {
            // 18.3a lock order: guard, designation range, accounts by id, staff profile.
            await AdminAvailabilityGuard.AcquireAsync(db, ct);
            if (await db.PracticeOwnerDesignations.AnyAsync(ct)) throw AlreadyDesignated();   // serializable: the "never designated" range stays locked
            var targetAccountId = await db.StaffProfiles.AsNoTracking().Where(s => s.Id == staffProfileId).Select(s => s.UserAccountId).SingleOrDefaultAsync(ct);
            var accounts = await PracticeOwnerGuard.LockAccountsAsync(db, targetAccountId is { } t ? [actor, t] : [actor], ct);

            accounts.TryGetValue(actor, out var lockedActor);
            if (!PracticeOwnerGuard.IsEligibleDesignator(lockedActor))
            {
                await tx.RollbackAsync(ct);
                db.ChangeTracker.Clear();
                await RefuseActorAsync(db, actor, lockedActor, ct);
            }

            var target = await db.StaffProfiles.FromSqlInterpolated($"SELECT * FROM [StaffProfiles] WITH (UPDLOCK, ROWLOCK) WHERE [Id] = {staffProfileId}")
                .AsNoTracking().SingleOrDefaultAsync(ct) ?? throw PracticeConfigurationService.NotFound("Staff profile");
            var account = target.UserAccountId is { } a && accounts.TryGetValue(a, out var found) ? found : null;
            if (!target.IsActive || account is null || account.IsDisabled || account.AccountKind != (byte)AccountKind.Staff)
                throw new ConfigurationException("owner_target_invalid", "The Practice Owner must be an active staff member with an enabled login.", 409);
            if (account.Role != (int)Role.Admin)
                throw new ConfigurationException(PracticeOwnerGuard.TargetMustBeAdministrator,
                    "The Practice Owner must sign in with the Administrator role. Change this person's role to Administrator in Security Administration first.", 409);

            row = StageDesignation(db, target.Id, lockedActor!, clock.UtcNow);
            displayName = target.DisplayName;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (AdminGuardBusyException)
        {
            await tx.RollbackAsync(CancellationToken.None);
            throw new ConfigurationException("admin_guard_busy", "Another administrator change is in progress. Try again in a moment.", 409);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2627 or 2601 or 1205 })
        {
            await tx.RollbackAsync(CancellationToken.None);
            throw AlreadyDesignated();
        }
        return new PracticeOwnerView(row.StaffProfileId, displayName, row.DesignatedAtUtc, row.Method.ToString(), row.Id);
    }

    /// <summary>
    /// The one place a first designation row and its audit entry are staged (shared by <see cref="DesignateInitialAsync"/> and the single owner-creation call,
    /// <see cref="StaffMemberService.CreateAsync"/>; brief 18.4). The caller holds the 18.3a locks inside a serializable transaction, has re-checked "never
    /// designated" and the actor (<see cref="PracticeOwnerGuard.IsEligibleDesignator"/>), and saves.
    /// </summary>
    internal static PracticeOwnerDesignation StageDesignation(AlveraDbContext db, Guid staffProfileId, LockedAccount actor, DateTimeOffset now)
    {
        var row = new PracticeOwnerDesignation
        {
            Id = Guid.NewGuid(), StaffProfileId = staffProfileId, DesignatedAtUtc = now, DesignatedByAccountId = actor.Id,
            Method = actor.AccountKind == (byte)AccountKind.DeploymentAdministrator
                ? PracticeOwnerDesignationMethod.InitialByDeploymentAdministrator : PracticeOwnerDesignationMethod.UpgradeTransition,
        };
        db.PracticeOwnerDesignations.Add(row);
        // ALV-N030-C01 (Q7): ids only in the stored text; the audit page resolves the name when an authorized user reads it
        AuditService.Record(db, AuditEventTypes.PracticeOwnerDesignated, nameof(StaffProfile), staffProfileId, actor.Id,
            $"staffProfileId={staffProfileId}; designationId={row.Id}; method={row.Method}");
        return row;
    }

    /// <summary>E1a refusal: audited on its own (ids and the rule only) and thrown as 403. Always throws.</summary>
    internal static async Task RefuseActorAsync(AlveraDbContext db, Guid actor, LockedAccount? actorRow, CancellationToken ct)
    {
        await AccountTargetRules.RecordRefusalAsync(db, actor, actorRow is null ? Role.Unassigned : (Role)actorRow.Role, actor, PracticeOwnerGuard.ActorMustBeAdministrator, ct);
        throw new ConfigurationException(PracticeOwnerGuard.ActorMustBeAdministrator, "Only an Administrator can designate the Practice Owner.", 403);
    }

    internal static ConfigurationException AlreadyDesignated() =>
        new("owner_already_designated", "A Practice Owner has already been designated.", 409);
}
