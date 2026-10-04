using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Forms;
using Alveara.Api.Architecture.Safety;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Scheduling;

/// <summary>
/// One visit on the board. <see cref="Readiness"/> is the check-in form cue, present only while the patient has not yet been seen (before treatment starts) and
/// only for callers allowed to see form status. <see cref="CarriedOver"/> marks a visit from an EARLIER day that is still open (arrived but never completed).
/// ALV-N011: <see cref="Safety"/> is the MINIMAL patient-safety indicator (alert yes/no, clearance open yes/no) and is present only for callers who hold
/// <c>ViewSafetyIndicator</c> and only when there is something to show - never a diagnosis, allergy, medication, category, severity or count.
/// </summary>
public record VisitCard(AppointmentView Appointment, CheckInReadiness? Readiness, bool CarriedOver, SafetyIndicator? Safety = null);

/// <summary><see cref="ServerNowUtc"/> is the server's clock, so the screen's elapsed-time figures never depend on the browser's clock.</summary>
public record VisitBoard(string Date, DateTimeOffset ServerNowUtc, IReadOnlyList<string> States, IReadOnlyList<VisitCard> Visits);

/// <summary>
/// ALV-011-C01: the live visit board for one practice day. Reads only persisted truth - every card is built from the stored appointment, so the board can
/// never show a state the database does not hold. Cancelled and no-show appointments are included (they stay distinct from completed). A visit left open from
/// an earlier day is included on today's board, because it still occupies its operatory (<see cref="VisitOccupancy"/> is about who is in the room now, not
/// about booked time) and would otherwise block the room with nothing on screen to explain why.
/// </summary>
public class VisitBoardService(AlveraDbContext db, IPracticeClock clock, CheckInReadinessService readiness, SafetyContextService? safety = null)
{
    private const int MaxCards = 500;
    private const int MaxCarriedOver = 100;
    private static readonly string[] OpenStates = [VisitStates.CheckedIn, VisitStates.Ready, VisitStates.Seated, VisitStates.InTreatment, VisitStates.CheckedOut];
    private static readonly string[] ReadinessStates = [VisitStates.Scheduled, VisitStates.Confirmed, VisitStates.CheckedIn, VisitStates.Ready];

    /// <summary>The current practice-local date, for the board's default.</summary>
    public DateOnly Today() => DateOnly.FromDateTime(clock.ToPracticeLocal(clock.UtcNow).DateTime);

    public async Task<VisitBoard> BoardAsync(DateOnly day, bool includeReadiness, CancellationToken ct, bool includeSafety = false)
    {
        var fromUtc = clock.FromPracticeLocal(day.ToDateTime(TimeOnly.MinValue));
        var toUtc = clock.FromPracticeLocal(day.AddDays(1).ToDateTime(TimeOnly.MinValue));

        var ids = await db.Appointments.AsNoTracking().Where(a => a.StartUtc >= fromUtc && a.StartUtc < toUtc)
            .OrderBy(a => a.StartUtc).ThenBy(a => a.Id).Select(a => a.Id).Take(MaxCards).ToListAsync(ct);
        var carried = day != Today() ? [] : await db.Appointments.AsNoTracking()
            .Where(a => a.StartUtc < fromUtc && a.Status == AppointmentStatuses.Scheduled && OpenStates.Contains(a.FlowState))
            .OrderBy(a => a.StartUtc).ThenBy(a => a.Id).Select(a => a.Id).Take(MaxCarriedOver).ToListAsync(ct);

        var builder = new AppointmentViewBuilder(db, clock);
        var views = new List<(AppointmentView View, bool Carried)>();
        foreach (var id in carried) views.Add((await builder.ViewAsync(id, ct), true));
        foreach (var id in ids) views.Add((await builder.ViewAsync(id, ct), false));

        IReadOnlyDictionary<Guid, CheckInReadiness> cues = new Dictionary<Guid, CheckInReadiness>();
        if (includeReadiness)
        {
            var waiting = views.Where(v => v.View.Status == AppointmentStatuses.Scheduled && ReadinessStates.Contains(v.View.FlowState)).Select(v => v.View.PatientId).Distinct().ToList();
            cues = await readiness.ForPatientsAsync(waiting, ct);
        }

        IReadOnlyDictionary<Guid, SafetyIndicator> indicators = new Dictionary<Guid, SafetyIndicator>();
        if (includeSafety && safety is not null) indicators = await safety.IndicatorsAsync(views.Select(v => v.View.PatientId).ToList(), ct);

        var cards = views.Select(v => new VisitCard(v.View, cues.TryGetValue(v.View.PatientId, out var r) && v.View.Status == AppointmentStatuses.Scheduled && ReadinessStates.Contains(v.View.FlowState) ? r : null, v.Carried,
            indicators.TryGetValue(v.View.PatientId, out var indicator) ? indicator : null)).ToList();
        return new VisitBoard(day.ToString("yyyy-MM-dd"), clock.UtcNow, VisitStates.InOrder, cards);
    }
}
