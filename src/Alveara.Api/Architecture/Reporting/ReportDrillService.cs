using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Alveara.Api.Architecture.Scheduling;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Reporting;

/// <summary>One appointment behind a number. Ids and times only: the screen shows the patient's name through the patient views it is already allowed to use, so a report never carries a name itself.</summary>
public sealed record ReportAppointmentRow(Guid Id, Guid PatientId, DateTimeOffset StartUtc, int DurationMinutes, Guid ProviderProfileId, Guid OperatoryId, string Status, string FlowState);

/// <summary>Where the next page starts: after this start time and, for appointments starting at the same instant, after this id.</summary>
public sealed record ReportCursor(DateTimeOffset AfterStart, Guid AfterId);

/// <summary><see cref="Total"/> is the number of appointments that match, which is the number in the report cell that was clicked (the reconciliation); <see cref="Rows"/> is one page of them.</summary>
public sealed record ReportPage<T>(string ReportId, ReportFilterView Filter, int Total, IReadOnlyList<T> Rows, ReportCursor? Next);

/// <summary>Which cell of a report to list: a booking status, a visit state (a visit state implies a Scheduled booking), a practice-local day and/or one booked provider, and the page.</summary>
public sealed record DrillCriteria(string? Status, string? FlowState, string? Date, Guid? ProviderId, int? Limit, DateTimeOffset? AfterStart, Guid? AfterId);

/// <summary>
/// ALV-N006: the supporting records behind a report number. The criteria narrow the SAME scoped set the report counted (<see cref="ReportSupport.Appointments"/>), so the <c>Total</c> of a drill-down equals the number
/// in the cell it came from. Pages are keyed on (start time, id), so a page never repeats or skips a record when others are added. Every call is recorded like a report run (the number of records returned).
/// Failure modes handled: an unknown status or visit state, a day outside the range, a bad limit or half a cursor (each a stable 400 code), an audit entry that cannot be saved (503).
/// </summary>
public class ReportDrillService(AlveraDbContext db, ReportFilterResolver resolver, IOptions<ReportOptions> options, ILogger<ReportDrillService> log)
{
    private static readonly string[] Statuses = [AppointmentStatuses.Scheduled, AppointmentStatuses.Cancelled, AppointmentStatuses.NoShow];

    public async Task<ReportPage<ReportAppointmentRow>> AppointmentsAsync(string reportId, ReportFilter filter, DrillCriteria criteria, Guid actor, CancellationToken ct)
    {
        var opts = options.Value;
        if (criteria.Status is not null && !Statuses.Contains(criteria.Status)) throw new ReportException("invalid_status", $"The status must be one of {string.Join(", ", Statuses)}.", 400);
        if (criteria.FlowState is not null && !VisitStates.IsKnown(criteria.FlowState)) throw new ReportException("invalid_flow_state", $"The visit state must be one of {string.Join(", ", VisitStates.InOrder)}.", 400);
        var limit = criteria.Limit ?? opts.DefaultDrillRows;
        if (limit < 1 || limit > opts.MaxDrillRows) throw new ReportException("invalid_limit", $"The page size must be between 1 and {opts.MaxDrillRows}.", 400);
        if ((criteria.AfterStart is null) != (criteria.AfterId is null)) throw new ReportException("invalid_cursor", "Give both parts of the page position, or neither.", 400);

        DateTimeOffset? dayStart = null, dayEnd = null;
        if (criteria.Date is not null)
        {
            var day = ReportFilterResolver.ParseDate(criteria.Date);
            if (day < filter.From || day > filter.To) throw new ReportException("date_outside_range", "That day is outside the report's range.", 400);
            (dayStart, dayEnd) = (resolver.StartOfDayUtc(day), resolver.StartOfDayUtc(day.AddDays(1)));
        }

        int total;
        List<ReportAppointmentRow> rows;
        await using (await ReportReadSession.OpenAsync(db, log, ct))
        {
            var query = ReportSupport.Appointments(db, filter);
            if (criteria.Status is not null) query = query.Where(a => a.Status == criteria.Status);
            if (criteria.FlowState is not null) query = query.Where(a => a.Status == AppointmentStatuses.Scheduled && a.FlowState == criteria.FlowState);
            if (criteria.ProviderId is { } provider) query = query.Where(a => a.ProviderProfileId == provider);
            if (dayStart is { } start && dayEnd is { } end) query = query.Where(a => a.StartUtc >= start && a.StartUtc < end);
            total = await query.CountAsync(ct);

            if (criteria.AfterStart is { } afterStart && criteria.AfterId is { } afterId)
                query = query.Where(a => a.StartUtc > afterStart || (a.StartUtc == afterStart && a.Id.CompareTo(afterId) > 0));
            rows = await query.OrderBy(a => a.StartUtc).ThenBy(a => a.Id).Take(limit + 1)
                .Select(a => new ReportAppointmentRow(a.Id, a.PatientId, a.StartUtc, a.DurationMinutes, a.ProviderProfileId, a.OperatoryId, a.Status, a.FlowState)).ToListAsync(ct);
        }

        var more = rows.Count > limit;
        if (more) rows.RemoveAt(rows.Count - 1);
        await ReportSupport.AuditAsync(db, log, reportId, true, filter, rows.Count, actor, ct);
        return new ReportPage<ReportAppointmentRow>(reportId, ReportSupport.View(filter), total, rows, more ? new ReportCursor(rows[^1].StartUtc, rows[^1].Id) : null);
    }
}
