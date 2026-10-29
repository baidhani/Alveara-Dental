using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Alveara.Api.Architecture.FollowUps;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Measurement;
using Alveara.Api.Architecture.Reporting;
using Alveara.Api.Data;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N006 slice 5b, baselines and comparison against a real SQL Server. A baseline stores every metric of a completed period with its definition version; the database refuses to change or delete a baseline or a stored metric;
/// the same key with the same request returns the same baseline (also when two identical requests arrive together) and with a different request is refused; a period that is not over, a missing key or a bad name is refused;
/// a comparison shows a change ONLY when the baseline period ended before this one started, the definition version is the same and both samples reach the minimum, and otherwise shows its own value, the reason and no change at
/// all; a valid change says Lower or Higher, never improved; a baseline rate of zero gives no relative change; no baseline at all is said for every metric; and a failed audit save refuses the work and stores nothing.
/// March 2025 is the baseline (conflicts 2 of 10, follow-ups 1 of 4) and April 2025 the period compared (4 of 10, 3 of 4); the minimum sample is lowered to 3 in those tests so that the small fixture can be compared, and the
/// default of 30 is shown to refuse the same data.
/// </summary>
public class MeasurementBaselineTests : CompletionTestBase
{
    private static readonly ReportOptions Small = new() { MinimumComparisonSample = 3 };

    private static MeasurementEvent Event(string name, string properties, DateTimeOffset at) => new() { Id = Guid.NewGuid(), EventName = name, SchemaVersion = 1, PropertiesJson = properties, OccurredAtUtc = at };
    private static string Rejected(string category) => $"{{\"category\":\"{category}\",\"outcome\":\"failure\"}}";

    private FollowUpTask Task(string status, DateTimeOffset due, Action<FollowUpTask>? change = null)
    {
        var t = new FollowUpTask { Id = Guid.NewGuid(), PatientId = Ann, AssigneeUserId = Other, Title = "Call", DueAtUtc = due, Status = status, Kind = FollowUpKinds.Manual, CreatedByUserId = S.Actor, CreatedAtUtc = due.AddDays(-10) };
        change?.Invoke(t);
        return t;
    }

    private async Task ArrangeAsync()
    {
        var march = new DateTimeOffset(2025, 3, 15, 15, 0, 0, TimeSpan.Zero);
        var april = new DateTimeOffset(2025, 4, 15, 15, 0, 0, TimeSpan.Zero);
        var rows = new List<object>();
        for (var i = 0; i < 6; i++) rows.Add(Event("appointment.scheduled", "{\"outcome\":\"success\"}", march));
        foreach (var code in new[] { "provider_double_booked", "provider_double_booked", "start_in_past", "start_in_past" }) rows.Add(Event("appointment.rejected", Rejected(code), march));
        for (var i = 0; i < 5; i++) rows.Add(Event("appointment.scheduled", "{\"outcome\":\"success\"}", april));
        foreach (var code in new[] { "provider_double_booked", "provider_double_booked", "patient_double_booked", "operatory_conflict", "start_in_past" }) rows.Add(Event("appointment.rejected", Rejected(code), april));
        FollowUpTask Done(DateTimeOffset due) => Task(FollowUpStatuses.Completed, due, t => { t.ClosedAtUtc = due.AddHours(-1); t.ClosedByUserId = S.Actor; });
        rows.AddRange([Task(FollowUpStatuses.Open, march), Done(march), Done(march), Done(march), Task(FollowUpStatuses.Open, april), Task(FollowUpStatuses.Open, april), Task(FollowUpStatuses.Open, april), Done(april)]);
        await AddRowsAsync(rows.ToArray());
    }

