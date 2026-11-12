using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Identity;

/// <summary>
/// ALV-N020-C01: permissions held by a NAMED ACCOUNT, never by a role. They are deliberately a separate type from <see cref="Permission"/>: the Admin role holds
/// every <see cref="Permission"/> value, role overrides act on <see cref="Permission"/> values, and the role matrix lists them; none of that can ever reach an
/// account permission, by construction. Granted and revoked only through <see cref="AccountPermissionService"/> (audited).
/// </summary>
public enum AccountPermission
{
    /// <summary>Changes the sign-in page presentation: background catalog, selection, display mode and footer notice.</summary>
    ManageLoginPresentation,
}

/// <summary>One account holding one account permission. A row exists only while the grant is held; the history is in the audit log.</summary>
public class AccountPermissionGrant
{
    public Guid Id { get; set; }
    public Guid UserAccountId { get; set; }
    public required string Permission { get; set; }
    /// <summary>Null when the grant came from the one-time administrator bootstrap of a new installation.</summary>
    public Guid? GrantedByUserAccountId { get; set; }
    public DateTimeOffset GrantedAtUtc { get; set; }
}

public static class AccountPermissionAuditEvents
{
    public const string Granted = "AccountPermissionGranted";
    public const string Revoked = "AccountPermissionRevoked";
}

public sealed class AccountPermissionException(string code, string message, int statusCode) : Exception(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
}

/// <summary>
/// Reads, grants and revokes account permissions. A grant needs an enabled account with a real role (never Unassigned); granting a held permission or revoking
/// one that is not held changes nothing and writes no audit entry (idempotent). Every change is audited with actor, target and permission.
/// Who may call grant/revoke is decided by the controller (a ManageUsers holder with a CSRF token; review BRIEFING-R03 Q-3).
/// </summary>
public class AccountPermissionService(AlveraDbContext db)
{
    public async Task<bool> HasAsync(Guid accountId, AccountPermission permission, CancellationToken ct)
    {
        var name = permission.ToString();
        return await db.AccountPermissionGrants.AsNoTracking().AnyAsync(g => g.UserAccountId == accountId && g.Permission == name, ct);
    }

    public async Task<IReadOnlyList<string>> ForAccountAsync(Guid accountId, CancellationToken ct) =>
        await db.AccountPermissionGrants.AsNoTracking().Where(g => g.UserAccountId == accountId).Select(g => g.Permission).OrderBy(p => p).ToListAsync(ct);

    /// <summary>Returns true when the grant was added, false when it was already held.</summary>
    public async Task<bool> GrantAsync(Guid targetAccountId, AccountPermission permission, Guid actor, CancellationToken ct)
    {
        var account = await db.UserAccounts.AsNoTracking().SingleOrDefaultAsync(a => a.Id == targetAccountId, ct)
            ?? throw new AccountPermissionException("account_not_found", "That account was not found.", 404);
        if (account.IsDisabled)
            throw new AccountPermissionException("account_disabled", "A disabled account cannot be given this permission. Enable it first.", 409);
        if (account.Role == Role.Unassigned)
            throw new AccountPermissionException("account_unassigned", "An account without a role cannot be given this permission. Give it a role first.", 409);
        if (await HasAsync(targetAccountId, permission, ct)) return false;

        db.AccountPermissionGrants.Add(new AccountPermissionGrant
        {
            Id = Guid.NewGuid(), UserAccountId = targetAccountId, Permission = permission.ToString(), GrantedByUserAccountId = actor, GrantedAtUtc = DateTimeOffset.UtcNow,
        });
        AuditService.Record(db, AccountPermissionAuditEvents.Granted, nameof(UserAccount), targetAccountId, actor, $"Account permission {permission} granted to '{account.Username}'.");
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 })
        {
            db.ChangeTracker.Clear();                                                                   // a concurrent grant of the same permission won: it is held
            return false;
        }
        return true;
    }

    /// <summary>Returns true when the grant was removed, false when it was not held.</summary>
    public async Task<bool> RevokeAsync(Guid targetAccountId, AccountPermission permission, Guid actor, CancellationToken ct)
    {
        var name = permission.ToString();
        var grant = await db.AccountPermissionGrants.SingleOrDefaultAsync(g => g.UserAccountId == targetAccountId && g.Permission == name, ct);
        if (grant is null) return false;
        var username = await db.UserAccounts.AsNoTracking().Where(a => a.Id == targetAccountId).Select(a => a.Username).SingleAsync(ct);
        db.AccountPermissionGrants.Remove(grant);
        AuditService.Record(db, AccountPermissionAuditEvents.Revoked, nameof(UserAccount), targetAccountId, actor, $"Account permission {permission} revoked from '{username}'.");
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// The one-time administrator bootstrap of a NEW installation: the first administrator also becomes the sign-in presentation administrator, in the
    /// bootstrap's own transaction (the caller saves). Never used for an existing installation: there the permission stays unassigned until an operator grants it
    /// (review BRIEFING-R02 correction 1).
    /// </summary>
    public static void StageBootstrapHolder(AlveraDbContext db, Guid firstAdministratorId, string username)
    {
        db.AccountPermissionGrants.Add(new AccountPermissionGrant
        {
            Id = Guid.NewGuid(), UserAccountId = firstAdministratorId, Permission = nameof(AccountPermission.ManageLoginPresentation), GrantedByUserAccountId = null,
            GrantedAtUtc = DateTimeOffset.UtcNow,
        });
        AuditService.Record(db, AccountPermissionAuditEvents.Granted, nameof(UserAccount), firstAdministratorId, null,
            $"Account permission {AccountPermission.ManageLoginPresentation} granted to '{username}': initial holder, the first administrator created by the installation bootstrap.");
    }
}
