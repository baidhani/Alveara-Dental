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

    // ALV-001-C01 additions.
    public const string AccountEnabled = "AccountEnabled";
    public const string AccountDisabled = "AccountDisabled";
    public const string MfaEnabled = "MfaEnabled";
    public const string PasswordResetIssued = "PasswordResetIssued";
    public const string PasswordReset = "PasswordReset";
    public const string SessionsRevoked = "SessionsRevoked";

    // ALV-001-C01 R02 additions (review findings 01/02/04): the MFA lifecycle now has its own
    // distinct, fully-audited events. PasswordVerifiedMfaPending replaces the R01 mistake of
    // recording LoginSucceeded before the second factor was actually verified.
    public const string PasswordVerifiedMfaPending = "PasswordVerifiedMfaPending";
    public const string MfaChallengeSucceeded = "MfaChallengeSucceeded";
    public const string MfaChallengeFailed = "MfaChallengeFailed";
    public const string MfaRecoveryCodeUsed = "MfaRecoveryCodeUsed";
    public const string MfaEnrollmentStarted = "MfaEnrollmentStarted";
    public const string MfaReplacementStarted = "MfaReplacementStarted";
    public const string MfaReplaced = "MfaReplaced";
    public const string SessionTimeoutChanged = "SessionTimeoutChanged";

    // ALV-001-C01 R03 addition (review finding ALV-001-C01-R02-02): the MFA-replacement
    // step-up password check now shares the login lockout boundary; its own failures get a
    // distinct event type so an audit reader can tell a step-up failure from an actual login
    // failure, even though both count toward (and can trigger) the same account lockout.
    public const string MfaStepUpFailed = "MfaStepUpFailed";
}