    private ReportFilterResolver Resolver(AlveraDbContext db, ReportOptions? options = null) => new(db, Clock, Options.Create(options ?? new ReportOptions()));
    private MetricsService Metrics(AlveraDbContext db, ReportOptions? options = null) => new(db, Clock, Options.Create(options ?? new ReportOptions()), NullLogger<MetricsService>.Instance);
    private MeasurementBaselineService Baselines(AlveraDbContext db) => new(db, Resolver(db), Metrics(db), Clock, NullLogger<MeasurementBaselineService>.Instance);
    private MeasurementComparisonService Compare(AlveraDbContext db, ReportOptions? options = null) => new(db, Metrics(db, options), Clock, Options.Create(options ?? new ReportOptions()), NullLogger<MeasurementComparisonService>.Instance);
    private static readonly SetBaselineInput March = new("March 2025", "2025-03-01", "2025-03-31");

    private async Task<ReportFilter> PeriodAsync(AlveraDbContext db, string from, string to) => await Resolver(db).ResolveAsync(new ReportFilterInput(from, to, null, null), default);

    private async Task<string> CodeAsync(Func<Task> action) => (await Assert.ThrowsAsync<ReportException>(action)).Code;

    // ---------- setting and reading a baseline ----------

    [Fact]
    public async Task A_baseline_stores_every_metric_of_a_completed_period_with_its_definition_version_and_is_audited_without_its_name()
    {
        await ArrangeAsync();
        await using var db = Fixture.CreateContext();
        var result = await Baselines(db).SetAsync("key-1", March, S.Actor, default);

        Assert.True(result.Created);
        var b = result.Baseline;
        Assert.Equal(("March 2025", "2025-03-01", "2025-03-31", S.Actor), (b.Name, b.From, b.To, b.SetByUserId));
        Assert.Equal(MetricDefinitions.All.Select(m => m.Key).Order(), b.Metrics.Select(m => m.MetricKey));
        Assert.All(b.Metrics, m => Assert.Equal(1, m.DefinitionVersion));
        var conflicts = b.Metrics.Single(m => m.MetricKey == MetricKeys.SchedulingConflicts);
        Assert.Equal((2, 10, 0.2m), (conflicts.NumeratorCount, conflicts.DenominatorCount, conflicts.Rate));
        var followUps = b.Metrics.Single(m => m.MetricKey == MetricKeys.LateFollowUps);
        Assert.Equal((1, 4, 0.25m), (followUps.NumeratorCount, followUps.DenominatorCount, followUps.Rate));
        Assert.Null(b.Metrics.Single(m => m.MetricKey == MetricKeys.CancellationRate).Rate);        // nothing was booked: no rate, not zero

        var entry = await db.AuditLogEntries.SingleAsync(e => e.EventType == AuditEventTypes.ReportBaselineSet);
        Assert.Equal(b.Id, entry.CorrelationId);
        Assert.Contains("2025-03-01 to 2025-03-31", entry.Details);
        Assert.DoesNotContain("March 2025", entry.Details);
        Assert.Equal(b.Id, (await Baselines(db).GetAsync(b.Id, default)).Id);
        Assert.Equal(new[] { b.Id }, (await Baselines(db).ListAsync(null, default)).Select(x => x.Id));
        Assert.Equal("baseline_not_found", await CodeAsync(() => Baselines(db).GetAsync(Guid.NewGuid(), default)));
    }

