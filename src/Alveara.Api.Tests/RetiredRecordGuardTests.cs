using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Alveara.Api.Architecture.Logging;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Alveara.Api.Architecture.Documents;
using Alveara.Api.Architecture.Patients.Merge;
using Alveara.Api.Architecture.Recall;
using Alveara.Api.Architecture.Treatment;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-017-C01 (error 51235): a patient record that was merged into another takes no new records. Against a real SQL Server and rows written around any service: a new treatment plan, document or recall plan naming the
/// retired patient is refused, the same insert for an active patient is not, the retired patient's existing rows can still be changed (the guard is for NEW records), every merge-scope trigger on a table that owns a
/// patient id carries the check and the tables that do not own one do not, and the migration takes the check out and puts it back.
/// </summary>
public class RetiredRecordGuardTests : TreatmentPlanTestBase
{
    private const string BeforeGuard = "20261025195735_AddPatientDuplicateDismissals";

    private async Task MergeAsync(Guid source, Guid survivor)
    {
        var preview = await WithDb(db => new PatientMergeService(db, Clock).PreviewAsync(source, survivor, default));
        await WithDb(db => new PatientMergeService(db, Clock).MergeAsync(S.Actor, new MergeRequest(source, survivor, "Same person", preview.SourceRowVersion, preview.SurvivorRowVersion, null, true, true), Guid.NewGuid().ToString("N"), default));
    }

    private async Task AddAsync(params object[] rows) { await using var db = Fixture.CreateContext(); db.AddRange(rows); await db.SaveChangesAsync(); }

    private static async Task<Microsoft.Data.SqlClient.SqlException> Refusal(Func<Task> action)
    {
        var e = await Assert.ThrowsAnyAsync<Exception>(action);
        return Assert.IsType<Microsoft.Data.SqlClient.SqlException>(e is DbUpdateException { InnerException: not null } d ? d.InnerException : e);
    }

    private TreatmentPlan Plan(Guid patient) => new() { Id = Guid.NewGuid(), PatientId = patient, IdempotencyKey = $"plan-{Guid.NewGuid():N}", Title = "Plan", Status = TreatmentPlanStatuses.Proposed, CreatedAtUtc = DateTimeOffset.UtcNow, CreatedByUserId = S.Actor };

    private PatientDocument Document(Guid patient) => new()
    {
        Id = Guid.NewGuid(), PatientId = patient, CategoryId = DocumentCategorySeeds.Radiograph, DocumentDate = new DateOnly(2026, 10, 1), Description = "Panoramic film.", OriginalFileName = "pano.png", DetectedType = "png",
        ContentType = "image/png", SizeBytes = 2048, Sha256 = new string('a', 64), BlobId = Guid.NewGuid(), ImportedByUserId = S.Actor, ImportedAtUtc = DateTimeOffset.UtcNow, IdempotencyKey = $"doc-{Guid.NewGuid():N}", RequestHash = new string('b', 64),
    };

    private PatientRecallPlan Recall(Guid patient) => new() { PatientId = patient, IntervalDays = 90, UpdatedAtUtc = DateTimeOffset.UtcNow, UpdatedByUserId = S.Actor };

    [Fact]
    public async Task A_new_record_for_a_patient_that_was_merged_away_is_refused_where_the_same_record_for_an_active_patient_is_not()
    {
        var kept = await S.PatientAsync("Cy", "Kim"); var gone = await S.PatientAsync("Dee", "Kim");
        await AddAsync(Plan(kept), Document(kept));                                                                       // before the merge, either is fine
        await MergeAsync(gone, kept);

        Assert.Equal(51235, (await Refusal(() => AddAsync(Plan(gone)))).Number);
        Assert.Equal(51235, (await Refusal(() => AddAsync(Document(gone)))).Number);
        Assert.Equal(51235, (await Refusal(() => AddAsync(Recall(gone)))).Number);                                        // a table with no trigger of its own
        await AddAsync(Plan(kept), Document(kept), Recall(kept));                                                         // the surviving record takes new records as ever
        await using var db = Fixture.CreateContext();
        Assert.Equal(0, await db.TreatmentPlans.CountAsync(p => p.PatientId == gone));
    }

