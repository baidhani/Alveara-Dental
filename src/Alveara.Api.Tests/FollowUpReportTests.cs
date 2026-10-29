using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Alveara.Api.Architecture.FollowUps;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Reporting;
using Alveara.Api.Data;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N006 slice 4, the recall and follow-up report and the tasks behind it, against a real SQL Server. Seven tasks are due between 2030-01-14 and 2030-01-16 and judged as of 2030-01-15 12:00 (practice time): five manual ones
/// (one overdue, one not yet due, one completed on time, one completed late, one cancelled) and two recall ones made by the system (one open and unassigned, one closed by the system). The five exclusive outcomes add up to the
/// seven; completed-late and open-unassigned are cuts; judging later turns not-yet-due tasks into overdue ones; a provider or location filter is refused; every drill-down total equals its cell; pages never repeat or skip;
/// bad criteria get stable codes; a task that is due outside the range is not counted; and a task's title is never in the answer.
/// </summary>
public class FollowUpReportTests : CompletionTestBase
{
    private static readonly DateTimeOffset AsOf = new(2030, 1, 15, 18, 0, 0, TimeSpan.Zero);
    private Guid _overdue, _notYet, _onTime, _late, _cancelled, _recallOpen, _recallSystem;

    private FollowUpTask Manual(string status, DateTimeOffset due, Action<FollowUpTask>? change = null)
    {
        var t = new FollowUpTask { Id = Guid.NewGuid(), PatientId = Ann, AssigneeUserId = Other, Title = "Call Ann about the quote", DueAtUtc = due, Status = status, Kind = FollowUpKinds.Manual, CreatedByUserId = S.Actor, CreatedAtUtc = DateTimeOffset.UtcNow };
        change?.Invoke(t);
        return t;
    }

    private FollowUpTask Recall(string status, DateTimeOffset due, Action<FollowUpTask>? change = null)
    {
        var t = new FollowUpTask { Id = Guid.NewGuid(), PatientId = Bo, AssigneeUserId = null, Title = "Recall due", DueAtUtc = due, Status = status, Kind = FollowUpKinds.Recall, CreatedByUserId = null, CreatedAtUtc = DateTimeOffset.UtcNow, SourceKey = $"recall:{Guid.NewGuid():N}:20300115" };
        change?.Invoke(t);
        return t;
    }

    private static void ClosedBy(FollowUpTask t, DateTimeOffset when, Guid? by, bool system = false) { t.ClosedAtUtc = when; t.ClosedByUserId = by; t.ClosedBySystem = system; }

    private async Task ArrangeAsync()
    {
        var tasks = new[]
        {
            Manual(FollowUpStatuses.Open, new(2030, 1, 14, 16, 0, 0, TimeSpan.Zero)),
            Manual(FollowUpStatuses.Open, new(2030, 1, 16, 16, 0, 0, TimeSpan.Zero)),
            Manual(FollowUpStatuses.Completed, new(2030, 1, 14, 16, 0, 0, TimeSpan.Zero), t => ClosedBy(t, new(2030, 1, 14, 15, 0, 0, TimeSpan.Zero), S.Actor)),
            Manual(FollowUpStatuses.Completed, new(2030, 1, 14, 16, 0, 0, TimeSpan.Zero), t => ClosedBy(t, new(2030, 1, 14, 20, 0, 0, TimeSpan.Zero), S.Actor)),
            Manual(FollowUpStatuses.Cancelled, new(2030, 1, 15, 14, 0, 0, TimeSpan.Zero), t => ClosedBy(t, new(2030, 1, 15, 13, 0, 0, TimeSpan.Zero), S.Actor)),
            Recall(FollowUpStatuses.Open, new(2030, 1, 15, 23, 0, 0, TimeSpan.Zero)),
            Recall(FollowUpStatuses.Completed, new(2030, 1, 15, 12, 0, 0, TimeSpan.Zero), t => ClosedBy(t, new(2030, 1, 15, 13, 0, 0, TimeSpan.Zero), null, system: true)),
            Manual(FollowUpStatuses.Open, new(2030, 1, 20, 16, 0, 0, TimeSpan.Zero)),        // due after the range
        };
        (_overdue, _notYet, _onTime, _late, _cancelled, _recallOpen, _recallSystem) = (tasks[0].Id, tasks[1].Id, tasks[2].Id, tasks[3].Id, tasks[4].Id, tasks[5].Id, tasks[6].Id);
        await AddRowsAsync(tasks.Cast<object>().ToArray());
    }

