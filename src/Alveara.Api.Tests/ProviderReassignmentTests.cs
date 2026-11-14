using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Configuration;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Notifications;
using Alveara.Api.Architecture.Permissions;
using Alveara.Api.Architecture.Scheduling;
using Xunit;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N021 A2 (OD-13 to OD-16) against real SQL Server: reassigning an appointment's booked provider and/or room before treatment starts. Replaces ALV-011-C01's VisitAssignmentTests
/// ("Seen by"), whose behaviour is retired: the booking itself now moves, through the booking guards, with a reason for a provider change and two Urgent notifications.
/// </summary>
public class ProviderReassignmentTests : IAsyncLifetime
{
    private const string Why = "Dr. Rivera called in sick";
    private readonly TestDatabaseFixture _fixture = new();
    private SchedulingTestSupport _s = null!;
    private Guid _ann, _bo;

    public Task InitializeAsync() => _fixture.InitializeOwnerAsync(async () =>
    {
        _s = new SchedulingTestSupport(_fixture);
        await _s.ArrangeAsync();
        _ann = await _s.PatientAsync();
        _bo = await _s.PatientAsync("Bo", "Kim");
    });
    public Task DisposeAsync() => _fixture.DisposeAsync();

    private async Task<AppointmentView> BookAsync(Guid patient, int hour, Guid? provider = null, Guid? operatory = null) =>
        (await _s.ScheduleAsync(_s.Request(patient, At(hour), provider, operatory))).Appointment;

    private Task<AppointmentView> ReassignAsync(AppointmentView a, Guid provider, Guid operatory, string? reason = Why, VisitActor? by = null, string? version = null) =>
        _s.AssignAsync(v => v.AssignAsync(a.Id, new AssignVisitRequest(provider, operatory, version ?? a.RowVersion, reason), by ?? _s.OfficeManager, default));

    private static async Task<SchedulingException> RefusedAsync(Func<Task<AppointmentView>> action) => await Assert.ThrowsAsync<SchedulingException>(action);

    private Task<int> EventsAsync(Guid id) => _s.CountAsync(db => db.AppointmentEvents.Where(e => e.AppointmentId == id && (e.EventType == AppointmentEventTypes.ProviderReassigned || e.EventType == AppointmentEventTypes.RoomChanged)));

    private async Task<List<Notification>> NotificationsAsync()
    {
        await using var db = _fixture.CreateContext();
        return await db.Notifications.AsNoTracking().Where(n => n.Kind == NotificationKinds.AppointmentProviderReassigned).ToListAsync();
    }

    // ---------- the happy path ----------

    [Fact]
    public async Task A_provider_change_moves_the_booking_records_who_why_and_from_where_and_notifies_both_providers_once()
    {
        var a = await BookAsync(_ann, 9);

        var moved = await ReassignAsync(a, _s.ProviderB, _s.Op2);

        Assert.Equal((_s.ProviderB, "Dr. Patel", _s.Op2, "Op 2"), (moved.ProviderId, moved.ProviderName, moved.OperatoryId, moved.OperatoryName));
        Assert.Equal((_s.ProviderB, _s.Op2), (moved.VisitProviderId!.Value, moved.VisitOperatoryId!.Value));                 // the booking is where the patient is: no separate "Seen by"
        // the old slot is free and the new one is held: Dr. Rivera can now take another patient at 09:00, Dr. Patel cannot
        Assert.True((await _s.ScheduleAsync(_s.Request(_bo, At(9)))).Created);
        Assert.Equal("provider_double_booked", (await RefusedAsync(async () => (await _s.ScheduleAsync(_s.Request(await _s.PatientAsync("Cy", "Poe"), At(9), _s.ProviderB, _s.Op1))).Appointment)).Code);

        var change = (await _s.HistoryAsync(a.Id)).Single(e => e.EventType == AppointmentEventTypes.ProviderReassigned);
        Assert.Equal((_s.Actor, "Provider and room changed.", "Dr. Rivera", "Op 1", Why), (change.ActorUserId, change.Detail, change.PreviousProviderName, change.PreviousOperatoryName, change.Reason));

        await using var db = _fixture.CreateContext();
        var audit = await db.AuditLogEntries.SingleAsync(e => e.EventType == SchedulingAuditEvents.ProviderReassigned);
        Assert.Equal((_s.Actor, a.Id), (audit.PerformedByUserAccountId, audit.TargetUserAccountId));
        foreach (var phi in new[] { "Ann", "Lee", "Patel", "Rivera", "sick" }) Assert.DoesNotContain(phi, audit.Details);

        var sent = await NotificationsAsync();
        Assert.Equal([_s.ProviderAUser, _s.ProviderBUser], sent.Select(n => n.RecipientUserId).Order());
        var eventId = await db.AppointmentEvents.Where(e => e.AppointmentId == a.Id && e.EventType == AppointmentEventTypes.ProviderReassigned).Select(e => e.Id).SingleAsync();
        Assert.All(sent, n =>
        {
            Assert.Equal((NotificationPriorities.Urgent, VisitAssignmentService.SourceKeyFor(a.Id, eventId)), (n.Priority, n.SourceKey));
            Assert.Equal((null, null, null, null), (n.MessageId, n.RelatedPatientId, n.RelatedDocumentId, n.RelatedOtherDocumentId));   // PHI-free: ids in the key only
        });
    }

