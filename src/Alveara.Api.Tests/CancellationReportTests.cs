using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Reporting;
using Alveara.Api.Architecture.Scheduling;
using Alveara.Api.Data;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N006 slice 4, the cancellations and no-shows report and the appointments behind it, against a real SQL Server with appointments booked through the real scheduler. Seven appointments between 2030-01-14 and
/// 2030-01-16: Dr. Rivera has four (one kept, cancelled two hours ahead, cancelled three days ahead, one no-show), Dr. Patel three (one kept, cancelled ten days ahead, cancelled with no recorded time). The booked, cancelled
/// and no-show counts and the two rates add up by provider and by type; the cancellations are cut by how early they were made; a rate is unavailable when nothing was booked; the provider and location filters work; every
/// drill-down total equals its cell; pages never repeat or skip; bad criteria get stable codes; and the cancel reason is never in the answer.
/// </summary>
public class CancellationReportTests : CompletionTestBase
{
    private Guid _a1, _a2, _a3, _a4, _a5, _a6, _a7;

    private async Task ArrangeAsync()
    {
        async Task<Guid> Book(string first, DateTime start, Guid? provider = null, Guid? operatory = null, Guid? type = null) =>
            (await S.ScheduleAsync(S.Request(await S.PatientAsync(first, "Zz"), start, provider, operatory, type))).Appointment.Id;
        _a1 = await Book("Ann", At(9));
        _a2 = await Book("Bo", At(10, 30), type: S.Short30);
        _a3 = await Book("Cy", At(9, dayOffset: 1));
        _a4 = await Book("Di", At(11, dayOffset: 1), type: S.Short30);
        _a5 = await Book("Ed", At(9, dayOffset: 2), S.ProviderB, S.Op2);
        _a6 = await Book("Fy", At(10, 30, 2), S.ProviderB, S.Op2, S.Short30);
        _a7 = await Book("Gi", At(14, dayOffset: 2), S.ProviderB, S.Op2);
        await using var db = Fixture.CreateContext();
        var starts = await db.Appointments.AsNoTracking().ToDictionaryAsync(a => a.Id, a => a.StartUtc);
        async Task Cancel(Guid id, TimeSpan before, bool timed = true)
        {
            if (timed) await db.Appointments.Where(a => a.Id == id).ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, AppointmentStatuses.Cancelled).SetProperty(a => a.CancelReason, "Private reason for Bo").SetProperty(a => a.StatusChangedAtUtc, (DateTimeOffset?)(starts[id] - before)));
            else await db.Appointments.Where(a => a.Id == id).ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, AppointmentStatuses.Cancelled).SetProperty(a => a.CancelReason, "Private reason for Bo"));
        }
        await Cancel(_a2, TimeSpan.FromHours(2));
        await Cancel(_a3, TimeSpan.FromDays(3));
        await Cancel(_a6, TimeSpan.FromDays(10));
        await Cancel(_a7, TimeSpan.Zero, timed: false);
        await db.Appointments.Where(a => a.Id == _a4).ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, AppointmentStatuses.NoShow).SetProperty(a => a.StatusChangedAtUtc, (DateTimeOffset?)(starts[_a4] + TimeSpan.FromHours(1))));
    }

    private ReportFilterResolver Resolver(AlveraDbContext db) => new(db, Clock, Options.Create(new ReportOptions()));
    private Task<ReportFilter> RangeAsync(AlveraDbContext db, Guid[]? providers = null, Guid? location = null, string from = "2030-01-14") => Resolver(db).ResolveAsync(new ReportFilterInput(from, "2030-01-16", providers, location), default);
    private CancellationReportService Report(AlveraDbContext db) => new(db, Clock, Options.Create(new ReportOptions()), NullLogger<CancellationReportService>.Instance);
    private CancellationDrillService Drill(AlveraDbContext db) => new(db, Options.Create(new ReportOptions()), NullLogger<CancellationDrillService>.Instance);
    private static readonly CancellationDrillCriteria None = new(null, null, null, null, null, null, null);

    [Fact]
    public async Task Counts_and_rates_add_up_by_provider_and_by_type_and_the_cancellations_are_cut_by_how_early_they_were_made()
    {
        await ArrangeAsync();
        await using var db = Fixture.CreateContext();
        var result = await Report(db).RunAsync(await RangeAsync(db), S.Actor, default);

        var d = result.Data;
        Assert.Equal((7, 4, 1, 0.5714m, 0.1429m), (d.Totals.Booked, d.Totals.Cancelled, d.Totals.NoShow, d.Totals.CancellationRate, d.Totals.NoShowRate));
        Assert.Equal(new[] { ("Dr. Patel", 3, 2, 0), ("Dr. Rivera", 4, 1, 1) }.Select(x => x.Item1), d.Providers.Select(p => p.ProviderName));
        Assert.Equal(new[] { (3, 2, 0), (4, 2, 1) }, d.Providers.Select(p => (p.Counts.Booked, p.Counts.Cancelled, p.Counts.NoShow)));
        Assert.Equal(new[] { ("Exam", 4, 2, 0), ("Quick check", 3, 2, 1) }.Select(x => x.Item1), d.Types.Select(t => t.TypeName));
        Assert.Equal(new[] { (4, 2, 0), (3, 2, 1) }, d.Types.Select(t => (t.Counts.Booked, t.Counts.Cancelled, t.Counts.NoShow)));
        Assert.Equal(new[] { (LeadTimeBuckets.LessThan24h, 1), (LeadTimeBuckets.Within7Days, 1), (LeadTimeBuckets.MoreThan7Days, 1), (LeadTimeBuckets.Unknown, 1) }, d.LeadTimes.Select(l => (l.Bucket, l.Cancelled)));
        Assert.True(d.Reconciled);
        Assert.Equal(await S.CountAsync(), result.RecordCount);
        Assert.Equal(("cancellations", false), (result.ReportId, result.NoData));
    }

    [Fact]
    public async Task A_rate_is_unavailable_when_nothing_was_booked_and_never_zero()
    {
        await ArrangeAsync();
        await using var db = Fixture.CreateContext();
        var empty = await Resolver(db).ResolveAsync(new ReportFilterInput("2030-02-04", "2030-02-08", null, null), default);
        var result = await Report(db).RunAsync(empty, S.Actor, default);
        Assert.True(result.NoData);
        Assert.Equal((0, null, null), (result.Data.Totals.Booked, result.Data.Totals.CancellationRate, result.Data.Totals.NoShowRate));
        Assert.True(result.Data.Reconciled);
    }

    [Fact]
    public async Task The_provider_and_location_filters_narrow_the_report()
    {
        await ArrangeAsync();
        await using var db = Fixture.CreateContext();
        var patel = (await Report(db).RunAsync(await RangeAsync(db, [S.ProviderB]), S.Actor, default)).Data;
        Assert.Equal((3, 2, 0), (patel.Totals.Booked, patel.Totals.Cancelled, patel.Totals.NoShow));
        Assert.Equal(new[] { "Dr. Patel" }, patel.Providers.Select(p => p.ProviderName));
        Assert.True(patel.Reconciled);

        var here = (await Report(db).RunAsync(await RangeAsync(db, location: S.LocationId), S.Actor, default)).Data;
        Assert.Equal(7, here.Totals.Booked);
        var other = Guid.NewGuid();
        db.PracticeLocations.Add(new Alveara.Api.Architecture.Configuration.PracticeLocation { Id = other, Name = "Closed branch", IsActive = false, CreatedAtUtc = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        var none = (await Report(db).RunAsync(await RangeAsync(db, location: other), S.Actor, default));
        Assert.True(none.NoData);
        Assert.Null(none.Data.Totals.CancellationRate);        // nothing booked: the rate is unavailable
        Assert.Null(none.Data.Totals.NoShowRate);
    }

    [Fact]
    public async Task Every_drill_down_total_equals_its_cell_and_the_cancel_reason_is_never_returned()
    {
        await ArrangeAsync();
        await using var db = Fixture.CreateContext();
        var filter = await RangeAsync(db);
        var report = (await Report(db).RunAsync(filter, S.Actor, default)).Data;
        Task<ReportPage<ReportAppointmentRow>> List(CancellationDrillCriteria c) => Drill(db).AppointmentsAsync(filter, c, S.Actor, default);

        Assert.Equal(report.Totals.Booked, (await List(None with { Measure = CancellationMeasures.Booked })).Total);
        Assert.Equal(report.Totals.Cancelled, (await List(None with { Measure = CancellationMeasures.Cancelled })).Total);
        Assert.Equal(report.Totals.NoShow, (await List(None with { Measure = CancellationMeasures.NoShow })).Total);
        foreach (var p in report.Providers) Assert.Equal(p.Counts.Cancelled, (await List(None with { Measure = CancellationMeasures.Cancelled, ProviderId = p.ProviderProfileId })).Total);
        foreach (var t in report.Types) Assert.Equal(t.Counts.NoShow, (await List(None with { Measure = CancellationMeasures.NoShow, AppointmentTypeId = t.AppointmentTypeId })).Total);
        foreach (var l in report.LeadTimes) Assert.Equal(l.Cancelled, (await List(None with { Measure = CancellationMeasures.Cancelled, LeadTime = l.Bucket })).Total);
        Assert.Equal(new[] { _a2 }, (await List(None with { Measure = CancellationMeasures.Cancelled, LeadTime = LeadTimeBuckets.LessThan24h })).Rows.Select(r => r.Id));
        Assert.Equal(new[] { _a7 }, (await List(None with { Measure = CancellationMeasures.Cancelled, LeadTime = LeadTimeBuckets.Unknown })).Rows.Select(r => r.Id));
        Assert.DoesNotContain("Private reason", System.Text.Json.JsonSerializer.Serialize(await List(None with { Measure = CancellationMeasures.Cancelled })));
    }

    [Fact]
    public async Task Pages_never_repeat_or_skip_and_bad_criteria_get_stable_codes()
    {
        await ArrangeAsync();
        await using var db = Fixture.CreateContext();
        var filter = await RangeAsync(db);
        var seen = new List<Guid>();
        ReportCursor? cursor = null;
        var pages = 0;
        do
        {
            var page = await Drill(db).AppointmentsAsync(filter, None with { Measure = CancellationMeasures.Booked, Limit = 3, AfterStart = cursor?.AfterStart, AfterId = cursor?.AfterId }, S.Actor, default);
            seen.AddRange(page.Rows.Select(r => r.Id));
            cursor = page.Next;
            pages++;
        } while (cursor is not null);
        Assert.Equal(3, pages);
        Assert.Equal(new[] { _a1, _a2, _a3, _a4, _a5, _a6, _a7 }, seen);

        async Task<string> Code(CancellationDrillCriteria c) => (await Assert.ThrowsAsync<ReportException>(() => Drill(db).AppointmentsAsync(filter, c, S.Actor, default))).Code;
        Assert.Equal("invalid_measure", await Code(None));
        Assert.Equal("invalid_lead_time", await Code(None with { Measure = CancellationMeasures.Cancelled, LeadTime = "Soon" }));
        Assert.Equal("invalid_lead_time", await Code(None with { Measure = CancellationMeasures.NoShow, LeadTime = LeadTimeBuckets.Unknown }));
        Assert.Equal("invalid_limit", await Code(None with { Measure = CancellationMeasures.Booked, Limit = 501 }));
        Assert.Equal("invalid_cursor", await Code(None with { Measure = CancellationMeasures.Booked, AfterId = Guid.NewGuid() }));
    }

    [Fact]
    public async Task Each_run_is_recorded_and_a_failed_audit_save_refuses_the_report()
    {
        await ArrangeAsync();
        await using (var db = Fixture.CreateContext())
        {
            await Report(db).RunAsync(await RangeAsync(db), S.Actor, default);
            var entry = await db.AuditLogEntries.SingleAsync(e => e.EventType == AuditEventTypes.ReportViewed);
            Assert.StartsWith("Report cancellations:", entry.Details);
            Assert.DoesNotContain("Private reason", entry.Details);
        }

        await using var failing = new SchedulingTestSupport(Fixture).FailingAuditContext();
        var filter = await RangeAsync(failing);
        var ex = await Assert.ThrowsAsync<ReportException>(() => Report(failing).RunAsync(filter, S.Actor, default));
        Assert.Equal(("audit_unavailable", 503), (ex.Code, ex.StatusCode));
    }
}
