namespace Alveara.Api.Architecture.Clinical;

/// <summary>
/// STORY-005: a clinical-documentation rule refused a request. Carries a stable machine code (the client branches on it), a message safe to show, the HTTP
/// status the API should answer with, and per-field/section messages when several things need attention at once. Same shape as the forms module's exception.
/// </summary>
public class ClinicalException(string code, string message, int statusCode, IReadOnlyDictionary<string, string>? fieldErrors = null)
    : Exception(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
    public IReadOnlyDictionary<string, string> FieldErrors { get; } = fieldErrors ?? new Dictionary<string, string>();
}

/// <summary>The fields a clinician types for one entry. Which ones apply depends on the entry's kind (see <see cref="EncounterRules"/>).</summary>
public sealed record EntryFields(string? Name, string? Detail, string? Reaction, string? Severity, string? Dose, string? Frequency);

public sealed record EntryView(
    Guid Id, string Kind, string Name, string? Detail, string? Reaction, string? Severity, string? Dose, string? Frequency,
    DateTimeOffset CreatedAtUtc, Guid? CreatedByUserId, DateTimeOffset? UpdatedAtUtc);

/// <summary>One of the four sections of an encounter: its active entries and whether the clinician has recorded something, reviewed it as none reported, or not yet addressed it.</summary>
public sealed record SectionView(string Kind, string Status, IReadOnlyList<EntryView> Entries, DateTimeOffset? ReviewedAtUtc, Guid? ReviewedByUserId);

public sealed record AddendumView(Guid Id, string Text, DateTimeOffset CreatedAtUtc, Guid? CreatedByUserId);

public sealed record EventView(string EventType, Guid? ActorUserId, DateTimeOffset OccurredAtUtc, string? Detail);

/// <summary>An encounter exactly as stored, with its sections, addenda (oldest first) and history. <see cref="RowVersion"/> is what a writer echoes back.</summary>
public sealed record EncounterDetail(
    Guid Id, Guid PatientId, Guid? AppointmentId, DateTimeOffset EncounterAtUtc, string Status,
    bool IsComplete, IReadOnlyList<string> MissingSections, IReadOnlyList<SectionView> Sections,
    IReadOnlyList<AddendumView> Addenda, IReadOnlyList<EventView> History,
    DateTimeOffset CreatedAtUtc, Guid? CreatedByUserId, DateTimeOffset? FinalizedAtUtc, Guid? FinalizedByUserId, string RowVersion);

public sealed record EncounterSummary(Guid Id, Guid? AppointmentId, DateTimeOffset EncounterAtUtc, string Status, bool IsComplete, int EntryCount, int AddendumCount);

public static class SectionStatuses
{
    /// <summary>At least one active entry.</summary>
    public const string Recorded = "Recorded";
    /// <summary>Reviewed, and the clinician states there is nothing to record.</summary>
    public const string NoneReported = "NoneReported";
    /// <summary>Neither - the documentation is incomplete until the clinician records something or marks it none reported.</summary>
    public const string Empty = "Empty";
}

/// <summary>Limits and the per-kind field rules for entries; one place so the service, the API and the tests agree.</summary>
public static class EncounterRules
{
    public const int NameMax = 200;
    public const int DetailMax = 1000;
    public const int ReactionMax = 200;
    public const int DoseMax = 100;
    public const int FrequencyMax = 100;
    public const int AddendumMax = 4000;
    public const int KeyMax = 100;

    public static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Validates and normalises the fields for an entry of <paramref name="kind"/>; throws a 400 naming every field that needs attention.</summary>
    public static EntryFields Validate(string kind, EntryFields input)
    {
        var errors = new Dictionary<string, string>();
        var f = new EntryFields(Clean(input.Name), Clean(input.Detail), Clean(input.Reaction), Clean(input.Severity), Clean(input.Dose), Clean(input.Frequency));
        if (f.Name is null) errors["name"] = "A name is required.";
        else if (f.Name.Length > NameMax) errors["name"] = $"Keep the name to {NameMax} characters or fewer.";
        if (f.Detail is { Length: > DetailMax }) errors["detail"] = $"Keep the detail to {DetailMax} characters or fewer.";

        if (kind == EncounterEntryKinds.Allergy)
        {
            if (f.Reaction is { Length: > ReactionMax }) errors["reaction"] = $"Keep the reaction to {ReactionMax} characters or fewer.";
            if (f.Severity is not null && !EncounterSeverities.All.Contains(f.Severity)) errors["severity"] = "Severity must be Mild, Moderate or Severe, or left blank when it is not known.";
        }
        else
        {
            if (f.Reaction is not null) errors["reaction"] = "A reaction applies to allergies only.";
            if (f.Severity is not null) errors["severity"] = "A severity applies to allergies only.";
        }

        if (kind == EncounterEntryKinds.Medication)
        {
            if (f.Dose is { Length: > DoseMax }) errors["dose"] = $"Keep the dose to {DoseMax} characters or fewer.";
            if (f.Frequency is { Length: > FrequencyMax }) errors["frequency"] = $"Keep the frequency to {FrequencyMax} characters or fewer.";
        }
        else
        {
            if (f.Dose is not null) errors["dose"] = "A dose applies to medications only.";
            if (f.Frequency is not null) errors["frequency"] = "A frequency applies to medications only.";
        }

        if (errors.Count > 0) throw new ClinicalException("validation_failed", "Some fields need attention.", 400, errors);
        return f;
    }

    public static string RequireKind(string? kind)
    {
        if (kind is null || !EncounterEntryKinds.All.Contains(kind))
            throw new ClinicalException("validation_failed", "That section does not exist.", 400,
                new Dictionary<string, string> { ["kind"] = "Choose MedicalHistory, DentalHistory, Allergy or Medication." });
        return kind;
    }

    /// <summary>The words used for a section in messages and in the (PHI-free) history.</summary>
    public static string Label(string kind) => kind switch
    {
        EncounterEntryKinds.MedicalHistory => "Medical history",
        EncounterEntryKinds.DentalHistory => "Dental history",
        EncounterEntryKinds.Allergy => "Allergies",
        EncounterEntryKinds.Medication => "Medications",
        _ => kind,
    };
}