    [Fact]
    public async Task Asking_again_for_what_the_appointment_already_has_writes_nothing_and_sends_nothing_new()
    {
        var a = await BookAsync(_ann, 9);
        var first = await ReassignAsync(a, _s.ProviderB, _s.Op1);

        var again = await ReassignAsync(a, _s.ProviderB, _s.Op1);   // the old version: a retry of the same request

        Assert.Equal(first.RowVersion, again.RowVersion);
        Assert.Equal(1, await EventsAsync(a.Id));
        Assert.Equal(2, (await NotificationsAsync()).Count);
        Assert.Equal(1, await _s.CountAsync(db => db.AuditLogEntries.Where(e => e.EventType == SchedulingAuditEvents.ProviderReassigned)));
    }

    [Fact]
    public async Task A_room_only_change_needs_no_reason_records_the_old_room_and_notifies_nobody()
    {
        var a = await BookAsync(_ann, 9);

        var moved = await ReassignAsync(a, _s.ProviderA, _s.Op2, reason: null, by: new VisitActor(_s.Actor, Role.Assistant));

        Assert.Equal((_s.ProviderA, _s.Op2), (moved.ProviderId, moved.OperatoryId));
        var change = (await _s.HistoryAsync(a.Id)).Single(e => e.EventType == AppointmentEventTypes.RoomChanged);
        Assert.Equal(("Room changed.", "Op 1", null), (change.Detail, change.PreviousOperatoryName, change.Reason));
        Assert.Equal(1, await _s.CountAsync(db => db.AuditLogEntries.Where(e => e.EventType == SchedulingAuditEvents.RoomChanged)));
        Assert.Empty(await NotificationsAsync());
    }

    // ---------- who ----------

    [Theory]
    [InlineData(Role.FrontDesk)]
    [InlineData(Role.Dentist)]
    [InlineData(Role.Hygienist)]
    [InlineData(Role.Assistant)]
    [InlineData(Role.Billing)]
    public async Task Without_the_reassignment_permission_a_provider_change_is_refused_and_nothing_is_written(Role role)
    {
        var a = await BookAsync(_ann, 9);

        var ex = await RefusedAsync(() => ReassignAsync(a, _s.ProviderB, _s.Op1, by: new VisitActor(_s.Actor, role)));

        Assert.Equal((403, "visit_move_forbidden", "reassign_not_permitted"), (ex.StatusCode, ex.Code, ex.Reason));
        Assert.Equal(_s.ProviderA, (await _s.ReloadAsync(a.Id)).ProviderId);
        Assert.Equal(0, await EventsAsync(a.Id));
        Assert.Empty(await NotificationsAsync());
    }

