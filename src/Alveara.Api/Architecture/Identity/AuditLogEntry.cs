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

    // ALV-002-C01 additions: this table is the concrete storage behind the shared
    // Architecture/Auditing/AuditService, not just Identity's own audit trail. TargetUserAccountId
    // is kept (not renamed) so STORY-002's existing tests/queries against it are untouched, but
    // EntityType now names what kind of thing it identifies - every write through this story
    // onward sets it explicitly; historical STORY-001/ALV-001-C01 rows predate the column and are
    // simply null, which is honest (they really do predate this metadata) rather than backfilled
    // with a guess.
    public string? EntityType { get; set; }

    /// <summary>Free-text business reason/context for the action, when the caller supplied one -
    /// distinct from <see cref="Details"/>, which is a fixed, code-generated summary.</summary>
    public string? Reason { get; set; }

    /// <summary>Links this entry to other events/requests that resulted from the same logical
    /// operation (e.g. a multi-step workflow, or a retried/idempotent command).</summary>
    public Guid? CorrelationId { get; set; }
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

    // ALV-001-C01 R04 addition (review finding ALV-001-C01-R03-02): a privacy-safe event for a
    // challenge that could not be consumed because it was already consumed (replayed), expired,
    // or revoked - never logs the token or code, and is indistinguishable across those three
    // causes in its own right, matching the public-facing exception's own refusal to disclose why.
    public const string MfaChallengeReplayRejected = "MfaChallengeReplayRejected";
}
