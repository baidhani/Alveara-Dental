using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Scheduling;
using Xunit;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>ALV-011-C01 against real SQL Server: the provider and operatory a patient is actually with during the visit, kept apart from the booking, audited, and safe under concurrency.</summary>
public class VisitAssignmentTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private SchedulingTestSupport _s = null!;
    private Guid _ann, _bo;

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
        _s = new SchedulingTestSupport(_fixture);
        await _s.ArrangeAsync();
        _ann = await _s.PatientAsync();
        _bo = await _s.PatientAsync("Bo", "Kim");
    }
    public Task DisposeAsync() => _fixture.DisposeAsync();

    private async Task<AppointmentView> BookAsync(Guid patient, int hour, Guid? provider = null, Guid? operatory = null) =>
        (await _s.ScheduleAsync(_s.Request(patient, At(hour), provider, operatory))).Appointment;

    private Task<AppointmentView> GoAsync(AppointmentView a, string target) => _s.FlowAsync(f => f.TransitionAsync(a.Id, target, a.RowVersion, _s.Actor, default));

    private Task<AppointmentView> AssignAsync(AppointmentView a, Guid provider, Guid operatory, string? version = null) =>
        _s.AssignAsync(v => v.AssignAsync(a.Id, new AssignVisitRequest(provider, operatory, version ?? a.RowVersion), _s.Actor, default));

    private static async Task<SchedulingException> RefusedAsync(Func<Task<AppointmentView>> action) => await Assert.ThrowsAsync<SchedulingException>(action);

    [Fact]
    public async Task A_new_appointment_shows_the_booked_provider_and_operatory_as_where_the_patient_will_be()
    {
        var a = await BookAsync(_ann, 9);
        Assert.Equal((_s.ProviderA, "Dr. Rivera", _s.Op1, "Op 1"), (a.VisitProviderId, a.VisitProviderName, a.VisitOperatoryId, a.VisitOperatoryName));
    }

    [Fact]
    public async Task Assigning_a_provider_and_operatory_is_visible_in_the_view_and_never_moves_the_booking()
    {
        var a = await BookAsync(_ann, 9);

        var assigned = await AssignAsync(a, _s.ProviderB, _s.Op2);

        Assert.Equal((_s.ProviderB, "Dr. Patel", _s.Op2, "Op 2"), (assigned.VisitProviderId, assigned.VisitProviderName, assigned.VisitOperatoryId, assigned.VisitOperatoryName));
        Assert.Equal((_s.ProviderA, "Dr. Rivera", _s.Op1, "Op 1", "2030-01-14T09:00"), (assigned.ProviderId, assigned.ProviderName, assigned.OperatoryId, assigned.OperatoryName, assigned.StartLocal));
        // the booked slot still belongs to the booking: another patient cannot take Dr. Rivera in Op 1 at 09:30
        Assert.Equal("provider_double_booked", (await Assert.ThrowsAsync<SchedulingException>(() => _s.ScheduleAsync(_s.Request(_bo, At(9, 30))))).Code);
        // and the calendar's appointment list still places it by the booked provider and operatory
        var listed = (await _s.ReloadAsync(a.Id));
        Assert.Equal((_s.ProviderA, _s.Op1), (listed.ProviderId, listed.OperatoryId));
    }

    [Fact]
    public async Task Assigning_the_booked_provider_and_operatory_back_clears_the_override()
    {
        var a = await BookAsync(_ann, 9);
        var moved = await AssignAsync(a, _s.ProviderB, _s.Op2);

        var back = await AssignAsync(moved, _s.ProviderA, _s.Op1);

        await using var db = _fixture.CreateContext();
        var row = await db.Appointments.AsNoTracking().SingleAsync(x => x.Id == a.Id);
        Assert.Equal((null, null), (row.VisitProviderProfileId, row.VisitOperatoryId));
        Assert.Equal((_s.ProviderA, _s.Op1), (back.VisitProviderId, back.VisitOperatoryId));
    }

    [Fact]
    public async Task Every_assignment_change_is_in_the_history_with_where_the_patient_was_and_in_the_audit_log_with_user_and_time_and_no_patient_details()
    {
        var a = await BookAsync(_ann, 9);
        await AssignAsync(a, _s.ProviderB, _s.Op1);

        var change = (await _s.HistoryAsync(a.Id)).Single(e => e.EventType == "AssignmentChanged");
        Assert.Equal((_s.Actor, "Provider changed.", "Dr. Rivera", "Op 1"), (change.ActorUserId, change.Detail, change.PreviousProviderName, change.PreviousOperatoryName));

        await using var db = _fixture.CreateContext();
        var audit = await db.AuditLogEntries.SingleAsync(e => e.EventType == SchedulingAuditEvents.VisitAssignmentChanged);
        Assert.Equal((_s.Actor, a.Id), (audit.PerformedByUserAccountId, audit.TargetUserAccountId));
        Assert.InRange(audit.TimestampUtc, DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddSeconds(5));
        Assert.DoesNotContain("Ann", audit.Details);
        Assert.DoesNotContain("Patel", audit.Details);
    }

    [Fact]
    public async Task Assigning_what_the_visit_already_has_changes_nothing_and_writes_nothing_whatever_the_version()
    {
        var a = await BookAsync(_ann, 9);
        var first = await AssignAsync(a, _s.ProviderB, _s.Op2);

        var again = await AssignAsync(a, _s.ProviderB, _s.Op2); // the old version, but the visit is already assigned this way

        Assert.Equal(first.RowVersion, again.RowVersion);
        Assert.Equal(1, await _s.CountAsync(db => db.AppointmentEvents.Where(e => e.AppointmentId == a.Id && e.EventType == AppointmentEventTypes.AssignmentChanged)));
        Assert.Equal(1, await _s.CountAsync(db => db.AuditLogEntries.Where(e => e.EventType == SchedulingAuditEvents.VisitAssignmentChanged)));
    }

    [Fact]
    public async Task A_stale_version_is_the_shared_conflict_and_two_people_assigning_at_once_never_overwrite_each_other()
    {
        var a = await BookAsync(_ann, 9);
        await _s.ManageAsync(m => m.UpdateNotesAsync(a.Id, "front desk note", a.RowVersion, _s.Actor, default)); // someone else changed it first
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => AssignAsync(a, _s.ProviderB, _s.Op2));
        Assert.Equal((_s.ProviderA, _s.Op1), ((await _s.ReloadAsync(a.Id)).VisitProviderId, (await _s.ReloadAsync(a.Id)).VisitOperatoryId));

        var fresh = await _s.ReloadAsync(a.Id);
        var results = await Task.WhenAll(new[] { (_s.ProviderB, _s.Op2), (_s.ProviderB, _s.Op1) }.Select(async t =>
        {
            try { return (await AssignAsync(fresh, t.Item1, t.Item2)).VisitOperatoryName!; }
            catch (ConcurrencyConflictException) { return "conflict"; }
        }));
        Assert.Equal(1, results.Count(r => r != "conflict"));
        Assert.Equal(1, await _s.CountAsync(db => db.AppointmentEvents.Where(e => e.AppointmentId == a.Id && e.EventType == AppointmentEventTypes.AssignmentChanged)));
    }

    [Fact]
    public async Task A_finished_cancelled_or_no_show_visit_cannot_be_reassigned()
    {
        var done = await BookAsync(_ann, 9);
        foreach (var step in VisitStates.InOrder.Skip(1)) done = await GoAsync(done, step);
        Assert.Equal("visit_completed", (await RefusedAsync(() => AssignAsync(done, _s.ProviderB, _s.Op2))).Code);

        var b = await BookAsync(_bo, 11);
        var cancelled = await _s.ManageAsync(m => m.CancelAsync(b.Id, "Illness", b.RowVersion, _s.Actor, default));
        Assert.Equal("appointment_not_scheduled", (await RefusedAsync(() => AssignAsync(cancelled, _s.ProviderB, _s.Op2))).Code);
    }

    [Fact]
    public async Task A_missing_inactive_or_unknown_provider_or_operatory_is_refused_and_nothing_changes()
    {
        var a = await BookAsync(_ann, 9);
        var unknown = await RefusedAsync(() => _s.AssignAsync(v => v.AssignAsync(Guid.NewGuid(), new AssignVisitRequest(_s.ProviderB, _s.Op2, "AAAA"), _s.Actor, default)));
        Assert.Equal(("appointment_not_found", 404), (unknown.Code, unknown.StatusCode));
        Assert.Equal("provider_not_found", (await RefusedAsync(() => AssignAsync(a, Guid.NewGuid(), _s.Op2))).Code);
        Assert.Equal("operatory_not_found", (await RefusedAsync(() => AssignAsync(a, _s.ProviderB, Guid.NewGuid()))).Code);

        await using (var db = _fixture.CreateContext())
        {
            (await db.ProviderProfiles.SingleAsync(p => p.Id == _s.ProviderB)).IsActive = false;
            await db.SaveChangesAsync();
        }
        Assert.Equal("provider_inactive", (await RefusedAsync(() => AssignAsync(a, _s.ProviderB, _s.Op2))).Code);
        Assert.Equal(0, await _s.CountAsync(db => db.AppointmentEvents.Where(e => e.EventType == AppointmentEventTypes.AssignmentChanged)));
    }

    [Fact]
    public async Task If_the_log_cannot_be_written_the_assignment_is_not_stored()
    {
        var a = await BookAsync(_ann, 9);
        await using (var failing = _s.FailingAuditContext())
            await Assert.ThrowsAsync<DbUpdateException>(() => _s.Assignment(failing).AssignAsync(a.Id, new AssignVisitRequest(_s.ProviderB, _s.Op2, a.RowVersion), _s.Actor, default));

        var unchanged = await _s.ReloadAsync(a.Id);
        Assert.Equal((_s.ProviderA, _s.Op1, a.RowVersion), (unchanged.VisitProviderId, unchanged.VisitOperatoryId, unchanged.RowVersion));
        Assert.Equal("Op 2", (await AssignAsync(a, _s.ProviderB, _s.Op2)).VisitOperatoryName); // the same request works once logging is back
    }

    [Fact]
    public async Task Two_seated_patients_moved_into_the_same_free_room_at_once_leave_exactly_one_there()
    {
        var a = await BookAsync(_ann, 9);
        foreach (var step in new[] { VisitStates.CheckedIn, VisitStates.Ready, VisitStates.Seated }) a = await GoAsync(a, step);
        var b = await BookAsync(_bo, 11, _s.ProviderB, _s.Op2);
        foreach (var step in new[] { VisitStates.CheckedIn, VisitStates.Ready, VisitStates.Seated }) b = await GoAsync(b, step);
        var room3 = await CreateOperatoryAsync("Op 3");

        // each pauses between its check and its write, so only the operatory lock can stop both being moved into Op 3
        var results = await Task.WhenAll(new[] { a, b }.Select(async x =>
        {
            await using var db = _s.SlowSaveContext(TimeSpan.FromMilliseconds(200));
            try { return (await _s.Assignment(db).AssignAsync(x.Id, new AssignVisitRequest(x.ProviderId, room3, x.RowVersion), _s.Actor, default)).VisitOperatoryName!; }
            catch (SchedulingException ex) { return ex.Code; }
        }));

        Assert.Equal(1, results.Count(r => r == "Op 3"));
        Assert.Equal(1, results.Count(r => r == "operatory_occupied"));
        Assert.Equal(1, await _s.CountAsync(db => db.Appointments.Where(x => x.VisitOperatoryId == room3)));
    }

    private async Task<Guid> CreateOperatoryAsync(string name)
    {
        await using var db = _fixture.CreateContext();
        return (await new Alveara.Api.Architecture.Configuration.PracticeConfigurationService(db, SchedulingTestSupport.Clock).CreateOperatoryAsync(name, _s.Actor, default)).Id;
    }

    [Fact]
    public async Task A_seated_patient_cannot_be_moved_into_an_occupied_room_but_a_waiting_patient_can_be_assigned_there_and_is_checked_at_seating()
    {
        var holder = await BookAsync(_ann, 9, _s.ProviderB, _s.Op2);
        foreach (var step in new[] { VisitStates.CheckedIn, VisitStates.Ready, VisitStates.Seated }) holder = await GoAsync(holder, step);

        var seated = await BookAsync(_bo, 11);
        foreach (var step in new[] { VisitStates.CheckedIn, VisitStates.Ready, VisitStates.Seated }) seated = await GoAsync(seated, step);
        var ex = await RefusedAsync(() => AssignAsync(seated, _s.ProviderA, _s.Op2));
        Assert.Equal(("operatory_occupied", holder.Id), (ex.Code, ex.ConflictingAppointmentId!.Value));
        Assert.Equal(_s.Op1, (await _s.ReloadAsync(seated.Id)).VisitOperatoryId); // stayed where they were

        var waiting = await BookAsync(await _s.PatientAsync("Cy", "Poe"), 13);
        var assigned = await AssignAsync(waiting, _s.ProviderA, _s.Op2); // not seated yet: assigning the room is fine
        Assert.Equal(_s.Op2, assigned.VisitOperatoryId);
        var ready = await GoAsync(await GoAsync(assigned, VisitStates.CheckedIn), VisitStates.Ready);
        Assert.Equal("operatory_occupied", (await RefusedAsync(() => GoAsync(ready, VisitStates.Seated))).Code);
    }
}
