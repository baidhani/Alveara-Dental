using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Configuration;

public record SchedulableProvider(Guid ProviderId, string DisplayName, string Specialty, IReadOnlyList<AvailabilityWindow> WeeklyAvailability);
public record SchedulableOperatory(Guid Id, string Name);
public record SchedulableAppointmentType(Guid Id, string Name, int DefaultDurationMinutes);

public record SchedulingConfigurationSnapshot(
    string TimeZoneId, Guid? ActiveLocationId, string? ActiveLocationName,
    IReadOnlyList<SchedulableProvider> Providers,
    IReadOnlyList<SchedulableOperatory> Operatories,
    IReadOnlyList<SchedulableAppointmentType> AppointmentTypes);

public record AvailabilityCheck(bool Available, string? Reason);

/// <summary>
/// ALV-N003's consumption seam: the read model the scheduling story builds on. It returns only
/// ACTIVE configuration (an inactive provider/operatory/type must never be offered for a new
/// appointment) and answers "is this provider free for this slot" using practice-local weekly
/// hours and UTC blocked-time instants. Historical resolution of an inactive record goes through
/// the services' Get-by-id methods, which return inactive rows deliberately.
/// </summary>
public class SchedulingConfiguration(AlveraDbContext db, IPracticeClock clock)
{
    public async Task<SchedulingConfigurationSnapshot> GetSnapshotAsync(CancellationToken ct)
    {
        var location = await db.PracticeLocations.AsNoTracking().SingleOrDefaultAsync(l => l.IsActive, ct);

        var providers = await db.ProviderProfiles.AsNoTracking().Include(p => p.StaffProfile)
            .Where(p => p.IsActive && p.StaffProfile!.IsActive)
            .OrderBy(p => p.StaffProfile!.DisplayName).ToListAsync(ct);
        var availability = (await db.ProviderWeeklyAvailabilities.AsNoTracking().ToListAsync(ct))
            .ToLookup(a => a.ProviderProfileId);

        var operatories = location is null
            ? []
            : await db.Operatories.AsNoTracking().Where(o => o.IsActive && o.LocationId == location.Id).OrderBy(o => o.Name).ToListAsync(ct);
        var types = await db.AppointmentTypes.AsNoTracking().Where(t => t.IsActive).OrderBy(t => t.Name).ToListAsync(ct);

        return new SchedulingConfigurationSnapshot(
            clock.PracticeTimeZone.Id, location?.Id, location?.Name,
            providers.Select(p => new SchedulableProvider(
                p.Id, p.StaffProfile!.DisplayName, p.Specialty,
                availability[p.Id].OrderBy(a => a.DayOfWeek).ThenBy(a => a.StartLocal)
                    .Select(a => new AvailabilityWindow(a.DayOfWeek, a.StartLocal.ToString("HH:mm"), a.EndLocal.ToString("HH:mm"))).ToList())).ToList(),
            operatories.Select(o => new SchedulableOperatory(o.Id, o.Name)).ToList(),
            types.Select(t => new SchedulableAppointmentType(t.Id, t.Name, t.DefaultDurationMinutes)).ToList());
    }

    /// <summary>
    /// A slot is available only if the provider is active, the whole slot sits inside ONE weekly
    /// window on a single practice-local day, and it does not overlap any blocked time.
    /// </summary>
    public async Task<AvailabilityCheck> CheckProviderAvailabilityAsync(Guid providerId, DateTimeOffset startUtc, int durationMinutes, CancellationToken ct)
    {
        PracticeConfigurationService.ValidateDuration(durationMinutes);
        var provider = await db.ProviderProfiles.AsNoTracking().Include(p => p.StaffProfile).SingleOrDefaultAsync(p => p.Id == providerId, ct)
            ?? throw PracticeConfigurationService.NotFound("Provider profile");
        if (!provider.IsActive || provider.StaffProfile is { IsActive: false })
            return new AvailabilityCheck(false, "provider_inactive");

        var endUtc = startUtc.AddMinutes(durationMinutes);
        var localStart = clock.ToPracticeLocal(startUtc);
        var localEnd = clock.ToPracticeLocal(endUtc);
        if (localStart.Date != localEnd.Date && localEnd.TimeOfDay != TimeSpan.Zero)
            return new AvailabilityCheck(false, "outside_working_hours");

        var startTime = TimeOnly.FromTimeSpan(localStart.TimeOfDay);
        var endTime = localEnd.TimeOfDay == TimeSpan.Zero ? TimeOnly.MaxValue : TimeOnly.FromTimeSpan(localEnd.TimeOfDay);
        var windows = await db.ProviderWeeklyAvailabilities.AsNoTracking()
            .Where(a => a.ProviderProfileId == providerId && a.DayOfWeek == localStart.DayOfWeek).ToListAsync(ct);
        if (!windows.Any(w => w.StartLocal <= startTime && endTime <= w.EndLocal))
            return new AvailabilityCheck(false, "outside_working_hours");

        var blocked = await db.ProviderBlockedTimes.AsNoTracking()
            .AnyAsync(b => b.ProviderProfileId == providerId && b.StartUtc < endUtc && startUtc < b.EndUtc, ct);
        return blocked ? new AvailabilityCheck(false, "blocked_time") : new AvailabilityCheck(true, null);
    }
}
