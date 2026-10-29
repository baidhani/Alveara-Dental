using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Alveara.Api.Architecture.Billing;
using Alveara.Api.Architecture.Clinical;
using Alveara.Api.Architecture.FollowUps;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Measurement;
using Alveara.Api.Architecture.Patients.Merge;
using Alveara.Api.Architecture.Reporting;
using Alveara.Api.Architecture.Scheduling;
using Alveara.Api.Data;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N006 slice 5a, the measured metrics against a real SQL Server. A practice is arranged so that every metric has a value that can be checked by hand: 4 of 12 booking requests refused for a conflict (events), 1 reversal among 3
/// ledger operations, 1 merge among 7 patients registered, 2 of 3 old clinical notes not finalized, 2 of 4 due follow-ups not done on time, and 2 cancellations and 1 no-show among 5 appointments. Those values are PINNED here on
/// purpose: if a change to the code moves one, the meaning of that metric changed and its definition version must be raised (a baseline taken under the old version could no longer be compared). A metric with nothing in its
/// denominator has no rate (never zero); the things the application cannot observe are listed with a reason and never given a number; a provider or location filter is refused; and every run is recorded or refused.
/// </summary>
public class MetricsTests : CompletionTestBase
{
    private static readonly DateTimeOffset AsOf = new(2030, 2, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly ReportOptions Wide = new() { MaxRangeDays = 5000 };

    private static MeasurementEvent Event(string name, string properties, DateTimeOffset? at = null) =>
        new() { Id = Guid.NewGuid(), EventName = name, SchemaVersion = 1, PropertiesJson = properties, OccurredAtUtc = at ?? DateTimeOffset.UtcNow };

    private static string Rejected(string category) => $"{{\"category\":\"{category}\",\"outcome\":\"failure\"}}";

    private FollowUpTask Task(string status, DateTimeOffset due, Action<FollowUpTask>? change = null)
    {
        var t = new FollowUpTask { Id = Guid.NewGuid(), PatientId = Ann, AssigneeUserId = Other, Title = "Call", DueAtUtc = due, Status = status, Kind = FollowUpKinds.Manual, CreatedByUserId = S.Actor, CreatedAtUtc = DateTimeOffset.UtcNow };
        change?.Invoke(t);
        return t;
    }

    private async Task ArrangeAsync()
    {
        // appointments: five bookings, two cancelled and one no-show
        var patients = new List<Guid>();
        foreach (var first in new[] { "Cy", "Di", "Ed", "Fy", "Gi" }) patients.Add(await S.PatientAsync(first, "Zz"));
        var ids = new List<Guid>();
        for (var i = 0; i < 5; i++) ids.Add((await S.ScheduleAsync(S.Request(patients[i], At(9 + i)))).Appointment.Id);
        await using (var db = Fixture.CreateContext())
        {
            foreach (var id in ids.Take(2)) await db.Appointments.Where(a => a.Id == id).ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, AppointmentStatuses.Cancelled).SetProperty(a => a.CancelReason, "x"));
            await db.Appointments.Where(a => a.Id == ids[2]).ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, AppointmentStatuses.NoShow));
        }

        // the ledger: on Cy's account two adjustments and a payment, the second adjustment and the payment reversed
        var account = await NewAccountAsync(patients[0]);
        var first1 = AdjustmentRow(account, 100m, 1, "a-1");
        var second = AdjustmentRow(account, 50m, 2, "a-2");
        var payment = PaymentRow(account, -30m, 3, "p-1");
        await AddRowsAsync(first1, second, payment);
        await AddRowsAsync(ReversalRow(account, second, 4, "r-1"));
        await AddRowsAsync(ReversalRow(account, payment, 5, "r-2"));                  // and the payment reversed: every kind of reversal that counts is pinned, so dropping one from the numerator changes a pinned value

        // measurement events: six bookings, six refusals (four are conflicts, one is not, one is not valid JSON), and one event outside the range
        var events = new List<MeasurementEvent>();
        for (var i = 0; i < 6; i++) events.Add(Event("appointment.scheduled", "{\"outcome\":\"success\"}"));
        foreach (var code in new[] { "provider_double_booked", "provider_double_booked", "patient_double_booked", "operatory_conflict", "start_in_past" }) events.Add(Event("appointment.rejected", Rejected(code)));
        events.Add(Event("appointment.rejected", "not json"));
        events.Add(Event("appointment.scheduled", "{\"outcome\":\"success\"}", new DateTimeOffset(2031, 1, 1, 0, 0, 0, TimeSpan.Zero)));
        await AddRowsAsync(events.Cast<object>().ToArray());

        // notes: the two drafts the base made, plus one finalized
        await AddRowsAsync(new Encounter { Id = Guid.NewGuid(), PatientId = patients[1], EncounterAtUtc = DateTimeOffset.UtcNow, Status = EncounterStatuses.Finalized, CreatedAtUtc = DateTimeOffset.UtcNow, FinalizedAtUtc = DateTimeOffset.UtcNow, FinalizedByUserId = S.Actor });

        // follow-ups due 2030-01-14 to 16, all past by the moment judged at: one open, one completed on time, one completed late, one cancelled
        static void Closed(FollowUpTask t, DateTimeOffset when, Guid by) { t.ClosedAtUtc = when; t.ClosedByUserId = by; }
        var due = new DateTimeOffset(2030, 1, 15, 16, 0, 0, TimeSpan.Zero);
        await AddRowsAsync(Task(FollowUpStatuses.Open, due), Task(FollowUpStatuses.Completed, due, t => Closed(t, due.AddHours(-1), S.Actor)), Task(FollowUpStatuses.Completed, due, t => Closed(t, due.AddHours(5), S.Actor)),
            Task(FollowUpStatuses.Cancelled, due, t => Closed(t, due.AddHours(-2), S.Actor)));

        // one merge: Ann into Bo (neither has a ledger)
        await WithDb(async db =>
        {
            var service = new PatientMergeService(db, Clock);
            var preview = await service.PreviewAsync(Ann, Bo, default);
            return await service.MergeAsync(S.Actor, new MergeRequest(Ann, Bo, "Same person registered twice", preview.SourceRowVersion, preview.SurvivorRowVersion, null, true, true), Guid.NewGuid().ToString("N"), default);
        });
    }

    private ReportFilterResolver Resolver(AlveraDbContext db) => new(db, Clock, Options.Create(Wide));

    private async Task<ReportFilter> RangeAsync(AlveraDbContext db, string from = "", string to = "2030-01-16", Guid[]? providers = null, Guid? location = null)
    {
        var yesterday = from.Length > 0 ? from : DateOnly.FromDateTime(Clock.ToPracticeLocal(DateTimeOffset.UtcNow).DateTime).AddDays(-1).ToString("yyyy-MM-dd");
        return await Resolver(db).ResolveAsync(new ReportFilterInput(yesterday, to, providers, location), default);
    }

    private MetricsService Metrics(AlveraDbContext db, ReportOptions? options = null) => new(db, Clock, Options.Create(options ?? Wide), NullLogger<MetricsService>.Instance);

    [Fact]
    public async Task Every_metric_has_the_value_that_can_be_checked_by_hand_and_these_are_pinned_to_the_definition_versions()
    {
        await ArrangeAsync();
        await using var db = Fixture.CreateContext();
        var result = await Metrics(db).RunAsync(await RangeAsync(db), S.Actor, default, AsOf);

        var m = result.Data.Metrics.ToDictionary(x => x.Key);
        Assert.Equal((4, 12, 0.3333m), (m[MetricKeys.SchedulingConflicts].NumeratorCount, m[MetricKeys.SchedulingConflicts].DenominatorCount, m[MetricKeys.SchedulingConflicts].Rate));
        Assert.Equal((2, 3, 0.6667m), (m[MetricKeys.BillingCorrections].NumeratorCount, m[MetricKeys.BillingCorrections].DenominatorCount, m[MetricKeys.BillingCorrections].Rate));
        Assert.Equal((1, 7, 0.1429m), (m[MetricKeys.DuplicateMerges].NumeratorCount, m[MetricKeys.DuplicateMerges].DenominatorCount, m[MetricKeys.DuplicateMerges].Rate));
        Assert.Equal((2, 3, 0.6667m), (m[MetricKeys.UnfinalizedNotes].NumeratorCount, m[MetricKeys.UnfinalizedNotes].DenominatorCount, m[MetricKeys.UnfinalizedNotes].Rate));
        Assert.Equal((2, 4, 0.5m), (m[MetricKeys.LateFollowUps].NumeratorCount, m[MetricKeys.LateFollowUps].DenominatorCount, m[MetricKeys.LateFollowUps].Rate));
        Assert.Equal((2, 5, 0.4m), (m[MetricKeys.CancellationRate].NumeratorCount, m[MetricKeys.CancellationRate].DenominatorCount, m[MetricKeys.CancellationRate].Rate));
        Assert.Equal((1, 5, 0.2m), (m[MetricKeys.NoShowRate].NumeratorCount, m[MetricKeys.NoShowRate].DenominatorCount, m[MetricKeys.NoShowRate].Rate));
        Assert.All(result.Data.Metrics, metric => Assert.Equal(1, metric.DefinitionVersion));        // raise a version when one of the pinned values above has to change because the METRIC means something else
        Assert.Equal(7, result.Data.Metrics.Count);
        Assert.Equal(("measurement", false, AsOf), (result.ReportId, result.NoData, result.Data.AsOfUtc));
    }

    [Fact]
    public void The_definitions_are_complete_the_keys_are_unique_and_the_unmeasurable_things_have_no_number()
    {
        Assert.Equal(MetricDefinitions.All.Count, MetricDefinitions.All.Select(d => d.Key).Distinct().Count());
        Assert.All(MetricDefinitions.All, d =>
        {
            Assert.True(d.Version >= 1);
            Assert.False(string.IsNullOrWhiteSpace(d.Name) || string.IsNullOrWhiteSpace(d.Numerator) || string.IsNullOrWhiteSpace(d.Denominator) || string.IsNullOrWhiteSpace(d.Source));
        });
        Assert.Equal(new[] { "duplicate_entry_time", "administrative_time", "nonclinical_overhead_reduction" }, MetricDefinitions.Unmeasurable.Select(u => u.Key));
        Assert.All(MetricDefinitions.Unmeasurable, u => Assert.False(string.IsNullOrWhiteSpace(u.Reason)));
        Assert.Contains("product target", MetricDefinitions.Unmeasurable.Single(u => u.Key == "nonclinical_overhead_reduction").Reason);
        Assert.Empty(typeof(NotMeasurable).GetProperties().Where(p => p.PropertyType == typeof(decimal) || p.PropertyType == typeof(decimal?) || p.PropertyType == typeof(int)));        // there is nowhere to put a number
        Assert.Contains("does not show what caused", MetricDefinitions.NoCausationNote);
        Assert.Empty(MetricDefinitions.ConflictCodes.Except(new[] { "provider_double_booked", "patient_double_booked", "operatory_conflict", "operatory_occupied" }));
    }

    [Fact]
    public async Task A_metric_with_nothing_in_its_denominator_has_no_rate_and_never_zero()
    {
        await ArrangeAsync();
        await using var db = Fixture.CreateContext();
        var empty = await Metrics(db).RunAsync(await RangeAsync(db, "2020-01-01", "2020-01-31"), S.Actor, default, AsOf);

        Assert.True(empty.NoData);
        Assert.All(empty.Data.Metrics, metric => Assert.Equal((0, null), (metric.DenominatorCount, metric.Rate)));

        var tooSoon = await Metrics(db).RunAsync(await RangeAsync(db), S.Actor, default, DateTimeOffset.UtcNow);        // judged now, the notes are less than a week old: not counted yet
        var notes = tooSoon.Data.Metrics.Single(x => x.Key == MetricKeys.UnfinalizedNotes);
        Assert.Equal((0, null), (notes.DenominatorCount, notes.Rate));
    }

    [Fact]
    public async Task Metrics_judged_at_a_moment_change_with_that_moment_and_the_others_do_not()
    {
        await ArrangeAsync();
        await using var db = Fixture.CreateContext();
        var early = (await Metrics(db).RunAsync(await RangeAsync(db), S.Actor, default, new DateTimeOffset(2030, 1, 15, 12, 0, 0, TimeSpan.Zero))).Data.Metrics.ToDictionary(x => x.Key);

        Assert.Equal((0, 0), (early[MetricKeys.LateFollowUps].NumeratorCount, early[MetricKeys.LateFollowUps].DenominatorCount));        // nothing was due yet at that moment
        Assert.Equal(0.3333m, early[MetricKeys.SchedulingConflicts].Rate);                                                              // an event metric does not depend on the moment
        Assert.Equal(0.4m, early[MetricKeys.CancellationRate].Rate);
    }

    [Fact]
    public async Task A_provider_or_location_filter_is_refused_and_nothing_is_recorded()
    {
        await ArrangeAsync();
        await using var db = Fixture.CreateContext();
        var withProvider = await RangeAsync(db, providers: [S.ProviderA]);
        var ex = await Assert.ThrowsAsync<ReportException>(() => Metrics(db).RunAsync(withProvider, S.Actor, default, AsOf));
        Assert.Equal(("filter_not_supported", 400), (ex.Code, ex.StatusCode));
        Assert.Equal(0, await db.AuditLogEntries.CountAsync(e => e.EventType == AuditEventTypes.ReportViewed));
    }

    [Fact]
    public async Task Each_run_is_recorded_a_failed_audit_save_refuses_it_and_too_many_refusal_events_are_refused()
    {
        await ArrangeAsync();
        await using (var db = Fixture.CreateContext())
        {
            await Metrics(db).RunAsync(await RangeAsync(db), S.Actor, default, AsOf);
            var entry = await db.AuditLogEntries.SingleAsync(e => e.EventType == AuditEventTypes.ReportViewed);
            Assert.StartsWith("Report measurement:", entry.Details);
            var small = new ReportOptions { MaxRangeDays = 5000, MaxSourceRows = 5 };
            var ex = await Assert.ThrowsAsync<ReportException>(async () => await Metrics(db, small).RunAsync(await RangeAsync(db), S.Actor, default, AsOf));        // six refusal events, limit five
            Assert.Equal(("report_too_large", 413), (ex.Code, ex.StatusCode));
        }

        await using var failing = new SchedulingTestSupport(Fixture).FailingAuditContext();
        var filter = await RangeAsync(failing);
        var failed = await Assert.ThrowsAsync<ReportException>(() => Metrics(failing).RunAsync(filter, S.Actor, default, AsOf));
        Assert.Equal(("audit_unavailable", 503), (failed.Code, failed.StatusCode));
    }
}