    [Theory]
    [InlineData(Role.FrontDesk, "room_change_not_permitted")]
    [InlineData(Role.Billing, "room_change_not_permitted")]
    [InlineData(Role.Unassigned, "room_change_not_permitted")]
    [InlineData(Role.Dentist, null)]
    [InlineData(Role.Hygienist, null)]
    [InlineData(Role.Assistant, null)]
    [InlineData(Role.OfficeManager, null)]
    [InlineData(Role.Admin, null)]
    public async Task A_room_only_change_is_for_reassignment_holders_and_chairside_staff(Role role, string? refusal)
    {
        var a = await BookAsync(_ann, 9);

        if (refusal is null)
            Assert.Equal(_s.Op2, (await ReassignAsync(a, _s.ProviderA, _s.Op2, reason: null, by: new VisitActor(_s.Actor, role))).OperatoryId);
        else
            Assert.Equal(refusal, (await RefusedAsync(() => ReassignAsync(a, _s.ProviderA, _s.Op2, reason: null, by: new VisitActor(_s.Actor, role)))).Reason);
    }

    [Fact]
    public async Task An_administrator_can_grant_reassignment_to_the_front_desk()
    {
        var a = await BookAsync(_ann, 9);
        var source = new ControlledPolicySource { Version = 1 };
        source.Overrides.Add(PolicyTestSupport.Override(nameof(Role.FrontDesk), nameof(Permission.ReassignAppointmentProvider), OverrideEffects.Grant));
        var granted = new EffectivePermissionService(PolicyTestSupport.Store(source));

        await using var db = _fixture.CreateContext();
        var service = new VisitAssignmentService(db, VisitDayClock, new SchedulingConfiguration(db, VisitDayClock), granted);
        var moved = await service.AssignAsync(a.Id, new AssignVisitRequest(_s.ProviderB, _s.Op1, a.RowVersion, Why), _s.Desk, default);

        Assert.Equal(_s.ProviderB, moved.ProviderId);
    }

    // ---------- reason, timing, guards ----------

    [Fact]
    public async Task A_provider_change_without_a_reason_is_refused_and_nothing_is_written()
    {
        var a = await BookAsync(_ann, 9);

        foreach (var blank in new string?[] { null, "", "   " })
            Assert.Equal((400, "reason_required"), await CodeAsync(() => ReassignAsync(a, _s.ProviderB, _s.Op1, reason: blank)));
        Assert.Equal((400, "reason_too_long"), await CodeAsync(() => ReassignAsync(a, _s.ProviderB, _s.Op1, reason: new string('x', VisitReasons.MaxLength + 1))));

        Assert.Equal(_s.ProviderA, (await _s.ReloadAsync(a.Id)).ProviderId);
        Assert.Equal(0, await EventsAsync(a.Id));
        Assert.Empty(await NotificationsAsync());
    }

    private static async Task<(int, string)> CodeAsync(Func<Task<AppointmentView>> action)
    {
        var ex = await RefusedAsync(action);
        return (ex.StatusCode, ex.Code);
    }

    [Theory]
    [InlineData(VisitStates.Confirmed)]
    [InlineData(VisitStates.CheckedIn)]
    [InlineData(VisitStates.Ready)]
    [InlineData(VisitStates.Seated)]
    public async Task Reassignment_is_allowed_up_to_seating(string state)
    {
        var a = await _s.WalkAsync(await BookAsync(_ann, 9), state);
        Assert.Equal(_s.ProviderB, (await ReassignAsync(a, _s.ProviderB, _s.Op1)).ProviderId);
    }

    [Theory]
    [InlineData(VisitStates.InTreatment)]
    [InlineData(VisitStates.CheckedOut)]
    [InlineData(VisitStates.Completed)]
    public async Task Once_treatment_has_started_the_provider_and_room_can_no_longer_change(string state)
    {
        var a = await _s.WalkAsync(await BookAsync(_ann, 9), state);

        Assert.Equal((409, "visit_already_started"), await CodeAsync(() => ReassignAsync(a, _s.ProviderB, _s.Op1)));
        Assert.Equal((409, "visit_already_started"), await CodeAsync(() => ReassignAsync(a, _s.ProviderA, _s.Op2, reason: null)));
        Assert.Equal(0, await EventsAsync(a.Id));
    }

    [Fact]
    public async Task A_cancelled_appointment_cannot_be_reassigned()
    {
        var a = await BookAsync(_ann, 9);
        var cancelled = await _s.ManageAsync(m => m.CancelAsync(a.Id, "patient", null, a.RowVersion, _s.Actor, default));
        Assert.Equal((409, "terminal_state"), await CodeAsync(() => ReassignAsync(cancelled, _s.ProviderB, _s.Op1)));
    }

