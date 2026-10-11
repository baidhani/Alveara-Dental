namespace Alveara.Api.Architecture.Billing;

/// <summary>What a billing entry is. A Charge is money the patient owes (positive), a Payment is money received (negative), an Adjustment corrects the balance either way (never zero).</summary>
public static class BillingKinds
{
    public const string Charge = "Charge";
    public const string Payment = "Payment";
    public const string Adjustment = "Adjustment";
    public static readonly IReadOnlyList<string> All = [Charge, Payment, Adjustment];
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
}
