namespace Alveara.Api.Architecture.ExternalCases;

/// <summary>ALV-009-C02: what a referral and a lab case have in common, so the status moves, the closure and the history are written once.</summary>
public interface IExternalCase
{
    Guid Id { get; }
    Guid PatientId { get; }
    Guid PartyId { get; set; }
    string Status { get; set; }
    DateOnly? SentOn { get; set; }
    DateOnly? ReceivedOn { get; set; }
    DateOnly? DueOn { get; set; }
    string? Notes { get; set; }
    DateTimeOffset UpdatedAtUtc { get; set; }
    DateTimeOffset? ClosedAtUtc { get; set; }
    Guid? ClosedByUserId { get; set; }
    string? ClosureNote { get; set; }
    byte[] RowVersion { get; set; }
}

/// <summary>ALV-009-C02: the event kinds of a referral or lab case's append-only history. Every one has a person (the system never changes a case; it only makes and closes the case's follow-up task).</summary>
public static class ExternalCaseEventKinds
{
    public const string Created = "Created";
    public const string Edited = "Edited";
    public const string StatusChanged = "StatusChanged";
    public const string DocumentLinked = "DocumentLinked";
    public const string DocumentUnlinked = "DocumentUnlinked";
    public static readonly IReadOnlyList<string> All = [Created, Edited, StatusChanged, DocumentLinked, DocumentUnlinked];
}

/// <summary>
/// An outside party the practice sends work to: a referral provider (a specialist, a clinic) or a lab vendor. Never deleted (the database refuses it); a party stops being offered by being made inactive, and the
/// cases already sent to it keep it. <see cref="Kind"/> never changes. A case must name an active party of the matching kind when it is made.
/// </summary>
public class ExternalParty
{
    public Guid Id { get; set; }
    /// <summary>One of <see cref="ExternalPartyKinds"/>.</summary>
    public required string Kind { get; set; }
    public required string Name { get; set; }
    /// <summary>Phone, e-mail, address or whatever the practice uses to reach them: plain text, never interpreted.</summary>
    public string? ContactDetails { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>
/// One referral of a patient to an outside provider: why, to whom, when it went out and when the provider's report came back, and when the practice will chase it. Moves through
/// <see cref="ExternalCaseRules"/>; the database holds the same status set and the shape each status needs (a sent case has a sent date, a received or completed one a received date, a final one who closed it and when,
/// a cancelled one its reason). Never deleted; a final case never changes. Documents are linked, not copied (<see cref="ExternalCaseDocument"/>).
/// </summary>
public class ReferralCase : IExternalCase
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    public Guid PartyId { get; set; }
    public required string Reason { get; set; }
    public required string Status { get; set; }
    public DateOnly? SentOn { get; set; }
    public DateOnly? ReceivedOn { get; set; }
    /// <summary>The practice-local date the practice will chase this; its follow-up task is due that day.</summary>
    public DateOnly? DueOn { get; set; }
    /// <summary>The visit it came out of, when it did (the database refuses an encounter of another patient).</summary>
    public Guid? EncounterId { get; set; }
    public string? Notes { get; set; }
    public required string IdempotencyKey { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public DateTimeOffset? ClosedAtUtc { get; set; }
    public Guid? ClosedByUserId { get; set; }
    /// <summary>For a cancellation the reason (required); for a completion an optional note.</summary>
    public string? ClosureNote { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>
/// One piece of work sent to a dental laboratory: what, for which tooth and treatment, to which vendor, when it went out, when it is due back and when it arrived. Linked to the planned procedure through the
/// treatment-plan item (the item carries the procedure and tooth; the case keeps its own tooth key so a case for a tooth not in a plan is possible). Same shape rules as <see cref="ReferralCase"/>, plus the lab
/// statuses.
/// </summary>
public class LabCase : IExternalCase
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    public Guid PartyId { get; set; }
    /// <summary>What is being made, in a few words ("Crown, zirconia").</summary>
    public required string Title { get; set; }
    public required string Status { get; set; }
    public DateOnly? SentOn { get; set; }
    public DateOnly? ReceivedOn { get; set; }
    public DateOnly? DueOn { get; set; }
    /// <summary>The tooth it is for, as its two-digit FDI key.</summary>
    public string? ToothKey { get; set; }
    /// <summary>The treatment-plan item it is for (the database refuses an item of another patient).</summary>
    public Guid? TreatmentPlanItemId { get; set; }
    public string? Material { get; set; }
    public string? Shade { get; set; }
    public string? Notes { get; set; }
    public required string IdempotencyKey { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public DateTimeOffset? ClosedAtUtc { get; set; }
    public Guid? ClosedByUserId { get; set; }
    public string? ClosureNote { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>
/// One entry of a case's append-only history (the database refuses any change or delete): created, edited, a status change with the status before and after and the note, a document linked or unlinked. Exactly one of
/// the two case ids is set. The note is free text, so it lives here and never in the audit log.
/// </summary>
public class ExternalCaseEvent
{
    public Guid Id { get; set; }
    public Guid? ReferralCaseId { get; set; }
    public Guid? LabCaseId { get; set; }
    /// <summary>One of <see cref="ExternalCaseEventKinds"/>.</summary>
    public required string Kind { get; set; }
    public string? FromStatus { get; set; }
    public string? ToStatus { get; set; }
    public Guid? DocumentId { get; set; }
    public Guid ActorUserId { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
    public string? Note { get; set; }
}

/// <summary>
/// A link from a case to an existing patient document: the document stays where it is and is shown in the case; nothing is copied. Exactly one of the two case ids is set, a document is linked to a case at most once,
/// and the database refuses a document of another patient than the case's.
/// </summary>
public class ExternalCaseDocument
{
    public Guid Id { get; set; }
    public Guid? ReferralCaseId { get; set; }
    public Guid? LabCaseId { get; set; }
    public Guid DocumentId { get; set; }
    public Guid LinkedByUserId { get; set; }
    public DateTimeOffset LinkedAtUtc { get; set; }
}
