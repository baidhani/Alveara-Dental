namespace Alveara.Api.Architecture.Identity;

/// <summary>
/// ALV-N030: a one-time first-run setup token. Only the SHA-256 hash of the token is stored; the token itself is shown once, on the installer's console, by the
/// setup CLI command. A token is single use (<see cref="ConsumedAtUtc"/>), expires 15 minutes after issue, and is voided after five failed attempts or when a
/// newer token is issued. Rows are kept (never deleted) so a setup attempt can always be explained.
/// </summary>
public class SetupToken
{
    public Guid Id { get; set; }

    /// <summary>Lower-case hex SHA-256 of the token (64 characters).</summary>
    public required string TokenHash { get; set; }

    public DateTimeOffset IssuedAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset? ConsumedAtUtc { get; set; }

    /// <summary>Set when a newer token was issued or the failure limit was reached; a voided token can never be used.</summary>
    public DateTimeOffset? VoidedAtUtc { get; set; }

    public int FailedAttempts { get; set; }
}

/// <summary>How a Practice Owner designation was made. Stored as a string; values are append-only.</summary>
public enum PracticeOwnerDesignationMethod
{
    /// <summary>The Deployment Administrator designated the first owner while adding them as a staff member.</summary>
    InitialByDeploymentAdministrator,

    /// <summary>An active Administrator designated the initial owner on an installation upgraded with no owner (D-7).</summary>
    UpgradeTransition,
}

/// <summary>
/// ALV-N030: the protected Practice Owner designation - a human staff member (a staff profile with a linked account), not a role and not a permission.
/// Exactly one designation may be active (<see cref="EndedAtUtc"/> null): a filtered unique index on <see cref="Slot"/> enforces it in the database.
/// Rows are never deleted, and a database trigger refuses every change except ending an active designation (only a future, separately approved
/// ownership-transfer story may do that; ALV-N030 has no code path that sets <see cref="EndedAtUtc"/>).
/// </summary>
public class PracticeOwnerDesignation
{
    public Guid Id { get; set; }
    public Guid StaffProfileId { get; set; }
    public StaffProfile? StaffProfile { get; set; }
    public DateTimeOffset DesignatedAtUtc { get; set; }
    public Guid DesignatedByAccountId { get; set; }
    public PracticeOwnerDesignationMethod Method { get; set; }
    public DateTimeOffset? EndedAtUtc { get; set; }

    /// <summary>Always 1. Exists only so a filtered unique index (<c>WHERE EndedAtUtc IS NULL</c>) can allow at most one active designation.</summary>
    public byte Slot { get; set; } = 1;
}
