namespace Alveara.Api.Architecture.Clinical;

/// <summary>
/// ALV-005-C01: free-text clinical note for one section of an encounter (SOAP, progress or treatment). One row per encounter and section; the body can be empty
/// (a section a template asked for, not yet written). <see cref="Required"/> is copied from the template when it is applied, so editing a template later never
/// changes what an existing encounter needs. Editable only while the encounter is a draft and not signed; database triggers refuse a change after it is finalized.
/// </summary>
public class EncounterNote
{
    public Guid Id { get; set; }
    public Guid EncounterId { get; set; }
    /// <summary>One of <see cref="NoteSections"/>.</summary>
    public required string Section { get; set; }
    public string Body { get; set; } = "";
    public bool Required { get; set; }
    /// <summary>The template's starter text for this section, copied when the template was applied. A required note still showing exactly this text has not been written yet.</summary>
    public string? StarterText { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset? UpdatedAtUtc { get; set; }
    public Guid? UpdatedByUserId { get; set; }
}

/// <summary>
/// A set of vital signs measured for a patient during an encounter, with the time they were measured. Metric units. At least one value is present; a reading entered
/// by mistake is VOIDED (kept, with who and why), never edited or deleted. The client key makes a retried save return the first reading.
/// </summary>
public class EncounterVitals
{
    public Guid Id { get; set; }
    public Guid EncounterId { get; set; }
    public Guid PatientId { get; set; }
    public DateTimeOffset MeasuredAtUtc { get; set; }
    public int? SystolicMmHg { get; set; }
    public int? DiastolicMmHg { get; set; }
    public int? PulseBpm { get; set; }
    public int? RespirationsPerMinute { get; set; }
    public decimal? TemperatureC { get; set; }
    public int? OxygenSaturationPercent { get; set; }
    public decimal? WeightKg { get; set; }
    public decimal? HeightCm { get; set; }
    public string? Note { get; set; }
    public required string ClientKey { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset? VoidedAtUtc { get; set; }
    public Guid? VoidedByUserId { get; set; }
    public string? VoidReason { get; set; }
}

/// <summary>
/// A configurable note template, owned by the clinical-documentation domain (not generic practice configuration). It names which note sections an encounter note
/// has, which are required before signing, and optional starter text. Deactivated, never deleted; applying one to an encounter copies what it says at that moment.
/// </summary>
public class NoteTemplate
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset? UpdatedAtUtc { get; set; }
    public Guid? UpdatedByUserId { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public class NoteTemplateSection
{
    public Guid Id { get; set; }
    public Guid TemplateId { get; set; }
    /// <summary>One of <see cref="NoteSections"/>.</summary>
    public required string Section { get; set; }
    public int SortOrder { get; set; }
    public bool Required { get; set; }
    public string? StarterText { get; set; }
}

/// <summary>The note sections an encounter can carry, in the order they are presented: SOAP, then progress and treatment notes.</summary>
public static class NoteSections
{
    public const string Subjective = "Subjective";
    public const string Objective = "Objective";
    public const string Assessment = "Assessment";
    public const string Plan = "Plan";
    public const string Progress = "Progress";
    public const string Treatment = "Treatment";
    public static readonly IReadOnlyList<string> All = [Subjective, Objective, Assessment, Plan, Progress, Treatment];

    public static string Label(string section) => section switch
    {
        Subjective => "Subjective", Objective => "Objective", Assessment => "Assessment", Plan => "Plan",
        Progress => "Progress note", Treatment => "Treatment note", _ => section,
    };
}
