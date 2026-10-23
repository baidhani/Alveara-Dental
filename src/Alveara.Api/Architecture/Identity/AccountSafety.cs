using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Permissions;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Identity;

/// <summary>Refused because it would leave no enabled administrator (409, <c>last_admin</c>). Nothing was changed.</summary>
public sealed class LastAdminException() : Exception("At least one enabled administrator must remain.");

/// <summary>The administrator-availability lock could not be taken in time (503, <c>admin_guard_busy</c>). Nothing was changed and the same request can be retried.</summary>
public sealed class AdminGuardBusyException() : Exception("The administrator guard is busy. Try again.");

/// <summary>The rules a non-administrator holder of <c>ManageAccountStatus</c> or <c>RevokeSessions</c> must satisfy to act on an account (403, <c>account_target_not_allowed</c>). <see cref="Rule"/> is a stable code.</summary>
public sealed class AccountTargetNotAllowedException(string rule) : Exception("You cannot act on that account.")
{
    public string Rule { get; } = rule;
}

/// <summary>
/// ALV-N017: the last-enabled-Administrator invariant. An administrator is <i>available</i> when the account's role is Admin and it is not disabled. After any committed change at least one is available. Inside the
/// operation's transaction: (1) take a transaction-scoped exclusive application lock <c>alveara.admin-availability</c> (bounded wait, 5 s); (2) count the available administrators excluding the account the operation
/// would remove, reading the latest committed state (<c>READCOMMITTEDLOCK</c>, so a row-versioned snapshot cannot hide a concurrent commit); (3) refuse when none would remain. The database backstop is the trigger
/// <c>TR_UserAccounts_LastAdmin</c> (51207, busy 51208), which takes the same lock, so raw SQL and bulk updates cannot break the rule either. Role and disabled state are written only by <see cref="AccountService"/>
/// (an architecture test enforces it).
/// </summary>
public static class AdminAvailabilityGuard
{
    public const string Resource = "alveara.admin-availability";
    public const int LockTimeoutMilliseconds = 5000;

    /// <summary>Takes the lock for the rest of the current transaction (the caller must have one). Throws <see cref="AdminGuardBusyException"/> when it cannot be had in time.</summary>
    public static async Task AcquireAsync(AlveraDbContext db, CancellationToken ct)
    {
        var transaction = db.Database.CurrentTransaction ?? throw new InvalidOperationException("The administrator guard needs the operation's transaction.");
        var connection = db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandTimeout = 10;
        command.CommandText = "DECLARE @r int; EXEC @r = sp_getapplock @Resource = @resource, @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = @timeout; SELECT @r;";
        var resource = command.CreateParameter(); resource.ParameterName = "@resource"; resource.Value = Resource; command.Parameters.Add(resource);
        var timeout = command.CreateParameter(); timeout.ParameterName = "@timeout"; timeout.Value = LockTimeoutMilliseconds; command.Parameters.Add(timeout);
        var result = Convert.ToInt32(await command.ExecuteScalarAsync(ct));
        if (result < 0) throw new AdminGuardBusyException();
    }

    /// <summary>Throws <see cref="LastAdminException"/> if, without <paramref name="removing"/>, no available administrator would remain. Call after <see cref="AcquireAsync"/>.</summary>
    public static async Task EnsureAnotherAsync(AlveraDbContext db, Guid removing, CancellationToken ct)
    {
        var admin = (int)Role.Admin;
        var others = await db.Database.SqlQuery<int>(
            $"SELECT COUNT(*) AS [Value] FROM [UserAccounts] WITH (READCOMMITTEDLOCK) WHERE [Role] = {admin} AND [IsDisabled] = 0 AND [Id] <> {removing}").SingleAsync(ct);
        if (others == 0) throw new LastAdminException();
    }
}

/// <summary>What a non-administrator holder is trying to do to an account.</summary>
public enum AccountAction { SetEnabled, Disable, SetSessionTimeout, RevokeSessions }

/// <summary>
/// ALV-N017 target rules (docs/design/ADMIN_GRANTABLE_PERMISSIONS_PROPOSAL.md section 3.3A), applied inside the account operations whenever the acting account is not an administrator. Let A be the actor's role,
/// T the target's role and Eff(R) the full effective permission set of role R. (1) A non-administrator may never act on an administrator account. (2) The action is allowed only if Eff(T) is a subset of Eff(A). (3)
/// A holder cannot disable their own account. Administrators are not restricted here (the last-administrator guard still applies to them). A refusal changes nothing and is audited with the actor, the target and
/// the rule that failed (ids and the rule code only).
/// </summary>
public static class AccountTargetRules
{
    public const string TargetIsAdmin = "target_is_admin";
    public const string TargetExceedsActor = "target_exceeds_actor";
    public const string SelfDisable = "self_disable";

    /// <summary>Returns the failed rule, or null when the action is allowed.</summary>
    public static async Task<string?> EvaluateAsync(IEffectivePermissions permissions, UserAccount actor, UserAccount target, AccountAction action, CancellationToken ct)
    {
        if (actor.Role == Role.Admin) return null;
        if (target.Role == Role.Admin) return TargetIsAdmin;
        if (action == AccountAction.Disable && actor.Id == target.Id) return SelfDisable;
        var actorSet = await permissions.PermissionsForAsync(actor.Role, ct);
        var targetSet = await permissions.PermissionsForAsync(target.Role, ct);
        return targetSet.IsSubsetOf(actorSet) ? null : TargetExceedsActor;
    }

    /// <summary>Records the refusal on its own so it survives the refused operation's rollback.</summary>
    public static async Task RecordRefusalAsync(AlveraDbContext db, Guid actorId, Role actorRole, Guid targetId, string rule, CancellationToken ct)
    {
        AuditService.Record(db, AuditEventTypes.AccountActionRefused, "UserAccount", targetId, actorId, $"An account action by a {actorRole} was refused: {rule}.");
        await db.SaveChangesAsync(ct);
    }
}
