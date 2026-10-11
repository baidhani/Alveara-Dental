using System.Text.RegularExpressions;

namespace Alveara.Api.Architecture.Billing;

/// <summary>A refusal the caller can show: a stable <see cref="Code"/>, a message safe to display and, where several fields need attention, a message per field.</summary>
public class BillingException(string code, string message, int statusCode, IReadOnlyDictionary<string, string>? fieldErrors = null, Exception? inner = null) : Exception(message, inner)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
    public IReadOnlyDictionary<string, string> FieldErrors { get; } = fieldErrors ?? new Dictionary<string, string>();
}

/// <summary>Everything is optional here so the rules can name every missing or wrong field in one answer. A charge names the completed plan procedure only: the amount is never supplied.</summary>
public sealed record ChargeInput(Guid? PlanItemId, string? IdempotencyKey);
/// <summary>
/// Money received. <c>Allocations</c> name the charges (by entry number) the payment pays; left out, it is applied to the oldest open charges first. <c>PaidByPatientId</c> is who paid when that is not the patient
/// (it must be someone recorded as responsible for the account). <c>CorrelationId</c> ties the entry to the operation that caused it.
/// </summary>
public sealed record PaymentInput(decimal? Amount, string? Method, string? Note, string? RowVersion, string? IdempotencyKey, IReadOnlyList<AllocationInput>? Allocations = null, Guid? PaidByPatientId = null, Guid? CorrelationId = null);

/// <summary>One charge, by entry number, and how much of a payment goes to it.</summary>
public sealed record AllocationInput(int? ChargeEntryNumber, decimal? Amount);

/// <summary>Apply what is left of an earlier payment (by entry number) to charges: the ones named, or the oldest open ones first when none are.</summary>
public sealed record ApplyPaymentInput(int? PaymentEntryNumber, IReadOnlyList<AllocationInput>? Allocations, string? RowVersion, string? IdempotencyKey);

/// <summary>Undo one earlier entry (by entry number) with a new entry that says why; nothing is edited or removed.</summary>
public sealed record ReversalInput(int? EntryNumber, string? Reason, string? RowVersion, string? IdempotencyKey);
public sealed record AdjustmentInput(decimal? Amount, string? Reason, string? RowVersion, string? IdempotencyKey);

/// <summary>One entry as shown: <see cref="Amount"/> is signed as stored (a payment is negative) and <see cref="BalanceAfter"/> is the balance owed once it was recorded, so the ledger reads top to bottom.</summary>
public sealed record BillingEntryView(
    int EntryNumber, string Kind, decimal Amount, decimal BalanceAfter, Guid? PlanItemId, string? ProcedureCode, string? ProcedureDescription, string? PaymentMethod, string? Reason,
    DateTimeOffset RecordedAtUtc, string? RecordedByName, int? ReversesEntryNumber = null, string? PaymentMethodName = null, string? PaidByName = null);

/// <summary>
/// What an entry stands for now, derived from the preserved history (so it can change as later entries are made, unlike the entry itself): whether a reversal has undone it, what a charge has been paid and has left,
/// and what a payment has been applied to and has not yet. Parallel to the entries, in the same order.
/// </summary>
public sealed record BillingEntryStanding(int EntryNumber, bool IsReversed, int? ReversedByEntryNumber, decimal? PaidAmount, decimal? OpenAmount, decimal? AppliedAmount, decimal? UnappliedAmount);

/// <summary>One allocation as shown: which payment paid which charge, for how much, and whether it has been released by reversing the payment.</summary>
public sealed record BillingAllocationView(int AllocationNumber, int PaymentEntryNumber, int ChargeEntryNumber, decimal Amount, bool Released, DateTimeOffset AllocatedAtUtc, string? AllocatedByName);

