using System.Security.Cryptography;
using System.Text;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Configuration;
using Alveara.Api.Architecture.Permissions;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Alveara.Api.Architecture.Identity;

/// <summary>The outcome of "Add staff member" or "Create a login for this legacy staff record". <see cref="TemporaryPassword"/> is set only on the first
/// call; a replay of the same Idempotency-Key reports <see cref="Replayed"/> and never shows the password again.</summary>
public sealed record StaffMemberResult(Guid StaffProfileId, Guid UserAccountId, string Username, string Role, string? TemporaryPassword, bool Replayed, bool PracticeOwner = false);

/// <summary>
/// ALV-N030 (brief sections 4 to 6, R01 CR-1): new staff get a staff profile and a login together, in one transaction.
/// <list type="bullet">
/// <item><see cref="CreateAsync"/>: profile, account (<see cref="AccountKind.Staff"/>, enabled, <see cref="UserAccount.MustChangePassword"/> set) and the link.
/// A server-generated 16-character temporary password is returned once, stored only as its PBKDF2 hash, and never logged or audited.</item>
/// <item><see cref="CreateLegacyLoginAsync"/>: the same for an existing active profile created before ACCT that has no login. It never links an existing account.</item>
/// <item>Role rule (ALV-N018 full-subset rule): an Administrator may create any working role; anyone else only a non-Admin role whose effective permissions are
/// a subset of their own.</item>
/// <item>Idempotency: the new ids are derived from the actor and the Idempotency-Key, so a replay finds the rows already made and answers "already created"
/// without the password; a concurrent duplicate meets the primary key and is answered the same way.</item>
/// </list>
/// Failure behaviour: any failure rolls the whole transaction back (no profile without its login); validation failures are <see cref="ConfigurationException"/>
/// with a stable code.
/// </summary>
public class StaffMemberService(AlveraDbContext db, IPracticeClock clock, IEffectivePermissions permissions)
{
    public const int TemporaryPasswordLength = 16;
    // No 0/O, 1/l/I: easy to read aloud and to type from a printout.
    private const string UnambiguousAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";

    /// <summary>Test hook: runs after the profile is written and before the account is, inside the transaction (S9 fault injection).</summary>
    internal Func<Task>? TestHook_AfterProfileInsert { get; set; }

    /// <summary>Test hook: runs after the account and the link are written and before the owner designation is, inside the transaction (S24 fault injection).</summary>
    internal Func<Task>? TestHook_BeforeDesignation { get; set; }

    public static string GenerateTemporaryPassword() =>
        new(Enumerable.Range(0, TemporaryPasswordLength).Select(_ => UnambiguousAlphabet[RandomNumberGenerator.GetInt32(UnambiguousAlphabet.Length)]).ToArray());

