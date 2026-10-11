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
public sealed record PaymentInput(decimal? Amount, string? Method, string? Note, string? RowVersion, string? IdempotencyKey);
public sealed record AdjustmentInput(decimal? Amount, string? Reason, string? RowVersion, string? IdempotencyKey);

/// <summary>One entry as shown: <see cref="Amount"/> is signed as stored (a payment is negative) and <see cref="BalanceAfter"/> is the balance owed once it was recorded, so the ledger reads top to bottom.</summary>
public sealed record BillingEntryView(
    int EntryNumber, string Kind, decimal Amount, decimal BalanceAfter, Guid? PlanItemId, string? ProcedureCode, string? ProcedureDescription, string? PaymentMethod, string? Reason,
    DateTimeOffset RecordedAtUtc, string? RecordedByName);

/// <summary>A patient's account. <see cref="BalanceOwed"/> is computed from the entries every time it is read; <see cref="RowVersion"/> is what a payment or adjustment must echo back. An account that does not exist yet has a zero balance and no row version.</summary>
public sealed record BillingAccountView(Guid PatientId, bool HasAccount, decimal BalanceOwed, string Currency, string? RowVersion, IReadOnlyList<BillingEntryView> Entries);

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

    /// <summary>The method as the fixed list spells it, or null with a message added.</summary>
    public static string? CleanMethod(IDictionary<string, string> errors, string? method)
    {
        var m = Clean(method);
        var match = m is null ? null : BillingPaymentMethods.All.FirstOrDefault(x => string.Equals(x, m, StringComparison.OrdinalIgnoreCase));
        if (match is null) errors["method"] = $"Choose how the payment was received: {string.Join(", ", BillingPaymentMethods.All)}.";
        return match;
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
