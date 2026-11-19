using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Clinical;

namespace Alveara.Api.Architecture.Billing;

/// <summary>
/// ALV-N024 (PD-17, OQ-4; briefing revision 2 section 2.1 S2 and section 4, approved by BRIEFING-R02 and ADDENDUM-01): one receipt per payment, and a deliberate replacement with a reason.
/// - A payment's receipt is "open" until another receipt names it in <c>ReplacesDocumentId</c>. Without a replacement a receipt is issued only for a payment with no open receipt (<c>receipt_exists</c>);
///   a replacement must name the LATEST open receipt of its payment (<c>receipt_replaced</c> if it was replaced first, <c>receipt_not_found</c> if it is not this payment's).
/// - Every receipt issue runs in ONE read-committed transaction that takes the transaction-owned application lock <c>alveara.receipt:{payment:N}</c> BEFORE anything is inserted, then re-reads (each EF
///   query is a new statement, so under READ_COMMITTED_SNAPSHOT it sees what a contender committed while this one waited), then checks, rebuilds, inserts the document with its audit row in one save, and
///   commits. The lock is released when the transaction ends (commit or rollback); nothing needs releasing by hand. Not granted within <see cref="ReceiptLockTimeoutMilliseconds"/>: 503 <c>receipt_busy</c>,
///   nothing written. The database trigger takes the same lock (re-entrant here) and enforces the same rule for writes that bypass this service (51294, 51295, 51297).
/// - What is true of a document now (replaces, superseded by, payment reversed or refunded, legacy duplicate) is derived when it is read and never stored on the original.
/// </summary>
public partial class BillingService
{
    /// <summary>How long a receipt issue waits for its payment's lock (10 s). A test may shorten it to prove the busy answer.</summary>
    public int ReceiptLockTimeoutMilliseconds { get; init; } = 10_000;

    private static string ReceiptLock(Guid paymentId) => $"alveara.receipt:{paymentId:N}";

    private async Task AcquireReceiptLockAsync(Guid paymentId, CancellationToken ct)
    {
        var result = new SqlParameter("@result", SqlDbType.Int) { Direction = ParameterDirection.Output };
        await db.Database.ExecuteSqlRawAsync("EXEC @result = sp_getapplock @Resource = {0}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = {1}",
            [ReceiptLock(paymentId), ReceiptLockTimeoutMilliseconds, result], ct);
        if (result.Value is not int code || code < 0)
            throw new BillingException("receipt_busy", "Receipts are being issued for this payment right now. Try again in a moment.", 503);
    }

    private async Task<Guid?> PaymentIdAsync(Guid accountId, int? paymentNumber, CancellationToken ct) => paymentNumber is int n
        ? await db.BillingEntries.AsNoTracking().Where(e => e.AccountId == accountId && e.EntryNumber == n && e.Kind == BillingKinds.Payment).Select(e => (Guid?)e.Id).FirstOrDefaultAsync(ct)
        : null;

    /// <summary>The payment's latest receipt that no other receipt replaces, if any.</summary>
    private Task<FinancialDocument?> LatestOpenReceiptAsync(Guid accountId, Guid paymentId, CancellationToken ct) =>
        db.FinancialDocuments.AsNoTracking()
            .Where(d => d.AccountId == accountId && d.PaymentEntryId == paymentId && d.DocumentType == FinancialDocumentTypes.Receipt && !db.FinancialDocuments.Any(r => r.ReplacesDocumentId == d.Id))
            .OrderByDescending(d => d.DocumentNumber).FirstOrDefaultAsync(ct);

