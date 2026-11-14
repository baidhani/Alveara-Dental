using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Scheduling;
using Xunit;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N021 A2 (OD-11) against real SQL Server: undo a check-in from Checked in or Ready, back to the state before check-in, front desk only, reason required; the original check-in event is
/// never changed; refused once the patient is seated.
/// </summary>
public class UndoCheckInTests : IAsyncLifetime
{
    private const string Why = "Checked in the wrong patient";
    private readonly TestDatabaseFixture _fixture = new();
    private SchedulingTestSupport _s = null!;
    private Guid _ann;

    public Task InitializeAsync() => _fixture.InitializeOwnerAsync(async () =>
    {
        _s = new SchedulingTestSupport(_fixture);
        await _s.ArrangeAsync();
        _ann = await _s.PatientAsync();
    });
    public Task DisposeAsync() => _fixture.DisposeAsync();

    private async Task<AppointmentView> BookAsync(int hour = 9) => (await _s.ScheduleAsync(_s.Request(_ann, At(hour)))).Appointment;

    private Task<AppointmentView> UndoAsync(AppointmentView a, string? reason = Why, VisitActor? by = null, string? version = null) =>
        _s.FlowAsync(f => f.UndoCheckInAsync(a.Id, reason, version ?? a.RowVersion, by ?? _s.Desk, default));

    private static async Task<SchedulingException> RefusedAsync(Func<Task<AppointmentView>> action) => await Assert.ThrowsAsync<SchedulingException>(action);

    private Task<int> UndoEventsAsync(Guid id) => _s.CountAsync(db => db.AppointmentEvents.Where(e => e.AppointmentId == id && e.EventType == AppointmentEventTypes.CheckInUndone));

    [Theory]
    [InlineData(VisitStates.CheckedIn)]
    [InlineData(VisitStates.Ready)]
    public async Task Undo_returns_a_confirmed_patient_to_confirmed_keeps_the_check_in_event_and_records_who_why_and_from_where(string from)
    {
        var a = await _s.WalkAsync(await BookAsync(), from);
        await using (var before = _fixture.CreateContext())
        {
            var checkIn = await before.AppointmentEvents.AsNoTracking().SingleAsync(e => e.AppointmentId == a.Id && e.EventType == AppointmentEventTypes.CheckedIn);

            var undone = await UndoAsync(a);

            Assert.Equal(VisitStates.Confirmed, undone.FlowState);
            Assert.Contains(VisitStates.CheckedIn, undone.NextFlowStates!);   // the patient can be checked in again
            await using var after = _fixture.CreateContext();
            var same = await after.AppointmentEvents.AsNoTracking().SingleAsync(e => e.Id == checkIn.Id);
            Assert.Equal((checkIn.EventType, checkIn.Detail, checkIn.ActorUserId, checkIn.OccurredAtUtc, checkIn.Reason), (same.EventType, same.Detail, same.ActorUserId, same.OccurredAtUtc, same.Reason));   // original unchanged
        }

        var undo = (await _s.HistoryAsync(a.Id)).Single(e => e.EventType == AppointmentEventTypes.CheckInUndone);
        Assert.Equal((_s.Actor, $"{from} -> Confirmed", Why), (undo.ActorUserId, undo.Detail, undo.Reason));
        await using var db = _fixture.CreateContext();
        var audit = await db.AuditLogEntries.SingleAsync(e => e.EventType == SchedulingAuditEvents.CheckInUndone);
        Assert.Equal((_s.Actor, a.Id), (audit.PerformedByUserAccountId, audit.TargetUserAccountId));
        Assert.DoesNotContain("wrong", audit.Details);   // the reason stays out of the audit log
        Assert.DoesNotContain("Ann", audit.Details);
    }

    [Fact]
    public async Task With_confirmation_switched_off_a_patient_checked_in_straight_from_scheduled_goes_back_to_scheduled()
    {
        await using (var db = _fixture.CreateContext())   // the same settings row VisitMovePolicyTests writes
        {
            await db.Database.ExecuteSqlRawAsync("DELETE FROM [PracticeSettings]");
            await db.Database.ExecuteSqlRawAsync("INSERT INTO [PracticeSettings] ([Id], [Name], [UpdatedAtUtc], [ConfirmationRequired], [MarkReadyRequired]) VALUES ({0}, N'Practice', SYSUTCDATETIME(), 0, 1)",
                Architecture.Configuration.PracticeSettings.SingletonId);
        }
        var a = await BookAsync();
        a = await _s.FlowAsync(f => f.TransitionAsync(a.Id, VisitStates.CheckedIn, a.RowVersion, _s.Desk, default));

        Assert.Equal(VisitStates.Scheduled, (await UndoAsync(a)).FlowState);
    }

    [Fact]
    public async Task A_reason_is_required_and_bounded_and_a_refusal_writes_nothing()
    {
        var a = await _s.WalkAsync(await BookAsync(), VisitStates.CheckedIn);

        foreach (var blank in new string?[] { null, "", "  \r\n " })
            Assert.Equal((400, "reason_required"), Code(await RefusedAsync(() => UndoAsync(a, blank))));
        Assert.Equal((400, "reason_too_long"), Code(await RefusedAsync(() => UndoAsync(a, new string('x', VisitReasons.MaxLength + 1)))));

        Assert.Equal(VisitStates.CheckedIn, (await _s.ReloadAsync(a.Id)).FlowState);
        Assert.Equal(0, await UndoEventsAsync(a.Id));
    }