    [Fact]
    public async Task The_database_refuses_to_change_or_delete_a_baseline_or_a_stored_metric()
    {
        await ArrangeAsync();
        await using var db = Fixture.CreateContext();
        var id = (await Baselines(db).SetAsync("key-1", March, S.Actor, default)).Baseline.Id;

        async Task<int> ErrorAsync(string sql)
        {
            await using var check = Fixture.CreateContext();
            return (await Assert.ThrowsAnyAsync<Exception>(() => check.Database.ExecuteSqlRawAsync(sql, new SqlParameter("id", id)))) is { } ex ? FindNumber(ex) : 0;
        }
        Assert.Equal(51270, await ErrorAsync("UPDATE [ReportBaselines] SET [Name] = N'changed' WHERE [Id] = @id"));
        Assert.Equal(547, await ErrorAsync("DELETE FROM [ReportBaselines] WHERE [Id] = @id"));        // refused first by the foreign key from its stored metrics
        var bare = new ReportBaseline { Id = Guid.NewGuid(), Name = "Bare", PeriodFrom = new(2025, 2, 1), PeriodTo = new(2025, 2, 28), PeriodStartUtc = new(2025, 2, 1, 6, 0, 0, TimeSpan.Zero), PeriodEndUtc = new(2025, 3, 1, 6, 0, 0, TimeSpan.Zero),
            TimeZoneId = "America/Chicago", AsOfUtc = new(2025, 3, 2, 0, 0, 0, TimeSpan.Zero), IdempotencyKey = "bare", SetByUserId = S.Actor, SetAtUtc = new(2025, 3, 2, 0, 0, 0, TimeSpan.Zero) };
        await AddRowsAsync(bare);
        id = bare.Id;
        Assert.Equal(51271, await ErrorAsync("DELETE FROM [ReportBaselines] WHERE [Id] = @id"));        // with no metrics to stop it, the trigger refuses the delete
        id = (await db.ReportBaselines.AsNoTracking().Where(b => b.IdempotencyKey == "key-1").Select(b => b.Id).SingleAsync());
        Assert.Equal(51272, await ErrorAsync("UPDATE [ReportBaselineMetrics] SET [NumeratorCount] = 99 WHERE [BaselineId] = @id"));
        Assert.Equal(51273, await ErrorAsync("DELETE FROM [ReportBaselineMetrics] WHERE [BaselineId] = @id"));
        Assert.Equal(7, await db.ReportBaselineMetrics.CountAsync(m => m.BaselineId == id));
    }

    private static int FindNumber(Exception ex) => ex is SqlException s ? s.Number : ex.InnerException is null ? 0 : FindNumber(ex.InnerException);

