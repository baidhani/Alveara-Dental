namespace Alveara.Api.Architecture.Patients;

/// <summary>
/// STORY-003: a registered patient - demographics and contact details only. Household/family
/// relationships and the separate guarantor are deliberately NOT modelled here; the master plan
/// assigns them to ALV-003-C01 (REQ-004's household clause is not in STORY-003's acceptance).
/// </summary>
public class Patient
{
    public Guid Id { get; set; }

    // Demographics
    public required string FirstName { get; set; }
    public string? MiddleName { get; set; }
    public required string LastName { get; set; }
    /// <summary>A calendar date, never a DateTime, so it can never be time-zone-shifted (ALV-N002).</summary>
    public DateOnly DateOfBirth { get; set; }
    public string? Sex { get; set; }

    // Contact details
    public required string Phone { get; set; }
    public string? Email { get; set; }
    public required string AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public required string City { get; set; }
    public required string State { get; set; }
    public required string PostalCode { get; set; }

    /// <summary>
    /// Normalized "last|first|date-of-birth" identity key (see <see cref="BuildDuplicateKey"/>). A unique
    /// index on it means two registrations of the same person cannot both succeed even when they race;
    /// the loser sees a duplicate, not a second record. ALV-003-C01 relaxes this to a warning + override.
    /// </summary>
    public required string DuplicateKey { get; set; }

    /// <summary>
    /// The client-supplied idempotency key of the registration request that created this patient. A retry
    /// with the same key finds this row and returns it instead of creating a second patient.
    /// </summary>
    public string? RegistrationKey { get; set; }

    /// <summary>Who registered the patient and when (the audit entry carries the same pair).</summary>
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }

    // ALV-003-C01: identity workspace state.

    /// <summary>A patient is inactivated, never deleted, so history, audit entries and relationships keep resolving.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>The household this patient belongs to (null = none). Independent of <see cref="GuarantorPatientId"/>.</summary>
    public Guid? HouseholdId { get; set; }
    public Household? Household { get; set; }
    /// <summary>How this patient relates to the household (one of <see cref="HouseholdRelationships.All"/>); set exactly when <see cref="HouseholdId"/> is.</summary>
    public string? HouseholdRelationship { get; set; }

    /// <summary>The patient financially responsible for this one (null = responsible for themselves). Independent of <see cref="HouseholdId"/>.</summary>
    public Guid? GuarantorPatientId { get; set; }
    public Patient? Guarantor { get; set; }

    public DateTimeOffset? UpdatedAtUtc { get; set; }
    public Guid? UpdatedByUserId { get; set; }

    /// <summary>SQL Server rowversion: a stale save is rejected, never silently overwrites (ALV-002-C01).</summary>
    public byte[] RowVersion { get; set; } = [];

    public static string BuildDuplicateKey(string firstName, string lastName, DateOnly dateOfBirth) =>
        $"{NormalizeName(lastName)}|{NormalizeName(firstName)}|{dateOfBirth:yyyy-MM-dd}";

    /// <summary>Trim, collapse inner whitespace, upper-case - so "  ann  LEE" and "Ann Lee" are the same person.</summary>
    public static string NormalizeName(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
}
