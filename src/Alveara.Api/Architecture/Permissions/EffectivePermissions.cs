using Microsoft.AspNetCore.Mvc;
using Alveara.Api.Architecture.Identity;

namespace Alveara.Api.Architecture.Permissions;

/// <summary>The answer to "does this role hold this permission, right now?". Only <see cref="Allowed"/> lets the request through; the rest are refusals with different stable codes.</summary>
public enum PermissionOutcome
{
    Allowed,
    /// <summary>The role does not hold the permission (403 <c>permission_denied</c>, the body callers already know).</summary>
    Denied,
    /// <summary>An invalid stored row names this role: every check for it is denied until an administrator repairs the row (403 <c>permission_policy_repair_required</c>).</summary>
    RepairRequired,
    /// <summary>A stored row could not be attributed to any role: every non-administrator check is denied (503 <c>permission_policy_degraded</c>).</summary>
    Degraded,
    /// <summary>The policy cannot be read and no recent verified copy exists (503 <c>permission_policy_unavailable</c>).</summary>
    Unavailable,
}

/// <summary>
/// ALV-N017: THE seam for every permission decision. Roles are code-defined; what a role holds is the code default plus the stored policy, resolved by <see cref="PermissionPolicyStore"/>. Everything that asks
/// "may this role do this" (the permission attributes, the controllers that decide inside an action, the permission endpoints, background code that picks recipients by permission) calls this and nothing reads
/// the static <see cref="PermissionMatrix"/> directly (an architecture test enforces it). The administrator always holds every permission and an unassigned account none, whatever any row says, so neither
/// needs the store.
/// </summary>
public interface IEffectivePermissions
{
    Task<PermissionOutcome> CheckAsync(Role role, Permission permission, CancellationToken ct = default);

    Task<PermissionOutcome> CheckAnyAsync(Role role, IReadOnlyCollection<Permission> permissions, CancellationToken ct = default);

    /// <summary>True only for <see cref="PermissionOutcome.Allowed"/>. Throws <see cref="PolicyUnavailableException"/> when the policy cannot be read.</summary>
    Task<bool> HasAsync(Role role, Permission permission, CancellationToken ct = default);

    /// <summary>The effective set of one role (empty for an unhealthy role, so a screen never shows what a request would refuse).</summary>
    Task<IReadOnlySet<Permission>> PermissionsForAsync(Role role, CancellationToken ct = default);

    /// <summary>The effective set of every role, with the same rule.</summary>
    Task<IReadOnlyDictionary<Role, IReadOnlySet<Permission>>> MatrixAsync(CancellationToken ct = default);

    /// <summary>The snapshot decisions are being made on (health, history and tests).</summary>
    Task<PolicyRead> SnapshotAsync(CancellationToken ct = default);
}

/// <summary>
/// The production implementation, one per request (so the version is read once per request, however many checks the request makes). Role and permission outcomes follow the design: administrator and unassigned
/// are answered from the code rules; any other role waits for the store, then is denied when the store is degraded or the role is in repair required, otherwise decided by its effective set.
/// </summary>
public sealed class EffectivePermissionService(PermissionPolicyStore store) : IEffectivePermissions
{
    private PolicyRead? _read;

    public async Task<PolicyRead> SnapshotAsync(CancellationToken ct = default) => _read ??= await store.GetAsync(ct);

    public async Task<PermissionOutcome> CheckAsync(Role role, Permission permission, CancellationToken ct = default) =>
        await CheckAnyAsync(role, [permission], ct);

    public async Task<PermissionOutcome> CheckAnyAsync(Role role, IReadOnlyCollection<Permission> permissions, CancellationToken ct = default)
    {
        if (role == Role.Admin) return PermissionOutcome.Allowed;
        if (role == Role.Unassigned || !Enum.IsDefined(role)) return PermissionOutcome.Denied;
        try
        {
            var snapshot = (await SnapshotAsync(ct)).Snapshot;
            if (snapshot.Degraded) return PermissionOutcome.Degraded;
            if (snapshot.HealthOf(role) == RoleHealth.RepairRequired) return PermissionOutcome.RepairRequired;
            var held = snapshot.EffectiveFor(role);
            return permissions.Any(held.Contains) ? PermissionOutcome.Allowed : PermissionOutcome.Denied;
        }
        catch (PolicyUnavailableException)
        {
            return PermissionOutcome.Unavailable;
        }
    }

    public async Task<bool> HasAsync(Role role, Permission permission, CancellationToken ct = default) =>
        await CheckAsync(role, permission, ct) switch
        {
            PermissionOutcome.Allowed => true,
            PermissionOutcome.Unavailable => throw new PolicyUnavailableException("The permission policy cannot be read."),
            _ => false,
        };

    public async Task<IReadOnlySet<Permission>> PermissionsForAsync(Role role, CancellationToken ct = default)
    {
        if (role == Role.Admin) return new HashSet<Permission>(Enum.GetValues<Permission>());
        if (role == Role.Unassigned || !Enum.IsDefined(role)) return new HashSet<Permission>();
        var snapshot = (await SnapshotAsync(ct)).Snapshot;                                                    // throws PolicyUnavailableException when unreadable
        if (snapshot.Degraded || snapshot.HealthOf(role) == RoleHealth.RepairRequired) return new HashSet<Permission>();
        return snapshot.EffectiveFor(role);
    }

    public async Task<IReadOnlyDictionary<Role, IReadOnlySet<Permission>>> MatrixAsync(CancellationToken ct = default)
    {
        var result = new Dictionary<Role, IReadOnlySet<Permission>>();
        foreach (var role in Enum.GetValues<Role>()) result[role] = await PermissionsForAsync(role, ct);
        return result;
    }
}

/// <summary>Turns a refusing <see cref="PermissionOutcome"/> into the response callers get. The 403 body of an ordinary denial is the one every client already handles.</summary>
public static class PermissionResults
{
    public static IActionResult? Refusal(PermissionOutcome outcome, string required) => outcome switch
    {
        PermissionOutcome.Allowed => null,
        PermissionOutcome.Denied => new ObjectResult(new { error = "permission_denied", required }) { StatusCode = StatusCodes.Status403Forbidden },
        PermissionOutcome.RepairRequired => new ObjectResult(new { error = "permission_policy_repair_required" }) { StatusCode = StatusCodes.Status403Forbidden },
        PermissionOutcome.Degraded => new ObjectResult(new { error = "permission_policy_degraded" }) { StatusCode = StatusCodes.Status503ServiceUnavailable },
        _ => new ObjectResult(new { error = "permission_policy_unavailable" }) { StatusCode = StatusCodes.Status503ServiceUnavailable },
    };
}

/// <summary>A controller action that asked the permission seam for something and found the policy unreadable (and no recent verified copy) answers 503 <c>permission_policy_unavailable</c>, never a 500 and never a guess.</summary>
public sealed class PolicyUnavailableFilter : Microsoft.AspNetCore.Mvc.Filters.IExceptionFilter
{
    public void OnException(Microsoft.AspNetCore.Mvc.Filters.ExceptionContext context)
    {
        if (context.Exception is not PolicyUnavailableException) return;
        context.Result = new ObjectResult(new { error = "permission_policy_unavailable" }) { StatusCode = StatusCodes.Status503ServiceUnavailable };
        context.ExceptionHandled = true;
    }
}
