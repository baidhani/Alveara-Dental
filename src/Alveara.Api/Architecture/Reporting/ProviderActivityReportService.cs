using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Alveara.Api.Architecture.Scheduling;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Reporting;

/// <summary>
/// What one provider did in the range. <see cref="Booked"/> counts the appointments BOOKED with the provider (<see cref="Scheduled"/>, <see cref="Cancelled"/> and <see cref="NoShow"/> add up to it); <see cref="VisitsCompleted"/>
/// counts the visits finished with the provider who ACTUALLY saw the patient (the visit provider where one was recorded, else the booked one); <see cref="ProceduresCompleted"/> counts the procedures now credited to the provider.
/// They are three different measures, so a visit booked with one provider and seen by another is in the first provider's Booked and the second's VisitsCompleted.
/// </summary>
public sealed record ProviderActivityRow(Guid ProviderProfileId, string ProviderName, int Booked, int Scheduled, int Cancelled, int NoShow, int VisitsCompleted, int ProceduresCompleted);

public sealed record ProviderActivityTotals(int Booked, int Scheduled, int Cancelled, int NoShow, int VisitsCompleted, int ProceduresCompleted);

/// <summary><see cref="Reconciled"/> is true when every total equals an independent count and Scheduled + Cancelled + NoShow equals Booked.</summary>
public sealed record ProviderActivityData(IReadOnlyList<ProviderActivityRow> Providers, ProviderActivityTotals Totals, bool Reconciled);

/// <summary>
/// ALV-N006 slice 3b: provider activity. Source: the appointments that start in the range (booked provider, booking status, and for finished visits the actual provider) and the completed procedures performed in the range
/// (current provider and time). The provider filter keeps only the chosen providers in every measure; a location filter is refused (<c>filter_not_supported</c>): a completed procedure has no location. Failure modes
/// handled: nothing in the range (<c>NoData</c>), more rows than the limit (413), an audit entry that cannot be saved (503). Definition version 1.
/// </summary>
public class ProviderActivityReportService(AlveraDbContext db, IPracticeClock clock, IOptions<ReportOptions> options, ILogger<ProviderActivityReportService> log)
{
    public const int DefinitionVersion = 1;

    /// <summary>The appointments in the range whose visit is finished: a Scheduled booking in the Completed visit state.</summary>
    public static IQueryable<Appointment> CompletedVisits(AlveraDbContext db, ReportFilter filter) =>
        db.Appointments.AsNoTracking().Where(a => a.StartUtc >= filter.StartUtc && a.StartUtc < filter.EndUtc && a.Status == AppointmentStatuses.Scheduled && a.FlowState == VisitStates.Completed);

    public async Task<ReportResult<ProviderActivityData>> RunAsync(ReportFilter filter, Guid actor, CancellationToken ct)
    {
        if (filter.LocationId is not null) throw new ReportException("filter_not_supported", "Provider activity cannot be filtered by location: a completed procedure has none.", 400);
        var chosen = filter.ProviderIds.ToArray();
        string consistency;
        ProviderActivityData data;
        await using (var session = await ReportReadSession.OpenAsync(db, log, ct))
        {
            consistency = session.ReadConsistency;
            var booked = db.Appointments.AsNoTracking().Where(a => a.StartUtc >= filter.StartUtc && a.StartUtc < filter.EndUtc);
            if (chosen.Length > 0) booked = booked.Where(a => chosen.Contains(a.ProviderProfileId));
            var visits = CompletedVisits(db, filter).Select(a => new { Provider = a.VisitProviderProfileId ?? a.ProviderProfileId });
            if (chosen.Length > 0) visits = visits.Where(v => chosen.Contains(v.Provider));
            var procedures = new ClinicalQueries(db).Procedures().Where(p => p.PerformedAtUtc >= filter.StartUtc && p.PerformedAtUtc < filter.EndUtc);
            if (chosen.Length > 0) procedures = procedures.Where(p => chosen.Contains(p.ProviderProfileId));

            var bookedTotal = await booked.CountAsync(ct);
            var visitTotal = await visits.CountAsync(ct);
            var procedureTotal = await procedures.CountAsync(ct);
            ReportSupport.GuardSize(bookedTotal + procedureTotal, options.Value);

            var bookedRows = await booked.GroupBy(a => new { a.ProviderProfileId, a.Status }).Select(g => new { g.Key.ProviderProfileId, g.Key.Status, Count = g.Count() }).ToListAsync(ct);
            var visitRows = await visits.GroupBy(v => v.Provider).Select(g => new { Provider = g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Provider, g => g.Count, ct);
            var procedureRows = await procedures.GroupBy(p => p.ProviderProfileId).Select(g => new { Provider = g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Provider, g => g.Count, ct);

            var ids = bookedRows.Select(r => r.ProviderProfileId).Concat(visitRows.Keys).Concat(procedureRows.Keys).Concat(chosen).Distinct().ToArray();
            var names = await db.ProviderProfiles.AsNoTracking().Where(p => ids.Contains(p.Id)).Select(p => new { p.Id, Name = p.StaffProfile!.DisplayName }).ToDictionaryAsync(p => p.Id, p => p.Name, ct);
            int Booked(Guid id, string? status = null) => bookedRows.Where(r => r.ProviderProfileId == id && (status == null || r.Status == status)).Sum(r => r.Count);
            var providers = ids.Select(id => new ProviderActivityRow(id, names.GetValueOrDefault(id, "Unknown provider"), Booked(id), Booked(id, AppointmentStatuses.Scheduled), Booked(id, AppointmentStatuses.Cancelled), Booked(id, AppointmentStatuses.NoShow),
                visitRows.GetValueOrDefault(id), procedureRows.GetValueOrDefault(id))).OrderBy(p => p.ProviderName, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.ProviderProfileId).ToList();

            var totals = new ProviderActivityTotals(providers.Sum(p => p.Booked), providers.Sum(p => p.Scheduled), providers.Sum(p => p.Cancelled), providers.Sum(p => p.NoShow), providers.Sum(p => p.VisitsCompleted), providers.Sum(p => p.ProceduresCompleted));
            var reconciled = totals.Booked == bookedTotal && totals.VisitsCompleted == visitTotal && totals.ProceduresCompleted == procedureTotal && totals.Scheduled + totals.Cancelled + totals.NoShow == totals.Booked;
            data = new ProviderActivityData(providers, totals, reconciled);
        }

        var records = data.Totals.Booked + data.Totals.ProceduresCompleted;
        await ReportSupport.AuditAsync(db, log, ReportIds.ProviderActivity, false, filter, records, actor, ct);
        return new ReportResult<ProviderActivityData>(ReportIds.ProviderActivity, DefinitionVersion, ReportSupport.View(filter), clock.UtcNow, consistency, records, records == 0, data);
    }
}
