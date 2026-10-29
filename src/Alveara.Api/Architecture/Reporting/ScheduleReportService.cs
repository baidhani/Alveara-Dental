using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Alveara.Api.Architecture.Scheduling;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Reporting;

/// <summary>Counts for a set of appointments. <see cref="BookedMinutes"/> is the booked length of the appointments that still hold time (Scheduled); a cancelled or no-show appointment holds none.</summary>
public sealed record ScheduleCounts(int Appointments, int Scheduled, int Cancelled, int NoShow, int BookedMinutes);

public sealed record ScheduleDayRow(string Date, ScheduleCounts Counts);

public sealed record ScheduleProviderRow(Guid ProviderProfileId, string ProviderName, ScheduleCounts Counts);

/// <summary>The schedule report: totals, one row for every practice-local day of the range (a day with nothing booked is a row of zeros, so a gap is visible), one row for each booked provider, and the reschedules recorded.</summary>
public sealed record ScheduleReportData(ScheduleCounts Totals, int RescheduleEvents, IReadOnlyList<ScheduleDayRow> Days, IReadOnlyList<ScheduleProviderRow> Providers);

/// <summary>
/// ALV-N006: the schedule report. Source: the appointments whose start falls in the range (<see cref="ReportSupport.Appointments"/>), grouped by practice-local day and by BOOKED provider, and the append-only
/// appointment history for the reschedules. The three views (totals, days, providers) are made from the same rows, and every cell can be listed with <see cref="ReportDrillService"/>, which is how the numbers are
/// reconciled to the records. Not included, on purpose: utilisation or availability (that needs the providers' weekly windows and blocked time, which this report does not interpret). Definition version 1.
/// Failure modes handled: a range with nothing in it (<c>NoData</c>), more rows than the limit (413), an audit entry that cannot be saved (503). The run only reads, so repeating it is safe.
/// </summary>
public class ScheduleReportService(AlveraDbContext db, IPracticeClock clock, IOptions<ReportOptions> options, ILogger<ScheduleReportService> log)
{
    public const int DefinitionVersion = 1;

    public async Task<ReportResult<ScheduleReportData>> RunAsync(ReportFilter filter, Guid actor, CancellationToken ct)
    {
        string consistency;
        ScheduleReportData data;
        await using (var session = await ReportReadSession.OpenAsync(db, log, ct))
        {
            consistency = session.ReadConsistency;
            var scoped = ReportSupport.Appointments(db, filter);
            ReportSupport.GuardSize(await scoped.CountAsync(ct), options.Value);
            var rows = await scoped.Select(a => new { a.ProviderProfileId, a.Status, a.StartUtc, a.DurationMinutes }).ToListAsync(ct);
            var reschedules = await db.AppointmentEvents.AsNoTracking().CountAsync(e => e.EventType == AppointmentEventTypes.Rescheduled && scoped.Any(a => a.Id == e.AppointmentId), ct);

            var providerIds = rows.Select(r => r.ProviderProfileId).Concat(filter.ProviderIds).Distinct().ToArray();
            var names = await db.ProviderProfiles.AsNoTracking().Where(p => providerIds.Contains(p.Id)).Select(p => new { p.Id, Name = p.StaffProfile!.DisplayName }).ToDictionaryAsync(p => p.Id, p => p.Name, ct);

            var days = new List<ScheduleDayRow>(filter.Days);
            var byDay = rows.GroupBy(r => DateOnly.FromDateTime(clock.ToPracticeLocal(r.StartUtc).DateTime)).ToDictionary(g => g.Key, g => Count(g.Select(x => (x.Status, x.DurationMinutes))));
            for (var day = filter.From; day <= filter.To; day = day.AddDays(1))
                days.Add(new ScheduleDayRow(day.ToString("yyyy-MM-dd"), byDay.TryGetValue(day, out var counts) ? counts : Count([])));

            var providers = providerIds
                .Select(id => new ScheduleProviderRow(id, names.GetValueOrDefault(id, "Unknown provider"), Count(rows.Where(r => r.ProviderProfileId == id).Select(x => (x.Status, x.DurationMinutes)))))
                .OrderBy(p => p.ProviderName, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.ProviderProfileId).ToList();
            data = new ScheduleReportData(Count(rows.Select(x => (x.Status, x.DurationMinutes))), reschedules, days, providers);
        }

        await ReportSupport.AuditAsync(db, log, ReportIds.Schedule, false, filter, data.Totals.Appointments, actor, ct);
        return new ReportResult<ScheduleReportData>(ReportIds.Schedule, DefinitionVersion, ReportSupport.View(filter), clock.UtcNow, consistency, data.Totals.Appointments, data.Totals.Appointments == 0, data);
    }

    private static ScheduleCounts Count(IEnumerable<(string Status, int DurationMinutes)> appointments)
    {
        int total = 0, scheduled = 0, cancelled = 0, noShow = 0, minutes = 0;
        foreach (var (status, duration) in appointments)
        {
            total++;
            if (status == AppointmentStatuses.Scheduled) { scheduled++; minutes += duration; }
            else if (status == AppointmentStatuses.Cancelled) cancelled++;
            else if (status == AppointmentStatuses.NoShow) noShow++;
        }
        return new ScheduleCounts(total, scheduled, cancelled, noShow, minutes);
    }
}