    /// <summary>Applies the one-receipt rule for a receipt of <paramref name="paymentId"/>; returns the receipt being replaced (null for a first receipt).</summary>
    private async Task<FinancialDocument?> CheckReceiptRuleAsync(Guid accountId, Guid paymentId, int paymentNumber, Guid? replaces, CancellationToken ct)
    {
        var latest = await LatestOpenReceiptAsync(accountId, paymentId, ct);
        if (replaces is null)
        {
            if (latest is not null)
                throw new BillingException("receipt_exists", $"Payment {paymentNumber} already has receipt {latest.DocumentNumber}. Open receipt {latest.DocumentNumber}, or issue a replacement with a reason.", 409);
            return null;
        }
        if (latest is not null && latest.Id == replaces) return latest;
        var named = await db.FinancialDocuments.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == replaces && d.AccountId == accountId && d.PaymentEntryId == paymentId && d.DocumentType == FinancialDocumentTypes.Receipt, ct)
            ?? throw new BillingException("receipt_not_found", "That receipt was not found for this payment.", 404);
        throw new BillingException("receipt_replaced",
            $"Receipt {named.DocumentNumber} has already been replaced{(latest is null ? "" : $" by receipt {latest.DocumentNumber}")}. Open the newer receipt, or replace that one instead.", 409);
    }

    private static string? CleanReplacementReason(Guid? replaces, string? reason)
    {
        if (replaces is null) return null;
        var text = reason?.Trim();
        var problem = string.IsNullOrEmpty(text) ? "Say why the receipt is being replaced."
            : text.Length > FinancialDocumentLimits.ReplacementReasonMax ? $"Keep the reason to {FinancialDocumentLimits.ReplacementReasonMax} characters."
            : text.Any(char.IsControl) ? "The reason cannot contain control characters."
            : null;
        return problem is null ? text : throw BillingRules.Invalid(new Dictionary<string, string> { ["replacementReason"] = problem });
    }

    /// <summary>The issued document as listed, with what is true of it now. <paramref name="math"/> is the account's ledger when the caller has it already (the list loads it once).</summary>
    private async Task<IssuedDocumentView> ViewOfDocumentAsync(FinancialDocument d, CancellationToken ct, IReadOnlyList<LedgerEntry>? math = null)
    {
        var names = await ClinicalNames.ResolveAsync(db, [(Guid?)d.IssuedByUserId], ct);
        var paymentNumber = d.PaymentEntryId is Guid id ? await db.BillingEntries.AsNoTracking().Where(e => e.Id == id).Select(e => (int?)e.EntryNumber).FirstAsync(ct) : null;
        var view = new IssuedDocumentView(d.Id, d.DocumentType, d.DocumentNumber, paymentNumber, new FinancialCutOffs(d.AsOfEntryNumber, d.AsOfAllocationNumber, d.AsOfReleaseNumber, d.AsOfResponsibilitySequence, d.AsOfUtc),
            d.TemplateVersion, d.ContentHash, d.IssuedAtUtc, ClinicalNames.Name(names, d.IssuedByUserId));
        if (d.DocumentType != FinancialDocumentTypes.Receipt || d.PaymentEntryId is not Guid payment || paymentNumber is not int number) return view;

        var siblings = await db.FinancialDocuments.AsNoTracking().Where(x => x.PaymentEntryId == payment && x.DocumentType == FinancialDocumentTypes.Receipt)
            .Select(x => new { x.Id, x.DocumentNumber, x.ReplacesDocumentId }).ToListAsync(ct);
        var replacesNumber = d.ReplacesDocumentId is Guid r ? siblings.FirstOrDefault(x => x.Id == r)?.DocumentNumber : null;
        var supersededBy = siblings.FirstOrDefault(x => x.ReplacesDocumentId == d.Id)?.DocumentNumber;
        var legacy = d.ReplacesDocumentId is null && siblings.Count(x => x.ReplacesDocumentId is null) > 1;      // several receipts with no replacement link between them (OQ-4)
        math ??= (await LoadLedgerAsync(d.AccountId, ct)).Math;
        return view with
        {
            ReplacesNumber = replacesNumber, ReplacementReason = d.ReplacementReason, SupersededByNumber = supersededBy, LegacyDuplicate = legacy,
            PaymentReversed = BillingLedgerMath.Reversed(math).Contains(number), PaymentRefunded = BillingLedgerMath.Refunded(math, number),
        };
    }

    /// <summary>The replacement and legacy facts of a receipt as notes beside it (not part of the document).</summary>
    private static IEnumerable<string> ReplacementNotes(IssuedDocumentView view)
    {
        if (view.ReplacesNumber is int replaced) yield return $"This receipt replaces receipt {replaced}. Reason: {view.ReplacementReason}";
        if (view.SupersededByNumber is int newer) yield return $"Superseded by receipt {newer}.";
        if (view.LegacyDuplicate) yield return "Multiple legacy receipts exist for this payment.";
    }

    /// <summary>Money in a note, written the way every screen writes it ("$264.00", "-$5.00").</summary>
    private static string NoteMoney(decimal amount) => amount.ToString("C2", CultureInfo.GetCultureInfo("en-US"));
}
