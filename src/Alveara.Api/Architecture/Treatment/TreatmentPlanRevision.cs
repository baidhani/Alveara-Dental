namespace Alveara.Api.Architecture.Treatment;

/// <summary>Where a patient stands on one proposed procedure. Nothing is recorded until someone decides, so no state row means Pending.</summary>
public static class ItemAcceptance
{
    public const string Pending = "Pending";
    public const string Accepted = "Accepted";
    public const string Declined = "Declined";
    public static readonly IReadOnlyList<string> All = [Pending, Accepted, Declined];
}

/// <summary>Whether care for an accepted procedure has been put on the calendar. A state and an optional planned date only: nothing here talks to the appointment module.</summary>
public static class ItemScheduling
{
    public const string Unscheduled = "Unscheduled";
    public const string Scheduled = "Scheduled";
    public static readonly IReadOnlyList<string> All = [Unscheduled, Scheduled];
}

/// <summary>Whether an accepted procedure has been done. Completed is final.</summary>
public static class ItemCompletion
{
    public const string NotStarted = "NotStarted";
    public const string Completed = "Completed";
    public static readonly IReadOnlyList<string> All = [NotStarted, Completed];
}

/// <summary>How a diagnosis's old free-text treatment-plan reference was tied to a real plan: by the plan's reference code, by the plan's id written as the reference, or by a person choosing the plan.</summary>
public static class ResolutionMethods
{
    public const string ReferenceCode = "ReferenceCode";
    public const string PlanId = "PlanId";
    public const string Chosen = "Chosen";
    public static readonly IReadOnlyList<string> All = [ReferenceCode, PlanId, Chosen];
}

/// <summary>
/// ALV-015-C01: one saved version of a plan's content (title, clinical rationale, the patient-facing explanation, the phases and where each procedure sits). A change to the content never edits a revision:
/// it copies the latest one and appends the next, so every earlier version stays readable and two versions can be compared. Append-only (a trigger refuses any edit or delete).
/// <see cref="ChangeType"/> and <see cref="EventNumber"/> say which history entry produced it.
/// </summary>
public class TreatmentPlanRevision
{
    public Guid Id { get; set; }
    public Guid PlanId { get; set; }
    public Guid PatientId { get; set; }
    /// <summary>1, 2, 3... unique per plan.</summary>
    public int RevisionNumber { get; set; }
    /// <summary>The history entry that produced this revision (Created, ItemAdded, ItemWithdrawn, Renamed or Revised).</summary>
    public required string ChangeType { get; set; }
    public int EventNumber { get; set; }
    public required string Title { get; set; }
    /// <summary>Why the clinician proposes this care; for staff.</summary>
    public string? ClinicalRationale { get; set; }
    /// <summary>The same care in words a patient can follow; shown on the patient copy.</summary>
    public string? PatientExplanation { get; set; }
    /// <summary>The reason a person gave for an explicit revision; null for the automatic ones.</summary>
    public string? Reason { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public Guid CreatedByUserId { get; set; }
}

/// <summary>A phase (a visit or stage of care) as one revision laid it out. Phases belong to a revision, so renaming or removing one in a later revision never changes an earlier one.</summary>
public class TreatmentPlanRevisionPhase
{
    public Guid Id { get; set; }
    public Guid RevisionId { get; set; }
    public Guid PlanId { get; set; }
    public Guid PatientId { get; set; }
    /// <summary>1, 2, 3... unique within the revision.</summary>
    public int PhaseNumber { get; set; }
    public required string Name { get; set; }
    /// <summary>Free words about the planned visit, e.g. "after the gums have healed".</summary>
    public string? PlannedVisit { get; set; }
    public DateOnly? TargetDate { get; set; }
}

/// <summary>Which procedures a revision contains and the phase each one sits in (none means not yet placed). A withdrawn procedure stays in the revisions it was part of and is marked withdrawn in the ones after.</summary>
public class TreatmentPlanRevisionItem
{
    public Guid Id { get; set; }
    public Guid RevisionId { get; set; }
    public Guid PlanId { get; set; }
    public Guid PatientId { get; set; }
    public Guid ItemId { get; set; }
    public int? PhaseNumber { get; set; }
    public bool IsWithdrawn { get; set; }
}

/// <summary>
/// ALV-015-C01: the full state of one proposed procedure after one change: whether it was accepted or declined (and by whom), whether it is scheduled, and whether it is completed. Every change appends a new row
/// with the next <see cref="Sequence"/>, so the history is never rewritten; the current state is the latest row, and an item with no row is Pending, Unscheduled and NotStarted. Append-only.
/// </summary>
public class TreatmentPlanItemState
{
    public Guid Id { get; set; }
    public Guid PlanId { get; set; }
    public Guid PatientId { get; set; }
    public Guid ItemId { get; set; }
    /// <summary>1, 2, 3... unique per item.</summary>
    public int Sequence { get; set; }
    public required string Acceptance { get; set; }
    public required string Scheduling { get; set; }
    public required string Completion { get; set; }
    /// <summary>Set only while the item is Scheduled.</summary>
    public DateOnly? PlannedDate { get; set; }
    /// <summary>Required when declining; optional otherwise.</summary>
    public string? Reason { get; set; }
    /// <summary>Who gave the answer (the patient, a guardian), as the staff member recorded it. The person who recorded it is <see cref="RecordedByUserId"/>.</summary>
    public string? DecidedByName { get; set; }
    /// <summary>The plan revision on screen when this was recorded, so it is clear what was accepted.</summary>
    public int RevisionNumber { get; set; }
    public Guid RecordedByUserId { get; set; }
    public DateTimeOffset RecordedAtUtc { get; set; }
}

/// <summary>
/// ALV-015-C01: a diagnosis's old free-text treatment-plan reference tied to a real plan of the same patient. The diagnosis row and its history are never touched: this row sits beside them, keeps the
/// original text, and is why the diagnosis is shown as resolved. One per diagnosis, so resolving twice changes nothing. Append-only.
/// </summary>
public class DiagnosisPlanResolution
{
    public Guid Id { get; set; }
    public Guid DiagnosisId { get; set; }
    public Guid PatientId { get; set; }
    public Guid PlanId { get; set; }
    /// <summary>The reference text the diagnosis held when it was resolved (the database checks it equals the diagnosis's reference).</summary>
    public required string ReferenceText { get; set; }
    /// <summary>One of <see cref="ResolutionMethods.All"/>.</summary>
    public required string Method { get; set; }
    public DateTimeOffset ResolvedAtUtc { get; set; }
    public Guid ResolvedByUserId { get; set; }
}
