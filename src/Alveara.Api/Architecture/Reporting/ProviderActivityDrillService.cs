using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Alveara.Api.Architecture.Scheduling;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Reporting;

public static class ActivityMeasures
{
    public const string Booked = "Booked";
    public const string Scheduled = "Scheduled";
    public const string Cancelled = "Cancelled";
    public const string NoShow = "NoShow";
    public const string VisitsCompleted = "VisitsCompleted";
    public static readonly IReadOnlyList<string> All = [Booked, Scheduled, Cancelled, NoShow, VisitsCompleted];
}

/// <summary>Which appointments: one measure of the provider-activity report (required), one provider (for VisitsCompleted the provider who actually saw the patient, for the others the booked one), and the page.</summary>
public sealed record ActivityDrillCriteria(string? Measure, Guid? ProviderId, int? Limit, DateTimeOffset? AfterStart, Guid? AfterId);

/// <summary>
/// ALV-N006 slice 3b: the appointments behind a provider-activity number. The measures use the SAME rules as the report (<see cref="ProviderActivityReportService.CompletedVisits"/> and the booked provider), so each total
/// equals the cell clicked. The completed procedures behind the third measure are listed by <see cref="ClinicalDrillService.ProceduresAsync"/>. Pages are keyed on (start time, id). Every call is recorded like a report run.
/// Failure modes handled: a missing or unknown measure, a bad limit or half a cursor (stable 400 codes), a location filter (<c>filter_not_supported</c>), an audit entry that cannot be saved (503).
/// </summary>
public class ProviderActivityDrillService(AlveraDbContext db, IOptions<ReportOptions> options, ILogger<ProviderActivityDrillService> log)
{
    public async Task<ReportPage<ReportAppointmentRow>> AppointmentsAsync(ReportFilter filter, ActivityDrillCriteria criteria, Guid actor, CancellationToken ct)
    {
        var opts = options.Value;
        if (filter.LocationId is not null) throw new ReportException("filter_not_supported", "That filter is not available for provider activity.", 400);
        if (criteria.Measure is null || !ActivityMeasures.All.Contains(criteria.Measure)) throw new ReportException("invalid_measure", $"The measure must be one of {string.Join(", ", ActivityMeasures.All)}.", 400);
        var limit = criteria.Limit ?? opts.DefaultDrillRows;
        if (limit < 1 || limit > opts.MaxDrillRows) throw new ReportException("invalid_limit", $"The page size must be between 1 and {opts.MaxDrillRows}.", 400);
        if ((criteria.AfterStart is null) != (criteria.AfterId is null)) throw new ReportException("invalid_cursor", "Give both parts of the page position, or neither.", 400);

        var chosen = filter.ProviderIds.ToArray();
        int total;
        List<ReportAppointmentRow> rows;
        await using (await ReportReadSession.OpenAsync(db, log, ct))
        {
            IQueryable<Appointment> query;
            if (criteria.Measure == ActivityMeasures.VisitsCompleted)
            {
                query = ProviderActivityReportService.CompletedVisits(db, filter);
                if (chosen.Length > 0) query = query.Where(a => chosen.Contains(a.VisitProviderProfileId ?? a.ProviderProfileId));
                if (criteria.ProviderId is { } actual) query = query.Where(a => (a.VisitProviderProfileId ?? a.ProviderProfileId) == actual);
            }
            else
            {
                query = db.Appointments.AsNoTracking().Where(a => a.StartUtc >= filter.StartUtc && a.StartUtc < filter.EndUtc);
                if (chosen.Length > 0) query = query.Where(a => chosen.Contains(a.ProviderProfileId));
                if (criteria.ProviderId is { } booked) query = query.Where(a => a.ProviderProfileId == booked);
                if (criteria.Measure != ActivityMeasures.Booked) query = query.Where(a => a.Status == criteria.Measure);
            }

            total = await query.CountAsync(ct);
            if (criteria.AfterStart is { } afterStart && criteria.AfterId is { } afterId)
                query = query.Where(a => a.StartUtc > afterStart || (a.StartUtc == afterStart && a.Id.CompareTo(afterId) > 0));
            rows = await query.OrderBy(a => a.StartUtc).ThenBy(a => a.Id).Take(limit + 1)
                .Select(a => new ReportAppointmentRow(a.Id, a.PatientId, a.StartUtc, a.DurationMinutes, a.ProviderProfileId, a.OperatoryId, a.Status, a.FlowState)).ToListAsync(ct);
        }

        var more = rows.Count > limit;
        if (more) rows.RemoveAt(rows.Count - 1);
        await ReportSupport.AuditAsync(db, log, ReportIds.ProviderActivity, true, filter, rows.Count, actor, ct);
        return new ReportPage<ReportAppointmentRow>(ReportIds.ProviderActivity, ReportSupport.View(filter), total, rows, more ? new ReportCursor(rows[^1].StartUtc, rows[^1].Id) : null);
    }
}
