namespace Alveara.Api.Architecture.Identity;

/// <summary>
/// STORY-002 Trust requirement: "Audit logs are immutable and protected from unauthorized
/// modification." Thrown by <see cref="Alveara.Api.Data.AlveraDbContext"/> whenever an update or
/// delete against an <see cref="AuditLogEntry"/> reaches SaveChanges, regardless of caller or
/// permission — there is no legitimate reason for any code path, present or future, to alter or
/// remove an existing entry. A distinct exception type (rather than a generic
/// <see cref="InvalidOperationException"/>) so this specific violation is unambiguous in logs and
/// directly catchable by tests.
/// </summary>
public sealed class AuditLogImmutableException() : InvalidOperationException(
    "Audit log entries are immutable and cannot be updated or deleted.");