    internal static Guid DerivedId(string purpose, Guid actor, string key, Guid? scope = null)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"alveara.staff-member.{purpose}:{actor:N}:{scope?.ToString("N")}:{key}"));
        return new Guid(bytes.AsSpan(0, 16));
    }

    /// <summary>
    /// "Add staff member". With <paramref name="practiceOwner"/> (ALV-N030-C01, brief 18.4, the U-046 single call) the same transaction also designates the new
    /// person the Practice Owner - both or neither: the role must be Administrator (E2, 400 before anything is written), the actor an enabled Administrator
    /// (E1a, checked under its lock), and no designation may ever have existed (409 <c>owner_already_designated</c>, nothing created). Locks follow 18.3a.
    /// </summary>
    public async Task<StaffMemberResult> CreateAsync(string? displayName, string? jobTitle, string? username, string? role, string? idempotencyKey, Guid actor, CancellationToken ct,
        bool practiceOwner = false)
    {
        var key = RequireKey(idempotencyKey);
        var staffId = DerivedId("profile", actor, key);
        if (await ReplayAsync(staffId, ct) is { } replay) return replay;

        var name = ConfigurationWrite.RequireName(displayName, "Display name");
        var title = ConfigurationWrite.OptionalText(jobTitle, "Job title", 80);
        var login = RequireUsername(username);
        var targetRole = await RequireAllowedRoleAsync(role, actor, ct);
        if (practiceOwner && targetRole != Role.Admin)
            throw new ConfigurationException(PracticeOwnerGuard.TargetMustBeAdministrator,
                "The Practice Owner must sign in with the Administrator role. Choose Administrator as the role.");
        if (practiceOwner && await db.PracticeOwnerDesignations.AnyAsync(ct)) throw PracticeOwnerService.AlreadyDesignated();   // early; re-checked under the locks
        if (await db.StaffProfiles.AnyAsync(s => s.DisplayName == name, ct))
            throw new ConfigurationException("staff_name_taken", "A staff profile with that display name already exists.", 409);
        if (await db.UserAccounts.AnyAsync(u => u.Username == login, ct))
            throw new ConfigurationException("username_taken", "That username is already in use.", 409);

        var password = GenerateTemporaryPassword();
        var account = NewAccount(DerivedId("account", actor, key), login, targetRole, password);
        var staff = new StaffProfile { Id = staffId, DisplayName = name, JobTitle = title, IsActive = true, CreatedAtUtc = clock.UtcNow };

        await using var tx = practiceOwner
            ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct)
            : await db.Database.BeginTransactionAsync(ct);
        try
        {
            LockedAccount? designator = null;
            if (practiceOwner)
            {
                // 18.3a: the guard, then the designation range ("never designated"), then the actor's account row
                await AdminAvailabilityGuard.AcquireAsync(db, ct);
                if (await db.PracticeOwnerDesignations.AnyAsync(ct)) throw PracticeOwnerService.AlreadyDesignated();
                (await PracticeOwnerGuard.LockAccountsAsync(db, [actor], ct)).TryGetValue(actor, out designator);
                if (!PracticeOwnerGuard.IsEligibleDesignator(designator))
                {
                    await tx.RollbackAsync(ct);
                    db.ChangeTracker.Clear();
                    await PracticeOwnerService.RefuseActorAsync(db, actor, designator, ct);
                }
            }
            db.StaffProfiles.Add(staff);
            ConfigurationWrite.Audit(db, ConfigurationAuditEvents.Created, nameof(StaffProfile), staff.Id, actor, "Staff profile created with a login account.");
            await db.SaveChangesAsync(ct);
            if (TestHook_AfterProfileInsert is { } hook) await hook();
            db.UserAccounts.Add(account);
            await db.SaveChangesAsync(ct);
            staff.UserAccountId = account.Id;
            AuditCreated(account, staff.Id, actor, "A staff member was added with a login account.");
            await db.SaveChangesAsync(ct);
            if (practiceOwner)
            {
                if (TestHook_BeforeDesignation is { } beforeDesignation) await beforeDesignation();
                PracticeOwnerService.StageDesignation(db, staff.Id, staff.DisplayName, designator!, clock.UtcNow);
                await db.SaveChangesAsync(ct);
            }
            await tx.CommitAsync(ct);
        }
        catch (AdminGuardBusyException)
        {
            await tx.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            throw new ConfigurationException("admin_guard_busy", "Another administrator change is in progress. Try again in a moment.", 409);
        }
        catch (DbUpdateException ex) when (IsDuplicate(ex))
        {
            await tx.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            if (await ReplayAsync(staffId, ct) is { } concurrent) return concurrent;   // the same key won a race
            throw new ConfigurationException("staff_member_conflict", "That display name or username was just taken. Check the staff list and try again.", 409);
        }
        return new StaffMemberResult(staff.Id, account.Id, account.Username, account.Role.ToString(), password, Replayed: false, PracticeOwner: practiceOwner);
    }

    public async Task<StaffMemberResult> CreateLegacyLoginAsync(Guid staffId, string? username, string? role, string? idempotencyKey, Guid actor, CancellationToken ct)
    {
        var key = RequireKey(idempotencyKey);
        var accountId = DerivedId("legacy-account", actor, key, staffId);
        var staff = await db.StaffProfiles.SingleOrDefaultAsync(s => s.Id == staffId, ct) ?? throw PracticeConfigurationService.NotFound("Staff profile");
        if (staff.UserAccountId == accountId && await ReplayAsync(staffId, ct) is { } replay) return replay;
        if (staff.UserAccountId is not null)
            throw new ConfigurationException("staff_has_login", "This staff member already has a login.", 409);
        if (!staff.IsActive)
            throw new ConfigurationException("staff_inactive", "Reactivate this staff member before creating a login.", 409);

        var login = RequireUsername(username);
        var targetRole = await RequireAllowedRoleAsync(role, actor, ct);
        if (await db.UserAccounts.AnyAsync(u => u.Username == login, ct))
            throw new ConfigurationException("username_taken", "That username is already in use.", 409);

        var password = GenerateTemporaryPassword();
        var account = NewAccount(accountId, login, targetRole, password);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            db.UserAccounts.Add(account);
            await db.SaveChangesAsync(ct);
            // Only a profile that still has no login is linked (a concurrent legacy-login on the same profile loses here).
            var linked = await db.StaffProfiles.Where(s => s.Id == staffId && s.UserAccountId == null && s.IsActive)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.UserAccountId, account.Id), ct);
            if (linked != 1) throw new ConfigurationException("staff_has_login", "This staff member already has a login.", 409);
            ConfigurationWrite.Audit(db, ConfigurationAuditEvents.StaffAccountLinkChanged, nameof(StaffProfile), staffId, actor, "A login was created for a legacy staff record.");
            AuditCreated(account, staffId, actor, "A login was created for a legacy staff record.");
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (IsDuplicate(ex))
        {
            await tx.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            if (await ReplayAsync(staffId, ct) is { UserAccountId: var id } concurrent && id == accountId) return concurrent;
            throw new ConfigurationException("username_taken", "That username is already in use.", 409);
        }
        return new StaffMemberResult(staffId, account.Id, account.Username, account.Role.ToString(), password, Replayed: false);
    }

    private async Task<StaffMemberResult?> ReplayAsync(Guid staffId, CancellationToken ct)
    {
        var row = await db.StaffProfiles.AsNoTracking().Where(s => s.Id == staffId && s.UserAccountId != null)
            .Select(s => new { s.Id, AccountId = s.UserAccountId!.Value, s.UserAccount!.Username, s.UserAccount.Role }).SingleOrDefaultAsync(ct);
        if (row is null) return null;
        var owner = await db.PracticeOwnerDesignations.AsNoTracking().AnyAsync(d => d.StaffProfileId == row.Id && d.EndedAtUtc == null, ct);
        return new StaffMemberResult(row.Id, row.AccountId, row.Username, row.Role.ToString(), null, Replayed: true, PracticeOwner: owner);
    }

    private UserAccount NewAccount(Guid id, string username, Role role, string temporaryPassword) => new()
    {
        Id = id, Username = username, PasswordHash = Pbkdf2PasswordHasher.Hash(temporaryPassword), Role = role, IsDisabled = false,
        AccountKind = AccountKind.Staff, MustChangePassword = true, CreatedAtUtc = clock.UtcNow,
    };

    private void AuditCreated(UserAccount account, Guid staffId, Guid actor, string what)
    {
        AuditService.Record(db, AuditEventTypes.StaffMemberCreated, "UserAccount", account.Id, actor, $"{what} Staff profile {staffId}; role {account.Role}.");
        AuditService.Record(db, AuditEventTypes.TemporaryPasswordIssued, "UserAccount", account.Id, actor, "A temporary password was issued; it must be changed at first sign-in.");
    }

    private static string RequireKey(string? key)
    {
        var k = key?.Trim() ?? "";
        if (k.Length is 0 or > 200) throw new ConfigurationException("idempotency_key_required", "An Idempotency-Key header (1 to 200 characters) is required.");
        return k;
    }

    private static string RequireUsername(string? username)
    {
        var name = AccountService.NormalizeUsername(username);
        if (name.Length == 0) throw new ConfigurationException("username_required", "A username is required.");
        if (name.Length > 256) throw new ConfigurationException("validation_failed", "A username is at most 256 characters.");
        return name;
    }

    /// <summary>The role must be a working role; a non-Administrator may only give a non-Admin role whose permissions are all their own (403 otherwise).</summary>
    private async Task<Role> RequireAllowedRoleAsync(string? role, Guid actor, CancellationToken ct)
    {
        if (!Enum.TryParse<Role>(role?.Trim(), ignoreCase: true, out var target) || target == Role.Unassigned || !Enum.IsDefined(target))
            throw new ConfigurationException("role_invalid", "Choose a role for the new login.");
        var actorRole = await db.UserAccounts.AsNoTracking().Where(u => u.Id == actor).Select(u => (Role?)u.Role).SingleOrDefaultAsync(ct);
        if (actorRole == Role.Admin) return target;
        if (actorRole is null || target == Role.Admin)
            throw new ConfigurationException(AccountTargetRules.TargetIsAdmin, "Only an Administrator can create an Administrator login.", 403);
        var actorSet = await permissions.PermissionsForAsync(actorRole.Value, ct);
        var targetSet = await permissions.PermissionsForAsync(target, ct);
        if (!targetSet.IsSubsetOf(actorSet))
            throw new ConfigurationException(AccountTargetRules.TargetExceedsActor, "You can only create a login whose permissions you hold yourself.", 403);
        return target;
    }

    private static bool IsDuplicate(DbUpdateException ex) => ex.InnerException is SqlException { Number: 2627 or 2601 };
}