/// <summary>Record who is financially responsible for a patient's account: the patient themselves or another active patient (a guarantor), from a date (not in the future), with an optional reason. The account version is needed once the account exists.</summary>
public sealed record ResponsibilityInput(Guid? ResponsiblePatientId, DateOnly? EffectiveFrom, string? Reason, string? RowVersion);

/// <summary>One recorded change of responsible party, as shown.</summary>
public sealed record BillingResponsibilityItem(int Sequence, Guid ResponsiblePatientId, string ResponsibleName, bool IsSelf, DateOnly EffectiveFrom, string? Reason, DateTimeOffset SetAtUtc, string? SetByName);

/// <summary>
/// Who is responsible for a patient's account now, kept apart from the patient's identity: the latest recorded change if billing has recorded one (<c>Recorded</c>), otherwise the guarantor registered on the patient
/// (<c>RegisteredGuarantor</c>), otherwise the patient (<c>Self</c>). Changing one never changes the other. <c>RowVersion</c> is null until the account exists.
/// </summary>
public sealed record BillingResponsibilityView(Guid PatientId, Guid ResponsiblePatientId, string ResponsibleName, bool IsSelf, string Source, DateOnly? EffectiveFrom, string? RowVersion, IReadOnlyList<BillingResponsibilityItem> History);

/// <summary>One account a guarantor is responsible for: the patient it belongs to and what is owed on it.</summary>
public sealed record GuarantorAccountView(Guid PatientId, string PatientName, bool IsGuarantor, decimal BalanceOwed, decimal OpenChargeTotal, decimal UnappliedTotal);

/// <summary>Everything one person is financially responsible for, with the total owed across those accounts (each balance is that account's own ledger figure).</summary>
public sealed record GuarantorSummaryView(Guid GuarantorPatientId, string GuarantorName, string Currency, decimal TotalOwed, IReadOnlyList<GuarantorAccountView> Accounts);

/// <summary>A payment method the practice records payments under: its name, the kind the ledger reports on, whether it is one of the four built in, and whether it can still be chosen.</summary>
public sealed record PaymentMethodView(Guid Id, string Name, string Kind, bool IsBuiltIn, bool IsActive, string RowVersion);

/// <summary>A new payment method: a name (unique, whatever its case) and one of the four kinds.</summary>
public sealed record PaymentMethodInput(string? Name, string? Kind);

/// <summary>The answer to "does the preserved history add up": whether every ledger rule holds, what does not, and the balance rebuilt from the entries.</summary>
public sealed record LedgerVerification(Guid PatientId, bool Consistent, IReadOnlyList<string> Findings, decimal BalanceOwed, int EntryCount, int AllocationCount);

/// <summary>A patient's account. <see cref="BalanceOwed"/> is computed from the entries every time it is read; <see cref="RowVersion"/> is what a payment or adjustment must echo back. An account that does not exist yet has a zero balance and no row version.</summary>
public sealed record BillingAccountView(Guid PatientId, bool HasAccount, decimal BalanceOwed, string Currency, string? RowVersion, IReadOnlyList<BillingEntryView> Entries,
    IReadOnlyList<BillingAllocationView>? Allocations = null, decimal UnappliedTotal = 0m, decimal OpenChargeTotal = 0m, IReadOnlyList<BillingEntryStanding>? Standing = null);

/// <summary>A completed, unbilled plan procedure as billing sees it: what is needed to bill it and nothing clinical (no diagnosis, rationale or plan wording).</summary>
public sealed record BillableProcedureView(Guid PlanItemId, string ProcedureCode, string Description, string? ToothKey, string? Surface, decimal Fee, string Currency);

/// <summary>The rules for what a billing entry may carry, shared by the service (which names every problem in one answer) and mirrored by the database checks.</summary>
public static partial class BillingRules
{
    [GeneratedRegex(@"[\u0000-\u001F]")]
    private static partial Regex ControlCharacter();

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static BillingException Invalid(IDictionary<string, string> errors) =>
        new("validation_failed", "Some entries need attention.", 400, new Dictionary<string, string>(errors));

