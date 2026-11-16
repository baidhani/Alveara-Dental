using System.Data;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using Alveara.Api.Architecture.Configuration;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Scheduling;

/// <summary>One appointment a blocked time would cover. The patient name is for the in-app confirmation list only; it never goes to the audit log.</summary>
public sealed record BlockOverlap(Guid AppointmentId, string StartLocal, string EndLocal, string ProviderName, string OperatoryName, string FlowState, string PatientName);

/// <summary>A blocked time was refused. <see cref="Overlaps"/> lists what it would cover (empty for a missing reason).</summary>
public sealed class BlockedTimeRefusedException(string code, string message, int statusCode, IReadOnlyList<BlockOverlap> overlaps) : Exception(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
    public IReadOnlyList<BlockOverlap> Overlaps { get; } = overlaps;
}

/// <summary>
/// ALV-N021 A2 (OD-17; briefing 5.6): adding a provider's blocked time over booked appointments. Replaces the unguarded add behind <c>POST api/configuration/providers/{id}/blocked-time</c>.
///
/// - <b>Active visits are never covered.</b> If the block overlaps any of the provider's appointments that is Checked in, Ready, Seated, In treatment or Ready for checkout, it is refused
///   409 <c>block_overlaps_active_visit</c> with that list, whatever <c>confirmOverlaps</c> says, and nothing is written.
/// - <b>Future appointments need a confirmation and a reason.</b> Over Scheduled/Confirmed appointments without <c>confirmOverlaps</c>: 409 <c>block_overlaps_appointments</c> with the list.
///   With confirmation but no reason: 400 <c>reason_required</c>. With both: the block is saved and audited, and each covered appointment is flagged (<see cref="BlockFlags"/>, derived).
/// - Completed, cancelled and no-show appointments are never touched or listed.
/// - <b>No race with check-in</b>: the overlap check and the save run in one transaction under the provider's booking lock, which check-in also takes, so a check-in racing the block either
///   lands first (and the block is refused) or waits and is refused as flagged (409 <c>block_flag_unresolved</c>).
/// - A block over no appointment behaves exactly as before (saved and audited; no reason needed).
/// </summary>
public sealed class BlockedTimeGuard(AlveraDbContext db, IPracticeClock clock)
{
    public async Task<ProviderBlockedTime> AddAsync(Guid providerId, DateTime startLocal, DateTime endLocal, string? reason, bool confirmOverlaps, Guid actor, CancellationToken ct, string? calendarLabel = null)
    {
        // ALV-N021 A2 (F-052, owner decision 2026-11-15): every new block says how the calendar shows it, from the controlled list; the reason stays private
        if (string.IsNullOrWhiteSpace(calendarLabel))
            throw new BlockedTimeRefusedException("calendar_label_required", "Choose how the blocked time is shown on the calendar (lunch, meeting, training, unavailable or other).", 400, []);
        var block = await new StaffProviderService(db, clock).BuildBlockedTimeAsync(providerId, startLocal, endLocal, reason, ct, calendarLabel);

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        await SchedulingGuards.AcquireLockAsync(db, SchedulingGuards.ProviderLock(providerId), ct);
        var covered = await db.Appointments.AsNoTracking()
            .Where(a => a.ProviderProfileId == providerId && a.Status == AppointmentStatuses.Scheduled && a.FlowState != VisitStates.Completed
                        && a.StartUtc < block.EndUtc && block.StartUtc < a.EndUtc)
            .OrderBy(a => a.StartUtc).ThenBy(a => a.Id).ToListAsync(ct);

        var active = covered.Where(a => BlockFlags.ActiveStates.Contains(a.FlowState)).ToList();
        if (active.Count > 0)
            throw new BlockedTimeRefusedException("block_overlaps_active_visit", "This blocked time covers a patient who is already here; it cannot be added.", 409, await DescribeAsync(active, ct));
        if (covered.Count > 0 && !confirmOverlaps)
            throw new BlockedTimeRefusedException("block_overlaps_appointments", "This blocked time covers booked appointments. Confirm to add it; they will be flagged for attention.", 409, await DescribeAsync(covered, ct));
        if (covered.Count > 0 && block.Reason is null)
            throw new BlockedTimeRefusedException("reason_required", "Give a reason for blocking time over booked appointments.", 400, []);

        db.ProviderBlockedTimes.Add(block);
        ConfigurationWrite.Audit(db, ConfigurationAuditEvents.ProviderBlockedTimeAdded, nameof(ProviderProfile), providerId, actor,
            covered.Count == 0 ? "Blocked time added." : string.Create(CultureInfo.InvariantCulture, $"Blocked time added over {covered.Count} booked appointment(s), confirmed; they are flagged."));
        await ConfigurationWrite.SaveAsync(db, nameof(ProviderProfile), providerId, ct);
        await transaction.CommitAsync(ct);
        return block;
    }

    private async Task<IReadOnlyList<BlockOverlap>> DescribeAsync(IReadOnlyList<Appointment> appointments, CancellationToken ct)
    {
        var ids = appointments.Select(a => a.Id).ToList();
        var rows = await (from a in db.Appointments.AsNoTracking()
                          join p in db.Patients.AsNoTracking() on a.PatientId equals p.Id
                          join pr in db.ProviderProfiles.AsNoTracking() on a.ProviderProfileId equals pr.Id
                          join s in db.StaffProfiles.AsNoTracking() on pr.StaffProfileId equals s.Id
                          join o in db.Operatories.AsNoTracking() on a.OperatoryId equals o.Id
                          where ids.Contains(a.Id)
                          select new { a.Id, a.StartUtc, a.EndUtc, a.FlowState, Provider = s.DisplayName, Operatory = o.Name, p.FirstName, p.LastName }).ToListAsync(ct);
        string Local(DateTimeOffset utc) => clock.ToPracticeLocal(utc).ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture);
        return rows.OrderBy(r => r.StartUtc).ThenBy(r => r.Id)
            .Select(r => new BlockOverlap(r.Id, Local(r.StartUtc), Local(r.EndUtc), r.Provider, r.Operatory, r.FlowState, $"{r.FirstName} {r.LastName}")).ToList();
    }
}
