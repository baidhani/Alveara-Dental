using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Scheduling;

/// <summary>One entry of an appointment's history, with the place and time it was moved FROM when it was rescheduled.</summary>
public record AppointmentEventView(
    string EventType, Guid? ActorUserId, DateTimeOffset OccurredAtUtc, string? Detail,
    string? PreviousStartLocal, string? PreviousProviderName, string? PreviousOperatoryName);

/// <summary>Builds the read model of an appointment (names resolved, times in both UTC and practice-local), shared by booking and the lifecycle actions.</summary>
internal sealed class AppointmentViewBuilder(AlveraDbContext db, IPracticeClock clock)
{
    public string Local(DateTimeOffset utc) => clock.ToPracticeLocal(utc).ToString("yyyy-MM-ddTHH:mm");

    public async Task<AppointmentView> ViewAsync(Guid id, CancellationToken ct)
    {
        var row = await (from a in db.Appointments.AsNoTracking()
                         join p in db.Patients.AsNoTracking() on a.PatientId equals p.Id
                         join pr in db.ProviderProfiles.AsNoTracking() on a.ProviderProfileId equals pr.Id
                         join s in db.StaffProfiles.AsNoTracking() on pr.StaffProfileId equals s.Id
                         join o in db.Operatories.AsNoTracking() on a.OperatoryId equals o.Id
                         join t in db.AppointmentTypes.AsNoTracking() on a.AppointmentTypeId equals t.Id
                         where a.Id == id
                         select new { a, p.FirstName, p.LastName, Provider = s.DisplayName, Operatory = o.Name, Type = t.Name }).SingleAsync(ct);
        var a1 = row.a;
        return new AppointmentView(
            a1.Id, a1.PatientId, $"{row.FirstName} {row.LastName}", a1.ProviderProfileId, row.Provider, a1.OperatoryId, row.Operatory, a1.AppointmentTypeId, row.Type,
            a1.StartUtc, a1.EndUtc, Local(a1.StartUtc), Local(a1.EndUtc), a1.DurationMinutes, a1.Status, a1.CreatedAtUtc,
            Convert.ToBase64String(a1.RowVersion), a1.Notes, a1.CancelReason, a1.StatusChangedAtUtc);
    }

    public async Task<IReadOnlyList<AppointmentEventView>> HistoryAsync(Guid appointmentId, CancellationToken ct)
    {
        var events = await db.AppointmentEvents.AsNoTracking().Where(e => e.AppointmentId == appointmentId).OrderBy(e => e.OccurredAtUtc).ThenBy(e => e.EventType).ToListAsync(ct);
        var providers = await (from pr in db.ProviderProfiles.AsNoTracking() join s in db.StaffProfiles.AsNoTracking() on pr.StaffProfileId equals s.Id select new { pr.Id, s.DisplayName }).ToDictionaryAsync(x => x.Id, x => x.DisplayName, ct);
        var operatories = await db.Operatories.AsNoTracking().ToDictionaryAsync(o => o.Id, o => o.Name, ct);
        return events.Select(e => new AppointmentEventView(
            e.EventType, e.ActorUserId, e.OccurredAtUtc, e.Detail,
            e.PreviousStartUtc is null ? null : Local(e.PreviousStartUtc.Value),
            e.PreviousProviderProfileId is { } p && providers.TryGetValue(p, out var pn) ? pn : null,
            e.PreviousOperatoryId is { } o && operatories.TryGetValue(o, out var on) ? on : null)).ToList();
    }
}