    /// <summary>The trimmed idempotency key, or null with a message added. A key is what makes a retried or double-clicked save add nothing the second time.</summary>
    public static string? CleanKey(IDictionary<string, string> errors, string? key)
    {
        var k = Clean(key);
        if (k is null) errors["idempotencyKey"] = "A save key is required so a repeated save adds nothing twice. Reload and try again.";
        else if (k.Length > BillingLimits.KeyMax || ControlCharacter().IsMatch(k)) errors["idempotencyKey"] = $"The save key must be {BillingLimits.KeyMax} characters or fewer with no control characters.";
        else return k;
        return null;
    }

    /// <summary>The amount as a positive number of dollars and cents, or null with a message added. Never more than a fraction-free number of cents, and never above the ceiling.</summary>
    public static decimal? CleanPositiveAmount(IDictionary<string, string> errors, decimal? amount, string what)
    {
        if (amount is null) errors["amount"] = $"Enter the {what} amount.";
        else if (amount <= 0) errors["amount"] = $"The {what} amount must be more than zero.";
        else if (Math.Round(amount.Value, 2) != amount) errors["amount"] = "Enter the amount in dollars and cents (no more than two decimal places).";
        else if (amount > BillingLimits.AmountMax) errors["amount"] = $"The {what} amount cannot be more than {BillingLimits.AmountMax:F2}.";
        else return amount;
        return null;
    }

    /// <summary>The configured, active payment method the caller named (case does not matter), or null with a message that lists the ones that can be chosen.</summary>
    public static BillingPaymentMethod? CleanMethod(IDictionary<string, string> errors, string? method, IReadOnlyList<BillingPaymentMethod> active)
    {
        var m = Clean(method);
        var match = m is null ? null : active.FirstOrDefault(x => string.Equals(x.Name, m, StringComparison.OrdinalIgnoreCase));
        if (match is null) errors["method"] = $"Choose how the payment was received: {string.Join(", ", active.Select(x => x.Name))}.";
        return match;
    }

    /// <summary>The trimmed name of a payment method, or null with a message added.</summary>
    public static string? CleanMethodName(IDictionary<string, string> errors, string? name)
    {
        var t = Clean(name);
        if (t is null) errors["name"] = "Give the payment method a name.";
        else if (t.Length > BillingLimits.MethodNameMax) errors["name"] = $"Keep the name to {BillingLimits.MethodNameMax} characters or fewer.";
        else if (ControlCharacter().IsMatch(t)) errors["name"] = "The name cannot contain control characters or line breaks.";
        else return t;
        return null;
    }

    /// <summary>A reason or note: trimmed, within the limit, no control characters. <paramref name="required"/> adds a message when it is missing; an optional one that is blank becomes null.</summary>
    public static string? CleanText(IDictionary<string, string> errors, string field, string? text, bool required, string missing)
    {
        var t = Clean(text);
        if (t is null) { if (required) errors[field] = missing; return null; }
        if (t.Length > BillingLimits.ReasonMax) { errors[field] = $"Keep this to {BillingLimits.ReasonMax} characters or fewer."; return null; }
        if (ControlCharacter().IsMatch(t)) { errors[field] = "This cannot contain control characters or line breaks."; return null; }
        return t;
    }

    /// <summary>Applies the caller's row version for an optimistic-concurrency check; a missing or malformed one is a 400 before anything changes.</summary>
    public static byte[] ParseVersion(string? rowVersion)
    {
        if (string.IsNullOrWhiteSpace(rowVersion))
            throw new BillingException("row_version_required", "The account version you are working on is required so a concurrent change is never overwritten. Reload and try again.", 400);
        try
        {
            return Convert.FromBase64String(rowVersion);
        }
        catch (FormatException)
        {
            throw new BillingException("row_version_invalid", "The supplied account version is not valid. Reload and try again.", 400);
        }
    }
}