    private ReportFilterResolver Resolver(AlveraDbContext db) => new(db, Clock, Options.Create(new ReportOptions()));
    private Task<ReportFilter> RangeAsync(AlveraDbContext db, Guid[]? providers = null, Guid? location = null) => Resolver(db).ResolveAsync(new ReportFilterInput("2030-01-14", "2030-01-16", providers, location), default);
    private FollowUpReportService Report(AlveraDbContext db, ReportOptions? options = null) => new(db, Clock, Options.Create(options ?? new ReportOptions()), NullLogger<FollowUpReportService>.Instance);
    private FollowUpDrillService Drill(AlveraDbContext db) => new(db, Clock, Options.Create(new ReportOptions()), NullLogger<FollowUpDrillService>.Instance);
    private static readonly FollowUpDrillCriteria None = new(null, null, AsOf, null, null, null);

    private static (int, int, int, int, int, int, int, int) Tuple(FollowUpOutcomeCounts c) => (c.Due, c.Completed, c.Cancelled, c.ClosedBySystem, c.OpenNotYetDue, c.OpenOverdue, c.CompletedLate, c.OpenUnassigned);

    [Fact]
    public async Task Each_task_due_in_the_range_is_in_exactly_one_outcome_and_the_outcomes_add_up()
    {
        await ArrangeAsync();
        await using var db = Fixture.CreateContext();
        var result = await Report(db).RunAsync(await RangeAsync(db), S.Actor, default, AsOf);

        var d = result.Data;
        Assert.Equal((7, 2, 1, 1, 2, 1, 1, 1), Tuple(d.Totals));
        Assert.Equal(FollowUpOutcomes.Kinds, d.Kinds.Select(k => k.Kind));
        Assert.Equal((5, 2, 1, 0, 1, 1, 1, 0), Tuple(d.Kinds.Single(k => k.Kind == FollowUpKinds.Manual).Counts));
        Assert.Equal((2, 0, 0, 1, 1, 0, 0, 1), Tuple(d.Kinds.Single(k => k.Kind == FollowUpKinds.Recall).Counts));
        Assert.Equal((0, 0, 0, 0, 0, 0, 0, 0), Tuple(d.Kinds.Single(k => k.Kind == FollowUpKinds.LabCaseDue).Counts));
        Assert.Equal((AsOf, true, "follow-ups", false, 7), (d.AsOfUtc, d.Reconciled, result.ReportId, result.NoData, result.RecordCount));
    }

    [Fact]
    public async Task Judging_at_a_later_moment_turns_tasks_not_yet_due_into_overdue_ones_and_nothing_else_changes()
    {
        await ArrangeAsync();
        await using var db = Fixture.CreateContext();
        var later = (await Report(db).RunAsync(await RangeAsync(db), S.Actor, default, new DateTimeOffset(2030, 1, 17, 12, 0, 0, TimeSpan.Zero))).Data.Totals;

        Assert.Equal((7, 2, 1, 1, 0, 3, 1, 1), Tuple(later));        // the manual not-yet-due task and the open recall are now overdue
    }

    [Fact]
    public async Task A_provider_or_location_filter_is_refused_and_nothing_is_recorded()
    {
        await ArrangeAsync();
        await using var db = Fixture.CreateContext();
        var withProvider = await RangeAsync(db, [S.ProviderA]);
        var ex = await Assert.ThrowsAsync<ReportException>(() => Report(db).RunAsync(withProvider, S.Actor, default, AsOf));
        Assert.Equal(("filter_not_supported", 400), (ex.Code, ex.StatusCode));
        Assert.Equal("filter_not_supported", (await Assert.ThrowsAsync<ReportException>(() => Drill(db).TasksAsync(withProvider, None, S.Actor, default))).Code);
        Assert.Equal(0, await db.AuditLogEntries.CountAsync(e => e.EventType == AuditEventTypes.ReportViewed));
    }

    [Fact]
    public async Task A_range_with_no_task_due_says_so_and_still_reconciles()
    {
        await ArrangeAsync();
        await using var db = Fixture.CreateContext();
        var empty = await Resolver(db).ResolveAsync(new ReportFilterInput("2031-01-14", "2031-01-16", null, null), default);
        var result = await Report(db).RunAsync(empty, S.Actor, default, AsOf);

        Assert.True(result.NoData);
        Assert.Equal((0, 0, 0, 0, 0, 0, 0, 0), Tuple(result.Data.Totals));
        Assert.True(result.Data.Reconciled);
    }

