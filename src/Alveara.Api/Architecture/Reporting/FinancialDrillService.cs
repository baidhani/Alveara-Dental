using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Reporting;

/// <summary>One ledger entry behind a financial number. Ids, kind, amount and time only: no name, no reason text. <see cref="Amount"/> is the entry as stored (its effect on the balance owed).</summary>
public sealed record FinancialEntryRow(Guid Id, Guid PatientId, int EntryNumber, string Kind, string Line, decimal Amount, DateTimeOffset RecordedAtUtc, string? PaymentMethod, string? ProcedureCode);

public sealed record FinancialCursor(DateTimeOffset AfterRecordedAtUtc, Guid AfterId);

/// <summary><see cref="Total"/> and <see cref="TotalAmount"/> are the number of entries and their amount as stored: the figures of the report cell that was clicked (a reconciliation); <see cref="Rows"/> is one page.</summary>
public sealed record FinancialPage(string ReportId, ReportFilterView Filter, int Total, decimal TotalAmount, IReadOnlyList<FinancialEntryRow> Rows, FinancialCursor? Next);

/// <summary>Which cell to list: a ledger line of the report, for collections a method kind (<c>Unspecified</c> for entries that recorded none), for production one provider (<c>Guid.Empty</c> for "not attributed"), and the page.</summary>
public sealed record FinancialDrillCriteria(string? Line, string? Method, Guid? ProviderId, int? Limit, DateTimeOffset? AfterRecordedAtUtc, Guid? AfterId);

/// <summary>
/// ALV-N006 slice 2: the ledger entries behind a production or collections number. The criteria narrow the SAME classified set the report summed, so <c>Total</c> and <c>TotalAmount</c> equal the cell clicked. Pages are keyed
/// on (recorded time, id). Every call is recorded like a report run. Failure modes handled: a line that does not belong to the report, a criterion the report does not support, a bad limit or half a cursor (stable 400
/// codes), an audit entry that cannot be saved (503).
/// </summary>
public class FinancialDrillService(AlveraDbContext db, IOptions<ReportOptions> options, ILogger<FinancialDrillService> log)
{
    private const string Unspecified = "Unspecified";

    public async Task<FinancialPage> EntriesAsync(string reportId, ReportFilter filter, FinancialDrillCriteria criteria, Guid actor, CancellationToken ct)
    {
        var production = reportId == ReportIds.Production;
        var opts = options.Value;
        if (filter.LocationId is not null || (!production && filter.ProviderIds.Count > 0)) throw new ReportException("filter_not_supported", "That filter is not available for this report.", 400);
        var allowed = production ? FinancialLines.Production : FinancialLines.Collections;
        if (criteria.Line is not null && !allowed.Contains(criteria.Line)) throw new ReportException("invalid_line", $"The line must be one of {string.Join(", ", allowed)}.", 400);
        if (criteria.ProviderId is not null && !production) throw new ReportException("filter_not_supported", "A provider can be chosen for production only.", 400);
        if (criteria.Method is not null && production) throw new ReportException("filter_not_supported", "A payment method can be chosen for collections only.", 400);
        var limit = criteria.Limit ?? opts.DefaultDrillRows;
        if (limit < 1 || limit > opts.MaxDrillRows) throw new ReportException("invalid_limit", $"The page size must be between 1 and {opts.MaxDrillRows}.", 400);
        if ((criteria.AfterRecordedAtUtc is null) != (criteria.AfterId is null)) throw new ReportException("invalid_cursor", "Give both parts of the page position, or neither.", 400);

        var ledger = new FinancialLedger(db);
        int total;
        decimal totalAmount;
        List<FinancialEntryRow> rows;
        await using (await ReportReadSession.OpenAsync(db, log, ct))
        {
            var batches = await ledger.OpeningBatchIdsAsync(ct);
            var lines = criteria.Line is null ? allowed.ToArray() : [criteria.Line];
            var query = ledger.Classified(filter.StartUtc, filter.EndUtc, batches).Where(c => lines.Contains(c.Line));
            if (production)
            {
                var effective = ledger.EffectiveProviders();
                if (filter.ProviderIds.Count > 0)
                {
                    var chosen = filter.ProviderIds.ToArray();
                    query = query.Where(c => c.PlanItemId != null && effective.Any(p => p.PlanItemId == c.PlanItemId && chosen.Contains(p.ProviderProfileId)));
                }
                if (criteria.ProviderId is { } provider)
                    query = provider == Guid.Empty
                        ? query.Where(c => c.PlanItemId == null || !effective.Any(p => p.PlanItemId == c.PlanItemId))
                        : query.Where(c => c.PlanItemId != null && effective.Any(p => p.PlanItemId == c.PlanItemId && p.ProviderProfileId == provider));
            }
            if (criteria.Method is { } method)
                query = method == Unspecified ? query.Where(c => c.PaymentMethod == null) : query.Where(c => c.PaymentMethod == method);

            total = await query.CountAsync(ct);
            totalAmount = await query.SumAsync(c => c.Amount, ct);
            if (criteria.AfterRecordedAtUtc is { } afterTime && criteria.AfterId is { } afterId)
                query = query.Where(c => c.RecordedAtUtc > afterTime || (c.RecordedAtUtc == afterTime && c.Id.CompareTo(afterId) > 0));
            rows = await query.OrderBy(c => c.RecordedAtUtc).ThenBy(c => c.Id).Take(limit + 1)
                .Select(c => new FinancialEntryRow(c.Id, c.PatientId, c.EntryNumber, c.Kind, c.Line, c.Amount, c.RecordedAtUtc, c.PaymentMethod, c.ProcedureCode)).ToListAsync(ct);
        }

        var more = rows.Count > limit;
        if (more) rows.RemoveAt(rows.Count - 1);
        await ReportSupport.AuditAsync(db, log, reportId, true, filter, rows.Count, actor, ct);
        return new FinancialPage(reportId, ReportSupport.View(filter), total, totalAmount, rows, more ? new FinancialCursor(rows[^1].RecordedAtUtc, rows[^1].Id) : null);
    }
}
