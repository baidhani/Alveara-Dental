using System.Collections.Frozen;
using Alveara.Api.Architecture.Identity;

namespace Alveara.Api.Architecture.Permissions;

/// <summary>How the engine treats one role, given the stored rows that could be attributed to it.</summary>
public enum RoleHealth
{
    /// <summary>Every row that names the role is valid; its cells are the default plus its overrides.</summary>
    Healthy,
    /// <summary>An invalid override names this known, non-administrator role. The row might have been a revoke whose permission name was corrupted, so no permission can be trusted: every check for the role is denied.</summary>
    RepairRequired,
}

/// <summary>What was wrong with one stored row (stable codes; no free text is ever logged).</summary>
public enum InvalidRowReason
{
    UnknownRole,
    UnknownPermission,
    UnknownEffect,
    FixedRoleOverride,
    ProtectedPermissionOverride,
    DelegationUnknownPermission,
    DelegationProtectedPermission,
    DelegationNotDelegable,
    DelegationBadDelegate,
    DelegationBadTarget,
}

/// <summary>One stored row the engine refused, with its raw values so an administrator can repair it. <see cref="Table"/> is <c>Override</c> or <c>Delegation</c>.</summary>
public sealed record InvalidPolicyRow(Guid Id, string Table, string Role, string Permission, string? TargetRole, string? Effect, InvalidRowReason Reason, byte[]? RowVersion);

/// <summary>A delegation that passed validation: the delegate role may grant or revoke the permission for the target role.</summary>
public sealed record ActiveDelegation(Role DelegateRole, Permission Permission, Role TargetRole);

/// <summary>
/// ALV-N017: one immutable picture of the effective permission policy at one policy version. Built whole, published by a single reference swap, never changed afterwards, so an evaluation that captured it
/// can never see a half-applied change. <see cref="Effective"/> holds the effective set of every role (the code default plus valid overrides; the administrator holds everything and an unassigned account
/// nothing, always). <see cref="Degraded"/> means a stored row could not be attributed to any role, so every non-administrator decision is denied. <see cref="RoleHealth"/> lists the roles that are not healthy.
/// </summary>
public sealed class PolicySnapshot
{
    public required long Version { get; init; }
    public required FrozenDictionary<Role, FrozenSet<Permission>> Effective { get; init; }
    public required FrozenSet<ActiveDelegation> Delegations { get; init; }
    public required FrozenDictionary<Role, RoleHealth> RoleHealth { get; init; }
    public required bool Degraded { get; init; }
    public required IReadOnlyList<InvalidPolicyRow> InvalidRows { get; init; }
    /// <summary>Rows that name a retired permission: ignored and harmless, reported only so they can be removed.</summary>
    public required int RetiredRowCount { get; init; }
    /// <summary>How many valid override rows are in force (for operational visibility).</summary>
    public required int OverrideCount { get; init; }

    public bool IsHealthy => !Degraded && InvalidRows.Count == 0;

    public RoleHealth HealthOf(Role role) => RoleHealth.TryGetValue(role, out var h) ? h : Permissions.RoleHealth.Healthy;

    public IReadOnlySet<Permission> EffectiveFor(Role role) => Effective.TryGetValue(role, out var set) ? set : FrozenSet<Permission>.Empty;
}
