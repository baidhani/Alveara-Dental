namespace Alveara.Api.Architecture.Treatment;

/// <summary>One line of a plan's revision history: which version it is, what produced it, who and when, and what the plan came to at that point (live procedures, phases and the practice fee estimate).</summary>
public sealed record RevisionSummaryView(int RevisionNumber, string ChangeType, string Title, string? Reason, string? ActorName, DateTimeOffset CreatedAtUtc, int ProcedureCount, decimal EstimateTotal, int PhaseCount);

/// <summary>A procedure as one revision held it: its code, wording and fee (copied when it was proposed), where it sat, and whether it had been withdrawn by then.</summary>
public sealed record RevisionItemView(Guid ItemId, int ItemNumber, string ProcedureCode, string ProcedureDescription, string DiagnosisLabel, string? ToothKey, string? Surface, decimal Fee, bool IsWithdrawn, int? PhaseNumber);

/// <summary>One whole earlier (or current) version of a plan, exactly as it was saved.</summary>
public sealed record RevisionView(RevisionSummaryView Summary, string? ClinicalRationale, string? PatientExplanation, IReadOnlyList<PlanPhaseView> Phases, IReadOnlyList<RevisionItemView> Items, string EstimateLabel);

public sealed record FieldChange(string? From, string? To);

public sealed record PhaseChange(int PhaseNumber, PlanPhaseView From, PlanPhaseView To);

public sealed record ItemMove(RevisionItemView Item, int? FromPhase, int? ToPhase);

/// <summary>
/// What changed between two revisions of one plan. A field that is the same is null; a list is empty when nothing of that kind changed. "Added" and "withdrawn" are about live procedures: added is live in
/// <c>To</c> and not live in <c>From</c> (new, or not yet proposed); withdrawn is live in <c>From</c> and not live in <c>To</c>. The estimate is the practice's catalog fee total of the live procedures of each.
/// </summary>
public sealed record RevisionComparison(
    RevisionSummaryView From, RevisionSummaryView To, FieldChange? Title, FieldChange? ClinicalRationale, FieldChange? PatientExplanation,
    IReadOnlyList<PlanPhaseView> PhasesAdded, IReadOnlyList<PlanPhaseView> PhasesRemoved, IReadOnlyList<PhaseChange> PhasesChanged,
    IReadOnlyList<RevisionItemView> ItemsAdded, IReadOnlyList<RevisionItemView> ItemsWithdrawn, IReadOnlyList<ItemMove> ItemsMoved,
    decimal EstimateFrom, decimal EstimateTo, decimal EstimateDelta, string EstimateLabel);
