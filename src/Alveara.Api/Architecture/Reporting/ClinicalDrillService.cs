using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Reporting;

/// <summary>One plan item behind a cell: ids, the item number, the practice's fee estimate and its state. No procedure name and no patient name.</summary>
public sealed record PlanItemDrillRow(Guid ItemId, Guid PlanId, Guid PatientId, int ItemNumber, decimal EstimatedFee, string Acceptance, string Scheduling, string Completion, bool Withdrawn);

/// <summary>One completed procedure behind a number: ids, the provider now credited, the effective performed time and the catalog code.</summary>
public sealed record ProcedureDrillRow(Guid Id, Guid PatientId, Guid PlanItemId, Guid ProviderProfileId, DateTimeOffset PerformedAtUtc, string CodeSystem, string Code, decimal EstimatedFee);

/// <summary>Where the next page starts: after this time (completed procedures only) and, for equal times, after this id.</summary>
public sealed record ClinicalCursor(DateTimeOffset? AfterTime, Guid AfterId);

/// <summary><see cref="Total"/> and <see cref="TotalEstimatedFee"/> are the figures of the report cell that was clicked (a reconciliation); <see cref="Rows"/> is one page.</summary>
public sealed record ClinicalPage<T>(string ReportId, ReportFilterView Filter, int Total, decimal TotalEstimatedFee, IReadOnlyList<T> Rows, ClinicalCursor? Next);

public sealed record PlanItemDrillCriteria(string? Cell, int? Limit, Guid? AfterId);

/// <summary>Which completed procedures: one provider, one catalog code (both parts of it), one practice-local day, and the page.</summary>
public sealed record ProcedureDrillCriteria(Guid? ProviderId, string? CodeSystem, string? Code, string? Date, int? Limit, DateTimeOffset? AfterPerformedAtUtc, Guid? AfterId);

/// <summary>
/// ALV-N006 slice 3a: the records behind the clinical numbers. The plan-item cells use the SAME definition as the report (<see cref="PlanItemCells.Predicate"/>), and the procedures narrow the same lines the report counted, so
/// each total equals the cell clicked. Pages are keyed on (time, id) or on the id. Every call is recorded like a report run. Failure modes handled: an unknown cell, half a code or half a cursor, a day outside the range, a bad
/// limit (stable 400 codes), a filter the report does not support, an audit entry that cannot be saved (503).
/// </summary>
public class ClinicalDrillService(AlveraDbContext db, ReportFilterResolver resolver, IOptions<ReportOptions> options, ILogger<ClinicalDrillService> log)
{
    private int Limit(int? requested)
    {
        var opts = options.Value;
        var limit = requested ?? opts.DefaultDrillRows;
        return limit < 1 || limit > opts.MaxDrillRows ? throw new ReportException("invalid_limit", $"The page size must be between 1 and {opts.MaxDrillRows}.", 400) : limit;
    }

    public async Task<ClinicalPage<PlanItemDrillRow>> PlanItemsAsync(ReportFilter filter, PlanItemDrillCriteria criteria, Guid actor, CancellationToken ct)
    {
        if (filter.ProviderIds.Count > 0 || filter.LocationId is not null) throw new ReportException("filter_not_supported", "That filter is not available for treatment-plan status.", 400);
        if (criteria.Cell is not null && !PlanItemCells.All.Contains(criteria.Cell)) throw new ReportException("invalid_cell", $"The cell must be one of {string.Join(", ", PlanItemCells.All)}.", 400);
        var limit = Limit(criteria.Limit);

        int total;
        decimal fee;
        List<PlanItemDrillRow> rows;
        await using (await ReportReadSession.OpenAsync(db, log, ct))
        {
            var query = new ClinicalQueries(db).PlanItems(filter.StartUtc, filter.EndUtc);
            if (criteria.Cell is not null) query = query.Where(PlanItemCells.Predicate(criteria.Cell));
            total = await query.CountAsync(ct);
            fee = total == 0 ? 0m : await query.SumAsync(r => r.Fee, ct);
            if (criteria.AfterId is { } after) query = query.Where(r => r.ItemId.CompareTo(after) > 0);
            rows = await query.OrderBy(r => r.ItemId).Take(limit + 1)
                .Select(r => new PlanItemDrillRow(r.ItemId, r.PlanId, r.PatientId, r.ItemNumber, r.Fee, r.Acceptance, r.Scheduling, r.Completion, r.Withdrawn)).ToListAsync(ct);
        }

        var more = rows.Count > limit;
        if (more) rows.RemoveAt(rows.Count - 1);
        await ReportSupport.AuditAsync(db, log, ReportIds.TreatmentPlans, true, filter, rows.Count, actor, ct);
        return new ClinicalPage<PlanItemDrillRow>(ReportIds.TreatmentPlans, ReportSupport.View(filter), total, fee, rows, more ? new ClinicalCursor(null, rows[^1].ItemId) : null);
    }

