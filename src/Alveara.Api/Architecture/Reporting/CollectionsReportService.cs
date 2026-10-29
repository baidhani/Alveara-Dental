using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Reporting;

/// <summary>Collections for one payment method kind (Cash, Check, Card, Other; "Unspecified" for older entries that recorded none). <see cref="Net"/> = received - reversed - refunded.</summary>
public sealed record CollectionsMethodRow(string Method, int Payments, decimal Received, decimal Reversed, decimal Refunded, decimal Net);

/// <summary>
/// The collections and payments report, in money (not the ledger's signs): <see cref="PaymentsReceived"/> taken, <see cref="PaymentsReversed"/> undone, <see cref="Refunded"/> returned to payers (less any refund that was
/// itself reversed), and <see cref="NetCollections"/> = received - reversed - refunded. <see cref="Ledger"/> shows every line of the ledger for the period and the proof that the lines add up to the ledger.
/// </summary>
public sealed record CollectionsData(int PaymentEntries, decimal PaymentsReceived, decimal PaymentsReversed, decimal Refunded, decimal NetCollections, IReadOnlyList<CollectionsMethodRow> Methods, FinancialLedgerLines Ledger);

/// <summary>
/// ALV-N006 slice 2: collections and payments. Source: the ledger's Payment, Refund and Reversal entries recorded in the range, dated by when they were recorded. A reversal belongs to the payment or refund it undoes and
/// takes that entry's method. Provider and location filters are refused (<c>filter_not_supported</c>): a payment is not the work of one provider and has no location. Failure modes handled: nothing in the range
/// (<c>NoData</c>), an unreconciled ledger is reported as such, an audit entry that cannot be saved (503). Definition version 1.
/// </summary>
public class CollectionsReportService(AlveraDbContext db, IPracticeClock clock, ILogger<CollectionsReportService> log)
{
    public const int DefinitionVersion = 1;
    private const string Unspecified = "Unspecified";

    public async Task<ReportResult<CollectionsData>> RunAsync(ReportFilter filter, Guid actor, CancellationToken ct)
    {
        if (filter.ProviderIds.Count > 0 || filter.LocationId is not null) throw new ReportException("filter_not_supported", "Collections cannot be filtered by provider or location: a payment has neither.", 400);
        var ledger = new FinancialLedger(db);
        string consistency;
        CollectionsData data;
        await using (var session = await ReportReadSession.OpenAsync(db, log, ct))
        {
            consistency = session.ReadConsistency;
            var batches = await ledger.OpeningBatchIdsAsync(ct);
            var proof = await ledger.WholeLedgerAsync(filter.StartUtc, filter.EndUtc, batches, ct);
            var rows = await ledger.Classified(filter.StartUtc, filter.EndUtc, batches).Where(c => FinancialLines.Collections.Contains(c.Line))
                .GroupBy(c => new { c.PaymentMethod, c.Line }).Select(g => new { g.Key.PaymentMethod, g.Key.Line, Entries = g.Count(), Amount = g.Sum(x => x.Amount) }).ToListAsync(ct);

            // In money: payments are stored negative, so what was received is the opposite sign; a payment reversal and a refund are stored positive (they give money back).
            var methods = rows.GroupBy(r => r.PaymentMethod ?? Unspecified).Select(g =>
            {
                var received = -g.Where(r => r.Line == FinancialLines.Payments).Sum(r => r.Amount);
                var reversed = g.Where(r => r.Line == FinancialLines.PaymentReversals).Sum(r => r.Amount);
                var refunded = g.Where(r => r.Line == FinancialLines.Refunds).Sum(r => r.Amount) + g.Where(r => r.Line == FinancialLines.RefundReversals).Sum(r => r.Amount);
                return new CollectionsMethodRow(g.Key, g.Where(r => r.Line == FinancialLines.Payments).Sum(r => r.Entries), received, reversed, refunded, received - reversed - refunded);
            }).OrderBy(m => m.Method, StringComparer.Ordinal).ToList();

            var received = methods.Sum(m => m.Received);
            var reversedTotal = methods.Sum(m => m.Reversed);
            var refundedTotal = methods.Sum(m => m.Refunded);
            data = new CollectionsData(methods.Sum(m => m.Payments), received, reversedTotal, refundedTotal, received - reversedTotal - refundedTotal, methods, proof);
        }

        await ReportSupport.AuditAsync(db, log, ReportIds.Collections, false, filter, data.PaymentEntries, actor, ct);
        return new ReportResult<CollectionsData>(ReportIds.Collections, DefinitionVersion, ReportSupport.View(filter), clock.UtcNow, consistency, data.PaymentEntries, data.PaymentEntries == 0, data);
    }
}
