using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Alveara.Api.Architecture.Scheduling;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Reporting;

public sealed record FlowStateCount(string State, int Visits);

public sealed record FlowTransitionCount(string Event, int Count);

/// <summary>
/// The patient-flow report: <see cref="ByState"/> counts the visits that are still on the schedule (booking status Scheduled) by their current visit state, in the order a visit passes through them, with a row for every
/// state even when it is zero; cancelled and no-show appointments are not visits and are counted apart, so <see cref="Appointments"/> = the sum of <see cref="ByState"/> + <see cref="Cancelled"/> + <see cref="NoShow"/>.
/// <see cref="Transitions"/> counts the recorded moves of those appointments. <see cref="PastNotCompleted"/> counts visits whose booked time has ended and that were never completed (a visit still open after its day).
/// <see cref="Reconciled"/> is true when the sum of the states equals an independent count of the scheduled appointments.
/// </summary>
public sealed record PatientFlowData(int Appointments, int Cancelled, int NoShow, IReadOnlyList<FlowStateCount> ByState, IReadOnlyList<FlowTransitionCount> Transitions, int PastNotCompleted, bool Reconciled);

/// <summary>
/// ALV-N006: the patient-flow report. Source: the appointments in the range, the visit state each one is in now (<see cref="Appointment.FlowState"/>) and their append-only history of moves. Every count can be listed
/// with <see cref="ReportDrillService"/>. Not included: waiting times (they need a defined start and stop per visit; they are a metric in a later slice, with their own definition). Definition version 1.
/// Failure modes handled: nothing in the range (<c>NoData</c>), more rows than the limit (413), an unknown stored state (reported as its own row, never dropped), an audit entry that cannot be saved (503).
/// </summary>
public class PatientFlowReportService(AlveraDbContext db, IPracticeClock clock, IOptions<ReportOptions> options, ILogger<PatientFlowReportService> log)
{
    public const int DefinitionVersion = 1;

    private static readonly string[] FlowEvents =
    [
        AppointmentEventTypes.Confirmed, AppointmentEventTypes.CheckedIn, AppointmentEventTypes.Ready, AppointmentEventTypes.Seated,
        AppointmentEventTypes.TreatmentStarted, AppointmentEventTypes.CheckedOut, AppointmentEventTypes.Completed,
    ];

    public async Task<ReportResult<PatientFlowData>> RunAsync(ReportFilter filter, Guid actor, CancellationToken ct)
    {
        string consistency;
        PatientFlowData data;
        await using (var session = await ReportReadSession.OpenAsync(db, log, ct))
        {
            consistency = session.ReadConsistency;
            var scoped = ReportSupport.Appointments(db, filter);
            var total = await scoped.CountAsync(ct);
            ReportSupport.GuardSize(total, options.Value);

            var byStatus = await scoped.GroupBy(a => a.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Status, g => g.Count, ct);
            var scheduledCount = await scoped.CountAsync(a => a.Status == AppointmentStatuses.Scheduled, ct);        // the independent count the states are reconciled to
            var states = await scoped.Where(a => a.Status == AppointmentStatuses.Scheduled).GroupBy(a => a.FlowState).Select(g => new { State = g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.State, g => g.Count, ct);
            var byState = VisitStates.InOrder.Select(s => new FlowStateCount(s, states.GetValueOrDefault(s))).ToList();
            byState.AddRange(states.Where(kv => !VisitStates.IsKnown(kv.Key)).OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => new FlowStateCount(kv.Key, kv.Value)));

            var moves = await db.AppointmentEvents.AsNoTracking()
                .Where(e => FlowEvents.Contains(e.EventType) && scoped.Any(a => a.Id == e.AppointmentId))
                .GroupBy(e => e.EventType).Select(g => new { Event = g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Event, g => g.Count, ct);
            var transitions = FlowEvents.Select(e => new FlowTransitionCount(e, moves.GetValueOrDefault(e))).ToList();

            var now = clock.UtcNow;
            var pastNotCompleted = await scoped.CountAsync(a => a.Status == AppointmentStatuses.Scheduled && a.EndUtc < now && a.FlowState != VisitStates.Completed, ct);
            data = new PatientFlowData(total, byStatus.GetValueOrDefault(AppointmentStatuses.Cancelled), byStatus.GetValueOrDefault(AppointmentStatuses.NoShow), byState, transitions, pastNotCompleted, byState.Sum(s => s.Visits) == scheduledCount);
        }

        await ReportSupport.AuditAsync(db, log, ReportIds.PatientFlow, false, filter, data.Appointments, actor, ct);
        return new ReportResult<PatientFlowData>(ReportIds.PatientFlow, DefinitionVersion, ReportSupport.View(filter), clock.UtcNow, consistency, data.Appointments, data.Appointments == 0, data);
    }
}
