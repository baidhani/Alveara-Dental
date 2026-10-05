namespace Alveara.Api.Architecture.Periodontal;

/// <summary>
/// STORY-012: one periodontal chart taken for a patient at one moment: who recorded it and when. A saved chart is never edited or deleted (triggers refuse both); a correction is a new
/// chart, so the earlier one stays as the record of what was found then. <see cref="IdempotencyKey"/> makes a retried save create the same chart once.
/// </summary>
public class PerioExam
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    /// <summary>Chosen by the caller (one per intended save). Unique per patient, so a retry or a double click cannot create a second chart.</summary>
    public required string IdempotencyKey { get; set; }
    public DateTimeOffset RecordedAtUtc { get; set; }
    /// <summary>Always set: every chart is attributable to the person who recorded it.</summary>
    public Guid RecordedByUserId { get; set; }
    /// <summary>How many site readings the chart holds (kept for quick display; the readings are the truth).</summary>
    public int ReadingCount { get; set; }
}

/// <summary>One probed site on one tooth within a <see cref="PerioExam"/>. Whole millimetres, 0 to 15 (<see cref="PerioRules"/>); the database checks the same bounds.</summary>
public class PerioReading
{
    public Guid Id { get; set; }
    public Guid ExamId { get; set; }
    /// <summary>One of the 32 permanent FDI keys.</summary>
    public required string ToothKey { get; set; }
    /// <summary>One of <see cref="PerioRules.Sites"/>.</summary>
    public required string Site { get; set; }
    public byte ProbingDepthMm { get; set; }
    public byte RecessionMm { get; set; }
    public bool Bleeding { get; set; }
}
