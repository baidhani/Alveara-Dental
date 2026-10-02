using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Measurement;
using Alveara.Api.Architecture.Scheduling;
using Xunit;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-004-C01 against real SQL Server: moving, cancelling and no-showing a booked appointment, patient-overlap, working notes, history, and every
/// race the schedule must survive. STORY-004's own tests (AppointmentSchedulerTests, AppointmentsApiTests) are unchanged and run alongside these.
/// </summary>
public class AppointmentLifecycleTests : IAsyncLifetime
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

    private async Task<AppointmentView> BookAsync(Guid patient, DateTime start, Guid? provider = null, Guid? operatory = null, int? duration = null, string? notes = null) =>
        (await _s.ScheduleAsync(_s.Request(patient, start, provider, operatory, duration: duration) with { Notes = notes })).Appointment;

    private Task<AppointmentView> MoveAsync(AppointmentView a, DateTime start, Guid? provider = null, Guid? operatory = null, int? duration = null, string? version = null) =>
        _s.ManageAsync(m => m.RescheduleAsync(a.Id, new RescheduleRequest(provider ?? a.ProviderId, operatory ?? a.OperatoryId, start, duration, version ?? a.RowVersion), _s.Actor, default));

    private async Task<SchedulingException> RefusedMoveAsync(AppointmentView a, DateTime start, Guid? provider = null, Guid? operatory = null, int? duration = null) =>
        await Assert.ThrowsAsync<SchedulingException>(() => MoveAsync(a, start, provider, operatory, duration));

    // ---------- reschedule ----------

    [Fact]
    public async Task A_rescheduled_appointment_keeps_its_identity_moves_to_the_new_provider_operatory_and_time_and_remembers_where_it_was()
    {
        var a = await BookAsync(_ann, At(9));

        var moved = await MoveAsync(a, At(14), _s.ProviderB, _s.Op2);

        Assert.Equal(a.Id, moved.Id);
        Assert.Equal(("2030-01-14T14:00", "2030-01-14T15:00", "Dr. Patel", "Op 2"), (moved.StartLocal, moved.EndLocal, moved.ProviderName, moved.OperatoryName));
        Assert.Equal(AppointmentStatuses.Scheduled, moved.Status);
        Assert.NotEqual(a.RowVersion, moved.RowVersion);

        var history = await _s.HistoryAsync(a.Id);
        Assert.Equal([AppointmentEventTypes.Scheduled, AppointmentEventTypes.Rescheduled], history.Select(h => h.EventType));
        var event_ = history[1];
        Assert.Equal(("2030-01-14T09:00", "Dr. Rivera", "Op 1"), (event_.PreviousStartLocal, event_.PreviousProviderName, event_.PreviousOperatoryName));
        Assert.Equal(_s.Actor, event_.ActorUserId);
    }

    [Fact]
    public async Task Rescheduling_frees_the_old_time_for_someone_else_and_keeps_the_duration_unless_a_new_one_is_given()
    {
        var a = await BookAsync(_ann, At(9), duration: 90);
        var moved = await MoveAsync(a, At(13));
        Assert.Equal(("2030-01-14T14:30", 90), (moved.EndLocal, moved.DurationMinutes)); // the 90 minutes it was booked for

        var longer = await MoveAsync(moved, At(13), duration: 30);
        Assert.Equal(("2030-01-14T13:30", 30), (longer.EndLocal, longer.DurationMinutes));

        Assert.True((await _s.ScheduleAsync(_s.Request(_bo, At(9)))).Created); // the old 09:00 slot is free again
    }

    [Fact]
    public async Task An_appointment_can_move_to_overlap_its_own_old_time_it_does_not_conflict_with_itself()
    {
        var a = await BookAsync(_ann, At(9));          // 09:00-10:00

        var moved = await MoveAsync(a, At(9, 30));      // 09:30-10:30 overlaps only itself

        Assert.Equal("2030-01-14T09:30", moved.StartLocal);
    }

    [Fact]
    public async Task Moving_to_the_same_place_and_time_changes_nothing_and_writes_no_history_or_audit()
    {
        var a = await BookAsync(_ann, At(9));
        var same = await MoveAsync(a, At(9));

        Assert.Equal(a.RowVersion, same.RowVersion);
        Assert.Equal(1, (await _s.HistoryAsync(a.Id)).Count);
        Assert.Equal(0, await _s.CountAsync(db => db.AuditLogEntries.Where(e => e.EventType == SchedulingAuditEvents.Rescheduled)));
    }

    [Fact]
    public async Task Rescheduling_into_a_providers_busy_time_is_refused_names_the_appointment_in_the_way_and_the_appointment_stays_where_it_was()
    {
        var other = await BookAsync(_bo, At(11), _s.ProviderB, _s.Op2);
        var a = await BookAsync(_ann, At(9));

        var ex = await RefusedMoveAsync(a, At(11, 30), _s.ProviderB, _s.Op1);

        Assert.Equal(("provider_double_booked", other.Id), (ex.Code, ex.ConflictingAppointmentId));
        var still = await _s.ReloadAsync(a.Id);
        Assert.Equal(("2030-01-14T09:00", "Dr. Rivera", a.RowVersion), (still.StartLocal, still.ProviderName, still.RowVersion));
    }

    [Fact]
    public async Task Rescheduling_into_a_busy_operatory_or_onto_the_patients_other_appointment_is_refused()
    {
        var busyOp = await BookAsync(_bo, At(11), _s.ProviderB, _s.Op2);
        var a = await BookAsync(_ann, At(9));
        var operatory = await RefusedMoveAsync(a, At(11), _s.ProviderA, _s.Op2);
        Assert.Equal(("operatory_conflict", busyOp.Id), (operatory.Code, operatory.ConflictingAppointmentId));

        var later = await BookAsync(_ann, At(15), _s.ProviderB, _s.Op2); // Ann's second appointment, elsewhere
        var patient = await RefusedMoveAsync(a, At(15, 30), _s.ProviderA, _s.Op1);
        Assert.Equal(("patient_double_booked", later.Id), (patient.Code, patient.ConflictingAppointmentId));
    }

    [Fact]
    public async Task Rescheduling_respects_working_hours_blocked_time_and_valid_durations_and_a_start_in_the_future()
    {
        await _s.BlockProviderAsync(_s.ProviderA, At(12), At(13));
        var a = await BookAsync(_ann, At(9));

        Assert.Equal(("provider_unavailable", "outside_working_hours"), ((await RefusedMoveAsync(a, At(7, 30))).Code, (await RefusedMoveAsync(a, At(7, 30))).Reason));
        Assert.Equal("blocked_time", (await RefusedMoveAsync(a, At(12, 30), duration: 30)).Reason);
        Assert.Equal("invalid_duration", (await RefusedMoveAsync(a, At(14), duration: 7)).Code);
        Assert.Equal("start_in_past", (await RefusedMoveAsync(a, new DateTime(2020, 1, 13, 9, 0, 0))).Code);
        Assert.Equal("invalid_local_time", (await RefusedMoveAsync(a, new DateTime(2030, 3, 10, 2, 30, 0))).Code);
        Assert.Equal("provider_not_found", (await RefusedMoveAsync(a, At(14), Guid.NewGuid())).Code);
        Assert.Equal("operatory_not_found", (await RefusedMoveAsync(a, At(14), operatory: Guid.NewGuid())).Code);
        Assert.Equal("2030-01-14T09:00", (await _s.ReloadAsync(a.Id)).StartLocal);
    }

    [Fact]
    public async Task A_refused_reschedule_is_audited_with_who_and_why_and_a_malformed_one_is_not()
    {
        var other = await BookAsync(_bo, At(11), _s.ProviderB, _s.Op2);
        var a = await BookAsync(_ann, At(9));
        await RefusedMoveAsync(a, At(11), _s.ProviderB, _s.Op1);
        await RefusedMoveAsync(a, At(14), duration: 7);

        await using var db = _fixture.CreateContext();
        var rejected = await db.AuditLogEntries.Where(e => e.EventType == SchedulingAuditEvents.Rejected).ToListAsync();
        var entry = Assert.Single(rejected);
        Assert.Equal(_s.Actor, entry.PerformedByUserAccountId);
        Assert.Contains("provider_double_booked", entry.Details);
        Assert.Equal(other.Id, entry.TargetUserAccountId);
    }

    [Fact]
    public async Task A_stale_or_missing_row_version_is_refused_and_nothing_changes()
    {
        var a = await BookAsync(_ann, At(9));
        var moved = await MoveAsync(a, At(10));

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => MoveAsync(a, At(11), version: a.RowVersion)); // a is now stale
        Assert.Equal("row_version_required", (await Assert.ThrowsAsync<SchedulingException>(() => _s.ManageAsync(m => m.RescheduleAsync(a.Id, new RescheduleRequest(a.ProviderId, a.OperatoryId, At(11), null, null), _s.Actor, default)))).Code);
        Assert.Equal("row_version_invalid", (await Assert.ThrowsAsync<SchedulingException>(() => MoveAsync(a, At(11), version: "not base64!!"))).Code);
        Assert.Equal("2030-01-14T10:00", (await _s.ReloadAsync(a.Id)).StartLocal);
        Assert.Equal(moved.RowVersion, (await _s.ReloadAsync(a.Id)).RowVersion);
    }

    [Fact]
    public async Task Only_a_scheduled_appointment_can_be_rescheduled()
    {
        var a = await BookAsync(_ann, At(9));
        var cancelled = await _s.ManageAsync(m => m.CancelAsync(a.Id, "Patient called", a.RowVersion, _s.Actor, default));

        var ex = await RefusedMoveAsync(cancelled, At(14));

        Assert.Equal("appointment_not_scheduled", ex.Code);
        Assert.Equal("appointment_not_found", (await Assert.ThrowsAsync<SchedulingException>(() => _s.ManageAsync(m => m.RescheduleAsync(Guid.NewGuid(), new RescheduleRequest(a.ProviderId, a.OperatoryId, At(14), null, "AAAA"), _s.Actor, default)))).Code);
    }

    [Fact]
    public async Task A_reschedule_is_audited_without_patient_details_and_if_the_audit_write_fails_the_appointment_does_not_move()
    {
        var a = await BookAsync(_ann, At(9));
        await using (var failing = _s.FailingAuditContext())
            await Assert.ThrowsAsync<DbUpdateException>(() => _s.Manager(failing).RescheduleAsync(a.Id, new RescheduleRequest(a.ProviderId, a.OperatoryId, At(14), null, a.RowVersion), _s.Actor, default));

        var unmoved = await _s.ReloadAsync(a.Id);
        Assert.Equal(("2030-01-14T09:00", a.RowVersion), (unmoved.StartLocal, unmoved.RowVersion));
        Assert.Equal(1, (await _s.HistoryAsync(a.Id)).Count);

        var moved = await MoveAsync(a, At(14));
        Assert.Equal("2030-01-14T14:00", moved.StartLocal);
        await using var db = _fixture.CreateContext();
        var entry = await db.AuditLogEntries.SingleAsync(e => e.EventType == SchedulingAuditEvents.Rescheduled);
        Assert.Equal((_s.Actor, a.Id), (entry.PerformedByUserAccountId, entry.TargetUserAccountId));
        Assert.DoesNotContain("Ann", entry.Details);
        Assert.InRange(entry.TimestampUtc, DateTimeOffset.UtcNow.AddMinutes(-2), DateTimeOffset.UtcNow.AddSeconds(5));
    }

    // ---------- cancel ----------

    [Fact]
    public async Task Cancelling_needs_a_reason_keeps_the_appointment_on_record_frees_the_time_and_is_audited_without_the_reason_text()
    {
        var a = await BookAsync(_ann, At(9));

        var noReason = await Assert.ThrowsAsync<SchedulingException>(() => _s.ManageAsync(m => m.CancelAsync(a.Id, "   ", a.RowVersion, _s.Actor, default)));
        Assert.Equal("reason_required", noReason.Code);
        Assert.Equal("reason_too_long", (await Assert.ThrowsAsync<SchedulingException>(() => _s.ManageAsync(m => m.CancelAsync(a.Id, new string('x', 401), a.RowVersion, _s.Actor, default)))).Code);

        var cancelled = await _s.ManageAsync(m => m.CancelAsync(a.Id, "Patient called to cancel", a.RowVersion, _s.Actor, default));

        Assert.Equal((AppointmentStatuses.Cancelled, "Patient called to cancel"), (cancelled.Status, cancelled.CancelReason));
        Assert.NotNull(cancelled.StatusChangedAtUtc);
        Assert.True((await _s.ScheduleAsync(_s.Request(_bo, At(9)))).Created); // the slot is free
        var history = await _s.HistoryAsync(a.Id);
        Assert.Equal(AppointmentEventTypes.Cancelled, history[^1].EventType);
        Assert.Equal("Patient called to cancel", history[^1].Detail);
        await using var db = _fixture.CreateContext();
        var audit = await db.AuditLogEntries.SingleAsync(e => e.EventType == SchedulingAuditEvents.Cancelled);
        Assert.Equal(_s.Actor, audit.PerformedByUserAccountId);
        Assert.DoesNotContain("called", audit.Details);
    }

    [Fact]
    public async Task A_cancelled_appointment_stays_visible_in_the_calendar_but_not_in_the_booked_list()
    {
        var a = await BookAsync(_ann, At(9));
        var b = await BookAsync(_bo, At(11), _s.ProviderB, _s.Op2);
        await _s.ManageAsync(m => m.CancelAsync(a.Id, "Moved to another practice", a.RowVersion, _s.Actor, default));

        await using var db = _fixture.CreateContext();
        var scheduler = _s.Scheduler(db);
        var from = Clock.FromPracticeLocal(At(0));
        var to = Clock.FromPracticeLocal(At(0, 0, 1));
        Assert.Equal([b.Id], (await scheduler.ListAsync(from, to, null, null, default)).Select(x => x.Id));
        var calendar = await scheduler.CalendarAsync(from, to, null, null, default);
        Assert.Equal([a.Id, b.Id], calendar.Select(x => x.Id));
        Assert.Equal([AppointmentStatuses.Cancelled, AppointmentStatuses.Scheduled], calendar.Select(x => x.Status));
    }

    [Fact]
    public async Task Cancelling_twice_changes_nothing_and_a_stale_cancel_is_a_conflict()
    {
        var a = await BookAsync(_ann, At(9));
        var first = await _s.ManageAsync(m => m.CancelAsync(a.Id, "First reason", a.RowVersion, _s.Actor, default));
        var again = await _s.ManageAsync(m => m.CancelAsync(a.Id, "Second reason", a.RowVersion, _s.Actor, default)); // stale version, but already done

        Assert.Equal(("First reason", first.RowVersion), (again.CancelReason, again.RowVersion));
        Assert.Equal(1, await _s.CountAsync(db => db.AuditLogEntries.Where(e => e.EventType == SchedulingAuditEvents.Cancelled)));

        var b = await BookAsync(_bo, At(11), _s.ProviderB, _s.Op2);
        await MoveAsync(b, At(12), _s.ProviderB, _s.Op2);
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => _s.ManageAsync(m => m.CancelAsync(b.Id, "Too late", b.RowVersion, _s.Actor, default)));
        Assert.Equal(AppointmentStatuses.Scheduled, (await _s.ReloadAsync(b.Id)).Status);
    }

    // ---------- no-show ----------

    [Fact]
    public async Task A_no_show_can_only_be_recorded_once_the_start_time_has_passed_and_stays_on_record()
    {
        var a = await BookAsync(_ann, At(9));

        var early = await Assert.ThrowsAsync<SchedulingException>(() => _s.ManageAsync(m => m.MarkNoShowAsync(a.Id, a.RowVersion, _s.Actor, default)));
        Assert.Equal("no_show_too_early", early.Code);

        var after = new TestClock(Clock.FromPracticeLocal(At(9, 30)));
        var noShow = await _s.ManageAsync(m => m.MarkNoShowAsync(a.Id, a.RowVersion, _s.Actor, default), after);

        Assert.Equal(AppointmentStatuses.NoShow, noShow.Status);
        Assert.NotNull(noShow.StatusChangedAtUtc);
        Assert.Equal(AppointmentEventTypes.NoShow, (await _s.HistoryAsync(a.Id))[^1].EventType);
        await using var db = _fixture.CreateContext();
        var audit = await db.AuditLogEntries.SingleAsync(e => e.EventType == SchedulingAuditEvents.NoShow);
        Assert.Equal((_s.Actor, a.Id), (audit.PerformedByUserAccountId, audit.TargetUserAccountId));
    }

    [Fact]
    public async Task A_no_show_no_longer_holds_time_marking_it_twice_changes_nothing_and_a_cancelled_one_cannot_be_a_no_show()
    {
        var a = await BookAsync(_ann, At(9));
        var after = new TestClock(Clock.FromPracticeLocal(At(10)));
        var first = await _s.ManageAsync(m => m.MarkNoShowAsync(a.Id, a.RowVersion, _s.Actor, default), after);
        var again = await _s.ManageAsync(m => m.MarkNoShowAsync(a.Id, a.RowVersion, _s.Actor, default), after);
        Assert.Equal(first.RowVersion, again.RowVersion);
        Assert.Equal(1, await _s.CountAsync(db => db.AuditLogEntries.Where(e => e.EventType == SchedulingAuditEvents.NoShow)));
        Assert.True((await _s.ScheduleAsync(_s.Request(_bo, At(9)))).Created);

        var b = await BookAsync(_bo, At(14));
        var cancelled = await _s.ManageAsync(m => m.CancelAsync(b.Id, "Illness", b.RowVersion, _s.Actor, default));
        var ex = await Assert.ThrowsAsync<SchedulingException>(() => _s.ManageAsync(m => m.MarkNoShowAsync(b.Id, cancelled.RowVersion, _s.Actor, default), new TestClock(Clock.FromPracticeLocal(At(16)))));
        Assert.Equal("appointment_not_scheduled", ex.Code);
    }

    // ---------- notes ----------

    [Fact]
    public async Task A_note_can_be_given_at_booking_changed_cleared_and_left_alone_and_the_audit_never_contains_it()
    {
        var a = await BookAsync(_ann, At(9), notes: "  Bring the old X-rays  ");
        Assert.Equal("Bring the old X-rays", a.Notes);

        var changed = await _s.ManageAsync(m => m.UpdateNotesAsync(a.Id, "Prefers a morning slot", a.RowVersion, _s.Actor, default));
        Assert.Equal("Prefers a morning slot", changed.Notes);
        var same = await _s.ManageAsync(m => m.UpdateNotesAsync(a.Id, "Prefers a morning slot", changed.RowVersion, _s.Actor, default));
        Assert.Equal(changed.RowVersion, same.RowVersion);
        var cleared = await _s.ManageAsync(m => m.UpdateNotesAsync(a.Id, "  ", same.RowVersion, _s.Actor, default));
        Assert.Null(cleared.Notes);

        Assert.Equal(["Note updated.", "Note removed."], (await _s.HistoryAsync(a.Id)).Where(h => h.EventType == AppointmentEventTypes.NotesChanged).Select(h => h.Detail));
        await using var db = _fixture.CreateContext();
        var audits = await db.AuditLogEntries.Where(e => e.EventType == SchedulingAuditEvents.NotesChanged).ToListAsync();
        Assert.Equal(2, audits.Count);
        Assert.All(audits, e => Assert.DoesNotContain("morning", e.Details));
    }

    [Fact]
    public async Task A_note_too_long_is_refused_and_a_note_can_still_be_added_to_a_cancelled_appointment()
    {
        var a = await BookAsync(_ann, At(9));
        Assert.Equal("invalid_notes", (await Assert.ThrowsAsync<SchedulingException>(() => _s.ManageAsync(m => m.UpdateNotesAsync(a.Id, new string('n', 1001), a.RowVersion, _s.Actor, default)))).Code);

        var cancelled = await _s.ManageAsync(m => m.CancelAsync(a.Id, "Illness", a.RowVersion, _s.Actor, default));
        var noted = await _s.ManageAsync(m => m.UpdateNotesAsync(a.Id, "Wants to rebook next month", cancelled.RowVersion, _s.Actor, default));
        Assert.Equal(("Wants to rebook next month", AppointmentStatuses.Cancelled), (noted.Notes, noted.Status));
        Assert.Equal("invalid_notes", (await Assert.ThrowsAsync<SchedulingException>(() => _s.Scheduler(_fixture.CreateContext()).ScheduleAsync(_s.Request(_bo, At(14)) with { Notes = new string('n', 1001) }, "note-key-0001", _s.Actor, default))).Code);
    }

    // ---------- patient overlap at booking ----------

    [Fact]
    public async Task A_patient_cannot_be_booked_in_two_places_at_once_but_back_to_back_is_fine_and_a_cancelled_one_does_not_block()
    {
        var first = await BookAsync(_ann, At(9));                                    // Dr. Rivera, Op 1, 09:00-10:00

        var ex = await Assert.ThrowsAsync<SchedulingException>(() => _s.ScheduleAsync(_s.Request(_ann, At(9, 30), _s.ProviderB, _s.Op2)));
        Assert.Equal(("patient_double_booked", first.Id), (ex.Code, ex.ConflictingAppointmentId));
        Assert.Equal(1, await _s.CountAsync());

        Assert.True((await _s.ScheduleAsync(_s.Request(_ann, At(10), _s.ProviderB, _s.Op2))).Created); // starts exactly when the first ends

        await _s.ManageAsync(m => m.CancelAsync(first.Id, "Illness", first.RowVersion, _s.Actor, default));
        Assert.True((await _s.ScheduleAsync(_s.Request(_ann, At(9), _s.ProviderB, _s.Op1))).Created);   // the cancelled one no longer blocks her
    }

    [Fact]
    public async Task A_refused_patient_overlap_is_audited_like_any_other_refusal()
    {
        await BookAsync(_ann, At(9));
        await Assert.ThrowsAsync<SchedulingException>(() => _s.ScheduleAsync(_s.Request(_ann, At(9, 30), _s.ProviderB, _s.Op2)));

        await using var db = _fixture.CreateContext();
        var entry = await db.AuditLogEntries.SingleAsync(e => e.EventType == SchedulingAuditEvents.Rejected);
        Assert.Contains("patient_double_booked", entry.Details);
        Assert.Equal(_s.Actor, entry.PerformedByUserAccountId);
    }

    // ---------- concurrency ----------

    [Fact]
    public async Task Two_people_rescheduling_the_same_appointment_at_once_produce_exactly_one_change()
    {
        var a = await BookAsync(_ann, At(9));

        var outcomes = await Task.WhenAll(new[] { At(11), At(14) }.Select(async start =>
        {
            try { await MoveAsync(a, start); return "moved"; }
            catch (ConcurrencyConflictException) { return "conflict"; }
        }));

        Assert.Equal(1, outcomes.Count(o => o == "moved"));
        Assert.Equal(1, outcomes.Count(o => o == "conflict"));
        Assert.Equal(1, (await _s.HistoryAsync(a.Id)).Count(h => h.EventType == AppointmentEventTypes.Rescheduled));
    }

    [Fact]
    public async Task Six_people_moving_different_appointments_into_the_same_slot_at_once_produce_one_winner_and_no_overlap()
    {
        var appointments = new List<AppointmentView>();
        for (var i = 0; i < 6; i++)
            appointments.Add(await BookAsync(await _s.PatientAsync($"Pat{i}", $"Mover{i}"), At(8 + i), i % 2 == 0 ? _s.ProviderA : _s.ProviderB, i % 2 == 0 ? _s.Op1 : _s.Op2));

        // all six want Dr. Rivera at 16:00 (each from its own slot, in alternating operatories)
        var outcomes = await Task.WhenAll(appointments.Select(async (a, i) =>
        {
            try { await MoveAsync(a, At(16), _s.ProviderA, i % 2 == 0 ? _s.Op1 : _s.Op2); return "moved"; }
            catch (SchedulingException ex) { return ex.Code; }
            catch (ConcurrencyConflictException) { return "conflict"; }
        }));

        Assert.Equal(1, outcomes.Count(o => o == "moved"));
        Assert.All(outcomes.Where(o => o != "moved"), o => Assert.Contains(o, new[] { "provider_double_booked", "operatory_conflict" }));
        await using var db = _fixture.CreateContext();
        Assert.Equal(1, await db.Appointments.CountAsync(a => a.ProviderProfileId == _s.ProviderA && a.StartUtc == Clock.FromPracticeLocal(At(16)).ToUniversalTime()));
        var scheduled = await db.Appointments.AsNoTracking().Where(a => a.Status == AppointmentStatuses.Scheduled).ToListAsync();
        foreach (var group in new[] { scheduled.GroupBy(a => a.ProviderProfileId), scheduled.GroupBy(a => a.OperatoryId), scheduled.GroupBy(a => a.PatientId) })
            foreach (var g in group)
            {
                var ordered = g.OrderBy(a => a.StartUtc).ToList();
                for (var i = 1; i < ordered.Count; i++) Assert.True(ordered[i - 1].EndUtc <= ordered[i].StartUtc, "two scheduled appointments overlap");
            }
    }

    [Fact]
    public async Task A_booking_and_a_reschedule_racing_for_the_same_slot_produce_one_winner()
    {
        var a = await BookAsync(_ann, At(9));
        var results = await Task.WhenAll(
            Task.Run(async () => { try { await MoveAsync(a, At(15), _s.ProviderB, _s.Op2); return "moved"; } catch (SchedulingException ex) { return ex.Code; } }),
            Task.Run(async () => { try { await _s.ScheduleAsync(_s.Request(_bo, At(15), _s.ProviderB, _s.Op2)); return "booked"; } catch (SchedulingException ex) { return ex.Code; } }));

        Assert.Equal(1, results.Count(r => r is "moved" or "booked"));
        Assert.Equal(1, await _s.CountAsync(db => db.Appointments.Where(x => x.ProviderProfileId == _s.ProviderB && x.Status == AppointmentStatuses.Scheduled)));
    }

    [Fact]
    public async Task Six_simultaneous_bookings_for_the_same_patient_and_time_with_different_providers_produce_one_appointment()
    {
        var outcomes = await Task.WhenAll(Enumerable.Range(0, 6).Select(async i =>
        {
            try { await _s.ScheduleAsync(_s.Request(_ann, At(9), i % 2 == 0 ? _s.ProviderA : _s.ProviderB, i % 2 == 0 ? _s.Op1 : _s.Op2)); return "booked"; }
            catch (SchedulingException ex) { return ex.Code; }
        }));

        Assert.Equal(1, outcomes.Count(o => o == "booked"));
        Assert.Equal(1, await _s.CountAsync());
    }

    // ---------- measurement ----------

    [Fact]
    public async Task Each_lifecycle_action_records_a_privacy_safe_measurement_event()
    {
        var a = await BookAsync(_ann, At(9));
        await using (var db = _fixture.CreateContext())
        {
            var manager = _s.Manager(db, null, new MeasurementEventSink(db));
            var moved = await manager.RescheduleAsync(a.Id, new RescheduleRequest(a.ProviderId, a.OperatoryId, At(10), null, a.RowVersion), _s.Actor, default);
            await manager.CancelAsync(a.Id, "Illness", moved.RowVersion, _s.Actor, default);
        }

        await using var read = _fixture.CreateContext();
        var events = await read.MeasurementEvents.OrderBy(e => e.OccurredAtUtc).ToListAsync();
        Assert.Equal(["appointment.rescheduled", "appointment.cancelled"], events.Select(e => e.EventName));
        Assert.All(events, e => Assert.Equal("""{"outcome":"success"}""", e.PropertiesJson));
    }
}
