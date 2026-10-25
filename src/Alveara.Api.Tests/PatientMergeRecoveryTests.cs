using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static Alveara.Api.Tests.SchedulingTestSupport;
using Alveara.Api.Architecture.Patients;
using Alveara.Api.Architecture.Patients.Merge;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-017-C01, the merge fails part way and nothing is half done. A real chart for each patient (see <see cref="MergeChartTestBase"/>); a fault is planted in the database at one of four points of a merge (while the rows are
/// moving, when the lineage is written, when the retired record is marked, when the audit entries are written); the merge fails with that fault and then every registered column of both patients holds exactly what it held,
/// no lineage, resolution, audit entry or merged mark exists, and the merge value did not leak onto the pooled connection. Once the fault is gone the very same request (the same key and the same versions) succeeds and
/// moves everything, and a second attempt at a record that is already merged is refused.
/// </summary>
public class PatientMergeRecoveryTests : MergeChartTestBase
{
    private async Task<(MergeRequest Request, Dictionary<string, int> Ann, Dictionary<string, int> Bo)> ArrangeAsync()
    {
        await GiveRetiringChartAsync(); await GiveSurvivingChartAsync();
        var preview = await WithDb(db => new PatientMergeService(db, Clock).PreviewAsync(Ann, Bo, default));
        Assert.True(preview.CanMerge, string.Join("; ", preview.Blockers.Select(b => b.Message)));
        var request = new MergeRequest(Ann, Bo, "Registered twice", preview.SourceRowVersion, preview.SurvivorRowVersion, preview.Conflicts.ToDictionary(c => c.Field, _ => "Survivor"), true, true);
        return (request, await CountsAsync(Ann), await CountsAsync(Bo));
    }

    private Task<MergeResult> MergeAsync(MergeRequest request, string key) => WithDb(db => new PatientMergeService(db, Clock).MergeAsync(S.Actor, request, key, default));

    private async Task AssertNothingChangedAsync(Dictionary<string, int> ann, Dictionary<string, int> bo)
    {
        Assert.Equal(ann, await CountsAsync(Ann)); Assert.Equal(bo, await CountsAsync(Bo));
        Assert.Equal(0, await Scalar("SELECT COUNT(*) AS [Value] FROM [PatientMerges]"));
        Assert.Equal(0, await Scalar("SELECT COUNT(*) AS [Value] FROM [PatientMergeMoves]"));
        Assert.Equal(0, await Scalar("SELECT COUNT(*) AS [Value] FROM [PatientMergeResolutions]"));
        Assert.Equal(0, await Scalar("SELECT COUNT(*) AS [Value] FROM [AuditLogEntries] WHERE [EventType] = 'PatientMerged'"));
        Assert.Equal(0, await Scalar("SELECT COUNT(*) AS [Value] FROM [Patients] WHERE [MergedAtUtc] IS NOT NULL OR ([IsActive] = 0 AND [Id] IN (SELECT [Id] FROM [Patients] WHERE [Id] IN ('" + Ann + "','" + Bo + "')))"));
        Assert.Equal(0, await AgreementViolationsAsync());
        // the merge value did not leak onto a pooled connection: an immutable record still refuses a change of patient from any connection
        for (var i = 0; i < 3; i++)
        {
            var refused = await Assert.ThrowsAnyAsync<Exception>(() => WithDb(db => db.Database.ExecuteSqlAsync($"UPDATE [TreatmentPlans] SET [PatientId] = {Bo} WHERE [PatientId] = {Ann}")));
            Assert.Contains("51086", refused.ToString());
        }
    }

    private async Task<int> Scalar(string sql) { await using var db = Fixture.CreateContext(); return await db.Database.SqlQueryRaw<int>(sql).SingleAsync(); }

    [Theory]
    [InlineData("while the rows are moving", "ToothFindings", "AFTER UPDATE", "IF CONVERT(int, SESSION_CONTEXT(N'alveara.patient_merge')) = 1 THROW 59010, 'a move failed', 1;")]
    [InlineData("when the lineage is written", "PatientMerges", "AFTER INSERT", "THROW 59011, 'the lineage could not be written', 1;")]
    [InlineData("when the retired record is marked", "Patients", "AFTER UPDATE", "IF EXISTS (SELECT 1 FROM inserted WHERE [MergedAtUtc] IS NOT NULL) THROW 59012, 'the patient could not be marked', 1;")]
    [InlineData("when the audit entries are written", "AuditLogEntries", "AFTER INSERT", "IF EXISTS (SELECT 1 FROM inserted WHERE [EventType] = 'PatientMerged') THROW 59013, 'the audit store is down', 1;")]
    public async Task A_merge_that_fails_part_way_changes_nothing_and_the_same_request_succeeds_once_the_fault_is_gone(string point, string table, string when, string body)
    {
        var (request, ann, bo) = await ArrangeAsync();
        await using (var db = Fixture.CreateContext()) await db.Database.ExecuteSqlRawAsync($"CREATE TRIGGER [TR_TestFault] ON [{table}] {when} AS BEGIN {body} END");
        try
        {
            var failure = await Assert.ThrowsAnyAsync<Exception>(() => MergeAsync(request, "recover-1"));
            var sql = Assert.IsType<SqlException>(failure is DbUpdateException { InnerException: not null } d ? d.InnerException : failure);
            Assert.InRange(sql.Number, 59010, 59013);                                                                                     // the planted fault, not some other error
            await AssertNothingChangedAsync(ann, bo);
        }
        finally { await using var db = Fixture.CreateContext(); await db.Database.ExecuteSqlRawAsync($"DROP TRIGGER [TR_TestFault]"); }

        var done = await MergeAsync(request, "recover-1");                                                                                // the very same request, once it works
        Assert.False(done.Replayed);
        var after = (Ann: await CountsAsync(Ann), Bo: await CountsAsync(Bo));
        foreach (var column in PatientOwnedData.All.Where(c => c.Table != "PatientHistory"))
        {
            var k = $"{column.Table}.{column.Column}";
            Assert.True(after.Ann[k] == 0 && after.Bo[k] == ann[k] + bo[k], $"{point}: {k}: retired {after.Ann[k]}, survivor {after.Bo[k]}, expected {ann[k]} + {bo[k]}");
        }
        Assert.Equal(0, await AgreementViolationsAsync());
        Assert.Equal(1, await Scalar("SELECT COUNT(*) AS [Value] FROM [PatientMerges]"));
        Assert.Equal(2, await Scalar("SELECT COUNT(*) AS [Value] FROM [AuditLogEntries] WHERE [EventType] = 'PatientMerged'"));
    }

    [Fact]
    public async Task A_second_attempt_at_a_record_that_is_already_merged_is_refused_and_changes_nothing_more()
    {
        var (request, _, _) = await ArrangeAsync();
        await MergeAsync(request, "first");
        var after = (Ann: await CountsAsync(Ann), Bo: await CountsAsync(Bo));
        var preview = await Assert.ThrowsAsync<PatientException>(() => WithDb(db => new PatientMergeService(db, Clock).PreviewAsync(Ann, Bo, default)));
        Assert.Equal("already_merged", preview.Code);
        var again = await Assert.ThrowsAsync<PatientException>(() => MergeAsync(request, "second"));
        Assert.Equal(("already_merged", 409), (again.Code, again.StatusCode));
        var other = await S.PatientAsync("Cy", "Kim");
        Assert.Equal("already_merged", (await Assert.ThrowsAsync<PatientException>(() => MergeAsync(request with { SurvivorPatientId = other }, "third"))).Code);       // into a third record
        Assert.Equal("survivor_merged", (await Assert.ThrowsAsync<PatientException>(() => MergeAsync(request with { SourcePatientId = other, SurvivorPatientId = Ann }, "fourth"))).Code);   // or keeping the retired one
        Assert.Equal(after.Ann, await CountsAsync(Ann)); Assert.Equal(after.Bo, await CountsAsync(Bo));
        Assert.Equal(1, await Scalar("SELECT COUNT(*) AS [Value] FROM [PatientMerges]"));
    }

    [Fact]
    public async Task A_change_made_to_either_record_after_the_review_stops_the_merge_with_nothing_moved()
    {
        var (request, ann, bo) = await ArrangeAsync();
        await using (var db = Fixture.CreateContext()) await db.Database.ExecuteSqlRawAsync("UPDATE [Patients] SET [City] = N'Dallas' WHERE [Id] = {0}", Ann);
        var stale = await Assert.ThrowsAsync<PatientException>(() => MergeAsync(request, "stale-1"));
        Assert.Equal(("patient_changed", 409), (stale.Code, stale.StatusCode));
        Assert.Equal(ann, await CountsAsync(Ann)); Assert.Equal(bo, await CountsAsync(Bo));
        Assert.Equal(0, await Scalar("SELECT COUNT(*) AS [Value] FROM [PatientMerges]"));
    }
}