    [Fact]
    public async Task Every_drill_down_total_equals_its_cell_and_a_task_title_is_never_returned()
    {
        await ArrangeAsync();
        await using var db = Fixture.CreateContext();
        var filter = await RangeAsync(db);
        var report = (await Report(db).RunAsync(filter, S.Actor, default, AsOf)).Data;
        Task<FollowUpReportPage> List(FollowUpDrillCriteria c) => Drill(db).TasksAsync(filter, c, S.Actor, default);

        Assert.Equal(report.Totals.Due, (await List(None)).Total);
        foreach (var (outcome, expected) in new[] { (FollowUpOutcomes.Completed, 2), (FollowUpOutcomes.Cancelled, 1), (FollowUpOutcomes.ClosedBySystem, 1), (FollowUpOutcomes.OpenNotYetDue, 2), (FollowUpOutcomes.OpenOverdue, 1), (FollowUpOutcomes.CompletedLate, 1), (FollowUpOutcomes.OpenUnassigned, 1) })
            Assert.Equal(expected, (await List(None with { Outcome = outcome })).Total);
        foreach (var kind in report.Kinds) Assert.Equal(kind.Counts.Due, (await List(None with { Kind = kind.Kind })).Total);
        Assert.Equal(new[] { _late }, (await List(None with { Outcome = FollowUpOutcomes.CompletedLate })).Rows.Select(r => r.Id));
        Assert.Equal(new[] { _recallOpen }, (await List(None with { Outcome = FollowUpOutcomes.OpenUnassigned })).Rows.Select(r => r.Id));
        Assert.Equal(new[] { _overdue }, (await List(None with { Outcome = FollowUpOutcomes.OpenOverdue })).Rows.Select(r => r.Id));
        Assert.Equal((AsOf, true), ((await List(None)).AsOfUtc, (await List(None with { Outcome = FollowUpOutcomes.ClosedBySystem })).Rows.Single().ClosedBySystem));
        Assert.DoesNotContain("Call Ann", System.Text.Json.JsonSerializer.Serialize(await List(None)));
    }

    [Fact]
    public async Task Pages_never_repeat_or_skip_a_task_and_bad_criteria_get_stable_codes()
    {
        await ArrangeAsync();
        await using var db = Fixture.CreateContext();
        var filter = await RangeAsync(db);
        var seen = new List<Guid>();
        FollowUpCursor? cursor = null;
        var pages = 0;
        do
        {
            var page = await Drill(db).TasksAsync(filter, None with { Limit = 3, AfterDueAtUtc = cursor?.AfterDueAtUtc, AfterId = cursor?.AfterId }, S.Actor, default);
            Assert.Equal(7, page.Total);
            seen.AddRange(page.Rows.Select(r => r.Id));
            cursor = page.Next;
            pages++;
        } while (cursor is not null);
        Assert.Equal((3, 7), (pages, seen.Distinct().Count()));

        async Task<string> Code(FollowUpDrillCriteria c) => (await Assert.ThrowsAsync<ReportException>(() => Drill(db).TasksAsync(filter, c, S.Actor, default))).Code;
        Assert.Equal("invalid_kind", await Code(None with { Kind = "Chore" }));
        Assert.Equal("invalid_outcome", await Code(None with { Outcome = "Lost" }));
        Assert.Equal("invalid_limit", await Code(None with { Limit = 0 }));
        Assert.Equal("invalid_cursor", await Code(None with { AfterId = Guid.NewGuid() }));
    }

    [Fact]
    public async Task Each_run_is_recorded_and_a_failed_audit_save_refuses_it_and_too_many_tasks_are_refused()
    {
        await ArrangeAsync();
        await using (var db = Fixture.CreateContext())
        {
            await Report(db).RunAsync(await RangeAsync(db), S.Actor, default, AsOf);
            var entry = await db.AuditLogEntries.SingleAsync(e => e.EventType == AuditEventTypes.ReportViewed);
            Assert.StartsWith("Report follow-ups:", entry.Details);
            Assert.DoesNotContain("Call Ann", entry.Details);
            var tooMany = await Assert.ThrowsAsync<ReportException>(async () => await Report(db, new ReportOptions { MaxSourceRows = 6 }).RunAsync(await RangeAsync(db), S.Actor, default, AsOf));
            Assert.Equal(("report_too_large", 413), (tooMany.Code, tooMany.StatusCode));
        }

        await using var failing = new SchedulingTestSupport(Fixture).FailingAuditContext();
        var filter = await RangeAsync(failing);
        var ex = await Assert.ThrowsAsync<ReportException>(() => Report(failing).RunAsync(filter, S.Actor, default, AsOf));
        Assert.Equal(("audit_unavailable", 503), (ex.Code, ex.StatusCode));
    }
}
