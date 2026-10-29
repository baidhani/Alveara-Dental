using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Alveara.Api.Architecture.Scheduling;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Reporting;

public static class CancellationMeasures
{
    public const string Booked = "Booked";
    public const string Cancelled = "Cancelled";
    public const string NoShow = "NoShow";
    public static readonly IReadOnlyList<string> All = [Booked, Cancelled, NoShow];
}

/// <summary>Which appointments: a measure (required), optionally one booked provider, one appointment type and, for cancelled ones, one lead-time bucket (see <see cref="LeadTimeBuckets"/>), and the page.</summary>
public sealed record CancellationDrillCriteria(string? Measure, Guid? ProviderId, Guid? AppointmentTypeId, string? LeadTime, int? Limit, DateTimeOffset? AfterStart, Guid? AfterId);

/// <summary>
/// ALV-N006 slice 4: the appointments behind a number of the cancellations report. The scope is the report's (<see cref="ReportSupport.Appointments"/>) and the lead-time buckets use the same whole-minute counts the report used,
/// so each total equals the cell clicked. Pages are keyed on (start time, id). Every call is recorded like a report run. Failure modes handled: a missing or unknown measure or bucket, a bucket asked for something other
/// than cancelled appointments, a bad limit or half a cursor (stable 400 codes), an audit entry that cannot be saved (503).
/// </summary>
public class CancellationDrillService(AlveraDbContext db, IOptions<ReportOptions> options, ILogger<CancellationDrillService> log)
{
    public async Task<ReportPage<ReportAppointmentRow>> AppointmentsAsync(ReportFilter filter, CancellationDrillCriteria criteria, Guid actor, CancellationToken ct)
    {
        var opts = options.Value;
        if (criteria.Measure is null || !CancellationMeasures.All.Contains(criteria.Measure)) throw new ReportException("invalid_measure", $"The measure must be one of {string.Join(", ", CancellationMeasures.All)}.", 400);
        if (criteria.LeadTime is not null && !LeadTimeBuckets.All.Contains(criteria.LeadTime)) throw new ReportException("invalid_lead_time", $"The lead time must be one of {string.Join(", ", LeadTimeBuckets.All)}.", 400);
        if (criteria.LeadTime is not null && criteria.Measure != CancellationMeasures.Cancelled) throw new ReportException("invalid_lead_time", "A lead time applies to cancelled appointments only.", 400);
        var limit = criteria.Limit ?? opts.DefaultDrillRows;
        if (limit < 1 || limit > opts.MaxDrillRows) throw new ReportException("invalid_limit", $"The page size must be between 1 and {opts.MaxDrillRows}.", 400);
        if ((criteria.AfterStart is null) != (criteria.AfterId is null)) throw new ReportException("invalid_cursor", "Give both parts of the page position, or neither.", 400);

        int total;
        List<ReportAppointmentRow> rows;
        await using (await ReportReadSession.OpenAsync(db, log, ct))
        {
            var query = ReportSupport.Appointments(db, filter);
            if (criteria.Measure != CancellationMeasures.Booked) query = query.Where(a => a.Status == criteria.Measure);
            if (criteria.ProviderId is { } provider) query = query.Where(a => a.ProviderProfileId == provider);
            if (criteria.AppointmentTypeId is { } type) query = query.Where(a => a.AppointmentTypeId == type);
            query = criteria.LeadTime switch
            {
                LeadTimeBuckets.LessThan24h => query.Where(a => a.StatusChangedAtUtc != null && EF.Functions.DateDiffMinute(a.StatusChangedAtUtc.Value, a.StartUtc) < LeadTimeBuckets.DayMinutes),
                LeadTimeBuckets.Within7Days => query.Where(a => a.StatusChangedAtUtc != null && EF.Functions.DateDiffMinute(a.StatusChangedAtUtc.Value, a.StartUtc) >= LeadTimeBuckets.DayMinutes && EF.Functions.DateDiffMinute(a.StatusChangedAtUtc.Value, a.StartUtc) < LeadTimeBuckets.WeekMinutes),
                LeadTimeBuckets.MoreThan7Days => query.Where(a => a.StatusChangedAtUtc != null && EF.Functions.DateDiffMinute(a.StatusChangedAtUtc.Value, a.StartUtc) >= LeadTimeBuckets.WeekMinutes),
                LeadTimeBuckets.Unknown => query.Where(a => a.StatusChangedAtUtc == null),
                _ => query,
            };
            total = await query.CountAsync(ct);
            if (criteria.AfterStart is { } afterStart && criteria.AfterId is { } afterId)
                query = query.Where(a => a.StartUtc > afterStart || (a.StartUtc == afterStart && a.Id.CompareTo(afterId) > 0));
            rows = await query.OrderBy(a => a.StartUtc).ThenBy(a => a.Id).Take(limit + 1)
                .Select(a => new ReportAppointmentRow(a.Id, a.PatientId, a.StartUtc, a.DurationMinutes, a.ProviderProfileId, a.OperatoryId, a.Status, a.FlowState)).ToListAsync(ct);
        }

        var more = rows.Count > limit;
        if (more) rows.RemoveAt(rows.Count - 1);
        await ReportSupport.AuditAsync(db, log, ReportIds.Cancellations, true, filter, rows.Count, actor, ct);
        return new ReportPage<ReportAppointmentRow>(ReportIds.Cancellations, ReportSupport.View(filter), total, rows, more ? new ReportCursor(rows[^1].StartUtc, rows[^1].Id) : null);
    }
}
