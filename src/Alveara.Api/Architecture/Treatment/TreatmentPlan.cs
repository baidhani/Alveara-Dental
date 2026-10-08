namespace Alveara.Api.Architecture.Treatment;

/// <summary>A plan is <c>Proposed</c> (the clinician's proposal) until it is <c>Withdrawn</c> with a reason. Withdrawn is final; later stories add the states that follow acceptance.</summary>
public static class TreatmentPlanStatuses
{
    public const string Proposed = "Proposed";
    public const string Withdrawn = "Withdrawn";
    public static readonly IReadOnlyList<string> All = [Proposed, Withdrawn];
}

/// <summary>What a history entry records. Every change to a plan is one of these, appended, never edited.</summary>
public static class TreatmentPlanChanges
{
    public const string Created = "Created";
    public const string ItemAdded = "ItemAdded";
    public const string ItemWithdrawn = "ItemWithdrawn";
    public const string Renamed = "Renamed";
    public const string Withdrawn = "Withdrawn";
    public static readonly IReadOnlyList<string> All = [Created, ItemAdded, ItemWithdrawn, Renamed, Withdrawn];
}

/// <summary>The words that must accompany every fee shown for a plan. The fee is the practice's catalog fee on the day the item was proposed; it is not insurer-adjudicated coverage and not a guaranteed patient cost.</summary>
public static class TreatmentPlanEstimate
{
    public const string Label = "Practice fee estimate: the practice's catalog fee when each item was proposed. It is not an insurance estimate and not a guaranteed patient cost.";
}

/// <summary>
/// STORY-015: a treatment plan for one patient: the authoritative identity a plan has from now on (diagnoses' opaque <c>TreatmentPlanReference</c> text is not it; reconciling those references
/// is ALV-015-C01's job). A plan is never deleted: a plan that should not stand is withdrawn with a reason, and every change appends a <see cref="TreatmentPlanEvent"/>.
/// The patient and the origin never change (a database trigger refuses it). <see cref="RowVersion"/> refuses a stale edit.
/// </summary>
public class TreatmentPlan
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    /// <summary>The caller's key for the save that created this plan; unique per patient, so a retry returns the plan instead of creating a second one.</summary>
    public required string IdempotencyKey { get; set; }
    public required string Title { get; set; }
    /// <summary>One of <see cref="TreatmentPlanStatuses.All"/>.</summary>
    public required string Status { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset? UpdatedAtUtc { get; set; }
    public Guid? UpdatedByUserId { get; set; }
    /// <summary>Set exactly when Status is Withdrawn (a database check keeps them in step).</summary>
    public DateTimeOffset? WithdrawnAtUtc { get; set; }
    public Guid? WithdrawnByUserId { get; set; }
    public string? WithdrawnReason { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>
/// One proposed procedure in a plan, for one diagnosis. It remembers the catalog procedure, the <b>exact catalog version</b> it was proposed under and the fee copied from that version, so a later
/// fee change in the catalog never changes what the plan says (the database checks the copy equals that version's fee). An item is never edited: a wrong one is withdrawn with a reason and the
/// right one added. The diagnosis must be the plan's patient's and not withdrawn, the procedure must be active, and the tooth and surface must fit the procedure; the database refuses anything else.
/// </summary>
public class TreatmentPlanItem
{
    public Guid Id { get; set; }
    public Guid PlanId { get; set; }
    /// <summary>The plan's patient, kept on the item so the database can check that the diagnosis belongs to the same patient.</summary>
    public Guid PatientId { get; set; }
    /// <summary>1, 2, 3... in the order items were added to the plan; unique per plan (a database index), so two simultaneous adds cannot share a number.</summary>
    public int ItemNumber { get; set; }
    /// <summary>The caller's key for the save that added this item; unique per plan, so a retry never adds it twice.</summary>
    public required string IdempotencyKey { get; set; }
    public Guid DiagnosisId { get; set; }
    public Guid ProcedureId { get; set; }
    public Guid ProcedureVersionId { get; set; }
    /// <summary>One of the odontogram's FDI keys when the procedure is tooth-level; otherwise null.</summary>
    public string? ToothKey { get; set; }
    /// <summary>A surface letter when the procedure is surface-level; otherwise null.</summary>
    public string? Surface { get; set; }
    /// <summary>US dollars, copied from the catalog version when the item was proposed. A practice estimate, never an insurance figure.</summary>
    public decimal Fee { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public Guid CreatedByUserId { get; set; }
    /// <summary>The three withdrawal fields are all set or all null; once set they never change.</summary>
    public DateTimeOffset? WithdrawnAtUtc { get; set; }
    public Guid? WithdrawnByUserId { get; set; }
    public string? WithdrawnReason { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>Append-only history of a <see cref="TreatmentPlan"/>: who did what and when, with the reason where one is required. A trigger refuses any edit or delete.</summary>
public class TreatmentPlanEvent
{
    public Guid Id { get; set; }
    public Guid PlanId { get; set; }
    public Guid PatientId { get; set; }
    public int EventNumber { get; set; }
    /// <summary>One of <see cref="TreatmentPlanChanges.All"/>.</summary>
    public required string ChangeType { get; set; }
    /// <summary>The item an ItemAdded or ItemWithdrawn event is about; null for the others.</summary>
    public Guid? ItemId { get; set; }
    /// <summary>The title a Created or Renamed event set; null for the others.</summary>
    public string? Title { get; set; }
    public string? Reason { get; set; }
    public Guid ActorUserId { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
}
