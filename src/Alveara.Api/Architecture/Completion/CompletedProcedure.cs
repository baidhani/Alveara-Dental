namespace Alveara.Api.Architecture.Completion;

/// <summary>What completing a procedure did to the patient's chart: moved a finding to Completed, found it already Completed, or had nothing to move (no linked finding, whole-mouth work, or an ineligible finding).</summary>
public static class ChartOutcomes
{
    public const string Updated = "Updated";
    public const string AlreadyCompleted = "AlreadyCompleted";
    public const string NotApplicable = "NotApplicable";
    public static readonly IReadOnlyList<string> All = [Updated, AlreadyCompleted, NotApplicable];
}

public static class CompletionLimits
{
    public const int KeyMax = 64;
    public const int ReasonMax = 500;
    /// <summary>A SHA-256 request hash written as lower-case hexadecimal.</summary>
    public const int HashLength = 64;
}

/// <summary>
/// ALV-008-C01: one accepted submission to complete a selected set of procedures of one plan. It is the idempotency anchor: the same key with the same canonical request (<see cref="RequestHash"/>) replays the
/// original result, the same key with a different request is a conflict. It is written in the same transaction as the completed procedures it produced and is never changed or deleted.
/// </summary>
public class CompletionSet
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    public Guid PlanId { get; set; }
    /// <summary>The caller's key for this submission; unique per plan.</summary>
    public required string IdempotencyKey { get; set; }
    /// <summary>SHA-256 (lower-case hexadecimal) of the canonical request: sorted item ids and the normalized encounter, provider and UTC performed time.</summary>
    public required string RequestHash { get; set; }
    /// <summary>Carried through the audit entries, the logs and the charge contract so one submission can be traced end to end.</summary>
    public Guid CorrelationId { get; set; }
    /// <summary>The plan revision in force when the set was submitted.</summary>
    public int PlanRevisionNumber { get; set; }
    public int ItemCount { get; set; }
    /// <summary>
    /// ALV-008-C02: set exactly when this set is the adoption of one procedure that was completed the old, state-only way (why the clinician is recording its missing attribution now). Null for an ordinary
    /// completion. An adopted set always has an item count of one.
    /// </summary>
    public string? AdoptionReason { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public Guid CreatedByUserId { get; set; }
}

/// <summary>
/// ALV-008-C01: the authoritative record that one planned procedure was completed. It names the exact plan, plan revision, plan item and catalog version, the tooth and surface, the encounter, the provider and
/// when it was performed, and what happened to the chart. It is finalized when it is created and is never changed or deleted; corrections are <see cref="CompletedProcedureAmendment"/> rows. One per plan item.
/// The charge is not copied here: the procedure's one charge is the billing entry that names the same plan item.
/// </summary>
public class CompletedProcedure
{
    public Guid Id { get; set; }
    public Guid SetId { get; set; }
    public Guid PatientId { get; set; }
    public Guid PlanId { get; set; }
    /// <summary>Unique: a procedure is completed once.</summary>
    public Guid PlanItemId { get; set; }
    public int PlanRevisionNumber { get; set; }
    public Guid ProcedureVersionId { get; set; }
    public string? ToothKey { get; set; }
    public string? Surface { get; set; }
    public Guid EncounterId { get; set; }
    public Guid ProviderProfileId { get; set; }
    public DateTimeOffset PerformedAtUtc { get; set; }
    /// <summary>The tooth finding this completion moved or found already completed; null exactly when <see cref="ChartOutcome"/> is NotApplicable.</summary>
    public Guid? ToothFindingId { get; set; }
    /// <summary>
    /// ALV-008-C02 R02: the exact chart-history boundary of this completion: the <see cref="Odontogram.ToothFindingVersion.VersionNumber"/> of <see cref="ToothFindingId"/> that this operation wrote (Updated) or observed
    /// (AlreadyCompleted), which shows the finding as an active Completed state. Reconciliation judges the completion against that version alone, so a later legitimate chart change can never be mistaken for the
    /// completion, even at the same clock instant. Null exactly when there is no finding, or for a completion recorded before this column existed whose version could not be derived (reported as a mismatch).
    /// </summary>
    public int? ToothFindingVersionNumber { get; set; }
    /// <summary>One of <see cref="ChartOutcomes.All"/>.</summary>
    public required string ChartOutcome { get; set; }
    public DateTimeOffset FinalizedAtUtc { get; set; }
    public Guid FinalizedByUserId { get; set; }
}

/// <summary>
/// ALV-008-C01: a correction to a completed procedure's provider, performed time or encounter, with the reason. Numbered 1, 2, 3 per procedure; a correction names the latest amendment it was made against (by
/// number) and carries its own idempotency key, so a stale correction is refused and an identical retry returns the original. Never changed or deleted. The current values of a procedure are its latest
/// amendment's where given, otherwise the original's.
/// </summary>
public class CompletedProcedureAmendment
{
    public Guid Id { get; set; }
    public Guid CompletedProcedureId { get; set; }
    public Guid PatientId { get; set; }
    public int AmendmentNumber { get; set; }
    public required string IdempotencyKey { get; set; }
    /// <summary>SHA-256 (lower-case hexadecimal) of the canonical amendment request, so the same key with a different request is a conflict.</summary>
    public required string RequestHash { get; set; }
    public required string Reason { get; set; }
    public Guid? ProviderProfileId { get; set; }
    public DateTimeOffset? PerformedAtUtc { get; set; }
    public Guid? EncounterId { get; set; }
    public DateTimeOffset AmendedAtUtc { get; set; }
    public Guid AmendedByUserId { get; set; }
}
