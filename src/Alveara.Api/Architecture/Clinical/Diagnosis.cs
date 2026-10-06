namespace Alveara.Api.Architecture.Clinical;

public static class DiagnosisStatuses
{
    public const string Active = "Active";
    /// <summary>ALV-013-C01: the condition is no longer present. Reversible (it can be reactivated), unlike Withdrawn.</summary>
    public const string Resolved = "Resolved";
    public const string Withdrawn = "Withdrawn";
    public static readonly IReadOnlyList<string> All = [Active, Resolved, Withdrawn];
}

public static class DiagnosisChangeTypes
{
    public const string Recorded = "Recorded";
    public const string Corrected = "Corrected";
    public const string Withdrawn = "Withdrawn";
    /// <summary>ALV-013-C01: a change to the structure of the diagnosis (coding, source, region), as distinct from fixing its label, tooth or notes.</summary>
    public const string Amended = "Amended";
    public const string Resolved = "Resolved";
    public const string Reactivated = "Reactivated";
    public static readonly IReadOnlyList<string> All = [Recorded, Corrected, Withdrawn, Amended, Resolved, Reactivated];
}

/// <summary>
/// ALV-013-C01: the coding systems a diagnosis can name. These are only the NAMES of systems; no terminology content is bundled, and a code is never checked against a code set. A diagnosis needs no
/// coding at all; when it has one, the system and the code come together.
/// </summary>
public static class DiagnosisCodingSystems
{
    public const string Icd10Cm = "ICD-10-CM";
    public const string Snodent = "SNODENT";
    public const string Local = "Local";
    public static readonly IReadOnlyList<string> All = [Icd10Cm, Snodent, Local];
}

/// <summary>ALV-013-C01: where a diagnosis came from. Kept so that data imported or mapped later still says so.</summary>
public static class DiagnosisSources
{
    public const string Manual = "Manual";
    public const string Imported = "Imported";
    public const string Mapped = "Mapped";
    public static readonly IReadOnlyList<string> All = [Manual, Imported, Mapped];
}

/// <summary>ALV-013-C01: the oral regions a diagnosis can be about when it is not about one tooth.</summary>
public static class DiagnosisRegions
{
    public static readonly IReadOnlyList<string> All = ["FullMouth", "UpperArch", "LowerArch", "UpperRight", "UpperLeft", "LowerRight", "LowerLeft", "SoftTissue", "Tmj"];
}

/// <summary>ALV-013-C01: the records that really exist and that a diagnosis can be linked to. A treatment plan is not one of them: it is a forward reference on the diagnosis itself.</summary>
public static class DiagnosisLinkTypes
{
    public const string Finding = "Finding";
    public const string PerioExam = "PerioExam";
    public static readonly IReadOnlyList<string> All = [Finding, PerioExam];
}

/// <summary>A link from a diagnosis to an odontogram finding or a periodontal chart of the same patient. Append-only; unique per diagnosis, type and target.</summary>
public class DiagnosisLink
{
    public Guid Id { get; set; }
    public Guid DiagnosisId { get; set; }
    public Guid PatientId { get; set; }
    /// <summary>One of <see cref="DiagnosisLinkTypes.All"/>.</summary>
    public required string LinkType { get; set; }
    public Guid TargetId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public Guid? CreatedByUserId { get; set; }
}

/// <summary>
/// STORY-013: one structured diagnosis, linked to its <b>patient</b> and the <b>encounter</b> it was made in (both required, and the encounter must be the same patient's: the database refuses
/// anything else, and neither link ever changes). A diagnosis is never deleted: a wrong entry is corrected (every correction appends a <see cref="DiagnosisVersion"/>) or withdrawn with a reason.
///
/// <see cref="TreatmentPlanReference"/> is a FORWARD reference: treatment plans do not exist yet, so it is an optional, opaque, normalized piece of text kept beside the diagnosis, in the style of
/// the odontogram's and periodontal chart's links. It is not a foreign key, is never looked up and says nothing about whether a plan exists; a later story gives plans real identities and
/// reconciles references. It is kept through every correction and withdrawal and appears in every version. <see cref="RowVersion"/> refuses a stale edit.
/// </summary>
public class Diagnosis
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    public Guid EncounterId { get; set; }
    /// <summary>The caller's key for the save that recorded this diagnosis; unique per patient, so a retry returns the diagnosis instead of creating a second one.</summary>
    public required string IdempotencyKey { get; set; }
    public required string Label { get; set; }
    /// <summary>One of the odontogram's FDI keys, or null when the diagnosis is not about one tooth.</summary>
    public string? ToothKey { get; set; }
    public string? Notes { get; set; }
    public string? TreatmentPlanReference { get; set; }
    /// <summary>ALV-013-C01: optional coding; <see cref="CodingSystem"/> and <see cref="Code"/> are both set or both null.</summary>
    public string? CodingSystem { get; set; }
    public string? Code { get; set; }
    /// <summary>One of <see cref="DiagnosisSources.All"/>.</summary>
    public string Source { get; set; } = DiagnosisSources.Manual;
    public string? SourceNote { get; set; }
    /// <summary>One of <see cref="DiagnosisRegions.All"/>, or null; never set together with a tooth.</summary>
    public string? RegionKey { get; set; }
    /// <summary>One of <see cref="DiagnosisStatuses.All"/>.</summary>
    public required string Status { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset? UpdatedAtUtc { get; set; }
    public Guid? UpdatedByUserId { get; set; }
    /// <summary>Set exactly when Status is Withdrawn (a database check keeps them in step).</summary>
    public DateTimeOffset? WithdrawnAtUtc { get; set; }
    public Guid? WithdrawnByUserId { get; set; }
    public string? WithdrawnReason { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>Append-only history of a <see cref="Diagnosis"/>: a full snapshot after every record, correction and withdrawal, with who, when and why. A trigger refuses any edit or delete.</summary>
public class DiagnosisVersion
{
    public Guid Id { get; set; }
    public Guid DiagnosisId { get; set; }
    public Guid PatientId { get; set; }
    public int VersionNumber { get; set; }
    /// <summary>One of <see cref="DiagnosisChangeTypes.All"/>.</summary>
    public required string ChangeType { get; set; }
    public required string Label { get; set; }
    public string? ToothKey { get; set; }
    public string? Notes { get; set; }
    public string? TreatmentPlanReference { get; set; }
    public string? CodingSystem { get; set; }
    public string? Code { get; set; }
    public string Source { get; set; } = DiagnosisSources.Manual;
    public string? SourceNote { get; set; }
    public string? RegionKey { get; set; }
    public required string Status { get; set; }
    public string? Reason { get; set; }
    public Guid? ActorUserId { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
}