    [Fact]
    public async Task The_booking_guards_apply_a_busy_provider_a_busy_room_and_a_blocked_provider_are_each_refused()
    {
        var a = await BookAsync(_ann, 9);
        var patelAt9 = await BookAsync(_bo, 9, _s.ProviderB, _s.Op2);

        var busy = await RefusedAsync(() => ReassignAsync(a, _s.ProviderB, _s.Op1));
        Assert.Equal(("provider_double_booked", patelAt9.Id), (busy.Code, busy.ConflictingAppointmentId!.Value));
        var room = await RefusedAsync(() => ReassignAsync(a, _s.ProviderA, _s.Op2, reason: null));
        Assert.Equal(("operatory_conflict", patelAt9.Id), (room.Code, room.ConflictingAppointmentId!.Value));

        var later = await BookAsync(await _s.PatientAsync("Cy", "Poe"), 13);
        await using (var db = _fixture.CreateContext())
            await new StaffProviderService(db, Clock).AddBlockedTimeAsync(_s.ProviderB, At(12), At(15), "Course", _s.Actor, default);
        var blocked = await RefusedAsync(() => ReassignAsync(later, _s.ProviderB, _s.Op1));
        Assert.Equal(("provider_unavailable", "blocked_time"), (blocked.Code, blocked.Reason));

        Assert.Equal(0, await EventsAsync(a.Id) + await EventsAsync(later.Id));
        Assert.Empty(await NotificationsAsync());
    }

    [Fact]
    public async Task A_missing_or_inactive_provider_or_room_is_refused()
    {
        var a = await BookAsync(_ann, 9);
        var unknown = await RefusedAsync(() => _s.AssignAsync(v => v.AssignAsync(Guid.NewGuid(), new AssignVisitRequest(_s.ProviderB, _s.Op2, "AAAA", Why), _s.OfficeManager, default)));
        Assert.Equal(("appointment_not_found", 404), (unknown.Code, unknown.StatusCode));
        Assert.Equal("provider_not_found", (await RefusedAsync(() => ReassignAsync(a, Guid.NewGuid(), _s.Op1))).Code);
        Assert.Equal("operatory_not_found", (await RefusedAsync(() => ReassignAsync(a, _s.ProviderA, Guid.NewGuid(), reason: null))).Code);

        await using (var db = _fixture.CreateContext())
        {
            (await db.ProviderProfiles.SingleAsync(p => p.Id == _s.ProviderB)).IsActive = false;
            await db.SaveChangesAsync();
        }
        Assert.Equal("provider_inactive", (await RefusedAsync(() => ReassignAsync(a, _s.ProviderB, _s.Op1))).Code);
        Assert.Equal(0, await EventsAsync(a.Id));
    }

    // ---------- concurrency, atomicity, legacy ----------

    [Fact]
    public async Task A_stale_version_is_the_shared_conflict_and_two_reassignments_at_once_produce_one()
    {
        var a = await BookAsync(_ann, 9);
        await _s.ManageAsync(m => m.UpdateNotesAsync(a.Id, "front desk note", a.RowVersion, _s.Actor, default));   // someone else changed it first
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => ReassignAsync(a, _s.ProviderB, _s.Op1));

