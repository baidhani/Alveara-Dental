namespace Alveara.Api.Architecture.Identity;

/// <summary>
/// STORY-001's audit trail: "All account and role changes are audited with user and timestamp."
/// Written in the same database transaction as the account/role change it records, so an audit
/// write can never silently fail separately from the change it documents — either both persist
/// or neither does.
/// </summary>
public class AuditLogEntry
{
    public Guid Id { get; set; }
    public required string EventType { get; set; }
    public Guid TargetUserAccountId { get; set; }

    // The account that performed the action. Null for a self-service action with no distinct
    // actor yet (e.g. a brand-new registration), never omitted for an actual admin-performed
    // change.
    public Guid? PerformedByUserAccountId { get; set; }

    public required string Details { get; set; }
    public DateTimeOffset TimestampUtc { get; set; }
}

public static class AuditEventTypes
{
    public const string AccountRegistered = "AccountRegistered";
    public const string LoginSucceeded = "LoginSucceeded";
    public const string LoginFailed = "LoginFailed";
    public const string AccountLockedOut = "AccountLockedOut";
    public const string RoleChanged = "RoleChanged";
}
