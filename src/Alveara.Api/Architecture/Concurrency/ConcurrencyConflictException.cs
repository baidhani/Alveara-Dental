namespace Alveara.Api.Architecture.Concurrency;

/// <summary>
/// ALV-002-C01: the shared "someone else changed this first" outcome. Wraps EF Core's own
/// <see cref="Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException"/> into a stable,
/// domain-level type (via <see cref="ConcurrencySaveGuard"/>) so callers and tests are never
/// coupled to EF's own exception shape, and so every future domain that adopts a version-token
/// gets the same conflict representation an API or UI can render consistently.
/// </summary>
public sealed class ConcurrencyConflictException(string entityType, Guid entityId)
    : Exception($"The {entityType} record ({entityId}) was changed by someone else since it was last read.")
{
    public string EntityType { get; } = entityType;
    public Guid EntityId { get; } = entityId;

    /// <summary>Shared conflict-result shape usable by any API's catch block (typically returned
    /// as an HTTP 409) or by a UI's stale-edit presentation, without either needing to know
    /// anything about EF Core.</summary>
    public ConcurrencyConflictProblem ToProblem() => new("concurrency_conflict", EntityType, EntityId);
}

/// <summary>The one shared shape every domain's concurrency-conflict response takes, whether
/// serialized straight to an HTTP 409 body or read by a UI's stale-edit presentation.</summary>
public sealed record ConcurrencyConflictProblem(string Error, string EntityType, Guid EntityId);
