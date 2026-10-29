using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Alveara.Api.Architecture.Configuration;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Reporting;
using Alveara.Api.Architecture.Scheduling;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N006 slice 1, the schedule and patient-flow reports and their supporting records, against a real SQL Server. A small practice is booked through the real scheduler (five appointments over three days, two providers,
/// two operatories) and then given statuses, visit states and history events: the totals, the days (a day with nothing booked is a row of zeros), the providers and the visit states all add up to one independent count of
/// the appointments; a day is a practice-local day (a late-evening appointment is on its own day, not the next UTC day); the filters narrow every view the same way; no data is reported as no data; repeating a run gives the
/// same numbers and a recorded audit entry that names no patient; a range holding too many records is refused and not recorded; audit storage being down refuses the report; and every drill-down total equals the cell it
/// came from, pages never repeat or skip a record, and bad criteria get stable codes.
/// </summary>
public class OperationalReportsTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private readonly IPracticeClock _clock = SchedulingTestSupport.Clock;
    private SchedulingTestSupport _s = null!;
    private Guid _actor;
    private Guid _a1, _a2, _a3, _a4, _a5;
    private Guid _patientA;

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
        _s = new SchedulingTestSupport(_fixture);
        await _s.ArrangeAsync();
        _actor = Guid.NewGuid();
        await using var db = _fixture.CreateContext();
        db.UserAccounts.Add(new UserAccount { Id = _actor, Username = $"manager-{Guid.NewGuid():N}", Role = Role.OfficeManager, CreatedAtUtc = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => _fixture.DisposeAsync();

    // Monday 2030-01-14 to Wednesday 2030-01-16: a1 Mon 09:00 (Scheduled, 60 min), a2 Mon 10:00 (Cancelled, 30), a3 Tue 09:00 (NoShow, 60), a4 Tue 11:00 (Scheduled, 30, CheckedIn),
    // a5 Wed 09:00 on the other provider (Scheduled, 60, InTreatment).
    private async Task ArrangeWeekAsync()
    {
        _patientA = await _s.PatientAsync("Zed", "Qwerty");
        _a1 = (await _s.ScheduleAsync(_s.Request(_patientA, SchedulingTestSupport.At(9)))).Appointment.Id;
        _a2 = (await _s.ScheduleAsync(_s.Request(await _s.PatientAsync("Bo", "Ng"), SchedulingTestSupport.At(10), type: _s.Short30))).Appointment.Id;
        _a3 = (await _s.ScheduleAsync(_s.Request(await _s.PatientAsync("Cy", "Ox"), SchedulingTestSupport.At(9, dayOffset: 1)))).Appointment.Id;
        _a4 = (await _s.ScheduleAsync(_s.Request(await _s.PatientAsync("Di", "Fo"), SchedulingTestSupport.At(11, dayOffset: 1), type: _s.Short30))).Appointment.Id;
        _a5 = (await _s.ScheduleAsync(_s.Request(await _s.PatientAsync("Ed", "Go"), SchedulingTestSupport.At(9, dayOffset: 2), provider: _s.ProviderB, operatory: _s.Op2))).Appointment.Id;
        await using var db = _fixture.CreateContext();
        await db.Appointments.Where(a => a.Id == _a2).ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, AppointmentStatuses.Cancelled).SetProperty(a => a.CancelReason, "test"));
        await db.Appointments.Where(a => a.Id == _a3).ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, AppointmentStatuses.NoShow));
        await db.Appointments.Where(a => a.Id == _a4).ExecuteUpdateAsync(s => s.SetProperty(a => a.FlowState, VisitStates.CheckedIn));
        await db.Appointments.Where(a => a.Id == _a5).ExecuteUpdateAsync(s => s.SetProperty(a => a.FlowState, VisitStates.InTreatment));
        foreach (var (id, type) in new[] { (_a1, AppointmentEventTypes.Confirmed), (_a4, AppointmentEventTypes.CheckedIn), (_a5, AppointmentEventTypes.CheckedIn), (_a5, AppointmentEventTypes.TreatmentStarted), (_a1, AppointmentEventTypes.Rescheduled) })
            db.AppointmentEvents.Add(new AppointmentEvent { Id = Guid.NewGuid(), AppointmentId = id, EventType = type, OccurredAtUtc = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
    }

    private ReportFilterResolver Resolver(AlveraDbContext db, ReportOptions? options = null) => new(db, _clock, Options.Create(options ?? new ReportOptions()));

    private async Task<ReportFilter> FilterAsync(AlveraDbContext db, string from = "2030-01-14", string to = "2030-01-16", Guid[]? providers = null, Guid? location = null, ReportOptions? options = null) =>
        await Resolver(db, options).ResolveAsync(new ReportFilterInput(from, to, providers, location), default);

    private ScheduleReportService Schedule(AlveraDbContext db, ReportOptions? options = null) => new(db, _clock, Options.Create(options ?? new ReportOptions()), NullLogger<ScheduleReportService>.Instance);

    private PatientFlowReportService Flow(AlveraDbContext db, IPracticeClock? clock = null) => new(db, clock ?? _clock, Options.Create(new ReportOptions()), NullLogger<PatientFlowReportService>.Instance);

    private ReportDrillService Drill(AlveraDbContext db, ReportOptions? options = null) =>
        new(db, Resolver(db, options), Options.Create(options ?? new ReportOptions()), NullLogger<ReportDrillService>.Instance);

    // ---------- the filter ----------

    [Fact]
    public async Task A_range_becomes_whole_practice_local_days_and_a_daylight_saving_day_is_23_or_25_hours_long()
    {
        await using var db = _fixture.CreateContext();
        var normal = await FilterAsync(db, "2030-01-14", "2030-01-14");
        Assert.Equal(_clock.FromPracticeLocal(new DateTime(2030, 1, 14)), normal.StartUtc);
        Assert.Equal(_clock.FromPracticeLocal(new DateTime(2030, 1, 15)), normal.EndUtc);
        Assert.Equal(1, normal.Days);

        var spring = await FilterAsync(db, "2030-03-10", "2030-03-10");        // the clocks go forward at 02:00: the local day has 23 hours
        Assert.Equal(TimeSpan.FromHours(23), spring.EndUtc - spring.StartUtc);
        var autumn = await FilterAsync(db, "2030-11-03", "2030-11-03");        // the clocks go back: 25 hours
        Assert.Equal(TimeSpan.FromHours(25), autumn.EndUtc - autumn.StartUtc);
        var three = await FilterAsync(db, "2030-01-14", "2030-01-16");
        Assert.Equal(_clock.FromPracticeLocal(new DateTime(2030, 1, 17)), three.EndUtc);
    }

    [Fact]
    public async Task A_bad_range_is_refused_with_a_stable_code_and_the_limit_is_exact()
    {
        await using var db = _fixture.CreateContext();
        var resolver = Resolver(db);
        async Task<string> CodeAsync(string? from, string? to, IReadOnlyList<Guid>? providers = null, Guid? location = null) =>
            (await Assert.ThrowsAsync<ReportException>(() => resolver.ResolveAsync(new ReportFilterInput(from, to, providers, location), default))).Code;

        Assert.Equal("dates_required", await CodeAsync(null, "2030-01-16"));
        Assert.Equal("dates_required", await CodeAsync("2030-01-14", " "));
        Assert.Equal("invalid_date", await CodeAsync("2030-13-01", "2030-01-16"));
        Assert.Equal("invalid_date", await CodeAsync("14/01/2030", "2030-01-16"));
        Assert.Equal("invalid_date", await CodeAsync("1800-01-01", "2030-01-16"));
        Assert.Equal("invalid_range", await CodeAsync("2030-01-16", "2030-01-14"));
        Assert.Equal("range_too_large", await CodeAsync("2028-01-01", "2029-01-01"));      // 367 days
        var exact = await resolver.ResolveAsync(new ReportFilterInput("2028-01-01", "2028-12-31", null, null), default);      // 366 days: allowed
        Assert.Equal(366, exact.Days);
        Assert.Equal("too_many_providers", await CodeAsync("2030-01-14", "2030-01-16", Enumerable.Range(0, 51).Select(_ => Guid.NewGuid()).ToList()));
        Assert.Equal("unknown_provider", await CodeAsync("2030-01-14", "2030-01-16", [Guid.NewGuid()]));
        Assert.Equal("unknown_location", await CodeAsync("2030-01-14", "2030-01-16", null, Guid.NewGuid()));
        Assert.Equal(0, await db.AuditLogEntries.CountAsync(e => e.EventType == AuditEventTypes.ReportViewed));         // a refused request is not a report
    }

    // ---------- the schedule report ----------

    [Fact]
    public async Task The_totals_the_days_and_the_providers_all_add_up_to_one_independent_count_of_the_appointments()
    {
        await ArrangeWeekAsync();
        await using var db = _fixture.CreateContext();
        var result = await Schedule(db).RunAsync(await FilterAsync(db), _actor, default);

        var t = result.Data.Totals;
        Assert.Equal((5, 3, 1, 1, 150), (t.Appointments, t.Scheduled, t.Cancelled, t.NoShow, t.BookedMinutes));
        Assert.Equal(await _s.CountAsync(), result.RecordCount);                                                      // the independent count of all appointments
        Assert.Equal(new[] { "2030-01-14", "2030-01-15", "2030-01-16" }, result.Data.Days.Select(d => d.Date));
        Assert.Equal(new[] { (2, 1, 1, 0, 60), (2, 1, 0, 1, 30), (1, 1, 0, 0, 60) }, result.Data.Days.Select(d => (d.Counts.Appointments, d.Counts.Scheduled, d.Counts.Cancelled, d.Counts.NoShow, d.Counts.BookedMinutes)));
        Assert.Equal(t.Appointments, result.Data.Days.Sum(d => d.Counts.Appointments));
        Assert.Equal(t.Appointments, result.Data.Providers.Sum(p => p.Counts.Appointments));
        Assert.Equal(t.BookedMinutes, result.Data.Providers.Sum(p => p.Counts.BookedMinutes));
        Assert.Equal(new[] { "Dr. Patel", "Dr. Rivera" }, result.Data.Providers.Select(p => p.ProviderName));
        Assert.Equal(new[] { 1, 4 }, result.Data.Providers.Select(p => p.Counts.Appointments));
        Assert.Equal(1, result.Data.RescheduleEvents);
        Assert.Equal((ReportIds.Schedule, ScheduleReportService.DefinitionVersion, false), (result.ReportId, result.DefinitionVersion, result.NoData));
        Assert.Contains(result.ReadConsistency, new[] { ReportReadModes.Snapshot, ReportReadModes.TableByTable });
    }

    [Fact]
    public async Task A_day_is_a_practice_local_day_and_the_filters_narrow_every_view_the_same_way()
    {
        await ArrangeWeekAsync();
        await using var db = _fixture.CreateContext();
        // a5 moves to 23:30 on Monday evening local time, which is already Tuesday in UTC
        var late = _clock.FromPracticeLocal(new DateTime(2030, 1, 14, 23, 30, 0));
        await db.Appointments.Where(a => a.Id == _a5).ExecuteUpdateAsync(s => s.SetProperty(a => a.StartUtc, late).SetProperty(a => a.EndUtc, late.AddMinutes(60)));

        var monday = await Schedule(db).RunAsync(await FilterAsync(db, "2030-01-14", "2030-01-14"), _actor, default);
        var tuesday = await Schedule(db).RunAsync(await FilterAsync(db, "2030-01-15", "2030-01-15"), _actor, default);
        Assert.Equal(3, monday.Data.Totals.Appointments);        // a1, a2 and the late evening one
        Assert.Equal(2, tuesday.Data.Totals.Appointments);

        var onlyB = await Schedule(db).RunAsync(await FilterAsync(db, providers: [_s.ProviderB]), _actor, default);
        Assert.Equal(1, onlyB.Data.Totals.Appointments);
        Assert.Equal(new[] { _s.ProviderB }, onlyB.Data.Providers.Select(p => p.ProviderProfileId));
        Assert.Equal(1, onlyB.Data.Days.Sum(d => d.Counts.Appointments));

        var here = await Schedule(db).RunAsync(await FilterAsync(db, location: _s.LocationId), _actor, default);
        Assert.Equal(5, here.Data.Totals.Appointments);
        var elsewhere = Guid.NewGuid();
        db.PracticeLocations.Add(new PracticeLocation { Id = elsewhere, Name = "Closed branch", IsActive = false, CreatedAtUtc = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        var none = await Schedule(db).RunAsync(await FilterAsync(db, location: elsewhere), _actor, default);
        Assert.True(none.NoData);
        Assert.Equal(0, none.Data.Totals.Appointments);
    }

    [Fact]
    public async Task Reschedules_are_counted_only_for_appointments_in_the_range()
    {
        await ArrangeWeekAsync();
        await using var db = _fixture.CreateContext();
        db.AppointmentEvents.Add(new AppointmentEvent { Id = Guid.NewGuid(), AppointmentId = _a5, EventType = AppointmentEventTypes.Rescheduled, OccurredAtUtc = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        Assert.Equal(1, (await Schedule(db).RunAsync(await FilterAsync(db, "2030-01-14", "2030-01-14"), _actor, default)).Data.RescheduleEvents);      // a1's only
        Assert.Equal(1, (await Schedule(db).RunAsync(await FilterAsync(db, "2030-01-16", "2030-01-16"), _actor, default)).Data.RescheduleEvents);      // a5's only
        Assert.Equal(2, (await Schedule(db).RunAsync(await FilterAsync(db), _actor, default)).Data.RescheduleEvents);
    }

    [Fact]
    public async Task A_range_with_nothing_in_it_says_so_and_still_lists_every_day_as_zeros()
    {
        await using var db = _fixture.CreateContext();
        var result = await Schedule(db).RunAsync(await FilterAsync(db, "2030-02-04", "2030-02-08"), _actor, default);

        Assert.True(result.NoData);
        Assert.Equal(0, result.RecordCount);
        Assert.Equal(5, result.Data.Days.Count);
        Assert.All(result.Data.Days, d => Assert.Equal(0, d.Counts.Appointments));
        Assert.Empty(result.Data.Providers);
        var flow = await Flow(db).RunAsync(await FilterAsync(db, "2030-02-04", "2030-02-08"), _actor, default);
        Assert.True(flow.NoData);
        Assert.Equal(8, flow.Data.ByState.Count);        // every visit state is still a row
    }

    [Fact]
    public async Task Running_again_gives_the_same_numbers_and_each_run_is_recorded_without_naming_a_patient()
    {
        await ArrangeWeekAsync();
        await using var db = _fixture.CreateContext();
        var filter = await FilterAsync(db);
        var first = await Schedule(db).RunAsync(filter, _actor, default);
        var second = await Schedule(db).RunAsync(filter, _actor, default);

        Assert.Equal(JsonSerializer.Serialize(first.Data), JsonSerializer.Serialize(second.Data));
        var audit = await db.AuditLogEntries.Where(e => e.EventType == AuditEventTypes.ReportViewed).ToListAsync();
        Assert.Equal(2, audit.Count);
        Assert.All(audit, e =>
        {
            Assert.Equal(_actor, e.PerformedByUserAccountId);
            Assert.Equal("Report", e.EntityType);
            Assert.Contains("schedule", e.Details);
            Assert.Contains("5 record(s)", e.Details);
            Assert.DoesNotContain("Zed", e.Details);
            Assert.DoesNotContain("Qwerty", e.Details);
        });
    }

    [Fact]
    public async Task A_range_holding_too_many_records_is_refused_and_nothing_is_recorded()
    {
        await ArrangeWeekAsync();
        await using var db = _fixture.CreateContext();
        var small = new ReportOptions { MaxSourceRows = 4 };
        var filter = await FilterAsync(db, options: small);

        var ex = await Assert.ThrowsAsync<ReportException>(() => Schedule(db, small).RunAsync(filter, _actor, default));
        Assert.Equal(("report_too_large", 413), (ex.Code, ex.StatusCode));
        Assert.Equal(0, await db.AuditLogEntries.CountAsync(e => e.EventType == AuditEventTypes.ReportViewed));
        var atTheLimit = await Schedule(db, new ReportOptions { MaxSourceRows = 5 }).RunAsync(filter, _actor, default);       // exactly the limit still runs
        Assert.Equal(5, atTheLimit.RecordCount);
    }

    [Fact]
    public async Task When_the_audit_entry_cannot_be_saved_the_report_is_not_delivered()
    {
        await ArrangeWeekAsync();
        await using var db = _s.FailingAuditContext();
        var filter = await FilterAsync(db);

        var ex = await Assert.ThrowsAsync<ReportException>(() => Schedule(db).RunAsync(filter, _actor, default));
        Assert.Equal(("audit_unavailable", 503), (ex.Code, ex.StatusCode));
        await using var check = _fixture.CreateContext();
        Assert.Equal(0, await check.AuditLogEntries.CountAsync(e => e.EventType == AuditEventTypes.ReportViewed));
    }

    // ---------- the patient-flow report ----------

    [Fact]
    public async Task Visits_are_counted_by_state_with_every_state_a_row_and_the_counts_reconcile()
    {
        await ArrangeWeekAsync();
        await using var db = _fixture.CreateContext();
        var result = await Flow(db).RunAsync(await FilterAsync(db), _actor, default);

        var data = result.Data;
        Assert.Equal(VisitStates.InOrder, data.ByState.Select(s => s.State));
        Assert.Equal(new[] { 1, 0, 1, 0, 0, 1, 0, 0 }, data.ByState.Select(s => s.Visits));
        Assert.Equal((5, 1, 1, true), (data.Appointments, data.Cancelled, data.NoShow, data.Reconciled));
        Assert.Equal(data.Appointments, data.ByState.Sum(s => s.Visits) + data.Cancelled + data.NoShow);
        Assert.Equal(await _s.CountAsync(), result.RecordCount);
        Assert.Equal(new[] { ("Confirmed", 1), ("CheckedIn", 2), ("Ready", 0), ("Seated", 0), ("TreatmentStarted", 1), ("CheckedOut", 0), ("Completed", 0) },
            data.Transitions.Select(t => (t.Event, t.Count)));
    }

    [Fact]
    public async Task A_visit_whose_time_has_passed_without_being_completed_is_counted_apart()
    {
        await ArrangeWeekAsync();
        await using var db = _fixture.CreateContext();
        var filter = await FilterAsync(db);
        Assert.Equal(0, (await Flow(db).RunAsync(filter, _actor, default)).Data.PastNotCompleted);        // 2030 has not happened yet

        var later = new TestClock(new DateTimeOffset(2030, 6, 1, 12, 0, 0, TimeSpan.Zero));
        Assert.Equal(3, (await Flow(db, later).RunAsync(filter, _actor, default)).Data.PastNotCompleted);        // a1, a4, a5 are Scheduled bookings that never reached Completed
        await db.Appointments.Where(a => a.Id == _a5).ExecuteUpdateAsync(s => s.SetProperty(a => a.FlowState, VisitStates.Completed));
        Assert.Equal(2, (await Flow(db, later).RunAsync(filter, _actor, default)).Data.PastNotCompleted);
    }

    // ---------- the supporting records ----------

    [Fact]
    public async Task Every_drill_down_total_equals_the_cell_it_came_from()
    {
        await ArrangeWeekAsync();
        await using var db = _fixture.CreateContext();
        var filter = await FilterAsync(db);
        var schedule = (await Schedule(db).RunAsync(filter, _actor, default)).Data;
        var flow = (await Flow(db).RunAsync(filter, _actor, default)).Data;
        async Task<ReportPage<ReportAppointmentRow>> ListAsync(string report, DrillCriteria c) => await Drill(db).AppointmentsAsync(report, filter, c, _actor, default);
        var none = new DrillCriteria(null, null, null, null, null, null, null);

        Assert.Equal(schedule.Totals.Appointments, (await ListAsync(ReportIds.Schedule, none)).Total);
        Assert.Equal(schedule.Totals.Cancelled, (await ListAsync(ReportIds.Schedule, none with { Status = "Cancelled" })).Total);
        Assert.Equal(schedule.Totals.NoShow, (await ListAsync(ReportIds.Schedule, none with { Status = "NoShow" })).Total);
        Assert.Equal(schedule.Totals.Scheduled, (await ListAsync(ReportIds.Schedule, none with { Status = "Scheduled" })).Total);
        foreach (var day in schedule.Days) Assert.Equal(day.Counts.Appointments, (await ListAsync(ReportIds.Schedule, none with { Date = day.Date })).Total);
        foreach (var provider in schedule.Providers) Assert.Equal(provider.Counts.Appointments, (await ListAsync(ReportIds.Schedule, none with { ProviderId = provider.ProviderProfileId })).Total);
        foreach (var state in flow.ByState) Assert.Equal(state.Visits, (await ListAsync(ReportIds.PatientFlow, none with { FlowState = state.State })).Total);

        var checkedIn = await ListAsync(ReportIds.PatientFlow, none with { FlowState = "CheckedIn" });
        var row = Assert.Single(checkedIn.Rows);
        Assert.Equal((_a4, "Scheduled", "CheckedIn", _s.ProviderA), (row.Id, row.Status, row.FlowState, row.ProviderProfileId));
    }

    [Fact]
    public async Task Pages_never_repeat_or_skip_an_appointment_and_each_page_is_recorded()
    {
        await ArrangeWeekAsync();
        await using var db = _fixture.CreateContext();
        var filter = await FilterAsync(db);
        var seen = new List<Guid>();
        ReportCursor? cursor = null;
        var pages = 0;
        do
        {
            var page = await Drill(db).AppointmentsAsync(ReportIds.Schedule, filter, new DrillCriteria(null, null, null, null, 2, cursor?.AfterStart, cursor?.AfterId), _actor, default);
            Assert.Equal(5, page.Total);
            seen.AddRange(page.Rows.Select(r => r.Id));
            cursor = page.Next;
            pages++;
        } while (cursor is not null);

        Assert.Equal(3, pages);        // 2 + 2 + 1
        Assert.Equal(5, seen.Distinct().Count());
        Assert.Equal(new[] { _a1, _a2, _a3, _a4, _a5 }, seen);        // in start order
        var audit = await db.AuditLogEntries.Where(e => e.EventType == AuditEventTypes.ReportViewed).ToListAsync();
        Assert.Equal(3, audit.Count);
        Assert.All(audit, e => Assert.Contains("(supporting records)", e.Details));
    }

    [Fact]
    public async Task Bad_drill_criteria_get_stable_codes_and_record_nothing()
    {
        await ArrangeWeekAsync();
        await using var db = _fixture.CreateContext();
        var filter = await FilterAsync(db);
        var none = new DrillCriteria(null, null, null, null, null, null, null);
        async Task<string> CodeAsync(DrillCriteria c) => (await Assert.ThrowsAsync<ReportException>(() => Drill(db).AppointmentsAsync(ReportIds.Schedule, filter, c, _actor, default))).Code;

        Assert.Equal("invalid_status", await CodeAsync(none with { Status = "Done" }));
        Assert.Equal("invalid_flow_state", await CodeAsync(none with { FlowState = "Lost" }));
        Assert.Equal("date_outside_range", await CodeAsync(none with { Date = "2030-01-17" }));
        Assert.Equal("invalid_date", await CodeAsync(none with { Date = "tomorrow" }));
        Assert.Equal("invalid_limit", await CodeAsync(none with { Limit = 0 }));
        Assert.Equal("invalid_limit", await CodeAsync(none with { Limit = 501 }));
        Assert.Equal("invalid_cursor", await CodeAsync(none with { AfterStart = DateTimeOffset.UtcNow }));
        Assert.Equal(5, (await Drill(db).AppointmentsAsync(ReportIds.Schedule, filter, none with { Limit = 500 }, _actor, default)).Rows.Count);        // the largest allowed page works
        Assert.Equal(1, await db.AuditLogEntries.CountAsync(e => e.EventType == AuditEventTypes.ReportViewed));        // only that last successful call
    }
}
