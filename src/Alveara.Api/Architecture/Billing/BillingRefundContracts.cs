namespace Alveara.Api.Architecture.Billing;

/// <summary>
/// ALV-007-C02: hand money back for one earlier payment (by entry number). There is no recipient field on purpose: the money goes back to whoever the payment records as having paid it (the payer, or the
/// account's patient when none was recorded). <c>Method</c> is how it is returned, one of the active payment methods; <c>Reason</c> is required.
/// </summary>
public sealed record RefundInput(int? PaymentEntryNumber, decimal? Amount, string? Method, string? Reason, string? RowVersion, string? IdempotencyKey);

/// <summary>
/// ALV-007-C02: move part of what one payment pays from some charges to others in one step. <c>Releases</c> name the charges to take money off (by entry number, each once) and <c>Allocations</c> the open charges
/// to put it on (each once); the two must add up to exactly the same amount. Nothing is edited: the old applications are released and new ones are recorded.
/// </summary>
public sealed record MoveAllocationInput(int? PaymentEntryNumber, IReadOnlyList<AllocationInput>? Releases, IReadOnlyList<AllocationInput>? Allocations, string? Reason, string? RowVersion, string? IdempotencyKey);

/// <summary>
/// ALV-007-C02: correct a payment or adjustment by reversing it exactly and recording a linked replacement in one step. <c>Reason</c> is the reversal's reason. For a payment the replacement carries
/// <c>Amount</c>, <c>Method</c>, <c>Note</c> and optionally <c>PaidByPatientId</c> and starts unapplied; for an adjustment it carries the signed <c>Amount</c> and <c>Note</c> (its reason, defaulting to <c>Reason</c>).
/// A charge cannot be corrected this way: it can only be reversed.
/// </summary>
public sealed record CorrectionInput(int? EntryNumber, string? Reason, decimal? Amount, string? Method, string? Note, Guid? PaidByPatientId, string? RowVersion, string? IdempotencyKey);
