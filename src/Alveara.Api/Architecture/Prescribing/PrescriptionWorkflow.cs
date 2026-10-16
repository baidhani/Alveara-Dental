namespace Alveara.Api.Architecture.Prescribing;

/// <summary>
/// ALV-016-C01: a prescription being written. The only mutable record of the prescription workflow, and only while its <see cref="Status"/> is Draft: the database refuses any change to a finalized or discarded
/// draft, and any deletion. It holds what the prescriber has typed (the instructions, quantity and refills may still be empty), a snapshot of the allergy and medication lists as the prescriber last saw them
/// (so finalization can tell when they changed), and, for an amendment, the prescription it will replace. <see cref="RowVersion"/> refuses a stale edit.
/// </summary>
public class PrescriptionDraft
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    public required string Medication { get; set; }
    public decimal DoseValue { get; set; }
    public required string DoseUnit { get; set; }
    public required string Frequency { get; set; }
    public string? Instructions { get; set; }
    public decimal? QuantityValue { get; set; }
    public string? QuantityUnit { get; set; }
    public int? Refills { get; set; }
    /// <summary>The prescriber's own words about an allergy match or an allergy list that is not established; carried into the prescription on finalization.</summary>
    public string? ReviewNote { get; set; }
    /// <summary>The prescription this draft will replace when finalized (an amendment); null for a new prescription.</summary>
    public Guid? SupersedesPrescriptionId { get; set; }
    /// <summary>SHA-256 of <see cref="SafetySnapshot"/>: what the allergy and medication lists looked like when the prescriber last looked (<see cref="SafetySnapshot"/> in the rules).</summary>
    public required string SafetyFingerprint { get; set; }
    /// <summary>The snapshot's lines, one per line, so a changed list can be described rather than only detected.</summary>
    public required string SafetySnapshot { get; set; }
    /// <summary>One of <see cref="DraftStatuses"/>.</summary>
    public required string Status { get; set; }
    public Guid? FinalizedPrescriptionId { get; set; }
    public string? DiscardedReason { get; set; }
    /// <summary>The caller's key for creating this draft; unique per patient, so a retry cannot create it twice.</summary>
    public required string CreateKey { get; set; }
    public required string CreateHash { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public Guid UpdatedByUserId { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>
/// ALV-016-C01: what finalizing added to a prescription: the instructions, the quantity, the refills, the safety snapshot the prescriber confirmed, the draft it came from and, for an amendment, the prescription it
/// replaced. One per finalized prescription, immutable (the database refuses any change or deletion). A prescription saved by STORY-016 has no detail row.
/// </summary>
public class PrescriptionDetail
{
    public Guid PrescriptionId { get; set; }
    public Guid? DraftId { get; set; }
    public Guid? SupersedesPrescriptionId { get; set; }
    public required string Instructions { get; set; }
    public decimal QuantityValue { get; set; }
    public required string QuantityUnit { get; set; }
    public int Refills { get; set; }
    public required string SafetyFingerprint { get; set; }
    public required string SafetySnapshot { get; set; }
    public Guid FinalizedByUserId { get; set; }
    public DateTimeOffset FinalizedAtUtc { get; set; }
}

/// <summary>
/// ALV-016-C01: one thing that happened to a finalized prescription: it was finalized, cancelled, discontinued or superseded by an amendment, who did it, when and (for a cancellation or discontinuation) why.
/// Append-only. A prescription has one Finalized event and at most one ending event, so its status can only move from Active to one ended state.
/// </summary>
public class PrescriptionEvent
{
    public Guid Id { get; set; }
    public Guid PrescriptionId { get; set; }
    /// <summary>One of <see cref="PrescriptionEventTypes"/>.</summary>
    public required string EventType { get; set; }
    public string? Reason { get; set; }
    /// <summary>For a Superseded event: the prescription that replaced this one.</summary>
    public Guid? SupersededByPrescriptionId { get; set; }
    public Guid ActorUserId { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
    public Guid CorrelationId { get; set; }
}
