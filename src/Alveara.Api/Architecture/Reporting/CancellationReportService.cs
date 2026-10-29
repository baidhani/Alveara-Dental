using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Alveara.Api.Architecture.Scheduling;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Reporting;

/// <summary>
/// How early a cancellation was made, measured from the moment the status changed to the start of the appointment, in whole minutes as the database counts them. A status change after the start counts as under 24 hours. An appointment
/// cancelled with no recorded time is <see cref="Unknown"/>, never guessed.
/// </summary>
public static class LeadTimeBuckets
{
    public const string LessThan24h = "LessThan24h";
    public const string Within7Days = "Within7Days";
    public const string MoreThan7Days = "MoreThan7Days";
    public const string Unknown = "Unknown";
    public static readonly IReadOnlyList<string> All = [LessThan24h, Within7Days, MoreThan7Days, Unknown];
    public const int DayMinutes = 1440;
    public const int WeekMinutes = 10080;

    public static string Of(int? minutes) => minutes is null ? Unknown : minutes < DayMinutes ? LessThan24h : minutes < WeekMinutes ? Within7Days : MoreThan7Days;
}

/// <summary>Counts for a set of appointments with the two rates. A rate is <c>null</c> (unavailable) when nothing was booked, never zero: it is the fraction of the appointments booked, rounded to four decimal places.</summary>
public sealed record CancellationCounts(int Booked, int Cancelled, int NoShow, decimal? CancellationRate, decimal? NoShowRate);

public sealed record CancellationLeadTimeRow(string Bucket, int Cancelled);

public sealed record CancellationProviderRow(Guid ProviderProfileId, string ProviderName, CancellationCounts Counts);

public sealed record CancellationTypeRow(Guid AppointmentTypeId, string TypeName, CancellationCounts Counts);

/// <summary>The cancellations and no-shows report. The providers and the types are two cuts of the same appointments, and the lead times cut the cancelled ones; <see cref="Reconciled"/> says each adds up.</summary>
public sealed record CancellationData(CancellationCounts Totals, IReadOnlyList<CancellationLeadTimeRow> LeadTimes, IReadOnlyList<CancellationProviderRow> Providers, IReadOnlyList<CancellationTypeRow> Types, bool Reconciled);

/// <summary>
/// ALV-N006 slice 4: cancellations and no-shows. Source: the appointments that start in the range (any booking status) with their booked provider, type and the time the status changed. The rates are fractions of the appointments
/// BOOKED in the range, whatever day they were cancelled. The cancel reason is free text and is never shown. The provider filter and the location filter (through the booked operatory) both work. Failure modes handled: nothing
/// booked (<c>NoData</c>, rates unavailable), more rows than the limit (413), an audit entry that cannot be saved (503). Definition version 1.
/// </summary>
public class CancellationReportService(AlveraDbContext db, IPracticeClock clock, IOptions<ReportOptions> options, ILogger<CancellationReportService> log)
{
    public const int DefinitionVersion = 1;

    public async Task<ReportResult<CancellationData>> RunAsync(ReportFilter filter, Guid actor, CancellationToken ct)
    {
        string consistency;
        CancellationData data;
        await using (var session = await ReportReadSession.OpenAsync(db, log, ct))
        {
            consistency = session.ReadConsistency;
            var scoped = ReportSupport.Appointments(db, filter);
            var independent = await scoped.CountAsync(ct);
            ReportSupport.GuardSize(independent, options.Value);
            var rows = await scoped.Select(a => new { a.ProviderProfileId, a.AppointmentTypeId, a.Status, Minutes = a.StatusChangedAtUtc == null ? (int?)null : EF.Functions.DateDiffMinute(a.StatusChangedAtUtc.Value, a.StartUtc) }).ToListAsync(ct);
            var providerIds = rows.Select(r => r.ProviderProfileId).Concat(filter.ProviderIds).Distinct().ToArray();
            var typeIds = rows.Select(r => r.AppointmentTypeId).Distinct().ToArray();
            var providerNames = await db.ProviderProfiles.AsNoTracking().Where(p => providerIds.Contains(p.Id)).Select(p => new { p.Id, Name = p.StaffProfile!.DisplayName }).ToDictionaryAsync(p => p.Id, p => p.Name, ct);
            var typeNames = await db.AppointmentTypes.AsNoTracking().Where(t => typeIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Name, ct);

            var providers = providerIds.Select(id => new CancellationProviderRow(id, providerNames.GetValueOrDefault(id, "Unknown provider"), Counts(rows.Where(r => r.ProviderProfileId == id).Select(r => r.Status))))
                .OrderBy(p => p.ProviderName, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.ProviderProfileId).ToList();
            var types = typeIds.Select(id => new CancellationTypeRow(id, typeNames.GetValueOrDefault(id, "Unknown type"), Counts(rows.Where(r => r.AppointmentTypeId == id).Select(r => r.Status))))
                .OrderBy(t => t.TypeName, StringComparer.OrdinalIgnoreCase).ThenBy(t => t.AppointmentTypeId).ToList();
            var totals = Counts(rows.Select(r => r.Status));
            var leadTimes = LeadTimeBuckets.All.Select(b => new CancellationLeadTimeRow(b, rows.Count(r => r.Status == AppointmentStatuses.Cancelled && LeadTimeBuckets.Of(r.Minutes) == b))).ToList();
            var reconciled = totals.Booked == independent && providers.Sum(p => p.Counts.Booked) == independent && types.Sum(t => t.Counts.Booked) == independent && leadTimes.Sum(l => l.Cancelled) == totals.Cancelled;
            data = new CancellationData(totals, leadTimes, providers, types, reconciled);
        }

        await ReportSupport.AuditAsync(db, log, ReportIds.Cancellations, false, filter, data.Totals.Booked, actor, ct);
        return new ReportResult<CancellationData>(ReportIds.Cancellations, DefinitionVersion, ReportSupport.View(filter), clock.UtcNow, consistency, data.Totals.Booked, data.Totals.Booked == 0, data);
    }

    private static CancellationCounts Counts(IEnumerable<string> statuses)
    {
        int booked = 0, cancelled = 0, noShow = 0;
        foreach (var status in statuses)
        {
            booked++;
            if (status == AppointmentStatuses.Cancelled) cancelled++;
            else if (status == AppointmentStatuses.NoShow) noShow++;
        }
        return new CancellationCounts(booked, cancelled, noShow, Rate(cancelled, booked), Rate(noShow, booked));
    }

    private static decimal? Rate(int part, int whole) => whole == 0 ? null : Math.Round(part / (decimal)whole, 4);
}
