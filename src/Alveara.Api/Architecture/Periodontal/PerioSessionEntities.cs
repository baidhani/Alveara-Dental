namespace Alveara.Api.Architecture.Periodontal;

/// <summary>Where a chart session is: being entered (Draft), saved as an immutable chart (Finalized), or discarded (Abandoned).</summary>
public static class PerioSessionStatuses
{
    public const string Draft = "Draft";
    public const string Finalized = "Finalized";
    public const string Abandoned = "Abandoned";
    public static readonly IReadOnlyList<string> All = [Draft, Finalized, Abandoned];
}

/// <summary>What a finalized chart can be linked to, by reference (the records do not all exist yet, so the reference is opaque text).</summary>
public static class PerioLinkTypes
{
    public const string Diagnosis = "Diagnosis";
    public const string TreatmentPlan = "TreatmentPlan";
    public const string Encounter = "Encounter";
    public const string HistoryEntry = "HistoryEntry";
    public static readonly IReadOnlyList<string> All = [Diagnosis, TreatmentPlan, Encounter, HistoryEntry];
}

/// <summary>
/// ALV-012-C01: one chart being entered. While it is a Draft its entries can be saved as the person goes (a tooth at a time), changed and cleared; <see cref="RowVersion"/> moves with every change so a
/// stale edit is refused. Finalizing validates the whole chart and creates the immutable <see cref="PerioExam"/> (named by <see cref="ExamId"/>) in one transaction; after that, or after it is abandoned,
/// the session and its entries can never change (triggers). At most one Draft per patient, so two people cannot chart the same mouth at once without noticing.
/// </summary>
public class PerioSession
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    /// <summary>One of <see cref="PerioSessionStatuses.All"/>.</summary>
    public required string Status { get; set; }
    public DateTimeOffset StartedAtUtc { get; set; }
    public Guid StartedByUserId { get; set; }
    public DateTimeOffset? UpdatedAtUtc { get; set; }
    public Guid? UpdatedByUserId { get; set; }
    /// <summary>When it was finalized or abandoned, and by whom; both null while it is a Draft (a database check keeps them in step with <see cref="Status"/>).</summary>
    public DateTimeOffset? ClosedAtUtc { get; set; }
    public Guid? ClosedByUserId { get; set; }
    /// <summary>The finalized chart this session became; set exactly when Status is Finalized.</summary>
    public Guid? ExamId { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>One site entered in a draft session. Same ranges as <see cref="PerioReading"/>; editable only while the session is a Draft.</summary>
public class PerioSessionReading
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public required string ToothKey { get; set; }
    public required string Site { get; set; }
    public byte ProbingDepthMm { get; set; }
    public byte RecessionMm { get; set; }
    public bool Bleeding { get; set; }
    public bool? Suppuration { get; set; }
    public bool? Plaque { get; set; }
}

/// <summary>What is entered about a whole tooth in a draft session: mobility, furcation, or that it is not charted. Editable only while the session is a Draft.</summary>
public class PerioSessionTooth
{
    public Guid Id { get; set; }
    public Guid SessionId { get; set; }
    public required string ToothKey { get; set; }
    public byte? Mobility { get; set; }
    public byte? Furcation { get; set; }
    public bool Excluded { get; set; }
}

/// <summary>What a finalized chart records about a whole tooth. Never edited or deleted (a trigger refuses both).</summary>
public class PerioToothRecord
{
    public Guid Id { get; set; }
    public Guid ExamId { get; set; }
    public required string ToothKey { get; set; }
    public byte? Mobility { get; set; }
    public byte? Furcation { get; set; }
    public bool Excluded { get; set; }
}

/// <summary>A link from a finalized chart to a diagnosis, treatment plan, encounter or history entry, by reference. Append-only; unique per chart, type and reference.</summary>
public class PerioExamLink
{
    public Guid Id { get; set; }
    public Guid ExamId { get; set; }
    public Guid PatientId { get; set; }
    public required string LinkType { get; set; }
    public required string Reference { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public Guid? CreatedByUserId { get; set; }
}
