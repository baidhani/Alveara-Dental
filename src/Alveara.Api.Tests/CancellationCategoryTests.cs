using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Alveara.Api.Architecture.Reporting;
using Alveara.Api.Architecture.Scheduling;
using Alveara.Api.Data;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N021 A2 (OD-19) against real SQL Server: a cancellation carries a controlled category; "short notice" is derived (under 24 hours before the start, counted like the report counts); the
/// cancellations report cuts the cancelled appointments by category with their short-notice count, cancellations from before categories show as uncategorised, and the drill-down by category
/// totals equal the cells.
/// </summary>
public class CancellationCategoryTests : CompletionTestBase
{
    private ReportFilterResolver Resolver(AlveraDbContext db) => new(db, Clock, Options.Create(new ReportOptions()));
    private Task<ReportFilter> RangeAsync(AlveraDbContext db) => Resolver(db).ResolveAsync(new ReportFilterInput("2030-01-14", "2030-01-18", null, null), default);
    private CancellationReportService Report(AlveraDbContext db) => new(db, Clock, Options.Create(new ReportOptions()), NullLogger<CancellationReportService>.Instance);
    private CancellationDrillService Drill(AlveraDbContext db) => new(db, Options.Create(new ReportOptions()), NullLogger<CancellationDrillService>.Instance);

    private async Task<AppointmentView> BookAsync(string first, DateTime start) => (await S.ScheduleAsync(S.Request(await S.PatientAsync(first, "Zz"), start))).Appointment;

    /// <summary>Cancels <paramref name="a"/> through the real service, with the clock set <paramref name="before"/> its start.</summary>
    private Task<AppointmentView> CancelAsync(AppointmentView a, string category, TimeSpan before, string? note = null) =>
        S.ManageAsync(m => m.CancelAsync(a.Id, category, note, a.RowVersion, S.Actor, default), new TestClock(a.StartUtc - before));

    [Theory]
    [InlineData(120, true)]                 // two hours ahead
    [InlineData(24 * 60 - 1, true)]         // one minute under a day
    [InlineData(24 * 60, false)]            // exactly a day ahead is not short notice
    [InlineData(3 * 24 * 60, false)]
    public async Task Short_notice_is_derived_from_how_long_before_the_start_the_appointment_was_cancelled(int minutesBefore, bool shortNotice)
    {
        var a = await BookAsync("Ann", At(9, dayOffset: 3));

        var cancelled = await CancelAsync(a, AppointmentCancelCategories.Office, TimeSpan.FromMinutes(minutesBefore));

        Assert.Equal((AppointmentCancelCategories.Office, (bool?)shortNotice), (cancelled.CancelCategory, cancelled.ShortNotice));
    }

    [Fact]
    public async Task An_appointment_that_is_not_cancelled_or_was_cancelled_with_no_recorded_time_has_no_short_notice_value()
    {
        var kept = await BookAsync("Ann", At(9));
        Assert.Null(kept.ShortNotice);

        var legacy = await BookAsync("Bo", At(11));
        await using (var db = Fixture.CreateContext())
            await db.Appointments.Where(x => x.Id == legacy.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, AppointmentStatuses.Cancelled).SetProperty(x => x.CancelReason, "Old free text"));
        var view = await S.ReloadAsync(legacy.Id);
        Assert.Equal((null, null), (view.CancelCategory, view.ShortNotice));   // shown as "Not categorised"
    }

    [Fact]
    public async Task Every_category_on_the_list_is_accepted()
    {
        var hour = 8;
        foreach (var category in AppointmentCancelCategories.All)
        {
            var a = await BookAsync($"P{hour}", At(hour++, dayOffset: 1));
            Assert.Equal(category, (await CancelAsync(a, category, TimeSpan.FromDays(1))).CancelCategory);
        }
    }

    [Fact]
    public async Task The_report_cuts_cancellations_by_category_with_short_notice_and_the_drill_down_by_category_equals_each_cell()
    {
        var office = await CancelAsync(await BookAsync("Ann", At(9)), AppointmentCancelCategories.Office, TimeSpan.FromHours(2));
        var patient = await CancelAsync(await BookAsync("Bo", At(10, 30)), AppointmentCancelCategories.Patient, TimeSpan.FromDays(3), "Private reason");
        var patientLate = await CancelAsync(await BookAsync("Cy", At(9, dayOffset: 1)), AppointmentCancelCategories.Patient, TimeSpan.FromHours(5));
        var legacy = await BookAsync("Di", At(11, dayOffset: 1));
        await BookAsync("Ed", At(14, dayOffset: 1));   // kept: not in any category row
        await using (var setup = Fixture.CreateContext())
            await setup.Appointments.Where(x => x.Id == legacy.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, AppointmentStatuses.Cancelled).SetProperty(x => x.StatusChangedAtUtc, (DateTimeOffset?)(legacy.StartUtc - TimeSpan.FromHours(1))));

        await using var db = Fixture.CreateContext();
        var data = (await Report(db).RunAsync(await RangeAsync(db), S.Actor, default)).Data;

        Assert.Equal(
            [("patient", 2, 1), ("office", 1, 1), ("provider", 0, 0), ("weather", 0, 0), ("other", 0, 0), ("uncategorised", 1, 1)],
            data.Categories!.Select(c => (c.Category, c.Cancelled, c.ShortNotice)));
        Assert.Equal(4, data.Totals.Cancelled);
        Assert.True(data.Reconciled);
        // the short-notice counts agree with the report's own under-24-hours bucket and with each appointment's derived flag
        Assert.Equal(data.LeadTimes.Single(l => l.Bucket == LeadTimeBuckets.LessThan24h).Cancelled, data.Categories!.Sum(c => c.ShortNotice));
        Assert.Equal((true, false, true), (office.ShortNotice, patient.ShortNotice, patientLate.ShortNotice));

        var filter = await RangeAsync(db);
        async Task<IReadOnlyList<Guid>> DrillAsync(string category) =>
            (await Drill(db).AppointmentsAsync(filter, new(CancellationMeasures.Cancelled, null, null, null, null, null, null, category), S.Actor, default)).Rows.Select(r => r.Id).ToList();
        foreach (var row in data.Categories!)
            Assert.Equal(row.Cancelled, (await DrillAsync(row.Category)).Count);
        Assert.Equal([office.Id], await DrillAsync(AppointmentCancelCategories.Office));
        Assert.Equal([legacy.Id], await DrillAsync(AppointmentCancelCategories.Uncategorised));
    }

    [Theory]
    [InlineData(CancellationMeasures.Cancelled, "Office")]
    [InlineData(CancellationMeasures.Cancelled, "illness")]
    [InlineData(CancellationMeasures.Booked, "office")]
    [InlineData(CancellationMeasures.NoShow, "uncategorised")]
    public async Task A_drill_down_by_an_unknown_category_or_for_something_other_than_cancellations_is_refused(string measure, string category)
    {
        await using var db = Fixture.CreateContext();
        var ex = await Assert.ThrowsAsync<ReportException>(async () => await Drill(db).AppointmentsAsync(await RangeAsync(db), new(measure, null, null, null, null, null, null, category), S.Actor, default));
        Assert.Equal(("invalid_category", 400), (ex.Code, ex.StatusCode));
    }
}
