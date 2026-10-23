namespace Alveara.Api.Architecture.Permissions;

/// <summary>The effect of a stored override on one (role, permission) cell of the code-defined default matrix.</summary>
public static class OverrideEffects
{
    public const string Grant = "Grant";
    public const string Revoke = "Revoke";

    public static readonly IReadOnlyList<string> All = [Grant, Revoke];
}

/// <summary>What a history row records. History is immutable and says what changed, who changed it and why; the policy version it belongs to ties it to the snapshot that took effect.</summary>
public static class PolicyChangeKinds
{
    public const string Granted = "Granted";
    public const string Revoked = "Revoked";
    public const string Reverted = "Reverted";
    public const string DelegationAdded = "DelegationAdded";
    public const string DelegationRemoved = "DelegationRemoved";
    public const string InvalidRowRemoved = "InvalidRowRemoved";

    public static readonly IReadOnlyList<string> All = [Granted, Revoked, Reverted, DelegationAdded, DelegationRemoved, InvalidRowRemoved];
}

/// <summary>
/// ALV-N017: one stored difference from the code default: a grant or a revoke of one permission for one role. A row exists only while it differs from the default. Role and permission are stable names, never
/// ordinals, and are validated against the committed permission registry when read; a row that fails validation is invalid persisted data and never restores access.
/// </summary>
public class RolePermissionOverride
{
    public Guid Id { get; set; }
    public required string Role { get; set; }
    public required string Permission { get; set; }
    public required string Effect { get; set; }
    public required string Reason { get; set; }
    public Guid ChangedByUserId { get; set; }
    public DateTimeOffset ChangedAtUtc { get; set; }
    public byte[]? RowVersion { get; set; }
}

/// <summary>ALV-N017: the immutable record of every policy change (database triggers refuse updates and deletes).</summary>
public class RolePermissionHistory
{
    public Guid Id { get; set; }
    public long PolicyVersion { get; set; }
    public required string Kind { get; set; }
    public required string Role { get; set; }
    public required string Permission { get; set; }
    /// <summary>For a delegation change, the role the delegate may act on; otherwise null.</summary>
    public string? TargetRole { get; set; }
    public bool OldEffective { get; set; }
    public bool NewEffective { get; set; }
    public required string Reason { get; set; }
    public Guid ChangedByUserId { get; set; }
    public bool ViaDelegation { get; set; }
    public DateTimeOffset ChangedAtUtc { get; set; }
}

/// <summary>
/// ALV-N017: lets one role (only the office manager) grant or revoke one delegable permission for one target role. The default row set (in the migration) is the office manager and <c>ImportDocuments</c>
/// for the five other working roles. Only an administrator may change these rows (the write path arrives in ALV-N018); a row naming a protected permission, <c>ManageAccountStatus</c> or
/// <c>RevokeSessions</c>, or a delegate or target that policy forbids, is invalid persisted data and the delegation is disabled.
/// </summary>
public class PermissionDelegation
{
    public Guid Id { get; set; }
    public required string DelegateRole { get; set; }
    public required string Permission { get; set; }
    public required string TargetRole { get; set; }
    public Guid? ChangedByUserId { get; set; }
    public DateTimeOffset ChangedAtUtc { get; set; }
    public byte[]? RowVersion { get; set; }
}

/// <summary>ALV-N017: the one row whose <see cref="Version"/> is incremented in the same transaction as every policy change; readers bracket their reads with it.</summary>
public class PermissionPolicyState
{
    public int Id { get; set; } = 1;
    public long Version { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
