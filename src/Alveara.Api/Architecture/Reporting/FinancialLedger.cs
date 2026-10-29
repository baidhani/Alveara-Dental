using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Billing;
using Alveara.Api.Architecture.Imports;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Reporting;

/// <summary>
/// ALV-N006: the lines every ledger entry falls into for the financial reports. Each entry is in exactly ONE line, and a reversal is in the line of the entry it undoes (a reversed charge is a charge reversal, a reversed
/// payment a payment reversal), so the lines add up to the ledger itself. An opening balance loaded by bulk import (an adjustment with the migration reason and an opening-balance batch as its correlation id, and any reversal
/// of one) is its own line and is never production, collections or an ordinary adjustment. <see cref="Unclassified"/> should always be empty; it exists so that an entry nobody foresaw is shown, never dropped.
/// </summary>
public static class FinancialLines
{
    public const string Charges = "Charges";
    public const string ChargeReversals = "ChargeReversals";
    public const string Payments = "Payments";
    public const string PaymentReversals = "PaymentReversals";
    public const string Refunds = "Refunds";
    public const string RefundReversals = "RefundReversals";
    public const string Adjustments = "Adjustments";
    public const string AdjustmentReversals = "AdjustmentReversals";
    public const string OpeningBalances = "OpeningBalances";
    public const string Unclassified = "Unclassified";

    public static readonly IReadOnlyList<string> All = [Charges, ChargeReversals, Payments, PaymentReversals, Refunds, RefundReversals, Adjustments, AdjustmentReversals, OpeningBalances, Unclassified];
    public static readonly IReadOnlyList<string> Production = [Charges, ChargeReversals];
    public static readonly IReadOnlyList<string> Collections = [Payments, PaymentReversals, Refunds, RefundReversals];
}

/// <summary>One ledger entry as the financial reports see it: its line, its amount as stored (the effect on the balance owed: charges positive, payments negative) and the facts a drill-down shows. No name.</summary>
public sealed class LedgerRow
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    public int EntryNumber { get; set; }
    public string Kind { get; set; } = "";
    public string Line { get; set; } = "";
    public decimal Amount { get; set; }
    public DateTimeOffset RecordedAtUtc { get; set; }
    /// <summary>The payment method kind (Cash, Check, Card, Other); for a reversal, the one of the payment it undoes. Null on older entries.</summary>
    public string? PaymentMethod { get; set; }
    /// <summary>The plan procedure a charge bills; for a reversal, the one of the charge it undoes.</summary>
    public Guid? PlanItemId { get; set; }
    public string? ProcedureCode { get; set; }
}

/// <summary>A plan procedure and the provider currently credited with it.</summary>
public sealed class ItemProvider
{
    public Guid PlanItemId { get; set; }
    public Guid ProviderProfileId { get; set; }
}

public sealed record FinancialLineTotal(string Line, int Entries, decimal Amount);

/// <summary>
/// The proof that a financial report agrees with the ledger: <see cref="LinesTotal"/> is the sum of the lines the report classified, <see cref="LedgerTotal"/> an independent sum of the ledger entries in scope (no
/// classification), and <see cref="Reconciled"/> is true when both the amounts and the entry counts match. <see cref="Scope"/> is <c>WholeLedger</c> or, when a provider filter is set, <c>ProviderProduction</c>.
/// </summary>
public sealed record FinancialLedgerLines(IReadOnlyList<FinancialLineTotal> Lines, decimal LinesTotal, int LinesEntries, decimal LedgerTotal, int LedgerEntries, bool Reconciled, string Scope);

/// <summary>ALV-N006: reads the ledger for the financial reports. Reads only; every figure comes from <c>BillingEntries</c> (the append-only ledger), never from a stored balance.</summary>
public sealed class FinancialLedger(AlveraDbContext db)
{
    /// <summary>The batches that loaded opening balances: a small set, so it is passed to the query as a list.</summary>
    public Task<Guid[]> OpeningBatchIdsAsync(CancellationToken ct) =>
        db.ImportBatches.AsNoTracking().Where(b => b.SourceKind == ImportSourceKind.OpeningBalances).Select(b => b.Id).ToArrayAsync(ct);

