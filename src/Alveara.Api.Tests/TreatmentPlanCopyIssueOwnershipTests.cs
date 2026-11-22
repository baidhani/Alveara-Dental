using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Alveara.Api.Architecture.Clinical;
using Alveara.Api.Architecture.Patients.Merge;
using Alveara.Api.Architecture.Treatment;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N026 S7 (issue-row ownership decision): a treatment-plan copy issue carries no PatientId and is owned through its plan. The database refuses any change or deletion of an issue (51307), and after an
/// approved merge of the plan's patient the issue - untouched - resolves to the surviving patient through the plan only, and its content still rebuilds (diagnosis labels are read by diagnosis id).
/// </summary>
public class TreatmentPlanCopyIssueOwnershipTests : TreatmentPlanTestBase
{
    private TreatmentPlanCopyIssueService Issues(Alveara.Api.Data.AlveraDbContext db) => new(db, Clock, NullLogger<TreatmentPlanCopyIssueService>.Instance);

    private async Task<(Guid Plan, Guid Issue)> IssuedForAnnAsync()
    {
        var (plan, _, _) = await SimplePlanAsync();
        var issued = await WithDb(db => Issues(db).IssueAsync(plan.Id, "own-1", null, S.Actor, default));
        return (plan.Id, issued.Issue.Id);
    }

    private static int SqlNumber(Exception e) => (e as SqlException ?? e.InnerException as SqlException)?.Number ?? 0;

    [Fact]
    public async Task The_database_refuses_a_change_or_a_deletion_of_an_issue()
    {
        var (_, issue) = await IssuedForAnnAsync();
        await using var db = Fixture.CreateContext();
        var update = await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlRawAsync("UPDATE [TreatmentPlanCopyIssues] SET [RevisionNumber] = 2 WHERE [Id] = @id", new SqlParameter("@id", issue)));
        var delete = await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlRawAsync("DELETE FROM [TreatmentPlanCopyIssues] WHERE [Id] = @id", new SqlParameter("@id", issue)));
        Assert.Equal((51307, 51307), (SqlNumber(update), SqlNumber(delete)));
        Assert.Equal(1, (await db.TreatmentPlanCopyIssues.AsNoTracking().SingleAsync(i => i.Id == issue)).RevisionNumber);
    }

    [Fact]
    public async Task The_issue_table_has_no_patient_column_and_is_not_in_the_merge_registry()
    {
        await using var db = Fixture.CreateContext();
        var columns = await db.Database.SqlQueryRaw<string>("SELECT [name] AS [Value] FROM sys.columns WHERE [object_id] = OBJECT_ID('TreatmentPlanCopyIssues')").ToListAsync();
        Assert.DoesNotContain("PatientId", columns);
        Assert.Contains("PlanId", columns);
        Assert.DoesNotContain(PatientOwnedData.All, c => c.Table == "TreatmentPlanCopyIssues");
    }

    [Fact]
    public async Task After_an_approved_merge_of_the_parent_the_unchanged_issue_is_reached_only_through_the_survivors_plan_and_still_rebuilds()
    {
        var (plan, issue) = await IssuedForAnnAsync();
        var before = await WithDb(db => db.TreatmentPlanCopyIssues.AsNoTracking().SingleAsync(i => i.Id == issue));

        var preview = await WithDb(db => new PatientMergeService(db, Clock).PreviewAsync(Ann, Bo, default));
        Assert.True(preview.CanMerge, string.Join("; ", preview.Blockers.Select(b => b.Message)));
        await WithDb(db => new PatientMergeService(db, Clock).MergeAsync(S.Actor, new MergeRequest(Ann, Bo, "Registered twice", preview.SourceRowVersion, preview.SurvivorRowVersion,
            preview.Conflicts.ToDictionary(c => c.Field, _ => "Survivor"), true, true), Guid.NewGuid().ToString("N"), default));

        var after = await WithDb(db => db.TreatmentPlanCopyIssues.AsNoTracking().SingleAsync(i => i.Id == issue));
        Assert.Equal((before.PlanId, before.EnvelopeJson, before.ItemStateCutoffJson), (after.PlanId, after.EnvelopeJson, after.ItemStateCutoffJson));   // the issue row itself was not touched
        var (_, owner) = await WithDb(db => Issues(db).GetAsync(issue, default));
        Assert.Equal(Bo, owner);
        Assert.Equal(0, await WithDb(db => db.TreatmentPlanCopyIssues.AsNoTracking().Join(db.TreatmentPlans, i => i.PlanId, p => p.Id, (i, p) => p.PatientId).CountAsync(p => p == Ann)));
        var content = await WithDb(db => Issues(db).ContentAsync(after, default));
        Assert.Equal("Chronic periodontitis", Assert.Single(content.Lines).DiagnosisLabel);
        Assert.Equal(plan, after.PlanId);
    }
}

/// <summary>ALV-N026 S7 (brief 3.3 proof): the diagnosis label as of an instant is the version with the greatest OccurredAtUtc at or before it; a tie takes the higher version number; none means refuse.</summary>
public class TreatmentPlanCopyDiagnosisLabelTests
{
    private static readonly DateTimeOffset T = new(2030, 5, 1, 12, 0, 0, TimeSpan.Zero);

    private static DiagnosisVersion V(int number, string label, DateTimeOffset at) =>
        new() { Id = Guid.NewGuid(), DiagnosisId = Guid.Empty, PatientId = Guid.Empty, VersionNumber = number, ChangeType = "Recorded", Label = label, Status = "Active", OccurredAtUtc = at };

    [Fact]
    public void The_label_is_the_latest_version_at_or_before_the_instant()
    {
        var versions = new[] { V(1, "first", T.AddDays(-2)), V(2, "second", T), V(3, "later", T.AddSeconds(1)) };
        Assert.Equal("second", TreatmentPlanCopyIssueService.DiagnosisLabelAsOf(versions, T));                 // at the instant counts; after it does not
        Assert.Equal("first", TreatmentPlanCopyIssueService.DiagnosisLabelAsOf(versions, T.AddTicks(-1)));
        Assert.Equal("later", TreatmentPlanCopyIssueService.DiagnosisLabelAsOf(versions, T.AddDays(1)));
    }

    [Fact]
    public void A_tie_on_the_time_takes_the_higher_version_number()
    {
        var versions = new[] { V(3, "three", T), V(2, "two", T) };
        Assert.Equal("three", TreatmentPlanCopyIssueService.DiagnosisLabelAsOf(versions, T));
    }

    [Fact]
    public void No_version_by_the_instant_gives_no_label()
    {
        Assert.Null(TreatmentPlanCopyIssueService.DiagnosisLabelAsOf([V(1, "first", T.AddDays(1))], T));
        Assert.Null(TreatmentPlanCopyIssueService.DiagnosisLabelAsOf([], T));
    }
}
