namespace Alveara.Api.Architecture.Identity;

/// <summary>
/// ALV-001-C01: proof that the one-time first-admin bootstrap has been consumed. A single row
/// with a fixed, well-known primary key (<see cref="SingletonId"/>) — the bootstrap operation
/// always attempts to INSERT this exact row first; the database's own primary-key uniqueness is
/// what makes "exactly once, even under a concurrent race" true, not application-level
/// check-then-act logic (which two simultaneous requests could both pass).
/// </summary>
public class BootstrapState
{
    public static readonly Guid SingletonId = new("00000000-0000-0000-0000-00000000b007");

    public Guid Id { get; set; } = SingletonId;
    public DateTimeOffset ConsumedAtUtc { get; set; }
    public required string ConsumedForUsername { get; set; }
}