    /// <summary>The completed procedures behind a number of the completed-procedures report, or (with <paramref name="reportId"/>) of the provider-activity report, which counts the same procedures.</summary>
    public async Task<ClinicalPage<ProcedureDrillRow>> ProceduresAsync(ReportFilter filter, ProcedureDrillCriteria criteria, Guid actor, CancellationToken ct, string reportId = ReportIds.CompletedProcedures)
    {
        if (filter.LocationId is not null) throw new ReportException("filter_not_supported", "That filter is not available for completed procedures.", 400);
        var limit = Limit(criteria.Limit);
        if ((criteria.CodeSystem is null) != (criteria.Code is null)) throw new ReportException("invalid_code", "Give both the code system and the code, or neither.", 400);
        if ((criteria.AfterPerformedAtUtc is null) != (criteria.AfterId is null)) throw new ReportException("invalid_cursor", "Give both parts of the page position, or neither.", 400);
        DateTimeOffset? dayStart = null, dayEnd = null;
        if (criteria.Date is not null)
        {
            var day = ReportFilterResolver.ParseDate(criteria.Date);
            if (day < filter.From || day > filter.To) throw new ReportException("date_outside_range", "That day is outside the report's range.", 400);
            (dayStart, dayEnd) = (resolver.StartOfDayUtc(day), resolver.StartOfDayUtc(day.AddDays(1)));
        }

        int total;
        decimal fee;
        List<ProcedureDrillRow> rows;
        await using (await ReportReadSession.OpenAsync(db, log, ct))
        {
            var query = new ClinicalQueries(db).ProcedureLines(filter.StartUtc, filter.EndUtc, filter.ProviderIds.ToArray());
            if (criteria.ProviderId is { } provider) query = query.Where(l => l.ProviderProfileId == provider);
            if (criteria.Code is { } code && criteria.CodeSystem is { } system) query = query.Where(l => l.Code == code && l.CodeSystem == system);
            if (dayStart is { } start && dayEnd is { } end) query = query.Where(l => l.PerformedAtUtc >= start && l.PerformedAtUtc < end);
            total = await query.CountAsync(ct);
            fee = total == 0 ? 0m : await query.SumAsync(l => l.EstimatedFee, ct);
            if (criteria.AfterPerformedAtUtc is { } afterTime && criteria.AfterId is { } afterId)
                query = query.Where(l => l.PerformedAtUtc > afterTime || (l.PerformedAtUtc == afterTime && l.Id.CompareTo(afterId) > 0));
            rows = await query.OrderBy(l => l.PerformedAtUtc).ThenBy(l => l.Id).Take(limit + 1)
                .Select(l => new ProcedureDrillRow(l.Id, l.PatientId, l.PlanItemId, l.ProviderProfileId, l.PerformedAtUtc, l.CodeSystem, l.Code, l.EstimatedFee)).ToListAsync(ct);
        }

        var more = rows.Count > limit;
        if (more) rows.RemoveAt(rows.Count - 1);
        await ReportSupport.AuditAsync(db, log, reportId, true, filter, rows.Count, actor, ct);
        return new ClinicalPage<ProcedureDrillRow>(reportId, ReportSupport.View(filter), total, fee, rows, more ? new ClinicalCursor(rows[^1].PerformedAtUtc, rows[^1].Id) : null);
    }
}
