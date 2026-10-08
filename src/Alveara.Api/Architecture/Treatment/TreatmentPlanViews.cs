namespace Alveara.Api.Architecture.Treatment;

/// <summary>One proposed procedure as a screen shows it: the diagnosis it is for, the catalog procedure and the exact catalog version it was proposed under, where on the mouth, and the fee copied then.</summary>
public sealed record PlanItemView(
    Guid Id, int ItemNumber, Guid DiagnosisId, string DiagnosisLabel, Guid ProcedureId, Guid ProcedureVersionId, int ProcedureVersionNumber, string ProcedureCodeSystem, string ProcedureCode,
    string ProcedureDescription, string? ToothKey, string? Surface, decimal Fee, string Currency, bool IsWithdrawn, string? WithdrawnReason, DateTimeOffset? WithdrawnAtUtc, string? WithdrawnByName,
    DateTimeOffset CreatedAtUtc, string? CreatedByName);

/// <summary>
/// A plan with its items. <see cref="EstimateTotal"/> is the sum of the fees of the items that have not been withdrawn, and <see cref="EstimateLabel"/> says what that figure is (and is not):
/// the practice's catalog fees, not an insurance estimate and not a guaranteed patient cost.
/// </summary>
public sealed record PlanView(
    Guid Id, Guid PatientId, string Title, string Status, int ActiveItemCount, decimal EstimateTotal, string Currency, string EstimateLabel, IReadOnlyList<PlanItemView> Items,
    DateTimeOffset CreatedAtUtc, string? CreatedByName, DateTimeOffset? UpdatedAtUtc, string? UpdatedByName, string? WithdrawnReason, DateTimeOffset? WithdrawnAtUtc, string? WithdrawnByName, string RowVersion);

public sealed record PlanEventView(int EventNumber, string ChangeType, Guid? ItemId, string? Title, string? Reason, string? ActorName, DateTimeOffset OccurredAtUtc);
