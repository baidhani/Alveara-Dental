using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Scheduling;

namespace Alveara.Api.Architecture.Treatment;

/// <summary>ALV-N023 (F-183, S13): the practice's name, phone and address for the printed patient copy - nothing else from the practice settings.</summary>
public sealed record PlanPracticeHeader(string? Name, string? Phone, string? AddressLine);

/// <summary>ALV-N023 (DR-1, C1): one of the patient's appointments a procedure can be linked to (still scheduled), with only when and with whom.</summary>
public sealed record LinkableAppointmentView(Guid AppointmentId, DateTimeOffset StartUtc, int DurationMinutes, string? ProviderName);

public partial class TreatmentPlanService
{
    public async Task<PlanPracticeHeader> PracticeHeaderAsync(CancellationToken ct)
    {
        var p = await db.PracticeSettings.AsNoTracking().FirstOrDefaultAsync(ct);
        return new PlanPracticeHeader(p?.Name, p?.Phone, p?.AddressLine);
    }

    /// <summary>The patient's appointments that are still Scheduled (a cancelled or no-show appointment cannot book anything), from 30 days ago onwards, soonest first.</summary>
    public async Task<IReadOnlyList<LinkableAppointmentView>> LinkableAppointmentsAsync(Guid patientId, CancellationToken ct)
    {
        if (!await db.Patients.AsNoTracking().AnyAsync(p => p.Id == patientId, ct)) throw PatientNotFound();
        var since = clock.UtcNow.AddDays(-30);
        return await (from a in db.Appointments.AsNoTracking()
                      join pp in db.ProviderProfiles.AsNoTracking() on a.ProviderProfileId equals pp.Id into pj
                      from pp in pj.DefaultIfEmpty()
                      join sp in db.StaffProfiles.AsNoTracking() on pp.StaffProfileId equals sp.Id into sj
                      from sp in sj.DefaultIfEmpty()
                      where a.PatientId == patientId && a.Status == AppointmentStatuses.Scheduled && a.StartUtc >= since
                      orderby a.StartUtc
                      select new LinkableAppointmentView(a.Id, a.StartUtc, a.DurationMinutes, sp == null ? null : sp.DisplayName)).Take(100).ToListAsync(ct);
    }
}
