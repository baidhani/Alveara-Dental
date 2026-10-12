namespace Alveara.Api.Architecture.Billing;

/// <summary>What a billing entry is. A Charge is money the patient owes (positive), a Payment is money received (negative), an Adjustment corrects the balance either way (never zero).</summary>
public static class BillingKinds
{
    public const string Charge = "Charge";
    public const string Payment = "Payment";
    public const string Adjustment = "Adjustment";
    /// <summary>ALV-007-C01: undoes one earlier entry (its amount is the opposite) without rewriting it; needs a reason.</summary>
    public const string Reversal = "Reversal";
    /// <summary>ALV-007-C02: money returned to a payer. Positive (it raises the balance owed, as undoing part of a payment does); it names the one payment it returns part of.</summary>
    public const string Refund = "Refund";
    public static readonly IReadOnlyList<string> All = [Charge, Payment, Adjustment, Reversal, Refund];
}

/// <summary>How a payment was received. A fixed list in this story; making it configurable is ALV-007-C01's.</summary>
public static class BillingPaymentMethods
{
    public const string Cash = "Cash";
    public const string Check = "Check";
    public const string Card = "Card";
    public const string Other = "Other";
    public static readonly IReadOnlyList<string> All = [Cash, Check, Card, Other];
}

/// <summary>Limits shared by the service and the database checks.</summary>
public static class BillingLimits
{
    /// <summary>The largest amount one entry can carry; the same ceiling as a catalog fee.</summary>
    public const decimal AmountMax = 1_000_000.00m;
    public const int KeyMax = 64;
    public const int ReasonMax = 500;
    public const int CodeMax = 20;
    public const int DescriptionMax = 200;
    public const int MethodNameMax = 40;
}

/// <summary>
/// STORY-007: a patient's billing account. One per patient, created the first time something is billed or paid. It holds no balance: the balance is always the sum of the account's
/// <see cref="BillingEntry"/> rows, so it cannot drift from the history. <see cref="RowVersion"/> changes with every entry, so two people recording at once cannot both act on the same balance.
/// An account is never deleted and its patient never changes (database triggers).
/// </summary>
public class BillingAccount
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public Guid CreatedByUserId { get; set; }
    /// <summary>Set on every entry, so the row changes and <see cref="RowVersion"/> moves with it.</summary>
    public DateTimeOffset? UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>
/// STORY-007: one line of a patient's financial history, appended and never edited or deleted (a database trigger refuses both): a mistake is corrected by a new entry. The sign of
/// <see cref="Amount"/> follows the kind (Charge positive, Payment negative, Adjustment either, never zero), so the balance owed is the plain sum. A Charge names the completed plan
/// procedure it bills and carries a copy of its code and description, and its amount is that procedure's fee (the database checks it).
/// </summary>
public class BillingEntry
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public Guid PatientId { get; set; }
    /// <summary>1, 2, 3... unique per account.</summary>
    public int EntryNumber { get; set; }
    /// <summary>One of <see cref="BillingKinds.All"/>.</summary>
    public required string Kind { get; set; }
    public decimal Amount { get; set; }
    /// <summary>Charge only: the plan procedure billed. At most one Charge per plan procedure.</summary>
    public Guid? PlanItemId { get; set; }
    public string? ProcedureCode { get; set; }
    public string? ProcedureDescription { get; set; }
    /// <summary>Payment only: one of <see cref="BillingPaymentMethods.All"/>.</summary>
    public string? PaymentMethod { get; set; }
    /// <summary>Adjustment: why (required). Payment: an optional note. Charge: none.</summary>
    public string? Reason { get; set; }
    /// <summary>The caller's key for the save that made this entry; unique per account, so a retry returns the entry instead of making a second one.</summary>
    public required string IdempotencyKey { get; set; }
    public DateTimeOffset RecordedAtUtc { get; set; }
    public Guid RecordedByUserId { get; set; }
    /// <summary>ALV-007-C01, Reversal only: the entry this one undoes. An entry is reversed at most once, a reversal is never itself reversed, and its amount is the opposite of that entry's.</summary>
    public Guid? ReversesEntryId { get; set; }
    /// <summary>ALV-007-C01: ties the entries made by one operation together (for example the charge ALV-008-C01 makes when it completes a procedure) across the audit log and the caller's own records.</summary>
    public Guid? CorrelationId { get; set; }
    /// <summary>ALV-007-C01, Payment only: who paid, when that is not the patient; it must be the account's own patient or someone who has been recorded as responsible for it.</summary>
    public Guid? PaidByPatientId { get; set; }
    /// <summary>ALV-007-C02, Refund only: the payment this refund returns part of (the same account's; the recipient is that payment's recorded payer, else the account's patient). A payment may be refunded in parts, never for more than it was.</summary>
    public Guid? RefundsEntryId { get; set; }
    /// <summary>ALV-007-C02, Payment or Adjustment only: the reversed entry of the same kind this one replaces (a correction records the exact reversal and this replacement together). An entry is replaced at most once.</summary>
    public Guid? ReplacesEntryId { get; set; }
    /// <summary>ALV-007-C01, Payment only: the configured method used. <see cref="PaymentMethod"/> keeps its kind (Cash, Check, Card, Other). Null on payments recorded before methods were configurable.</summary>
    public Guid? PaymentMethodId { get; set; }
}

