namespace Alveara.Api.Architecture.Treatment;

/// <summary>One proposed procedure as a screen shows it: the diagnosis it is for, the catalog procedure and the exact catalog version it was proposed under, where on the mouth, and the fee copied then.</summary>
public sealed record PlanItemView(
    Guid Id, int ItemNumber, Guid DiagnosisId, string DiagnosisLabel, Guid ProcedureId, Guid ProcedureVersionId, int ProcedureVersionNumber, string ProcedureCodeSystem, string ProcedureCode,
    string ProcedureDescription, string? ToothKey, string? Surface, decimal Fee, string Currency, bool IsWithdrawn, string? WithdrawnReason, DateTimeOffset? WithdrawnAtUtc, string? WithdrawnByName,
    DateTimeOffset CreatedAtUtc, string? CreatedByName,
    int? PhaseNumber = null, string Acceptance = "Pending", string Scheduling = "Unscheduled", string Completion = "NotStarted", DateOnly? PlannedDate = null,
    string? DecisionReason = null, string? DecidedByName = null, string? RecordedByName = null, DateTimeOffset? RecordedAtUtc = null, int? DecisionRevision = null);

/// <summary>A phase (visit or stage of care) of the plan's current revision.</summary>
public sealed record PlanPhaseView(int PhaseNumber, string Name, string? PlannedVisit, DateOnly? TargetDate);

/// <summary>
/// A plan with its items. <see cref="EstimateTotal"/> is the sum of the fees of the items that have not been withdrawn, and <see cref="EstimateLabel"/> says what that figure is (and is not):
/// the practice's catalog fees, not an insurance estimate and not a guaranteed patient cost.
/// </summary>
public sealed record PlanView(
    Guid Id, Guid PatientId, string Title, string Status, int ActiveItemCount, decimal EstimateTotal, string Currency, string EstimateLabel, IReadOnlyList<PlanItemView> Items,
    DateTimeOffset CreatedAtUtc, string? CreatedByName, DateTimeOffset? UpdatedAtUtc, string? UpdatedByName, string? WithdrawnReason, DateTimeOffset? WithdrawnAtUtc, string? WithdrawnByName, string RowVersion,
    int CurrentRevision = 1, string? ReferenceCode = null, string? ClinicalRationale = null, string? PatientExplanation = null, IReadOnlyList<PlanPhaseView>? Phases = null,
    string AcceptanceStatus = "Pending", decimal AcceptedEstimateTotal = 0m);

public sealed record PlanEventView(int EventNumber, string ChangeType, Guid? ItemId, string? Title, string? Reason, string? ActorName, DateTimeOffset OccurredAtUtc, int? RevisionNumber = null);
