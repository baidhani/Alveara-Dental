namespace Alveara.Api.Architecture.Patients;

/// <summary>
/// ALV-003-C01: a family/household grouping. Membership lives on <see cref="Patient.HouseholdId"/>; a household has no
/// meaning beyond "these patients belong together" and is deliberately NOT the same thing as the guarantor
/// (a household can have several guarantors, and a guarantor need not live in the household).
/// </summary>
public class Household
{
    public Guid Id { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public Guid? CreatedByUserId { get; set; }
}

/// <summary>
/// ALV-003-C01: one row per changed field per edit, with who and when. This is the "history" of safe edits; the audit log
/// separately records THAT an edit happened without any patient values (the audit log is kept PHI-free).
/// </summary>
public class PatientHistoryEntry
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    public DateTimeOffset ChangedAtUtc { get; set; }
    public Guid? ChangedByUserId { get; set; }
    /// <summary>"Updated", "Activated", "Inactivated", "GuarantorChanged" or "HouseholdChanged".</summary>
    public required string ChangeType { get; set; }
    public required string FieldName { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
}

/// <summary>
/// ALV-003-C01: the practice's own registration requirements, on top of the always-required fields (name, birth date, phone, address).
/// A single row (created on first save); the defaults - nothing extra required - are STORY-003's behavior exactly. Kept in its own table
/// rather than on the practice-configuration record (ALV-N003) so that completed story is not modified.
/// </summary>
public class PatientRegistrationSettings
{
    public Guid Id { get; set; }
    /// <summary>Always true; a unique index on it makes a second settings row impossible, even if two first saves race.</summary>
    public bool Singleton { get; set; } = true;
    public bool RequireEmail { get; set; }
    public bool RequireSex { get; set; }
    public DateTimeOffset? UpdatedAtUtc { get; set; }
    public Guid? UpdatedByUserId { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>The extra required fields a practice has chosen. Default = none, i.e. STORY-003's rules.</summary>
public record PatientRequirements(bool RequireEmail = false, bool RequireSex = false)
{
    public static readonly PatientRequirements None = new();
}

public static class HouseholdRelationships
{
    public static readonly IReadOnlyList<string> All = ["Self", "Spouse", "Parent", "Child", "Sibling", "Grandparent", "Grandchild", "Other"];
    public static bool IsValid(string? value) => value is not null && All.Contains(value);
}

public static class PatientAuditEventsV2
{
    public const string Updated = "PatientUpdated";
    public const string StatusChanged = "PatientStatusChanged";
    public const string HouseholdChanged = "PatientHouseholdChanged";
    public const string GuarantorChanged = "PatientGuarantorChanged";
    public const string RequirementsChanged = "PatientRegistrationSettingsChanged";
}

/// <summary>A likely duplicate shown side by side with what the front desk typed, so a human decides - never a silent merge.</summary>
public record DuplicateCandidate(
    Guid Id, string FirstName, string? MiddleName, string LastName, string DateOfBirth, string Phone, string? Email,
    string City, string State, bool IsActive, IReadOnlyList<string> Reasons, bool Exact);

/// <summary>A patient operation was refused. <see cref="Code"/> is stable for the UI.</summary>
public class PatientException(string code, string message, int statusCode, IReadOnlyDictionary<string, string>? fieldErrors = null, Guid? existingPatientId = null, IReadOnlyList<DuplicateCandidate>? candidates = null)
    : Exception(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
    public IReadOnlyDictionary<string, string> FieldErrors { get; } = fieldErrors ?? new Dictionary<string, string>();
    public Guid? ExistingPatientId { get; } = existingPatientId;
    public IReadOnlyList<DuplicateCandidate> Candidates { get; } = candidates ?? [];
}