/// <summary>
/// ALV-007-C01: how much of one payment pays one charge. Appended, never edited or deleted. A payment's allocations cannot add up to more than the payment and a charge's cannot add up to more than
/// the charge; allocations of a reversed payment no longer count (the payment's reversal releases them), so the "live" allocations are the ones whose payment has not been reversed.
/// </summary>
public class BillingAllocation
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public Guid PatientId { get; set; }
    /// <summary>1, 2, 3... unique per account.</summary>
    public int AllocationNumber { get; set; }
    public Guid PaymentEntryId { get; set; }
    public Guid ChargeEntryId { get; set; }
    public decimal Amount { get; set; }
    /// <summary>The caller's key for the command that made this allocation (shared by every row of one command), so a retry returns what the first one made.</summary>
    public required string IdempotencyKey { get; set; }
    public DateTimeOffset AllocatedAtUtc { get; set; }
    public Guid AllocatedByUserId { get; set; }
}

/// <summary>
/// ALV-007-C01: who is financially responsible for an account, kept apart from patient identity. Each row says "from this date, this patient (the account's own patient, or a guarantor) is responsible",
/// with who recorded it and why; the current responsible party is the latest row. Appended, never edited or deleted. It starts from the patient's registered guarantor but changing one never changes the other.
/// </summary>
public class BillingResponsibility
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public Guid PatientId { get; set; }
    /// <summary>1, 2, 3... unique per account.</summary>
    public int Sequence { get; set; }
    public Guid ResponsiblePatientId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public string? Reason { get; set; }
    public DateTimeOffset SetAtUtc { get; set; }
    public Guid SetByUserId { get; set; }
}

/// <summary>
/// ALV-007-C01: a payment method the practice records payments under, owned by the billing domain. Each has a name and one of the four kinds (Cash, Check, Card, Other) that the ledger itself reports on;
/// the four built-in methods always exist and stay active. A method is never deleted; one that is no longer used is inactivated and cannot be chosen for new payments but stays readable on old ones.
/// </summary>
public class BillingPaymentMethod
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public required string Kind { get; set; }
    public bool IsBuiltIn { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset? UpdatedAtUtc { get; set; }
    public Guid? UpdatedByUserId { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>The ids of the four built-in payment methods, fixed so every database (and every test) has the same ones.</summary>
public static class BillingPaymentMethodIds
{
    public static readonly Guid Cash = Guid.Parse("b1100000-0000-0000-0000-000000000001");
    public static readonly Guid Check = Guid.Parse("b1100000-0000-0000-0000-000000000002");
    public static readonly Guid Card = Guid.Parse("b1100000-0000-0000-0000-000000000003");
    public static readonly Guid Other = Guid.Parse("b1100000-0000-0000-0000-000000000004");
}
