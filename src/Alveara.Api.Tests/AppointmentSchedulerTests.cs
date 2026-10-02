using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Measurement;
using Alveara.Api.Architecture.Scheduling;
using Xunit;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>
/// STORY-004: scheduling without conflicts, against real SQL Server. Covers the acceptance criteria (a confirmed appointment when the provider
/// is free; a double-booked provider is refused; every scheduling action audited with user and time) and the failure paths (double booking,
/// unavailable provider, operatory conflict, audit failure, incorrect duration).
/// </summary>
public class AppointmentSchedulerTests : IAsyncLifetime
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

    private async Task<SchedulingException> RefusedAsync(ScheduleAppointmentRequest request, string? key = null) =>
        await Assert.ThrowsAsync<SchedulingException>(() => _s.ScheduleAsync(request, key));

    // ---------- acceptance 1: an available provider's appointment is confirmed ----------

    [Fact]
    public async Task An_appointment_with_an_available_provider_is_confirmed_and_stored_in_practice_time()
    {
        var result = await _s.ScheduleAsync(_s.Request(_ann, At(9)));

        Assert.True(result.Created);
        var a = result.Appointment;
        Assert.Equal(AppointmentStatuses.Scheduled, a.Status);
        Assert.Equal("2030-01-14T09:00", a.StartLocal);
        Assert.Equal("2030-01-14T10:00", a.EndLocal);              // the type's 60-minute default
        Assert.Equal(DateTimeOffset.Parse("2030-01-14T15:00:00Z"), a.StartUtc); // 09:00 CST is 15:00 UTC
        Assert.Equal(60, a.DurationMinutes);
        Assert.Equal("Dr. Rivera", a.ProviderName);
        Assert.Equal("Op 1", a.OperatoryName);
        Assert.Equal("Exam", a.AppointmentTypeName);
        Assert.Equal("Ann Lee", a.PatientName);
        Assert.Equal(1, await _s.CountAsync());
    }

    [Fact]
    public async Task An_explicit_duration_overrides_the_types_default_and_the_other_type_uses_its_own()
    {
        var longer = await _s.ScheduleAsync(_s.Request(_ann, At(9), duration: 90));
        var shorter = await _s.ScheduleAsync(_s.Request(_ann, At(13), type: _s.Short30));

        Assert.Equal((90, "2030-01-14T10:30"), (longer.Appointment.DurationMinutes, longer.Appointment.EndLocal));
        Assert.Equal((30, "2030-01-14T13:30"), (shorter.Appointment.DurationMinutes, shorter.Appointment.EndLocal));
    }

    [Fact]
    public async Task Back_to_back_appointments_do_not_conflict_for_the_same_provider_or_operatory()
    {
        var bo = await _s.PatientAsync("Bo", "Kim");
        await _s.ScheduleAsync(_s.Request(_ann, At(9)));                   // 09:00-10:00

        var next = await _s.ScheduleAsync(_s.Request(bo, At(10)));          // 10:00-11:00, same provider and operatory
        var before = await _s.ScheduleAsync(_s.Request(bo, At(8)));         // 08:00-09:00

        Assert.True(next.Created && before.Created);
        Assert.Equal(3, await _s.CountAsync());
    }

    [Fact]
    public async Task Two_providers_in_two_operatories_can_see_patients_at_the_same_time()
    {
        var bo = await _s.PatientAsync("Bo", "Kim");
        await _s.ScheduleAsync(_s.Request(_ann, At(9)));

        var other = await _s.ScheduleAsync(_s.Request(bo, At(9), provider: _s.ProviderB, operatory: _s.Op2));

        Assert.True(other.Created);
    }

    // ---------- acceptance 2 / failure path: double booking ----------

    [Theory]
    [InlineData(9, 0)]    // exactly the same time
    [InlineData(9, 30)]   // starts inside the existing appointment
    [InlineData(8, 30)]   // ends inside it
    [InlineData(9, 15)]   // sits entirely inside (with a 30-minute duration below)
    public async Task A_provider_who_is_double_booked_rejects_the_new_appointment_and_nothing_is_stored(int hour, int minute)
    {
        var bo = await _s.PatientAsync("Bo", "Kim");
        var first = await _s.ScheduleAsync(_s.Request(_ann, At(9)));         // Dr. Rivera 09:00-10:00 in Op 1

        // a different operatory, so only the provider can be the conflict
        var ex = await RefusedAsync(_s.Request(bo, At(hour, minute), operatory: _s.Op2, duration: minute == 15 ? 30 : 60));

        Assert.Equal("provider_double_booked", ex.Code);
        Assert.Equal(409, ex.StatusCode);
        Assert.Equal(first.Appointment.Id, ex.ConflictingAppointmentId);
        Assert.Equal(1, await _s.CountAsync());
    }

    [Fact]
    public async Task A_new_appointment_that_wraps_around_an_existing_one_is_also_a_conflict()
    {
        var bo = await _s.PatientAsync("Bo", "Kim");
        await _s.ScheduleAsync(_s.Request(_ann, At(10), type: _s.Short30));   // 10:00-10:30

        var ex = await RefusedAsync(_s.Request(bo, At(9), operatory: _s.Op2, duration: 120)); // 09:00-11:00

        Assert.Equal("provider_double_booked", ex.Code);
    }

    // ---------- failure path: operatory conflict ----------

    [Fact]
    public async Task An_operatory_already_in_use_rejects_a_second_appointment_even_with_a_different_provider()
    {
        var bo = await _s.PatientAsync("Bo", "Kim");
        var first = await _s.ScheduleAsync(_s.Request(_ann, At(9)));

        var ex = await RefusedAsync(_s.Request(bo, At(9, 30), provider: _s.ProviderB, operatory: _s.Op1));

        Assert.Equal("operatory_conflict", ex.Code);
        Assert.Equal(first.Appointment.Id, ex.ConflictingAppointmentId);
        Assert.Equal(1, await _s.CountAsync());
    }

    [Fact]
    public async Task Only_a_scheduled_appointment_blocks_time()
    {
        var bo = await _s.PatientAsync("Bo", "Kim");
        var first = await _s.ScheduleAsync(_s.Request(_ann, At(9)));
        await using (var db = _fixture.CreateContext())
            await db.Database.ExecuteSqlRawAsync("UPDATE Appointments SET Status = 'Cancelled' WHERE Id = {0}", first.Appointment.Id); // ALV-004-C01 will add cancel; the rule is already "only Scheduled blocks"

        var again = await _s.ScheduleAsync(_s.Request(bo, At(9)));

        Assert.True(again.Created);
    }

    // ---------- failure path: unavailable provider ----------

    [Theory]
    [InlineData(7, 30, "outside_working_hours")]     // before the 08:00 start
    [InlineData(16, 30, "outside_working_hours")]    // 16:30 + 60 minutes runs past 17:00
    [InlineData(18, 0, "outside_working_hours")]     // after hours
    public async Task A_provider_outside_their_working_hours_is_unavailable(int hour, int minute, string reason)
    {
        var ex = await RefusedAsync(_s.Request(_ann, At(hour, minute)));

        Assert.Equal("provider_unavailable", ex.Code);
        Assert.Equal(reason, ex.Reason);
        Assert.Equal(0, await _s.CountAsync());
    }

    [Fact]
    public async Task A_provider_on_a_day_they_do_not_work_is_unavailable()
    {
        var ex = await RefusedAsync(_s.Request(_ann, At(9, 0, dayOffset: 6))); // the following Sunday

        Assert.Equal(("provider_unavailable", "outside_working_hours"), (ex.Code, ex.Reason));
    }

    [Fact]
    public async Task A_provider_with_blocked_time_is_unavailable_for_the_blocked_part_only()
    {
        await _s.BlockProviderAsync(_s.ProviderA, At(12), At(13)); // lunch

        var ex = await RefusedAsync(_s.Request(_ann, At(12, 30), duration: 30));
        var before = await _s.ScheduleAsync(_s.Request(_ann, At(11), duration: 60));  // 11:00-12:00 ends exactly when lunch starts
        var after = await _s.ScheduleAsync(_s.Request(_ann, At(13), duration: 60));   // starts exactly when lunch ends

        Assert.Equal(("provider_unavailable", "blocked_time"), (ex.Code, ex.Reason));
        Assert.True(before.Created && after.Created);
    }

    [Fact]
    public async Task An_inactive_provider_is_unavailable()
    {
        await using (var db = _fixture.CreateContext())
        {
            var provider = await db.ProviderProfiles.SingleAsync(p => p.Id == _s.ProviderA);
            provider.IsActive = false;
            await db.SaveChangesAsync();
        }

        var ex = await RefusedAsync(_s.Request(_ann, At(9)));

        Assert.Equal(("provider_unavailable", "provider_inactive"), (ex.Code, ex.Reason));
    }

    // ---------- failure path: incorrect duration ----------

    [Theory]
    [InlineData(0)]
    [InlineData(-30)]
    [InlineData(7)]       // not a multiple of 5
    [InlineData(481)]     // longer than the 8-hour maximum
    [InlineData(100000)]
    public async Task An_incorrect_duration_is_rejected_and_nothing_is_stored(int minutes)
    {
        var ex = await RefusedAsync(_s.Request(_ann, At(9), duration: minutes));

        Assert.Equal("invalid_duration", ex.Code);
        Assert.Equal(400, ex.StatusCode);
        Assert.Equal(0, await _s.CountAsync());
    }

    // ---------- the rest of the request ----------

    [Fact]
    public async Task Unknown_or_inactive_patients_providers_operatories_and_types_are_refused()
    {
        Assert.Equal("patient_not_found", (await RefusedAsync(_s.Request(Guid.NewGuid(), At(9)))).Code);
        Assert.Equal("provider_not_found", (await RefusedAsync(_s.Request(_ann, At(9), provider: Guid.NewGuid()))).Code);
        Assert.Equal("operatory_not_found", (await RefusedAsync(_s.Request(_ann, At(9), operatory: Guid.NewGuid()))).Code);
        Assert.Equal("appointment_type_not_found", (await RefusedAsync(_s.Request(_ann, At(9), type: Guid.NewGuid()))).Code);

        await using (var db = _fixture.CreateContext())
        {
            (await db.Operatories.SingleAsync(o => o.Id == _s.Op2)).IsActive = false;
            (await db.AppointmentTypes.SingleAsync(t => t.Id == _s.Short30)).IsActive = false;
            (await db.Patients.SingleAsync(p => p.Id == _ann)).IsActive = false;
            await db.SaveChangesAsync();
        }
        Assert.Equal("patient_inactive", (await RefusedAsync(_s.Request(_ann, At(9)))).Code);
        var bo = await _s.PatientAsync("Bo", "Kim");
        Assert.Equal("operatory_inactive", (await RefusedAsync(_s.Request(bo, At(9), operatory: _s.Op2))).Code);
        Assert.Equal("appointment_type_inactive", (await RefusedAsync(_s.Request(bo, At(9), type: _s.Short30))).Code);
        Assert.Equal(0, await _s.CountAsync());
    }

    [Fact]
    public async Task A_start_in_the_past_or_at_a_time_that_does_not_exist_is_refused()
    {
        Assert.Equal("start_in_past", (await RefusedAsync(_s.Request(_ann, new DateTime(2020, 1, 13, 9, 0, 0)))).Code);
        // 02:30 on 2030-03-10 does not exist in Chicago (clocks jump from 02:00 to 03:00)
        Assert.Equal("invalid_local_time", (await RefusedAsync(_s.Request(_ann, new DateTime(2030, 3, 10, 2, 30, 0)))).Code);
        Assert.Equal(0, await _s.CountAsync());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short")]
    public async Task A_request_without_a_usable_idempotency_key_is_refused(string? key)
    {
        await using var db = _fixture.CreateContext();
        var ex = await Assert.ThrowsAsync<SchedulingException>(() => _s.Scheduler(db).ScheduleAsync(_s.Request(_ann, At(9)), key, _s.Actor, default));
        Assert.Equal("idempotency_key_required", ex.Code);
    }

    // ---------- acceptance 3 / failure path: audit ----------

    [Fact]
    public async Task A_scheduled_appointment_is_audited_with_the_user_and_timestamp_and_without_patient_details()
    {
        var before = DateTimeOffset.UtcNow.AddSeconds(-5);
        var result = await _s.ScheduleAsync(_s.Request(_ann, At(9)));

        await using var db = _fixture.CreateContext();
        var entry = await db.AuditLogEntries.SingleAsync(a => a.EventType == SchedulingAuditEvents.Scheduled);
        Assert.Equal(_s.Actor, entry.PerformedByUserAccountId);
        Assert.Equal(result.Appointment.Id, entry.TargetUserAccountId);
        Assert.Equal("Appointment", entry.EntityType);
        Assert.InRange(entry.TimestampUtc, before, DateTimeOffset.UtcNow.AddSeconds(5));
        Assert.DoesNotContain("Ann", entry.Details);
        Assert.DoesNotContain("Lee", entry.Details);
    }

    [Fact]
    public async Task A_refused_attempt_is_audited_too_with_who_tried_and_why_and_books_nothing()
    {
        var bo = await _s.PatientAsync("Bo", "Kim");
        var first = await _s.ScheduleAsync(_s.Request(_ann, At(9)));
        await RefusedAsync(_s.Request(bo, At(9), operatory: _s.Op2));                 // provider double-booked
        await RefusedAsync(_s.Request(bo, At(7), operatory: _s.Op2));                 // provider unavailable

        await using var db = _fixture.CreateContext();
        var rejections = await db.AuditLogEntries.Where(a => a.EventType == SchedulingAuditEvents.Rejected).OrderBy(a => a.TimestampUtc).ToListAsync();
        Assert.Equal(2, rejections.Count);
        Assert.All(rejections, r => Assert.Equal(_s.Actor, r.PerformedByUserAccountId));
        Assert.Contains("provider_double_booked", rejections[0].Details);
        Assert.Equal(first.Appointment.Id, rejections[0].TargetUserAccountId);
        Assert.Contains("provider_unavailable (outside_working_hours)", rejections[1].Details);
        Assert.DoesNotContain("Bo", rejections[0].Details);
        Assert.Equal(1, await _s.CountAsync());
    }

    [Fact]
    public async Task A_malformed_request_is_not_logged_as_a_scheduling_attempt()
    {
        await RefusedAsync(_s.Request(_ann, At(9), duration: 7));
        Assert.Equal(0, await _s.CountAsync(db => db.AuditLogEntries.Where(a => a.EventType == SchedulingAuditEvents.Rejected)));
    }

    [Fact]
    public async Task If_the_audit_write_fails_the_appointment_is_not_stored_either_and_the_same_retry_then_succeeds()
    {
        await using (var failing = _s.FailingAuditContext())
            await Assert.ThrowsAsync<DbUpdateException>(() => _s.Scheduler(failing).ScheduleAsync(_s.Request(_ann, At(9)), "audit-fail-key", _s.Actor, default));

        Assert.Equal(0, await _s.CountAsync());
        Assert.Equal(0, await _s.CountAsync(db => db.AuditLogEntries.Where(a => a.EventType == SchedulingAuditEvents.Scheduled)));

        Assert.True((await _s.ScheduleAsync(_s.Request(_ann, At(9)), "audit-fail-key")).Created);
        Assert.Equal(1, await _s.CountAsync());
    }

    [Fact]
    public async Task A_refusal_stands_even_when_it_cannot_be_audited()
    {
        var bo = await _s.PatientAsync("Bo", "Kim");
        await _s.ScheduleAsync(_s.Request(_ann, At(9)));

        await using var failing = _s.FailingAuditContext();
        var ex = await Assert.ThrowsAsync<SchedulingException>(() => _s.Scheduler(failing).ScheduleAsync(_s.Request(bo, At(9), operatory: _s.Op2), "refusal-key-1", _s.Actor, default));

        Assert.Equal("provider_double_booked", ex.Code);
        Assert.Equal(1, await _s.CountAsync());
    }

    // ---------- idempotency ----------

    [Fact]
    public async Task Retrying_a_request_with_the_same_key_returns_the_same_appointment_and_books_once()
    {
        var first = await _s.ScheduleAsync(_s.Request(_ann, At(9)), "retry-key-0001");
        var again = await _s.ScheduleAsync(_s.Request(_ann, At(9)), "retry-key-0001");

        Assert.True(first.Created);
        Assert.False(again.Created);
        Assert.Equal(first.Appointment.Id, again.Appointment.Id);
        Assert.Equal(1, await _s.CountAsync());
        Assert.Equal(1, await _s.CountAsync(db => db.AuditLogEntries.Where(a => a.EventType == SchedulingAuditEvents.Scheduled)));
    }

    [Fact]
    public async Task Reusing_a_key_for_a_different_appointment_is_refused()
    {
        await _s.ScheduleAsync(_s.Request(_ann, At(9)), "reused-key-001");

        var ex = await RefusedAsync(_s.Request(_ann, At(14)), "reused-key-001");

        Assert.Equal("idempotency_key_reused", ex.Code);
        Assert.Equal(1, await _s.CountAsync());
    }

    // ---------- concurrency: the guarantee must hold under a race ----------

    [Fact]
    public async Task Eight_people_booking_the_same_provider_slot_at_once_produce_exactly_one_appointment()
    {
        var patients = new List<Guid>();
        for (var i = 0; i < 8; i++) patients.Add(await _s.PatientAsync($"Pat{i}", $"Racer{i}"));

        // each uses its own operatory-free combination: operatories alternate, so only the PROVIDER is common to all eight
        var outcomes = await Task.WhenAll(patients.Select(async (p, i) =>
        {
            try { await _s.ScheduleAsync(_s.Request(p, At(9), operatory: i % 2 == 0 ? _s.Op1 : _s.Op2)); return "booked"; }
            catch (SchedulingException ex) { return ex.Code; }
        }));

        Assert.Equal(1, outcomes.Count(o => o == "booked"));
        Assert.All(outcomes.Where(o => o != "booked"), o => Assert.Contains(o, new[] { "provider_double_booked", "operatory_conflict" }));
        Assert.Equal(1, await _s.CountAsync());
    }

    [Fact]
    public async Task Eight_people_booking_the_same_operatory_slot_with_different_providers_produce_exactly_one_appointment()
    {
        var patients = new List<Guid>();
        for (var i = 0; i < 8; i++) patients.Add(await _s.PatientAsync($"Pat{i}", $"Racer{i}"));

        var outcomes = await Task.WhenAll(patients.Select(async (p, i) =>
        {
            try { await _s.ScheduleAsync(_s.Request(p, At(9), provider: i % 2 == 0 ? _s.ProviderA : _s.ProviderB, operatory: _s.Op1)); return "booked"; }
            catch (SchedulingException ex) { return ex.Code; }
        }));

        Assert.Equal(1, outcomes.Count(o => o == "booked"));
        Assert.Equal(1, await _s.CountAsync());
    }

    [Fact]
    public async Task Overlapping_but_not_identical_requests_racing_still_never_double_book()
    {
        var patients = new List<Guid>();
        for (var i = 0; i < 6; i++) patients.Add(await _s.PatientAsync($"Pat{i}", $"Racer{i}"));

        // 09:00, 09:15, 09:30 ... every one overlaps the next; at most a non-overlapping subset can win
        var outcomes = await Task.WhenAll(patients.Select(async (p, i) =>
        {
            try { await _s.ScheduleAsync(_s.Request(p, At(9, i * 15), operatory: i % 2 == 0 ? _s.Op1 : _s.Op2, duration: 60)); return true; }
            catch (SchedulingException) { return false; }
        }));

        await using var db = _fixture.CreateContext();
        var booked = await db.Appointments.AsNoTracking().OrderBy(a => a.StartUtc).ToListAsync();
        Assert.Equal(outcomes.Count(o => o), booked.Count);
        for (var i = 1; i < booked.Count; i++) Assert.True(booked[i - 1].EndUtc <= booked[i].StartUtc, "two booked appointments overlap");
    }

    [Fact]
    public async Task Requests_for_different_slots_racing_do_not_block_each_other_or_conflict_falsely()
    {
        var patients = new List<Guid>();
        for (var i = 0; i < 6; i++) patients.Add(await _s.PatientAsync($"Pat{i}", $"Racer{i}"));

        var results = await Task.WhenAll(patients.Select((p, i) => _s.ScheduleAsync(_s.Request(p, At(8 + i), duration: 60)))); // 08:00, 09:00 ... 13:00

        Assert.All(results, r => Assert.True(r.Created));
        Assert.Equal(6, await _s.CountAsync());
    }

    [Fact]
    public async Task Eight_simultaneous_submits_of_one_request_with_the_same_key_book_once()
    {
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            try { var r = await _s.ScheduleAsync(_s.Request(_ann, At(9)), "same-key-race-1"); return r.Created ? "created" : "replayed"; }
            catch (SchedulingException ex) { return ex.Code; }
        }));

        Assert.Equal(1, results.Count(r => r == "created"));
        Assert.All(results, r => Assert.Contains(r, new[] { "created", "replayed", "provider_double_booked", "operatory_conflict" })); // an early loser may see the winner's appointment before its key
        Assert.Equal(1, await _s.CountAsync());
    }

    // ---------- reading ----------

    [Fact]
    public async Task Appointments_can_be_read_back_by_id_and_listed_by_range_provider_and_operatory_earliest_first()
    {
        var bo = await _s.PatientAsync("Bo", "Kim");
        var a1 = await _s.ScheduleAsync(_s.Request(_ann, At(11)));
        var a2 = await _s.ScheduleAsync(_s.Request(bo, At(9), provider: _s.ProviderB, operatory: _s.Op2));
        var a3 = await _s.ScheduleAsync(_s.Request(bo, At(9, 0, dayOffset: 1)));

        await using var db = _fixture.CreateContext();
        var scheduler = _s.Scheduler(db);
        Assert.Equal(a1.Appointment.Id, (await scheduler.GetAsync(a1.Appointment.Id, default))!.Id);
        Assert.Null(await scheduler.GetAsync(Guid.NewGuid(), default));

        var monday = await scheduler.ListAsync(Clock.FromPracticeLocal(At(0)), Clock.FromPracticeLocal(At(0, 0, 1)), null, null, default);
        Assert.Equal([a2.Appointment.Id, a1.Appointment.Id], monday.Select(a => a.Id));
        var rivera = await scheduler.ListAsync(Clock.FromPracticeLocal(At(0)), Clock.FromPracticeLocal(At(0, 0, 2)), _s.ProviderA, null, default);
        Assert.Equal([a1.Appointment.Id, a3.Appointment.Id], rivera.Select(a => a.Id));
        var op2 = await scheduler.ListAsync(Clock.FromPracticeLocal(At(0)), Clock.FromPracticeLocal(At(0, 0, 2)), null, _s.Op2, default);
        Assert.Equal([a2.Appointment.Id], op2.Select(a => a.Id));
    }

    // ---------- measurement ----------

    [Fact]
    public async Task Scheduling_and_refusals_record_privacy_safe_measurement_events()
    {
        var bo = await _s.PatientAsync("Bo", "Kim");
        await using (var db = _fixture.CreateContext())
        {
            var scheduler = _s.Scheduler(db, new MeasurementEventSink(db));
            await scheduler.ScheduleAsync(_s.Request(_ann, At(9)), "measure-key-001", _s.Actor, default);
            await Assert.ThrowsAsync<SchedulingException>(() => scheduler.ScheduleAsync(_s.Request(bo, At(9), operatory: _s.Op2), "measure-key-002", _s.Actor, default));
        }

        await using var read = _fixture.CreateContext();
        var events = await read.MeasurementEvents.OrderBy(e => e.OccurredAtUtc).ToListAsync();
        Assert.Equal(["appointment.scheduled", "appointment.rejected"], events.Select(e => e.EventName));
        Assert.Equal("""{"outcome":"success"}""", events[0].PropertiesJson);
        Assert.Equal("""{"category":"provider_double_booked","outcome":"failure"}""", events[1].PropertiesJson);
    }
}
