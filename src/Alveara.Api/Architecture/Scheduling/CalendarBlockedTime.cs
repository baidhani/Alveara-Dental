using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Configuration;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Scheduling;

/// <summary>
/// ALV-N021 A2 (F-052): one blocked time as the calendar may show it. Deliberately has NO reason: <see cref="DisplayLabel"/> comes from the controlled calendar label
/// (<see cref="BlockedTimeLabels.ForCalendar"/>), so nothing an administrator typed privately can reach the calendar. Provider names are the ones the calendar already shows.
/// </summary>
public sealed record CalendarBlockView(
    Guid Id, Guid ProviderId, string ProviderName, DateTimeOffset StartUtc, DateTimeOffset EndUtc, string StartLocal, string EndLocal, string DisplayLabel);

/// <summary>
/// ALV-N021 A2 (F-052, owner decision 2026-11-15): the blocked time that overlaps a range of practice-local days, for the calendar's Day and Week views. Read-only; the route that serves it
/// needs only <c>ViewSchedule</c>. The raw reason stays behind the practice-configuration route.
/// </summary>
public sealed class CalendarBlockedTimeReader(AlveraDbContext db, IPracticeClock clock)
{
    private string Local(DateTimeOffset utc) => clock.ToPracticeLocal(utc).ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture);

    /// <summary>Blocks that overlap [fromUtc, toUtc), optionally of one provider, in start order.</summary>
    public async Task<IReadOnlyList<CalendarBlockView>> ListAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, Guid? providerId, CancellationToken ct)
    {
        var rows = await (from b in db.ProviderBlockedTimes.AsNoTracking()
                          join p in db.ProviderProfiles.AsNoTracking() on b.ProviderProfileId equals p.Id
                          join s in db.StaffProfiles.AsNoTracking() on p.StaffProfileId equals s.Id
                          where b.StartUtc < toUtc && fromUtc < b.EndUtc && (providerId == null || b.ProviderProfileId == providerId)
                          orderby b.StartUtc, b.Id
                          select new { b.Id, b.ProviderProfileId, s.DisplayName, b.StartUtc, b.EndUtc, b.CalendarLabel, b.Reason }).ToListAsync(ct);
        // the reason is read only to recognise a pre-M5 lunch; it never leaves this method
        return rows.Select(r => new CalendarBlockView(r.Id, r.ProviderProfileId, r.DisplayName, r.StartUtc, r.EndUtc, Local(r.StartUtc), Local(r.EndUtc),
            BlockedTimeLabels.ForCalendar(r.CalendarLabel, r.Reason))).ToList();
    }
}