    [Fact]
    public async Task The_same_key_and_request_returns_the_same_baseline_a_different_request_is_refused_and_two_at_once_make_one()
    {
        await ArrangeAsync();
        await using var db = Fixture.CreateContext();
        var first = await Baselines(db).SetAsync("key-1", March, S.Actor, default);
        var again = await Baselines(db).SetAsync("key-1", March with { Name = "  March 2025 " }, S.Actor, default);
        Assert.Equal((first.Baseline.Id, false), (again.Baseline.Id, again.Created));
        Assert.Equal("key_reused", await CodeAsync(() => Baselines(db).SetAsync("key-1", March with { To = "2025-03-30" }, S.Actor, default)));
        Assert.Equal("key_reused", await CodeAsync(() => Baselines(db).SetAsync("key-1", March with { Name = "Another" }, S.Actor, default)));
        Assert.Equal(1, await db.ReportBaselines.CountAsync());

        var results = await System.Threading.Tasks.Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            await using var own = Fixture.CreateContext();
            return await Baselines(own).SetAsync("key-race", new SetBaselineInput("April 2025", "2025-04-01", "2025-04-30"), S.Actor, default);
        }));
        Assert.Single(results.Select(r => r.Baseline.Id).Distinct());
        Assert.Equal(1, results.Count(r => r.Created));
        Assert.Equal(2, await db.ReportBaselines.CountAsync());
        Assert.Equal(1, await db.AuditLogEntries.CountAsync(e => e.EventType == AuditEventTypes.ReportBaselineSet && e.Details.Contains("2025-04-01")));
    }

    [Fact]
    public async Task A_period_that_is_not_over_a_missing_key_or_a_bad_name_or_range_is_refused_and_nothing_is_stored()
    {
        await using var db = Fixture.CreateContext();
        var today = DateOnly.FromDateTime(Clock.ToPracticeLocal(DateTimeOffset.UtcNow).DateTime);
        Task Set(string? key, SetBaselineInput input) => Baselines(db).SetAsync(key, input, S.Actor, default);

        Assert.Equal("period_not_complete", await CodeAsync(() => Set("k", new("Now", today.AddDays(-3).ToString("yyyy-MM-dd"), today.ToString("yyyy-MM-dd")))));
        Assert.Equal("period_not_complete", await CodeAsync(() => Set("k", new("Future", "2099-01-01", "2099-01-31"))));
        Assert.Equal("idempotency_key_required", await CodeAsync(() => Set(null, March)));
        Assert.Equal("idempotency_key_required", await CodeAsync(() => Set("  ", March)));
        Assert.Equal("idempotency_key_too_long", await CodeAsync(() => Set(new string('k', 65), March)));
        Assert.Equal("name_required", await CodeAsync(() => Set("k", March with { Name = " " })));
        Assert.Equal("name_too_long", await CodeAsync(() => Set("k", March with { Name = new string('n', 101) })));
        Assert.Equal("invalid_date", await CodeAsync(() => Set("k", March with { From = "March" })));
        Assert.Equal("invalid_range", await CodeAsync(() => Set("k", March with { From = "2025-03-31", To = "2025-03-01" })));
        Assert.Equal("range_too_large", await CodeAsync(() => Set("k", new("Long", "2023-01-01", "2024-12-31"))));
        Assert.Equal(0, await db.ReportBaselines.CountAsync());
        Assert.Equal(0, await db.AuditLogEntries.CountAsync(e => e.EventType == AuditEventTypes.ReportBaselineSet));
    }

    [Fact]
    public async Task A_failed_audit_save_refuses_the_baseline_and_stores_nothing()
    {
        await ArrangeAsync();
        await using var failing = new SchedulingTestSupport(Fixture).FailingAuditContext();
        var ex = await Assert.ThrowsAsync<ReportException>(() => Baselines(failing).SetAsync("key-1", March, S.Actor, default));

        Assert.Equal(("save_failed", 503), (ex.Code, ex.StatusCode));
        await using var check = Fixture.CreateContext();
        Assert.Equal((0, 0), (await check.ReportBaselines.CountAsync(), await check.ReportBaselineMetrics.CountAsync()));
    }

    // ---------- comparison ----------

    [Fact]
    public async Task With_no_baseline_every_metric_says_so_and_shows_no_change()
    {
        await ArrangeAsync();
        await using var db = Fixture.CreateContext();
        var result = await Compare(db, Small).RunAsync(await PeriodAsync(db, "2025-04-01", "2025-04-30"), null, S.Actor, default);

        Assert.Null(result.Data.Baseline);
        Assert.All(result.Data.Comparisons, c => { Assert.Equal(ComparisonStatus.NoBaseline, c.Status); Assert.Null(c.Baseline); Assert.Equal((null, null, null), (c.AbsoluteChange, c.RelativeChange, c.Movement)); });
        Assert.Equal(7, result.Data.Comparisons.Count);
        Assert.Equal("baseline_not_found", await CodeAsync(() => Compare(db, Small).RunAsync(PeriodAsync(db, "2025-04-01", "2025-04-30").Result, Guid.NewGuid(), S.Actor, default)));
    }

    [Fact]
    public async Task A_valid_comparison_shows_lower_or_higher_and_the_change_and_never_the_word_improved()
    {
        await ArrangeAsync();
        await using var db = Fixture.CreateContext();
        var baseline = (await Baselines(db).SetAsync("key-1", March, S.Actor, default)).Baseline;
        var result = await Compare(db, Small).RunAsync(await PeriodAsync(db, "2025-04-01", "2025-04-30"), null, S.Actor, default);

        Assert.Equal(baseline.Id, result.Data.Baseline!.Id);
        var c = result.Data.Comparisons.ToDictionary(x => x.Key);
        var conflicts = c[MetricKeys.SchedulingConflicts];
        Assert.Equal((ComparisonStatus.Comparable, 0.2m, 0.4m, 0.2m, 1.0m, "Higher"), (conflicts.Status, conflicts.Baseline!.Rate, conflicts.Current.Rate, conflicts.AbsoluteChange, conflicts.RelativeChange, conflicts.Movement));
        Assert.Equal((10, 10), (conflicts.Baseline.DenominatorCount, conflicts.Current.DenominatorCount));
        var followUps = c[MetricKeys.LateFollowUps];
        Assert.Equal((ComparisonStatus.Comparable, 0.25m, 0.75m, 0.5m, 2.0m, "Higher"), (followUps.Status, followUps.Baseline!.Rate, followUps.Current.Rate, followUps.AbsoluteChange, followUps.RelativeChange, followUps.Movement));
        Assert.Contains("does not show what caused", result.Data.Note);
        Assert.DoesNotContain("mprov", System.Text.Json.JsonSerializer.Serialize(result.Data.Comparisons.Select(x => (x.Status, x.Message, x.Movement))));        // no "improved" / "improvement" anywhere in what is returned
        Assert.Equal(("measurement-comparison", 3), (result.ReportId, result.Data.MinimumSample));
    }

    [Fact]
    public async Task A_metric_that_cannot_honestly_be_compared_gives_its_reason_and_no_change_at_all()
    {
        await ArrangeAsync();
        await using var db = Fixture.CreateContext();
        await Baselines(db).SetAsync("key-1", March, S.Actor, default);
        var result = await Compare(db, Small).RunAsync(await PeriodAsync(db, "2025-04-01", "2025-04-30"), null, S.Actor, default);

        var notComparable = result.Data.Comparisons.Where(c => c.Status != ComparisonStatus.Comparable).ToList();
        Assert.Equal(5, notComparable.Count);
        Assert.All(notComparable, c =>
        {
            Assert.Equal(ComparisonStatus.BaselineSampleTooSmall, c.Status);        // nothing was recorded for these in March
            Assert.Equal((null, null, null), (c.AbsoluteChange, c.RelativeChange, c.Movement));
            Assert.False(string.IsNullOrWhiteSpace(c.Message));
        });

        var defaultMinimum = await Compare(db).RunAsync(await PeriodAsync(db, "2025-04-01", "2025-04-30"), null, S.Actor, default);        // the same data with the default minimum of 30
        Assert.All(defaultMinimum.Data.Comparisons, c => { Assert.Equal(ComparisonStatus.BaselineSampleTooSmall, c.Status); Assert.Null(c.Movement); });
        Assert.Equal(30, defaultMinimum.Data.MinimumSample);
    }

    [Fact]
    public async Task A_small_current_sample_a_changed_definition_and_an_overlapping_period_each_refuse_the_comparison_for_their_own_reason()
    {
        await ArrangeAsync();
        await using var db = Fixture.CreateContext();
        await Baselines(db).SetAsync("key-1", March, S.Actor, default);

        var thin = await Compare(db, Small).RunAsync(await PeriodAsync(db, "2025-05-01", "2025-05-31"), null, S.Actor, default);        // nothing in May
        Assert.Equal(ComparisonStatus.CurrentSampleTooSmall, thin.Data.Comparisons.Single(c => c.Key == MetricKeys.SchedulingConflicts).Status);
        Assert.Null(thin.Data.Comparisons.Single(c => c.Key == MetricKeys.SchedulingConflicts).AbsoluteChange);

        var overlap = await Compare(db, Small).RunAsync(await PeriodAsync(db, "2025-03-15", "2025-04-30"), null, S.Actor, default);
        Assert.All(overlap.Data.Comparisons, c => { Assert.Equal(ComparisonStatus.BaselineNotBefore, c.Status); Assert.Null(c.Movement); });

        var other = new ReportBaseline { Id = Guid.NewGuid(), Name = "Old definition", PeriodFrom = new(2025, 2, 1), PeriodTo = new(2025, 2, 28), PeriodStartUtc = new(2025, 2, 1, 6, 0, 0, TimeSpan.Zero), PeriodEndUtc = new(2025, 3, 1, 6, 0, 0, TimeSpan.Zero),
            TimeZoneId = "America/Chicago", AsOfUtc = new(2025, 3, 2, 0, 0, 0, TimeSpan.Zero), IdempotencyKey = "old", SetByUserId = S.Actor, SetAtUtc = new(2025, 3, 2, 0, 0, 0, TimeSpan.Zero),
            Metrics = [new ReportBaselineMetric { Id = Guid.NewGuid(), MetricKey = MetricKeys.SchedulingConflicts, DefinitionVersion = 2, NumeratorCount = 5, DenominatorCount = 10, Rate = 0.5m }] };
        await AddRowsAsync(other);
        var changed = await Compare(db, Small).RunAsync(await PeriodAsync(db, "2025-04-01", "2025-04-30"), other.Id, S.Actor, default);
        var conflicts = changed.Data.Comparisons.Single(c => c.Key == MetricKeys.SchedulingConflicts);
        Assert.Equal((ComparisonStatus.DefinitionChanged, null, null, null), (conflicts.Status, conflicts.AbsoluteChange, conflicts.RelativeChange, conflicts.Movement));
        Assert.Equal(2, conflicts.Baseline!.DefinitionVersion);
        Assert.Equal(1, conflicts.Current.DefinitionVersion);
    }

    [Fact]
    public async Task A_baseline_rate_of_zero_gives_no_relative_change_but_still_the_absolute_one()
    {
        await ArrangeAsync();
        await using var db = Fixture.CreateContext();
        await AddRowsAsync(Enumerable.Range(0, 4).Select(_ => Event("appointment.scheduled", "{\"outcome\":\"success\"}", new DateTimeOffset(2025, 2, 10, 15, 0, 0, TimeSpan.Zero))).Cast<object>().ToArray());
        await Baselines(db).SetAsync("zero", new SetBaselineInput("February 2025", "2025-02-01", "2025-02-28"), S.Actor, default);        // 0 conflicts of 4 requests
        var result = await Compare(db, Small).RunAsync(await PeriodAsync(db, "2025-04-01", "2025-04-30"), null, S.Actor, default);

        var conflicts = result.Data.Comparisons.Single(c => c.Key == MetricKeys.SchedulingConflicts);
        Assert.Equal((ComparisonStatus.Comparable, 0.4m, null, "Higher"), (conflicts.Status, conflicts.AbsoluteChange, conflicts.RelativeChange, conflicts.Movement));
    }

    [Fact]
    public async Task A_comparison_is_recorded_a_provider_filter_is_refused_and_a_failed_audit_save_refuses_it()
    {
        await ArrangeAsync();
        await using (var db = Fixture.CreateContext())
        {
            await Compare(db, Small).RunAsync(await PeriodAsync(db, "2025-04-01", "2025-04-30"), null, S.Actor, default);
            Assert.StartsWith("Report measurement-comparison:", (await db.AuditLogEntries.SingleAsync(e => e.EventType == AuditEventTypes.ReportViewed)).Details);
            var withProvider = await Resolver(db).ResolveAsync(new ReportFilterInput("2025-04-01", "2025-04-30", [S.ProviderA], null), default);
            Assert.Equal("filter_not_supported", await CodeAsync(() => Compare(db, Small).RunAsync(withProvider, null, S.Actor, default)));
        }

        await using var failing = new SchedulingTestSupport(Fixture).FailingAuditContext();
        var filter = await PeriodAsync(failing, "2025-04-01", "2025-04-30");
        var ex = await Assert.ThrowsAsync<ReportException>(() => Compare(failing, Small).RunAsync(filter, null, S.Actor, default));
        Assert.Equal(("audit_unavailable", 503), (ex.Code, ex.StatusCode));
    }
}
