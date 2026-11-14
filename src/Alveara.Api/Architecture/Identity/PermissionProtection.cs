namespace Alveara.Api.Architecture.Identity;

/// <summary>
/// ALV-N017: which permissions policy may move. <see cref="AdminOnly"/> permissions are held by the administrator alone, can never be granted to another role (by anyone, including an administrator) and can never
/// be delegated; a stored row naming one is invalid persisted data. <see cref="AdminAssignableNeverDelegable"/> permissions may be granted to or revoked from a working role by an administrator but never delegated.
/// Every other permission is grantable and, only through an administrator-defined delegation row, delegable. This type has no policy storage: it is the code-defined rule the engine and its validation read.
/// </summary>
public static class PermissionProtection
{
    public static readonly IReadOnlySet<Permission> AdminOnly = new HashSet<Permission>
    {
        Permission.ManageUsers, Permission.ManageRoles, Permission.ManageMfaPolicy, Permission.IssuePasswordResets, Permission.ManagePermissionGrants,
    };

    public static readonly IReadOnlySet<Permission> AdminAssignableNeverDelegable = new HashSet<Permission>
    {
        Permission.ManageAccountStatus, Permission.RevokeSessions,
    };

    /// <summary>
    /// ALV-N021 A2 (owner addendum 4): permissions that only some working roles may ever hold. An override granting one to any other role is refused when written and, if found in stored
    /// data, is an invalid row that puts that role in repair. These permissions are never delegable. The administrator still holds every permission (fixed role).
    /// </summary>
    public static readonly IReadOnlyDictionary<Permission, IReadOnlySet<Role>> RoleLimited = new Dictionary<Permission, IReadOnlySet<Role>>
    {
        [Permission.CompleteOwnStartedVisit] = new HashSet<Role> { Role.Dentist, Role.Hygienist },
    };

    /// <summary>May an override row name this permission for this working role? (<see cref="IsGrantable"/> plus the <see cref="RoleLimited"/> rule.)</summary>
    public static bool IsGrantableTo(Permission permission, Role role) =>
        IsGrantable(permission) && (!RoleLimited.TryGetValue(permission, out var roles) || roles.Contains(role));

    /// <summary>The roles whose cells policy may never change: the administrator holds everything, an unassigned account holds nothing.</summary>
    public static bool IsFixedRole(Role role) => role is Role.Admin or Role.Unassigned;

    /// <summary>May an override row (grant or revoke) name this permission for a working role?</summary>
    public static bool IsGrantable(Permission permission) => !AdminOnly.Contains(permission);

    /// <summary>May a delegation row name this permission?</summary>
    public static bool IsDelegable(Permission permission) => !AdminOnly.Contains(permission) && !AdminAssignableNeverDelegable.Contains(permission) && !RoleLimited.ContainsKey(permission);

    /// <summary>The roles a delegation row may name as a delegate: only the office manager; never the administrator or an unassigned account.</summary>
    public static bool MayDelegate(Role delegateRole) => delegateRole == Role.OfficeManager;

    /// <summary>The roles a delegation row may name as a target: the five working roles other than the office manager.</summary>
    public static bool IsDelegationTarget(Role target) => target is Role.Dentist or Role.Hygienist or Role.Assistant or Role.FrontDesk or Role.Billing;
}
