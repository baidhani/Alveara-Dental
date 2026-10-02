using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Scheduling;
using Xunit;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>
/// STORY-011 against real SQL Server: a booked appointment moves Scheduled -> CheckedIn -> (InTreatment) -> Completed, every move is logged with the
/// user and time, repeats and races are harmless, a failed audit write stores nothing, and the existing cancel / no-show / reschedule respect a visit
/// that is under way. STORY-004's and ALV-004-C01's own tests are unchanged.
/// </summary>
public class PatientFlowLifecycleTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private SchedulingTestSupport _s = null!;
    private Guid _ann;

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
        _s = new SchedulingTestSupport(_fixture);
        await _s.ArrangeAsync();
        _ann = await _s.PatientAsync();
    }
    public Task DisposeAsync() => _fixture.DisposeAsync();

    private async Task<AppointmentView> BookAsync(DateTime start) => (await _s.ScheduleAsync(_s.Request(_ann, start))).Appointment;

    private Task<AppointmentView> CheckInAsync(AppointmentView a, string? version = null) => _s.FlowAsync(f => f.CheckInAsync(a.Id, version ?? a.RowVersion, _s.Actor, default));
    private Task<AppointmentView> StartAsync(AppointmentView a, string? version = null) => _s.FlowAsync(f => f.StartTreatmentAsync(a.Id, version ?? a.RowVersion, _s.Actor, default));
    private Task<AppointmentView> CompleteAsync(AppointmentView a, string? version = null) => _s.FlowAsync(f => f.CompleteAsync(a.Id, version ?? a.RowVersion, _s.Actor, default));

    private static async Task<SchedulingException> RefusedAsync(Func<Task<AppointmentView>> action) => await Assert.ThrowsAsync<SchedulingException>(action);

    // ---------- acceptance 1 and 2 ----------

    [Fact]
    public async Task A_scheduled_patient_who_checks_in_becomes_CheckedIn()
    {
        var a = await BookAsync(At(9));
        Assert.Equal(PatientFlowStates.Scheduled, a.FlowState);
        Assert.Null(a.FlowChangedAtUtc);

        var checkedIn = await CheckInAsync(a);

        Assert.Equal(PatientFlowStates.CheckedIn, checkedIn.FlowState);
        Assert.NotNull(checkedIn.FlowChangedAtUtc);
        Assert.Equal(PatientFlowStates.CheckedIn, (await _s.ReloadAsync(a.Id)).FlowState); // it is stored, not just returned
    }

    [Fact]
    public async Task A_checked_in_patient_whose_treatment_is_completed_becomes_Completed_and_InTreatment_is_optional()
    {
        var direct = await BookAsync(At(9));
        var done = await CompleteAsync(await CheckInAsync(direct));
        Assert.Equal(PatientFlowStates.Completed, done.FlowState);
        Assert.Equal(PatientFlowStates.Completed, (await _s.ReloadAsync(direct.Id)).FlowState);

        var stepwise = await BookAsync(At(11));
        var inTreatment = await StartAsync(await CheckInAsync(stepwise));
        Assert.Equal(PatientFlowStates.InTreatment, inTreatment.FlowState);
        Assert.Equal(PatientFlowStates.Completed, (await CompleteAsync(inTreatment)).FlowState);
    }

    [Fact]
    public async Task A_visit_under_way_or_finished_still_holds_its_time()
    {
        var a = await BookAsync(At(9));
        await CompleteAsync(await CheckInAsync(a));

        var bo = await _s.PatientAsync("Bo", "Kim");
        var clash = await Assert.ThrowsAsync<SchedulingException>(() => _s.ScheduleAsync(_s.Request(bo, At(9, 30))));
        Assert.Equal("provider_double_booked", clash.Code);
    }

    // ---------- acceptance 3: every change is logged with a timestamp and user ----------

    [Fact]
    public async Task Every_flow_move_is_in_the_history_and_the_audit_log_with_user_and_time_and_no_patient_details()
    {
        var a = await BookAsync(At(9));
        await CompleteAsync(await StartAsync(await CheckInAsync(a)));

        var flowEvents = (await _s.HistoryAsync(a.Id)).Where(e => e.EventType is "CheckedIn" or "TreatmentStarted" or "Completed").ToList();
        Assert.Equal(["CheckedIn", "TreatmentStarted", "Completed"], flowEvents.Select(e => e.EventType));
        Assert.Equal(["Scheduled -> CheckedIn", "CheckedIn -> InTreatment", "InTreatment -> Completed"], flowEvents.Select(e => e.Detail));
        Assert.All(flowEvents, e => Assert.Equal(_s.Actor, e.ActorUserId));

        await using var db = _fixture.CreateContext();
        var audits = await db.AuditLogEntries
            .Where(e => e.EventType == SchedulingAuditEvents.PatientCheckedIn || e.EventType == SchedulingAuditEvents.TreatmentStarted || e.EventType == SchedulingAuditEvents.TreatmentCompleted)
            .OrderBy(e => e.TimestampUtc).ToListAsync();
        Assert.Equal(3, audits.Count);
        Assert.All(audits, e =>
        {
            Assert.Equal((_s.Actor, a.Id), (e.PerformedByUserAccountId, e.TargetUserAccountId));
            Assert.InRange(e.TimestampUtc, DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddSeconds(5));
            Assert.DoesNotContain("Ann", e.Details);
            Assert.DoesNotContain("Lee", e.Details);
        });
    }

    // ---------- failure paths ----------

    [Fact]
    public async Task If_the_log_cannot_be_written_the_status_does_not_change_either()
    {
        var a = await BookAsync(At(9));
        await using (var failing = _s.FailingAuditContext())
            await Assert.ThrowsAsync<DbUpdateException>(() => _s.Flow(failing).CheckInAsync(a.Id, a.RowVersion, _s.Actor, default));

        var unchanged = await _s.ReloadAsync(a.Id);
        Assert.Equal((PatientFlowStates.Scheduled, a.RowVersion), (unchanged.FlowState, unchanged.RowVersion));
        Assert.Equal(1, (await _s.HistoryAsync(a.Id)).Count); // only "Scheduled"

        // and the same request can simply be retried once logging is back
        Assert.Equal(PatientFlowStates.CheckedIn, (await CheckInAsync(a)).FlowState);
    }

    [Fact]
    public async Task A_move_the_rules_refuse_is_a_409_and_changes_nothing()
    {
        var a = await BookAsync(At(9));

        Assert.Equal("invalid_flow_transition", (await RefusedAsync(() => StartAsync(a))).Code);   // cannot skip check-in
        Assert.Equal("invalid_flow_transition", (await RefusedAsync(() => CompleteAsync(a))).Code); // cannot skip check-in

        var done = await CompleteAsync(await CheckInAsync(a));
        var backwards = await RefusedAsync(() => CheckInAsync(done)); // Completed -> CheckedIn would be backwards (the version is current)
        Assert.Equal(("invalid_flow_transition", 409), (backwards.Code, backwards.StatusCode));
        Assert.Equal(PatientFlowStates.Completed, (await _s.ReloadAsync(a.Id)).FlowState);
    }

    [Fact]
    public async Task A_stale_version_is_the_shared_concurrency_conflict_and_changes_nothing()
    {
        var a = await BookAsync(At(9));
        await _s.ManageAsync(m => m.UpdateNotesAsync(a.Id, "front desk note", a.RowVersion, _s.Actor, default)); // someone else changed it first

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => CheckInAsync(a));
        Assert.Equal(PatientFlowStates.Scheduled, (await _s.ReloadAsync(a.Id)).FlowState);

        var missing = await RefusedAsync(() => _s.FlowAsync(f => f.CheckInAsync(a.Id, null, _s.Actor, default)));
        Assert.Equal(("row_version_required", 400), (missing.Code, missing.StatusCode));
        var junk = await RefusedAsync(() => _s.FlowAsync(f => f.CheckInAsync(a.Id, "not base64!", _s.Actor, default)));
        Assert.Equal("row_version_invalid", junk.Code);
    }

    [Fact]
    public async Task A_cancelled_or_no_show_appointment_has_no_flow_and_an_unknown_one_is_404()
    {
        var toCancel = await BookAsync(At(9));
        var cancelled = await _s.ManageAsync(m => m.CancelAsync(toCancel.Id, "Illness", toCancel.RowVersion, _s.Actor, default));
        var refusedCancelled = await RefusedAsync(() => CheckInAsync(cancelled));
        Assert.Equal(("appointment_not_scheduled", 409), (refusedCancelled.Code, refusedCancelled.StatusCode));

        var toMiss = await BookAsync(At(11));
        var missed = await _s.ManageAsync(m => m.MarkNoShowAsync(toMiss.Id, toMiss.RowVersion, _s.Actor, default), new TestClock(toMiss.StartUtc.AddHours(1)));
        Assert.Equal("appointment_not_scheduled", (await RefusedAsync(() => CheckInAsync(missed))).Code);

        var unknown = await RefusedAsync(() => _s.FlowAsync(f => f.CheckInAsync(Guid.NewGuid(), "AAAA", _s.Actor, default)));
        Assert.Equal(("appointment_not_found", 404), (unknown.Code, unknown.StatusCode));
    }

    // ---------- boundary, repeats and races ----------

    [Fact]
    public async Task Repeating_a_move_changes_nothing_writes_nothing_and_needs_no_current_version()
    {
        var a = await BookAsync(At(9));
        var first = await CheckInAsync(a);
        var changedAt = first.FlowChangedAtUtc;

        var again = await CheckInAsync(a); // even the old version: the patient is already checked in
        var onceMore = await CheckInAsync(first);

        Assert.Equal((first.RowVersion, changedAt), (again.RowVersion, again.FlowChangedAtUtc));
        Assert.Equal(first.RowVersion, onceMore.RowVersion);
        Assert.Equal(1, await _s.CountAsync(db => db.AppointmentEvents.Where(e => e.AppointmentId == a.Id && e.EventType == AppointmentEventTypes.CheckedIn)));
        Assert.Equal(1, await _s.CountAsync(db => db.AuditLogEntries.Where(e => e.EventType == SchedulingAuditEvents.PatientCheckedIn)));
    }

    [Fact]
    public async Task Six_people_pressing_Check_in_at_once_produce_one_check_in()
    {
        var a = await BookAsync(At(9));

        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ =>
        {
            try { return (await CheckInAsync(a)).FlowState; }
            catch (ConcurrencyConflictException) { return "conflict"; }
        }));

        Assert.All(results, r => Assert.Contains(r, new[] { PatientFlowStates.CheckedIn, "conflict" }));
        Assert.Contains(PatientFlowStates.CheckedIn, results);
        Assert.Equal(1, await _s.CountAsync(db => db.AppointmentEvents.Where(e => e.AppointmentId == a.Id && e.EventType == AppointmentEventTypes.CheckedIn)));
        Assert.Equal(1, await _s.CountAsync(db => db.AuditLogEntries.Where(e => e.EventType == SchedulingAuditEvents.PatientCheckedIn)));
    }

    [Fact]
    public async Task The_database_itself_refuses_a_flow_beyond_Scheduled_on_a_cancelled_appointment_and_an_unknown_state()
    {
        var a = await BookAsync(At(9));
        await _s.ManageAsync(m => m.CancelAsync(a.Id, "Illness", a.RowVersion, _s.Actor, default));

        await using (var db = _fixture.CreateContext())
        {
            var row = await db.Appointments.SingleAsync(x => x.Id == a.Id);
            row.FlowState = PatientFlowStates.CheckedIn;
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
        await using (var db = _fixture.CreateContext())
        {
            var other = await BookAsync(At(13));
            var row = await db.Appointments.SingleAsync(x => x.Id == other.Id);
            row.FlowState = "Arrived";
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
    }

    // ---------- the existing actions respect a visit that is under way ----------

    [Fact]
    public async Task Once_the_patient_has_checked_in_the_appointment_cannot_be_cancelled_marked_no_show_or_rescheduled()
    {
        var a = await BookAsync(At(9));
        var ci = await CheckInAsync(a);

        var cancel = await RefusedAsync(() => _s.ManageAsync(m => m.CancelAsync(a.Id, "Illness", ci.RowVersion, _s.Actor, default)));
        var noShow = await RefusedAsync(() => _s.ManageAsync(m => m.MarkNoShowAsync(a.Id, ci.RowVersion, _s.Actor, default), new TestClock(a.StartUtc.AddHours(1))));
        var move = await RefusedAsync(() => _s.ManageAsync(m => m.RescheduleAsync(a.Id, new RescheduleRequest(a.ProviderId, a.OperatoryId, At(14), null, ci.RowVersion), _s.Actor, default)));

        Assert.All(new[] { cancel, noShow, move }, e => Assert.Equal(("appointment_in_progress", 409), (e.Code, e.StatusCode)));
        var after = await _s.ReloadAsync(a.Id);
        Assert.Equal((AppointmentStatuses.Scheduled, PatientFlowStates.CheckedIn, "2030-01-14T09:00"), (after.Status, after.FlowState, after.StartLocal));
    }

    [Fact]
    public async Task A_note_can_still_be_added_during_the_visit()
    {
        var a = await BookAsync(At(9));
        var ci = await CheckInAsync(a);

        var noted = await _s.ManageAsync(m => m.UpdateNotesAsync(a.Id, "Prefers the left chair", ci.RowVersion, _s.Actor, default));

        Assert.Equal(("Prefers the left chair", PatientFlowStates.CheckedIn), (noted.Notes, noted.FlowState));
    }
}
