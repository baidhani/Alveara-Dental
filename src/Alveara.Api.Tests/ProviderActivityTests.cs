using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Reporting;
using Alveara.Api.Architecture.Scheduling;
using Alveara.Api.Architecture.Treatment;
using Alveara.Api.Data;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N006 slice 3b, the provider-activity report and the records behind it, against a real SQL Server with appointments booked through the real scheduler and procedures completed through the real completion service.
/// Dr. Rivera (provider A) has three bookings (two scheduled, one cancelled), Dr. Patel (provider B) two (one scheduled, one no-show). Two visits are finished: one booked with and seen by Dr. Rivera, and one booked with
/// Dr. Rivera but seen by Dr. Patel. Dr. Rivera completes two procedures and Dr. Patel one. The three measures differ on purpose (a visit seen by another provider is in the first provider's Booked and the second
/// provider's VisitsCompleted); each column adds up to an independent count; the provider filter narrows every measure; a location filter is refused; and every drill-down total equals its cell.
/// </summary>
public class ProviderActivityTests : CompletionTestBase
{
    private Guid _a1, _a2, _a3, _a4, _a5;
    private static readonly ReportOptions Wide = new() { MaxRangeDays = 5000 };

    private async Task ArrangeAsync()
    {
        _a1 = (await S.ScheduleAsync(S.Request(Ann, At(9)))).Appointment.Id;                                                                            // Rivera, finished, seen by Rivera
        _a2 = (await S.ScheduleAsync(S.Request(Bo, At(10, 30), type: S.Short30))).Appointment.Id;                                                      // Rivera, finished, seen by Patel
        _a3 = (await S.ScheduleAsync(S.Request(await S.PatientAsync("Cy", "Ox"), At(9, dayOffset: 1)))).Appointment.Id;                                // Rivera, cancelled
        _a4 = (await S.ScheduleAsync(S.Request(await S.PatientAsync("Di", "Fo"), At(11, dayOffset: 1), provider: S.ProviderB, operatory: S.Op2))).Appointment.Id;        // Patel, no-show
        _a5 = (await S.ScheduleAsync(S.Request(await S.PatientAsync("Ed", "Go"), At(9, dayOffset: 2), provider: S.ProviderB, operatory: S.Op2))).Appointment.Id;        // Patel, scheduled only
        await using (var db = Fixture.CreateContext())
        {
            await db.Appointments.Where(a => a.Id == _a1).ExecuteUpdateAsync(s => s.SetProperty(a => a.FlowState, VisitStates.Completed));
            await db.Appointments.Where(a => a.Id == _a2).ExecuteUpdateAsync(s => s.SetProperty(a => a.FlowState, VisitStates.Completed).SetProperty(a => a.VisitProviderProfileId, (Guid?)S.ProviderB));
            await db.Appointments.Where(a => a.Id == _a3).ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, AppointmentStatuses.Cancelled).SetProperty(a => a.CancelReason, "test"));
            await db.Appointments.Where(a => a.Id == _a4).ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, AppointmentStatuses.NoShow));
        }

        var plan = await AcceptedPlanAsync(3);
        async Task<PlanView> CompleteAsync(PlanView p, int item, Guid provider, string key)
        {
            await Completion(s => s.CompleteAsync(p.Id, RequestFor(p, [item], AnnEncounter, provider, key), S.Actor, default));
            return await PlanNowAsync(p.Id);
        }
        plan = await CompleteAsync(plan, 0, S.ProviderA, "set-1");
        plan = await CompleteAsync(plan, 1, S.ProviderB, "set-2");
        await CompleteAsync(plan, 2, S.ProviderA, "set-3");
    }

    private ReportFilterResolver Resolver(AlveraDbContext db) => new(db, Clock, Options.Create(Wide));

    /// <summary>From yesterday to Wednesday 2030-01-16: it holds the five appointments (2030) and the procedures completed today.</summary>
    private async Task<ReportFilter> RangeAsync(AlveraDbContext db, Guid[]? providers = null, Guid? location = null, string to = "2030-01-16")
    {
        var yesterday = DateOnly.FromDateTime(Clock.ToPracticeLocal(DateTimeOffset.UtcNow).DateTime).AddDays(-1).ToString("yyyy-MM-dd");
        return await Resolver(db).ResolveAsync(new ReportFilterInput(yesterday, to, providers, location), default);
    }

    private ProviderActivityReportService Report(AlveraDbContext db, ReportOptions? options = null) => new(db, Clock, Options.Create(options ?? Wide), NullLogger<ProviderActivityReportService>.Instance);
    private ProviderActivityDrillService AppointmentsDrill(AlveraDbContext db) => new(db, Options.Create(Wide), NullLogger<ProviderActivityDrillService>.Instance);
    private ClinicalDrillService ProceduresDrill(AlveraDbContext db) => new(db, Resolver(db), Options.Create(Wide), NullLogger<ClinicalDrillService>.Instance);
    private static readonly ActivityDrillCriteria None = new(null, null, null, null, null);

    [Fact]
    public async Task Each_provider_has_booked_cancelled_no_show_visit_and_procedure_counts_and_every_column_adds_up()
    {
        await ArrangeAsync();
        await using var db = Fixture.CreateContext();
        var result = await Report(db).RunAsync(await RangeAsync(db), S.Actor, default);

        var d = result.Data;
        Assert.Equal(new[] { "Dr. Patel", "Dr. Rivera" }, d.Providers.Select(p => p.ProviderName));
        Assert.Equal(new[] { (2, 1, 0, 1, 1, 1), (3, 2, 1, 0, 1, 2) }, d.Providers.Select(p => (p.Booked, p.Scheduled, p.Cancelled, p.NoShow, p.VisitsCompleted, p.ProceduresCompleted)));
        Assert.Equal((5, 3, 1, 1, 2, 3), (d.Totals.Booked, d.Totals.Scheduled, d.Totals.Cancelled, d.Totals.NoShow, d.Totals.VisitsCompleted, d.Totals.ProceduresCompleted));
        Assert.True(d.Reconciled);
        Assert.Equal(await S.CountAsync(), d.Totals.Booked);                                         // the independent count of all appointments
        Assert.Equal(await db.CompletedProcedures.CountAsync(), d.Totals.ProceduresCompleted);
        Assert.Equal(("provider-activity", false, 8), (result.ReportId, result.NoData, result.RecordCount));
    }

    [Fact]
    public async Task A_visit_booked_with_one_provider_and_seen_by_another_is_in_the_second_providers_visits_only()
    {
        await ArrangeAsync();
        await using var db = Fixture.CreateContext();
        var d = (await Report(db).RunAsync(await RangeAsync(db), S.Actor, default)).Data;
        var rivera = d.Providers.Single(p => p.ProviderName == "Dr. Rivera");
        var patel = d.Providers.Single(p => p.ProviderName == "Dr. Patel");

        Assert.Equal((3, 1), (rivera.Booked, rivera.VisitsCompleted));        // booked with her three times; she saw one of the two finished visits
        Assert.Equal((2, 1), (patel.Booked, patel.VisitsCompleted));          // booked twice; he saw the other finished visit
    }

    [Fact]
    public async Task The_provider_filter_keeps_only_the_chosen_provider_in_every_measure_and_a_location_filter_is_refused()
    {
        await ArrangeAsync();
        await using var db = Fixture.CreateContext();
        var d = (await Report(db).RunAsync(await RangeAsync(db, [S.ProviderB]), S.Actor, default)).Data;

        Assert.Equal(new[] { "Dr. Patel" }, d.Providers.Select(p => p.ProviderName));
        Assert.Equal((2, 1, 0, 1, 1, 1), (d.Totals.Booked, d.Totals.Scheduled, d.Totals.Cancelled, d.Totals.NoShow, d.Totals.VisitsCompleted, d.Totals.ProceduresCompleted));
        Assert.True(d.Reconciled);
        var withLocation = await RangeAsync(db, location: S.LocationId);
        Assert.Equal("filter_not_supported", (await Assert.ThrowsAsync<ReportException>(() => Report(db).RunAsync(withLocation, S.Actor, default))).Code);
    }

    [Fact]
    public async Task A_range_with_nothing_in_it_says_so_and_a_range_over_the_limit_is_refused()
    {
        await ArrangeAsync();
        await using var db = Fixture.CreateContext();
        var empty = await Report(db).RunAsync(await Resolver(db).ResolveAsync(new ReportFilterInput("2020-01-01", "2020-01-31", null, null), default), S.Actor, default);
        Assert.True(empty.NoData);
        Assert.Empty(empty.Data.Providers);
        Assert.True(empty.Data.Reconciled);

        var small = new ReportOptions { MaxRangeDays = 5000, MaxSourceRows = 7 };
        var ex = await Assert.ThrowsAsync<ReportException>(async () => await Report(db, small).RunAsync(await RangeAsync(db), S.Actor, default));        // 5 appointments + 3 procedures, limit 7
        Assert.Equal(("report_too_large", 413), (ex.Code, ex.StatusCode));
        Assert.Equal(1, await db.AuditLogEntries.CountAsync(e => e.EventType == AuditEventTypes.ReportViewed));        // only the empty run
    }

    [Fact]
    public async Task Every_appointment_drill_total_equals_the_cell_it_came_from()
    {
        await ArrangeAsync();
        await using var db = Fixture.CreateContext();
        var filter = await RangeAsync(db);
        var report = (await Report(db).RunAsync(filter, S.Actor, default)).Data;
        Task<ReportPage<ReportAppointmentRow>> List(ActivityDrillCriteria c) => AppointmentsDrill(db).AppointmentsAsync(filter, c, S.Actor, default);

        foreach (var measure in ActivityMeasures.All)
            foreach (var provider in report.Providers)
            {
                var cell = measure switch
                {
                    ActivityMeasures.Booked => provider.Booked, ActivityMeasures.Scheduled => provider.Scheduled, ActivityMeasures.Cancelled => provider.Cancelled,
                    ActivityMeasures.NoShow => provider.NoShow, _ => provider.VisitsCompleted,
                };
                Assert.Equal(cell, (await List(None with { Measure = measure, ProviderId = provider.ProviderProfileId })).Total);
            }
        Assert.Equal(report.Totals.Booked, (await List(None with { Measure = ActivityMeasures.Booked })).Total);
        Assert.Equal(report.Totals.VisitsCompleted, (await List(None with { Measure = ActivityMeasures.VisitsCompleted })).Total);
        var seenByPatel = await List(None with { Measure = ActivityMeasures.VisitsCompleted, ProviderId = S.ProviderB });
        Assert.Equal(new[] { _a2 }, seenByPatel.Rows.Select(r => r.Id));        // booked with Rivera, seen by Patel
        Assert.Equal(S.ProviderA, seenByPatel.Rows.Single().ProviderProfileId);
    }

    [Fact]
    public async Task Every_procedure_drill_total_equals_the_cell_and_pages_never_repeat_or_skip()
    {
        await ArrangeAsync();
        await using var db = Fixture.CreateContext();
        var filter = await RangeAsync(db);
        var report = (await Report(db).RunAsync(filter, S.Actor, default)).Data;
        foreach (var provider in report.Providers)
        {
            var page = await ProceduresDrill(db).ProceduresAsync(filter, new ProcedureDrillCriteria(provider.ProviderProfileId, null, null, null, null, null, null), S.Actor, default, ReportIds.ProviderActivity);
            Assert.Equal(provider.ProceduresCompleted, page.Total);
            Assert.Equal(ReportIds.ProviderActivity, page.ReportId);
        }

        var seen = new List<Guid>();
        ReportCursor? cursor = null;
        var pages = 0;
        do
        {
            var page = await AppointmentsDrill(db).AppointmentsAsync(filter, new ActivityDrillCriteria(ActivityMeasures.Booked, null, 2, cursor?.AfterStart, cursor?.AfterId), S.Actor, default);
            Assert.Equal(5, page.Total);
            seen.AddRange(page.Rows.Select(r => r.Id));
            cursor = page.Next;
            pages++;
        } while (cursor is not null);
        Assert.Equal(3, pages);
        Assert.Equal(new[] { _a1, _a2, _a3, _a4, _a5 }, seen);        // in start order, none repeated or skipped
    }

    [Fact]
    public async Task Bad_drill_criteria_get_stable_codes_and_each_run_is_recorded_or_the_report_refused()
    {
        await ArrangeAsync();
        await using (var db = Fixture.CreateContext())
        {
            var filter = await RangeAsync(db);
            async Task<string> Code(ActivityDrillCriteria c) => (await Assert.ThrowsAsync<ReportException>(() => AppointmentsDrill(db).AppointmentsAsync(filter, c, S.Actor, default))).Code;
            Assert.Equal("invalid_measure", await Code(None));
            Assert.Equal("invalid_measure", await Code(None with { Measure = "Seen" }));
            Assert.Equal("invalid_limit", await Code(None with { Measure = ActivityMeasures.Booked, Limit = 0 }));
            Assert.Equal("invalid_cursor", await Code(None with { Measure = ActivityMeasures.Booked, AfterId = Guid.NewGuid() }));
            Assert.Equal(0, await db.AuditLogEntries.CountAsync(e => e.EventType == AuditEventTypes.ReportViewed));

            await Report(db).RunAsync(filter, S.Actor, default);
            var entry = await db.AuditLogEntries.SingleAsync(e => e.EventType == AuditEventTypes.ReportViewed);
            Assert.StartsWith("Report provider-activity:", entry.Details);
            Assert.DoesNotContain("Ann", entry.Details);
        }

        await using var failing = new SchedulingTestSupport(Fixture).FailingAuditContext();
        var failingFilter = await RangeAsync(failing);
        var ex = await Assert.ThrowsAsync<ReportException>(() => Report(failing).RunAsync(failingFilter, S.Actor, default));
        Assert.Equal(("audit_unavailable", 503), (ex.Code, ex.StatusCode));
    }
}