        var fresh = await _s.ReloadAsync(a.Id);
        var results = await Task.WhenAll(new[] { _s.Op1, _s.Op2 }.Select(async room =>
        {
            try { return (await ReassignAsync(fresh, _s.ProviderB, room)).OperatoryName; }
            catch (ConcurrencyConflictException) { return "conflict"; }
        }));
        Assert.Equal(1, results.Count(r => r != "conflict"));
        Assert.Equal(1, await EventsAsync(a.Id));
        Assert.Equal(2, (await NotificationsAsync()).Count);
    }

    [Fact]
    public async Task If_the_audit_entry_cannot_be_written_neither_the_reassignment_nor_the_notifications_are_stored()
    {
        var a = await BookAsync(_ann, 9);
        await using (var failing = _s.FailingAuditContext())
            await Assert.ThrowsAsync<DbUpdateException>(() => _s.Assignment(failing).AssignAsync(a.Id, new AssignVisitRequest(_s.ProviderB, _s.Op1, a.RowVersion, Why), _s.OfficeManager, default));

        var unchanged = await _s.ReloadAsync(a.Id);
        Assert.Equal((_s.ProviderA, a.RowVersion), (unchanged.ProviderId, unchanged.RowVersion));
        Assert.Equal(0, await EventsAsync(a.Id));
        Assert.Empty(await NotificationsAsync());
        Assert.Equal(_s.ProviderB, (await ReassignAsync(a, _s.ProviderB, _s.Op1)).ProviderId);   // the same request works once logging is back
    }

    [Fact]
    public async Task Two_seated_patients_moved_into_the_same_free_room_at_once_leave_exactly_one_there()
    {
        var a = await _s.WalkAsync(await BookAsync(_ann, 9), VisitStates.Seated);
        var b = await _s.WalkAsync(await BookAsync(_bo, 11, _s.ProviderB, _s.Op2), VisitStates.Seated);
        Guid room3;
        await using (var setup = _fixture.CreateContext())
            room3 = (await new PracticeConfigurationService(setup, Clock).CreateOperatoryAsync("Op 3", _s.Actor, default)).Id;

        // each pauses between its check and its write, so only the operatory lock can stop both being moved into Op 3
        var results = await Task.WhenAll(new[] { a, b }.Select(async x =>
        {
            await using var db = _s.SlowSaveContext(TimeSpan.FromMilliseconds(200));
            try { return (await _s.Assignment(db).AssignAsync(x.Id, new AssignVisitRequest(x.ProviderId, room3, x.RowVersion), new VisitActor(_s.Actor, Role.Assistant), default)).OperatoryName; }
            catch (SchedulingException ex) { return ex.Code; }
        }));

        Assert.Equal(1, results.Count(r => r == "Op 3"));
        Assert.Equal(1, results.Count(r => r == "operatory_occupied"));
        Assert.Equal(1, await _s.CountAsync(db => db.Appointments.Where(x => x.OperatoryId == room3)));
    }

    [Fact]
    public async Task A_seated_patient_cannot_be_moved_into_a_room_another_patient_is_seated_in()
    {
        // Dr. Patel's 08:00 patient is still seated in Op 2 (running over) when Ann, seated in Op 1 at 09:00, is to be moved there
        var holder = await _s.WalkAsync(await BookAsync(_bo, 8, _s.ProviderB, _s.Op2), VisitStates.Seated);
        var seated = await _s.WalkAsync(await BookAsync(_ann, 9), VisitStates.Seated);

        var ex = await RefusedAsync(() => ReassignAsync(seated, _s.ProviderA, _s.Op2, reason: null, by: new VisitActor(_s.Actor, Role.Assistant)));

        Assert.Equal(("operatory_occupied", holder.Id), (ex.Code, ex.ConflictingAppointmentId!.Value));
        Assert.Equal(_s.Op1, (await _s.ReloadAsync(seated.Id)).OperatoryId);
    }

    [Fact]
    public async Task An_old_seen_by_override_is_kept_until_the_booking_is_reassigned_and_then_cleared_with_its_values_in_the_history()
    {
        var a = await BookAsync(_ann, 9);
        await using (var db = _fixture.CreateContext())
            await db.Database.ExecuteSqlRawAsync("UPDATE [Appointments] SET [VisitProviderProfileId] = {0}, [VisitOperatoryId] = {1} WHERE [Id] = {2}", _s.ProviderB, _s.Op2, a.Id);
        var legacy = await _s.ReloadAsync(a.Id);
        Assert.Equal(("Dr. Patel", "Op 2"), (legacy.VisitProviderName, legacy.VisitOperatoryName));   // read-only: shown as it was

        var moved = await ReassignAsync(legacy, _s.ProviderA, _s.Op1, reason: null, by: new VisitActor(_s.Actor, Role.Assistant));   // back to the booked room: a room change from Op 2

        await using var check = _fixture.CreateContext();
        var row = await check.Appointments.AsNoTracking().SingleAsync(x => x.Id == a.Id);
        Assert.Equal((null, null), (row.VisitProviderProfileId, row.VisitOperatoryId));
        Assert.Equal(("Dr. Rivera", "Op 1"), (moved.VisitProviderName, moved.VisitOperatoryName));
        Assert.Equal("Op 2", (await _s.HistoryAsync(a.Id)).Single(e => e.EventType == AppointmentEventTypes.RoomChanged).PreviousOperatoryName);
    }
}