    [Fact]
    public async Task The_error_handler_answers_the_databases_refusal_with_a_plain_409_and_never_sends_its_text_while_any_other_failure_stays_a_generic_500()
    {
        var kept = await S.PatientAsync("Cy", "Kim"); var gone = await S.PatientAsync("Dee", "Kim");
        await MergeAsync(gone, kept);
        var refusal = await Assert.ThrowsAnyAsync<Exception>(() => AddAsync(Plan(gone)));                                  // the real exception the database raises (51235), chain and all

        async Task<(int Status, string Body)> Through(Exception ex)
        {
            var context = new DefaultHttpContext(); context.Response.Body = new MemoryStream();
            await new SafeExceptionHandlingMiddleware(_ => throw ex, NullLogger<SafeExceptionHandlingMiddleware>.Instance).InvokeAsync(context);
            context.Response.Body.Position = 0;
            return (context.Response.StatusCode, await new StreamReader(context.Response.Body).ReadToEndAsync());
        }

        var merged = await Through(refusal);
        Assert.Equal(409, merged.Status);
        Assert.Contains("\"error\":\"patient_merged\"", merged.Body);
        Assert.DoesNotContain("51235", merged.Body); Assert.DoesNotContain("TR_", merged.Body);
        var other = await Through(new InvalidOperationException("boom"));
        Assert.Equal(500, other.Status); Assert.Contains("internal_error", other.Body);
        var otherSql = await Refusal(() => AddAsync(Plan(Guid.NewGuid())));                                                 // a foreign-key failure is still a generic 500
        Assert.Equal(500, (await Through(otherSql)).Status);
    }

    [Fact]
    public async Task The_guard_is_for_new_records_only_a_retired_patients_existing_rows_can_still_be_changed()
    {
        var kept = await S.PatientAsync("Cy", "Kim"); var gone = await S.PatientAsync("Dee", "Kim");
        await AddAsync(Recall(kept), Recall(gone));                                                                       // both have their one: the retired one stays where it is
        await MergeAsync(gone, kept);
        await using var db = Fixture.CreateContext();
        Assert.Equal(1, await db.Database.ExecuteSqlAsync($"UPDATE [PatientRecallPlans] SET [IntervalDays] = 120 WHERE [PatientId] = {gone}"));
        Assert.Equal(1, await db.PatientRecallPlans.CountAsync(p => p.PatientId == gone && p.IntervalDays == 120));
    }

    [Fact]
    public async Task Every_merge_scope_trigger_on_a_table_that_owns_a_patient_id_carries_the_check_and_no_other_does()
    {
        await using var db = Fixture.CreateContext();
        var triggers = await db.Database.SqlQueryRaw<string>(@"SELECT t.name + '|' + CASE WHEN EXISTS (SELECT 1 FROM sys.columns c WHERE c.object_id = t.parent_id AND c.name = 'PatientId') THEN 'owns' ELSE 'refers' END + '|' + CASE WHEN m.definition LIKE '%/*retired-record-guard*/%' THEN 'guarded' ELSE 'plain' END AS [Value]
            FROM sys.triggers t JOIN sys.sql_modules m ON m.object_id = t.object_id WHERE t.name LIKE 'TR[_]%[_]MergeScope'").ToListAsync();
        Assert.Equal(PatientOwnedData.Tables.Count, triggers.Count);
        Assert.All(triggers, t => { var p = t.Split('|'); Assert.Equal(p[1] == "owns" ? "guarded" : "plain", p[2]); });
        Assert.Contains(triggers, t => t.StartsWith("TR_Patients_MergeScope|refers|plain"));
        Assert.Contains(triggers, t => t.StartsWith("TR_Messages_MergeScope|refers|plain"));
        Assert.True(triggers.Count(t => t.Contains("|owns|guarded")) >= 50);
    }

    [Fact]
    public async Task The_migration_takes_the_check_out_and_puts_it_back()
    {
        var kept = await S.PatientAsync("Cy", "Kim"); var gone = await S.PatientAsync("Dee", "Kim");
        await MergeAsync(gone, kept);
        await using (var db = Fixture.CreateContext()) await db.GetInfrastructure().GetRequiredService<IMigrator>().MigrateAsync(BeforeGuard);
        await using (var db = Fixture.CreateContext())
        {
            Assert.Equal(0, await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM sys.sql_modules WHERE definition LIKE N'%retired-record-guard%'").SingleAsync());
            Assert.Equal(PatientOwnedData.Tables.Count, await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM sys.triggers WHERE name LIKE 'TR[_]%[_]MergeScope'").SingleAsync());          // the merge-scope triggers are back as they were
        }
        await AddAsync(Plan(gone));                                                                                        // before the guard existed, nothing refused it
        await using (var db = Fixture.CreateContext()) await db.Database.MigrateAsync();
        await using (var db = Fixture.CreateContext())
            Assert.True(await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM sys.sql_modules WHERE definition LIKE N'%retired-record-guard%'").SingleAsync() >= 50);
        Assert.Equal(51235, (await Refusal(() => AddAsync(Plan(gone)))).Number);
    }
}
