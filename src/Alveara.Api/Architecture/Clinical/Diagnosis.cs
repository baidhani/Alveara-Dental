namespace Alveara.Api.Architecture.Clinical;

public static class DiagnosisStatuses
{
    public const string Active = "Active";
    public const string Withdrawn = "Withdrawn";
    public static readonly IReadOnlyList<string> All = [Active, Withdrawn];
}

public static class DiagnosisChangeTypes
{
    public const string Recorded = "Recorded";
    public const string Corrected = "Corrected";
    public const string Withdrawn = "Withdrawn";
    public static readonly IReadOnlyList<string> All = [Recorded, Corrected, Withdrawn];
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
    /// <summary><see cref="DiagnosisStatuses.Active"/> or <see cref="DiagnosisStatuses.Withdrawn"/>.</summary>
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
    public required string Status { get; set; }
    public string? Reason { get; set; }
    public Guid? ActorUserId { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
}
