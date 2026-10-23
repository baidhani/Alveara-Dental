using System.Collections.Frozen;
using Alveara.Api.Architecture.Identity;

namespace Alveara.Api.Architecture.Permissions;

/// <summary>
/// ALV-N017: turns the code-defined default matrix and the stored rows into a <see cref="PolicySnapshot"/>. It is a pure function (no I/O, no clock): the same rows always give the same snapshot. This is one of
/// the two places allowed to read <see cref="PermissionMatrix"/> (the other is the service that serves its defaults). Validation is conservative: an invalid row never restores access (docs/design/
/// ADMIN_GRANTABLE_PERMISSIONS_PROPOSAL.md section 3.6). A row that names a retired permission is ignored; a row that names a known non-administrator role but is otherwise invalid puts that role in repair
/// required (every check for it is denied); a row whose role cannot be attributed degrades the whole store (every non-administrator check is denied); an invalid delegation is disabled.
/// </summary>
public static class PolicySnapshotBuilder
{
    /// <summary>Does the code default give this role this permission? (the only way outside this file to ask the default, for the policy writer)</summary>
    public static bool DefaultHolds(Role role, Permission permission) => PermissionMatrix.RoleHas(role, permission);

    public static PolicySnapshot Build(long version, IEnumerable<RolePermissionOverride> overrides, IEnumerable<PermissionDelegation> delegations, IPermissionNames? names = null)
    {
        names ??= PermissionNames.Committed;
        var effective = Enum.GetValues<Role>().ToDictionary(r => r, r => new HashSet<Permission>(PermissionMatrix.PermissionsFor(r)));
        var health = new Dictionary<Role, RoleHealth>();
        var invalid = new List<InvalidPolicyRow>();
        var retired = new List<RetiredPolicyRow>();
        var valid = 0;
        var degraded = false;

        foreach (var row in overrides.OrderBy(o => o.Id))
        {
            if (!TryRole(row.Role, out var role))
            {
                invalid.Add(Invalid(row, InvalidRowReason.UnknownRole));
                degraded = true;                                                                              // the role cannot be known, so which cell was meant cannot be known either
                continue;
            }

            if (names.IsRetired(row.Permission)) { retired.Add(new RetiredPolicyRow(row.Id, "Override", row.Role, row.Permission)); continue; }

            if (!TryPermission(row.Permission, names, out var permission))
            {
                Reject(row, InvalidRowReason.UnknownPermission, role);
                continue;
            }
            if (PermissionProtection.IsFixedRole(role)) { Reject(row, InvalidRowReason.FixedRoleOverride, role); continue; }
            if (row.Effect is not (OverrideEffects.Grant or OverrideEffects.Revoke)) { Reject(row, InvalidRowReason.UnknownEffect, role); continue; }
            if (!PermissionProtection.IsGrantable(permission)) { Reject(row, InvalidRowReason.ProtectedPermissionOverride, role); continue; }

            if (row.Effect == OverrideEffects.Grant) effective[role].Add(permission); else effective[role].Remove(permission);
            valid++;
        }

        var active = new List<ActiveDelegation>();
        foreach (var d in delegations.OrderBy(x => x.Id))
        {
            var reason = CheckDelegation(d, names, out var del);
            if (reason is null) active.Add(del!);
            else if (reason == InvalidRowReason.DelegationRetiredPermission) retired.Add(new RetiredPolicyRow(d.Id, "Delegation", d.DelegateRole, d.Permission));
            else invalid.Add(new InvalidPolicyRow(d.Id, "Delegation", d.DelegateRole, d.Permission, d.TargetRole, null, reason.Value, d.RowVersion));
        }

        // the administrator holds everything and an unassigned account nothing, whatever any row says
        effective[Role.Admin] = [.. Enum.GetValues<Permission>()];
        effective[Role.Unassigned] = [];

        return new PolicySnapshot
        {
            Version = version,
            Effective = effective.ToFrozenDictionary(kv => kv.Key, kv => kv.Value.ToFrozenSet()),
            Delegations = active.ToFrozenSet(),
            RoleHealth = health.ToFrozenDictionary(),
            Degraded = degraded,
            InvalidRows = invalid,
            RetiredRows = retired,
            OverrideCount = valid,
        };

        void Reject(RolePermissionOverride row, InvalidRowReason reason, Role role)
        {
            invalid.Add(Invalid(row, reason));
            if (!PermissionProtection.IsFixedRole(role)) health[role] = RoleHealth.RepairRequired;          // a fixed role's cells cannot be changed by a row, so a bad row there changes no decision
        }
    }

    private static InvalidPolicyRow Invalid(RolePermissionOverride row, InvalidRowReason reason) =>
        new(row.Id, "Override", row.Role, row.Permission, null, row.Effect, reason, row.RowVersion);

    private static InvalidRowReason? CheckDelegation(PermissionDelegation d, IPermissionNames names, out ActiveDelegation? result)
    {
        result = null;
        if (!TryRole(d.DelegateRole, out var delegateRole) || !PermissionProtection.MayDelegate(delegateRole)) return InvalidRowReason.DelegationBadDelegate;
        if (!TryRole(d.TargetRole, out var target) || !PermissionProtection.IsDelegationTarget(target)) return InvalidRowReason.DelegationBadTarget;
        if (names.IsRetired(d.Permission)) return InvalidRowReason.DelegationRetiredPermission;
        if (!TryPermission(d.Permission, names, out var permission)) return InvalidRowReason.DelegationUnknownPermission;
        if (PermissionProtection.AdminOnly.Contains(permission)) return InvalidRowReason.DelegationProtectedPermission;
        if (!PermissionProtection.IsDelegable(permission)) return InvalidRowReason.DelegationNotDelegable;
        result = new ActiveDelegation(delegateRole, permission, target);
        return null;
    }

    /// <summary>A role name is exact and known: never case-folded, never a number.</summary>
    private static bool TryRole(string name, out Role role) =>
        Enum.TryParse(name, ignoreCase: false, out role) && Enum.IsDefined(role) && !int.TryParse(name, out _) && role.ToString() == name;

    /// <summary>A permission name is exact, registered as active and defined in code.</summary>
    private static bool TryPermission(string name, IPermissionNames names, out Permission permission)
    {
        permission = default;
        return names.IsActive(name) && Enum.TryParse(name, ignoreCase: false, out permission) && Enum.IsDefined(permission) && permission.ToString() == name;
    }
}

/// <summary>What the builder asks the permission registry. A seam so the retired-permission behaviour can be proven; production uses <see cref="PermissionNames.Committed"/>.</summary>
public interface IPermissionNames
{
    bool IsActive(string name);
    bool IsRetired(string name);
}

public sealed class PermissionNames : IPermissionNames
{
    public static readonly PermissionNames Committed = new();
    public bool IsActive(string name) => PermissionRegistry.IsActive(name);
    public bool IsRetired(string name) => PermissionRegistry.IsRetired(name);
}