    /// <summary>The entries recorded in [start, end), each with its line. Dated by when it was recorded.</summary>
    public IQueryable<LedgerRow> Classified(DateTimeOffset start, DateTimeOffset end, Guid[] openingBatches)
    {
        var reason = BillingService.OpeningBalanceReason;
        return from e in db.BillingEntries.AsNoTracking()
               where e.RecordedAtUtc >= start && e.RecordedAtUtc < end
               join r in db.BillingEntries.AsNoTracking() on e.ReversesEntryId equals r.Id into reversed
               from r in reversed.DefaultIfEmpty()
               select new LedgerRow
               {
                   Id = e.Id, PatientId = e.PatientId, EntryNumber = e.EntryNumber, Kind = e.Kind, Amount = e.Amount, RecordedAtUtc = e.RecordedAtUtc,
                   PaymentMethod = e.PaymentMethod ?? r.PaymentMethod, PlanItemId = e.PlanItemId ?? r.PlanItemId, ProcedureCode = e.ProcedureCode ?? r.ProcedureCode,
                   Line =
                       e.Kind == BillingKinds.Charge ? FinancialLines.Charges
                     : e.Kind == BillingKinds.Payment ? FinancialLines.Payments
                     : e.Kind == BillingKinds.Refund ? FinancialLines.Refunds
                     : e.Kind == BillingKinds.Adjustment ? (e.Reason == reason && e.CorrelationId != null && openingBatches.Contains(e.CorrelationId.Value) ? FinancialLines.OpeningBalances : FinancialLines.Adjustments)
                     : e.Kind == BillingKinds.Reversal
                         ? (r.Kind == BillingKinds.Charge ? FinancialLines.ChargeReversals
                          : r.Kind == BillingKinds.Payment ? FinancialLines.PaymentReversals
                          : r.Kind == BillingKinds.Refund ? FinancialLines.RefundReversals
                          : r.Kind == BillingKinds.Adjustment ? (r.Reason == reason && r.CorrelationId != null && openingBatches.Contains(r.CorrelationId.Value) ? FinancialLines.OpeningBalances : FinancialLines.AdjustmentReversals)
                          : FinancialLines.Unclassified)
                     : FinancialLines.Unclassified,
               };
    }

    /// <summary>The totals of every line (a row for each line, zero or not, in a fixed order), and the proof against an independent sum of the same entries.</summary>
    public async Task<FinancialLedgerLines> WholeLedgerAsync(DateTimeOffset start, DateTimeOffset end, Guid[] openingBatches, CancellationToken ct)
    {
        var grouped = await Classified(start, end, openingBatches).GroupBy(c => c.Line).Select(g => new { Line = g.Key, Entries = g.Count(), Amount = g.Sum(c => c.Amount) }).ToDictionaryAsync(g => g.Line, ct);
        var lines = FinancialLines.All.Select(l => grouped.TryGetValue(l, out var g) ? new FinancialLineTotal(l, g.Entries, g.Amount) : new FinancialLineTotal(l, 0, 0m)).ToList();
        var inRange = db.BillingEntries.AsNoTracking().Where(e => e.RecordedAtUtc >= start && e.RecordedAtUtc < end);
        var ledgerTotal = await inRange.SumAsync(e => e.Amount, ct);
        var ledgerEntries = await inRange.CountAsync(ct);
        return Proof(lines, ledgerTotal, ledgerEntries, "WholeLedger");
    }

    /// <summary>
    /// Each completed procedure's CURRENT provider: the latest amendment that names one, else the provider it was completed with (the rule the completion screens use). One row for each plan procedure (a procedure
    /// is completed once). A charge whose procedure is not here (for example a legacy charge that was never adopted) has no provider and is shown as not attributed.
    /// </summary>
    public IQueryable<ItemProvider> EffectiveProviders() =>
        db.CompletedProcedures.AsNoTracking().Select(cp => new ItemProvider
        {
            PlanItemId = cp.PlanItemId,
            ProviderProfileId = db.CompletedProcedureAmendments.Where(a => a.CompletedProcedureId == cp.Id && a.ProviderProfileId != null).OrderByDescending(a => a.AmendmentNumber).Select(a => a.ProviderProfileId).FirstOrDefault() ?? cp.ProviderProfileId,
        });

    public static FinancialLedgerLines Proof(IReadOnlyList<FinancialLineTotal> lines, decimal ledgerTotal, int ledgerEntries, string scope)
    {
        var total = lines.Sum(l => l.Amount);
        var entries = lines.Sum(l => l.Entries);
        return new FinancialLedgerLines(lines, total, entries, ledgerTotal, ledgerEntries, total == ledgerTotal && entries == ledgerEntries, scope);
    }
}
