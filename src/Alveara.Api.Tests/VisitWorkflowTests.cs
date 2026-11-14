using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Scheduling;
using Xunit;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-011-C01 against real SQL Server: a visit travels the whole production chain with every move logged, the room is never shared, the visit's own provider
/// and operatory are kept apart from the booking, and concurrent changes never overwrite each other. STORY-011's own tests run alongside and are unchanged.
/// </summary>
public class VisitWorkflowTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private SchedulingTestSupport _s = null!;
    private Guid _ann, _bo, _cy;

    public Task InitializeAsync() => _fixture.InitializeOwnerAsync(async () =>
    {
        _s = new SchedulingTestSupport(_fixture);
        await _s.ArrangeAsync();
        _ann = await _s.PatientAsync();
        _bo = await _s.PatientAsync("Bo", "Kim");
        _cy = await _s.PatientAsync("Cy", "Poe");
    });
    public Task DisposeAsync() => _fixture.DisposeAsync();

    private async Task<AppointmentView> BookAsync(Guid patient, int hour, Guid? provider = null, Guid? operatory = null) =>
        (await _s.ScheduleAsync(_s.Request(patient, At(hour), provider, operatory))).Appointment;

    private Task<AppointmentView> GoAsync(AppointmentView a, string target, string? version = null) =>
        _s.FlowAsync(f => f.TransitionAsync(a.Id, target, version ?? a.RowVersion, _s.MoverFor(target, a), default));

    /// <summary>Walks the visit to <paramref name="target"/> along the one path (ALV-N021 A2: each move by the person who makes it; the checkout resolved before completion).</summary>
    private Task<AppointmentView> WalkAsync(AppointmentView a, string target) => _s.WalkAsync(a, target);

    private Task<AppointmentView> AssignAsync(AppointmentView a, Guid provider, Guid operatory, string? version = null) =>
        _s.AssignAsync(v => v.AssignAsync(a.Id, new AssignVisitRequest(provider, operatory, version ?? a.RowVersion), _s.Actor, default));

    private static async Task<SchedulingException> RefusedAsync(Func<Task<AppointmentView>> action) => await Assert.ThrowsAsync<SchedulingException>(action);

    // ---------- the whole chain ----------

    [Fact]
    public async Task A_visit_traverses_the_whole_production_chain_and_each_move_is_in_the_history_and_the_audit_log_with_user_and_time()
    {
        var a = await BookAsync(_ann, 9);
        Assert.Equal(VisitStates.Scheduled, a.FlowState);
        Assert.Equal([VisitStates.Confirmed], a.NextFlowStates);   // ALV-N021 A2: confirmation is required by default

        var done = await WalkAsync(a, VisitStates.Completed);

        Assert.Equal(VisitStates.Completed, done.FlowState);
        Assert.Empty(done.NextFlowStates!);
        var moves = (await _s.HistoryAsync(a.Id)).Where(e => e.EventType is not ("Scheduled" or "CheckoutRecorded")).ToList();   // A2: checkout decisions are history too
        Assert.Equal(["Confirmed", "CheckedIn", "Ready", "Seated", "TreatmentStarted", "CheckedOut", "Completed"], moves.Select(e => e.EventType));
        Assert.Equal(["Scheduled -> Confirmed", "Confirmed -> CheckedIn", "CheckedIn -> Ready", "Ready -> Seated", "Seated -> InTreatment", "InTreatment -> CheckedOut", "CheckedOut -> Completed"], moves.Select(e => e.Detail));
        // ALV-N021 A2: the front desk confirms, checks in and completes; the assistant readies and seats (test actor); the booked provider starts and ends treatment
        Assert.Equal([_s.Actor, _s.Actor, _s.Actor, _s.Actor, _s.ProviderAUser, _s.ProviderAUser, _s.Actor], moves.Select(e => e.ActorUserId));

        await using var db = _fixture.CreateContext();
        var names = new[] { "PatientConfirmed", "PatientCheckedIn", "PatientReady", "PatientSeated", "PatientTreatmentStarted", "PatientCheckedOut", "PatientTreatmentCompleted" };
        var audits = await db.AuditLogEntries.Where(e => names.Contains(e.EventType)).OrderBy(e => e.TimestampUtc).ToListAsync();
        Assert.Equal(7, audits.Count);
        Assert.Equal([_s.Actor, _s.Actor, _s.Actor, _s.Actor, _s.ProviderAUser, _s.ProviderAUser, _s.Actor], audits.Select(e => e.PerformedByUserAccountId));
        Assert.All(audits, e =>
        {
            Assert.Equal(a.Id, e.TargetUserAccountId);
            Assert.InRange(e.TimestampUtc, DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddSeconds(5));
            Assert.DoesNotContain("Ann", e.Details);
        });
    }

    [Fact]
    public async Task Cancelled_and_no_show_stay_distinct_from_completed()
    {
        var done = await WalkAsync(await BookAsync(_ann, 9), VisitStates.Completed);
        var toCancel = await BookAsync(_bo, 11);
        var cancelled = await _s.ManageAsync(m => m.CancelAsync(toCancel.Id, "Illness", toCancel.RowVersion, _s.Actor, default));
        var toMiss = await BookAsync(_cy, 13);
        var missed = await _s.ManageAsync(m => m.MarkNoShowAsync(toMiss.Id, toMiss.RowVersion, _s.Actor, default), new TestClock(toMiss.StartUtc.AddHours(1)));

        Assert.Equal((AppointmentStatuses.Scheduled, VisitStates.Completed), (done.Status, done.FlowState));
        Assert.Equal((AppointmentStatuses.Cancelled, VisitStates.Scheduled), (cancelled.Status, cancelled.FlowState));
        Assert.Equal((AppointmentStatuses.NoShow, VisitStates.Scheduled), (missed.Status, missed.FlowState));
        Assert.Empty(cancelled.NextFlowStates!);
        Assert.Empty(missed.NextFlowStates!);
        Assert.Equal("terminal_state", (await RefusedAsync(() => GoAsync(cancelled, VisitStates.CheckedIn))).Code);   // ALV-N021 A2 code
        Assert.Equal("terminal_state", (await RefusedAsync(() => GoAsync(missed, VisitStates.Confirmed))).Code);
    }

    // ---------- invalid transitions ----------

    [Theory]
    [InlineData(VisitStates.Scheduled, VisitStates.Ready)]
    [InlineData(VisitStates.Scheduled, VisitStates.Seated)]
    [InlineData(VisitStates.Confirmed, VisitStates.InTreatment)]
    [InlineData(VisitStates.CheckedIn, VisitStates.Seated)]
    [InlineData(VisitStates.Ready, VisitStates.InTreatment)]
    [InlineData(VisitStates.Seated, VisitStates.Completed)]
    [InlineData(VisitStates.InTreatment, VisitStates.Seated)]
    [InlineData(VisitStates.CheckedOut, VisitStates.Confirmed)]
    [InlineData(VisitStates.CheckedIn, VisitStates.InTreatment)]   // ALV-N021 A2: STORY-011's shortcuts are refused too
    [InlineData(VisitStates.CheckedIn, VisitStates.Completed)]
    [InlineData(VisitStates.InTreatment, VisitStates.Completed)]
    [InlineData(VisitStates.Scheduled, VisitStates.CheckedIn)]    // confirmation is required by default
    public async Task A_move_the_state_machine_refuses_is_a_409_and_changes_nothing(string from, string to)
    {
        var a = await WalkAsync(await BookAsync(_ann, 9), from);
        Assert.Equal(from, a.FlowState);

        var ex = await RefusedAsync(() => GoAsync(a, to));

        Assert.Equal(("illegal_transition", 409), (ex.Code, ex.StatusCode));   // ALV-N021 A2 code
        var after = await _s.ReloadAsync(a.Id);
        Assert.Equal((from, a.RowVersion), (after.FlowState, after.RowVersion));
    }

    [Fact]
    public async Task A_completed_visit_refuses_every_move_as_final()
    {
        var a = await WalkAsync(await BookAsync(_ann, 9), VisitStates.Completed);
        var ex = await RefusedAsync(() => GoAsync(a, VisitStates.CheckedOut));
        Assert.Equal(("terminal_state", 409), (ex.Code, ex.StatusCode));
        Assert.Equal((VisitStates.Completed, a.RowVersion), ((await _s.ReloadAsync(a.Id)).FlowState, (await _s.ReloadAsync(a.Id)).RowVersion));
    }

    [Fact]
    public async Task An_unknown_target_state_is_refused()
    {
        var a = await BookAsync(_ann, 9);
        Assert.Equal("illegal_transition", (await RefusedAsync(() => GoAsync(a, "Arrived"))).Code);
        Assert.Equal("illegal_transition", (await RefusedAsync(() => GoAsync(a, "Cancelled"))).Code);
    }

    // ---------- confirmed has not arrived; ready and seated have ----------

    [Fact]
    public async Task A_confirmed_appointment_can_still_be_rescheduled_or_cancelled()
    {
        var a = await BookAsync(_ann, 9);
        var confirmed = await GoAsync(a, VisitStates.Confirmed);
        var moved = await _s.ManageAsync(m => m.RescheduleAsync(a.Id, new RescheduleRequest(a.ProviderId, a.OperatoryId, At(14), null, confirmed.RowVersion), _s.Actor, default));
        Assert.Equal(("2030-01-14T14:00", VisitStates.Confirmed), (moved.StartLocal, moved.FlowState));

        var b = await BookAsync(_bo, 11);
        var confirmedB = await GoAsync(b, VisitStates.Confirmed);
        var cancelled = await _s.ManageAsync(m => m.CancelAsync(b.Id, "Changed mind", confirmedB.RowVersion, _s.Actor, default));
        Assert.Equal((AppointmentStatuses.Cancelled, VisitStates.Scheduled), (cancelled.Status, cancelled.FlowState));
        Assert.Equal(1, (await _s.HistoryAsync(b.Id)).Count(e => e.EventType == "Confirmed")); // the confirmation is not erased by the cancellation

        var c = await BookAsync(_ann, 13);
        var confirmedC = await GoAsync(c, VisitStates.Confirmed);
        var missed = await _s.ManageAsync(m => m.MarkNoShowAsync(c.Id, confirmedC.RowVersion, _s.Actor, default), new TestClock(c.StartUtc.AddHours(1)));
        Assert.Equal((AppointmentStatuses.NoShow, VisitStates.Scheduled), (missed.Status, missed.FlowState));
    }

    [Theory]
    [InlineData(VisitStates.CheckedIn)]
    [InlineData(VisitStates.Ready)]
    [InlineData(VisitStates.Seated)]
    [InlineData(VisitStates.InTreatment)]
    [InlineData(VisitStates.CheckedOut)]
    [InlineData(VisitStates.Completed)]
    public async Task Once_the_patient_has_arrived_the_appointment_cannot_be_cancelled_marked_no_show_or_rescheduled(string state)
    {
        var a = await WalkAsync(await BookAsync(_ann, 9), state);

        var cancel = await RefusedAsync(() => _s.ManageAsync(m => m.CancelAsync(a.Id, "x", a.RowVersion, _s.Actor, default)));
        var noShow = await RefusedAsync(() => _s.ManageAsync(m => m.MarkNoShowAsync(a.Id, a.RowVersion, _s.Actor, default), new TestClock(a.StartUtc.AddHours(1))));
        var move = await RefusedAsync(() => _s.ManageAsync(m => m.RescheduleAsync(a.Id, new RescheduleRequest(a.ProviderId, a.OperatoryId, At(14), null, a.RowVersion), _s.Actor, default)));

        Assert.All(new[] { cancel, noShow, move }, e => Assert.Equal(("appointment_in_progress", 409), (e.Code, e.StatusCode)));
        Assert.Equal((AppointmentStatuses.Scheduled, state), ((await _s.ReloadAsync(a.Id)).Status, (await _s.ReloadAsync(a.Id)).FlowState));
    }

    // ---------- failure handling ----------

    [Fact]
    public async Task If_the_log_cannot_be_written_the_move_is_not_stored_and_a_retry_works()
    {
        var a = await WalkAsync(await BookAsync(_ann, 9), VisitStates.Ready);
        await using (var failing = _s.FailingAuditContext())
            await Assert.ThrowsAsync<DbUpdateException>(() => _s.Flow(failing).TransitionAsync(a.Id, VisitStates.Seated, a.RowVersion, _s.MoverFor(VisitStates.Seated, a), default));

        var unchanged = await _s.ReloadAsync(a.Id);
        Assert.Equal((VisitStates.Ready, a.RowVersion), (unchanged.FlowState, unchanged.RowVersion));
        Assert.Equal(VisitStates.Seated, (await GoAsync(a, VisitStates.Seated)).FlowState);
    }

    [Fact]
    public async Task A_repeat_changes_nothing_and_a_stale_version_is_the_shared_conflict()
    {
        var a = await BookAsync(_ann, 9);
        var confirmed = await GoAsync(a, VisitStates.Confirmed);
        var again = await GoAsync(a, VisitStates.Confirmed); // old version, but already there: a quiet no-op
        Assert.Equal(confirmed.RowVersion, again.RowVersion);
        Assert.Equal(1, await _s.CountAsync(db => db.AppointmentEvents.Where(e => e.AppointmentId == a.Id && e.EventType == AppointmentEventTypes.Confirmed)));

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => GoAsync(a, VisitStates.CheckedIn)); // a different move with the old version
        Assert.Equal(VisitStates.Confirmed, (await _s.ReloadAsync(a.Id)).FlowState);
        Assert.Equal("row_version_required", (await RefusedAsync(() => _s.FlowAsync(f => f.TransitionAsync(a.Id, VisitStates.CheckedIn, null, _s.Desk, default)))).Code);
    }

    [Fact]
    public async Task Two_people_making_different_moves_on_the_same_visit_never_overwrite_each_other()
    {
        var a = await WalkAsync(await BookAsync(_ann, 9), VisitStates.Ready);

        // ALV-N021 A2: from Ready only Seated is on the path; the other two are refused by the rules or lose the race
        var results = await Task.WhenAll(new[] { VisitStates.Seated, VisitStates.InTreatment, VisitStates.Completed }.Select(async target =>
        {
            try { return (await GoAsync(a, target)).FlowState; }
            catch (ConcurrencyConflictException) { return "conflict"; }
            catch (SchedulingException ex) { return ex.Code; }
        }));

        Assert.Equal(1, results.Count(r => r is not ("conflict" or "illegal_transition" or "visit_move_forbidden")));
        var moves = (await _s.HistoryAsync(a.Id)).Count(e => e.EventType is "Seated" or "TreatmentStarted" or "Completed");
        Assert.Equal(1, moves); // exactly one of the three moves happened, and it is the one recorded
    }
}