    private static (int, string) Code(SchedulingException ex) => (ex.StatusCode, ex.Code);

    [Theory]
    [InlineData(Role.Dentist)]
    [InlineData(Role.Hygienist)]
    [InlineData(Role.Assistant)]
    [InlineData(Role.Billing)]
    [InlineData(Role.Unassigned)]
    public async Task Only_the_front_office_can_undo_a_check_in(Role role)
    {
        var a = await _s.WalkAsync(await BookAsync(), VisitStates.CheckedIn);

        var ex = await RefusedAsync(() => UndoAsync(a, by: new VisitActor(_s.Actor, role)));

        Assert.Equal((403, "visit_move_forbidden", "front_office_only"), (ex.StatusCode, ex.Code, ex.Reason));
        Assert.Equal(VisitStates.CheckedIn, (await _s.ReloadAsync(a.Id)).FlowState);
    }

    [Theory]
    [InlineData(Role.OfficeManager)]
    [InlineData(Role.Admin)]
    public async Task The_office_manager_and_administrator_can_undo_a_check_in(Role role)
    {
        var a = await _s.WalkAsync(await BookAsync(), VisitStates.CheckedIn);
        Assert.Equal(VisitStates.Confirmed, (await UndoAsync(a, by: new VisitActor(_s.Actor, role))).FlowState);
    }

    [Theory]
    [InlineData(VisitStates.Seated)]
    [InlineData(VisitStates.InTreatment)]
    [InlineData(VisitStates.CheckedOut)]
    public async Task Once_the_patient_is_seated_the_check_in_can_no_longer_be_undone(string state)
    {
        var a = await _s.WalkAsync(await BookAsync(), state);

        Assert.Equal((409, "undo_check_in_too_late"), Code(await RefusedAsync(() => UndoAsync(a))));
        Assert.Equal(state, (await _s.ReloadAsync(a.Id)).FlowState);
        Assert.Equal(0, await UndoEventsAsync(a.Id));
    }

    [Fact]
    public async Task A_completed_cancelled_or_never_checked_in_appointment_has_no_check_in_to_undo()
    {
        var done = await _s.WalkAsync(await BookAsync(9), VisitStates.Completed);
        Assert.Equal((409, "undo_check_in_too_late"), Code(await RefusedAsync(() => UndoAsync(done))));

        var waiting = await _s.WalkAsync(await BookAsync(11), VisitStates.Confirmed);
        Assert.Equal((409, "illegal_transition"), Code(await RefusedAsync(() => UndoAsync(waiting))));

        var cancelled = await _s.ManageAsync(m => m.CancelAsync(waiting.Id, "patient", null, waiting.RowVersion, _s.Actor, default));
        Assert.Equal((409, "terminal_state"), Code(await RefusedAsync(() => UndoAsync(cancelled))));
    }

    [Fact]
    public async Task A_retry_of_the_same_undo_changes_nothing_and_a_stale_version_is_the_shared_conflict()
    {
        var a = await _s.WalkAsync(await BookAsync(), VisitStates.CheckedIn);
        var first = await UndoAsync(a);

        var again = await UndoAsync(a);   // the same request again, with the version from before the undo

        Assert.Equal((first.FlowState, first.RowVersion), (again.FlowState, again.RowVersion));
        Assert.Equal(1, await UndoEventsAsync(a.Id));
        Assert.Equal((409, "illegal_transition"), Code(await RefusedAsync(() => UndoAsync(a, "A different reason"))));   // not a retry: there is nothing to undo

        var b = await _s.WalkAsync(await BookAsync(11), VisitStates.CheckedIn);
        await _s.ManageAsync(m => m.UpdateNotesAsync(b.Id, "note", b.RowVersion, _s.Actor, default));   // someone else changed it first
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => UndoAsync(b));
        Assert.Equal(VisitStates.CheckedIn, (await _s.ReloadAsync(b.Id)).FlowState);
    }

    [Fact]
    public async Task After_an_undo_the_patient_can_be_checked_in_again_and_the_history_shows_both_check_ins()
    {
        var a = await _s.WalkAsync(await BookAsync(), VisitStates.CheckedIn);
        var undone = await UndoAsync(a);

        var again = await _s.MoveAsync(undone, VisitStates.CheckedIn);

        Assert.Equal(VisitStates.CheckedIn, again.FlowState);
        var types = (await _s.HistoryAsync(a.Id)).Select(e => e.EventType).ToList();
        Assert.Equal(["Scheduled", "Confirmed", "CheckedIn", "CheckInUndone", "CheckedIn"], types);
    }

    [Fact]
    public async Task If_the_audit_entry_cannot_be_written_the_undo_is_not_stored()
    {
        var a = await _s.WalkAsync(await BookAsync(), VisitStates.CheckedIn);
        await using (var failing = _s.FailingAuditContext())
            await Assert.ThrowsAsync<DbUpdateException>(() => _s.Flow(failing).UndoCheckInAsync(a.Id, Why, a.RowVersion, _s.Desk, default));

        Assert.Equal(VisitStates.CheckedIn, (await _s.ReloadAsync(a.Id)).FlowState);
        Assert.Equal(0, await UndoEventsAsync(a.Id));
    }
}
