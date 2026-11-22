using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Alveara.Api.Architecture.Patients;
using Alveara.Api.Architecture.Patients.Merge;
using Alveara.Api.Architecture.Patients.Portability.Package;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N026 S8 (D4; brief 3 "merge scope", review item 4): a clinical record copy issue is directly patient-owned and joins the established merge machinery. Proven directly in SQL: the row is
/// append-only (51309) and its patient cannot be changed outside a merge; inside a merge only PatientId may change (51234); a merged-away record takes no new copy (51235); an approved merge moves
/// the row to the survivor without touching anything else, and its undo moves it back.
/// </summary>
public class ClinicalRecordCopyOwnershipTests : TreatmentPlanTestBase
{
    private ClinicalRecordCopyService Copies(Alveara.Api.Data.AlveraDbContext db) => new(db, new ReadableRecordLoader(db, Clock), Clock, NullLogger<ClinicalRecordCopyService>.Instance);

    private async Task<Guid> IssuedAsync(Guid patient, string key = "copy-1") => (await WithDb(db => Copies(db).IssueAsync(patient, key, null, S.Actor, default))).Issue.Id;

    private static int SqlNumber(Exception e) => (e as SqlException ?? e.InnerException as SqlException)?.Number ?? 0;

    /// <summary>Runs one statement with the merge session value set (as the merge service does), clearing it again whatever happens.</summary>
    private async Task<int> InMergeAsync(string statement)
    {
        await using var db = Fixture.CreateContext();
        var sql = "EXEC sp_set_session_context N'alveara.patient_merge', 1; BEGIN TRY " + statement + "; EXEC sp_set_session_context N'alveara.patient_merge', NULL; END TRY " +
                  "BEGIN CATCH EXEC sp_set_session_context N'alveara.patient_merge', NULL; THROW; END CATCH";
        try { await db.Database.ExecuteSqlRawAsync(sql); return 0; }
        catch (Exception e) { return SqlNumber(e); }
    }

    private async Task<int> DirectAsync(string statement)
    {
        await using var db = Fixture.CreateContext();
        try { await db.Database.ExecuteSqlRawAsync(statement); return 0; }
        catch (Exception e) { return SqlNumber(e); }
    }

    private Task<ClinicalRecordCopyIssue> RowAsync(Guid id) => WithDb(db => db.ClinicalRecordCopyIssues.AsNoTracking().SingleAsync(i => i.Id == id));

    private async Task<Guid> MergeAsync(Guid source, Guid survivor)
    {
        var preview = await WithDb(db => new PatientMergeService(db, Clock).PreviewAsync(source, survivor, default));
        Assert.True(preview.CanMerge, string.Join("; ", preview.Blockers.Select(b => b.Message)));
        var done = await WithDb(db => new PatientMergeService(db, Clock).MergeAsync(S.Actor, new MergeRequest(source, survivor, "Registered twice", preview.SourceRowVersion, preview.SurvivorRowVersion,
            preview.Conflicts.ToDictionary(c => c.Field, _ => "Survivor"), true, true), Guid.NewGuid().ToString("N"), default));
        return done.MergeId;
    }

    [Fact]
    public async Task The_database_refuses_a_change_a_deletion_and_a_direct_change_of_patient_outside_a_merge()
    {
        var id = await IssuedAsync(Ann);
        Assert.Equal(51309, await DirectAsync($"UPDATE [ClinicalRecordCopyIssues] SET [TemplateVersion] = 'v1' WHERE [Id] = '{id}'"));
        Assert.Equal(51309, await DirectAsync($"DELETE FROM [ClinicalRecordCopyIssues] WHERE [Id] = '{id}'"));
        Assert.Equal(51309, await DirectAsync($"UPDATE [ClinicalRecordCopyIssues] SET [PatientId] = '{Bo}' WHERE [Id] = '{id}'"));       // the patient scope is not changed by hand
        Assert.Equal(Ann, (await RowAsync(id)).PatientId);
    }

    [Fact]
    public async Task Inside_a_merge_only_the_patient_id_may_change_and_no_row_may_be_added_or_removed()
    {
        var id = await IssuedAsync(Ann);
        Assert.Equal(51309, await InMergeAsync($"DELETE FROM [ClinicalRecordCopyIssues] WHERE [Id] = '{id}'"));
        Assert.Equal(51234, await InMergeAsync($"INSERT INTO [ClinicalRecordCopyIssues] ([Id],[PatientId],[GeneratedAsOfUtc],[IssuedByUserId],[TemplateVersion],[IdempotencyKey],[CorrelationId]) " +
                                               $"VALUES (NEWID(), '{Ann}', SYSDATETIMEOFFSET(), '{S.Actor}', 'v1', N'in-merge', NEWID())"));
        Assert.Equal((Ann, "copy-1"), ((await RowAsync(id)).PatientId, (await RowAsync(id)).IdempotencyKey));                          // nothing changed
        Assert.Equal(1, await WithDb(db => db.ClinicalRecordCopyIssues.CountAsync()));

        // the append-only trigger's stand-aside applies ONLY the patient id of the rows named (the AddPatientMerges INSTEAD OF shape): any other value in the same statement is not written
        Assert.Equal(0, await InMergeAsync($"UPDATE [ClinicalRecordCopyIssues] SET [PatientId] = '{Bo}', [IdempotencyKey] = N'changed', [TemplateVersion] = N'v9' WHERE [Id] = '{id}'"));
        var row = await RowAsync(id);
        Assert.Equal((Bo, "copy-1", "v1"), (row.PatientId, row.IdempotencyKey, row.TemplateVersion));
    }

    [Fact]
    public async Task The_table_is_registered_as_patient_owned_with_its_replay_key_and_carries_both_merge_guards()
    {
        Assert.Contains(PatientOwnedData.All, c => c.Table == "ClinicalRecordCopyIssues" && c.Column == "PatientId" && c.Role == PatientDataRole.Owner
            && c.UniqueRules.Single().Policy == MergeCollisionPolicy.ReplayKey);
        await using var db = Fixture.CreateContext();
        var scope = await db.Database.SqlQueryRaw<string>("SELECT OBJECT_DEFINITION(OBJECT_ID('TR_ClinicalRecordCopyIssues_MergeScope')) AS [Value]").SingleAsync();
        var immutable = await db.Database.SqlQueryRaw<string>("SELECT OBJECT_DEFINITION(OBJECT_ID('TR_ClinicalRecordCopyIssues_Immutable')) AS [Value]").SingleAsync();
        Assert.Contains("/*retired-record-guard*/", scope);
        Assert.Contains("/*patient-merge*/", immutable);
    }

    [Fact]
    public async Task A_merged_away_record_takes_no_new_copy_and_the_service_says_why()
    {
        var gone = await S.PatientAsync("Dee", "Kim"); var kept = await S.PatientAsync("Cy", "Kim");
        await MergeAsync(gone, kept);
        Assert.Equal(51235, await DirectAsync($"INSERT INTO [ClinicalRecordCopyIssues] ([Id],[PatientId],[GeneratedAsOfUtc],[IssuedByUserId],[TemplateVersion],[IdempotencyKey],[CorrelationId]) " +
                                              $"VALUES (NEWID(), '{gone}', SYSDATETIMEOFFSET(), '{S.Actor}', 'v1', N'after-merge', NEWID())"));
        var refused = await Assert.ThrowsAsync<PatientException>(() => WithDb(db => Copies(db).IssueAsync(gone, "after-merge", null, S.Actor, default)));
        Assert.Equal(("patient_merged", 409), (refused.Code, refused.StatusCode));
        await IssuedAsync(kept, "survivor");                                                                                             // the surviving record takes copies as ever
    }

    [Fact]
    public async Task An_approved_merge_moves_the_copy_to_the_survivor_untouched_and_its_undo_moves_it_back()
    {
        var id = await IssuedAsync(Ann);
        var before = await RowAsync(id);
        var mergeId = await MergeAsync(Ann, Bo);

        var moved = await RowAsync(id);
        Assert.Equal(Bo, moved.PatientId);
        Assert.Equal((before.GeneratedAsOfUtc, before.IdempotencyKey, before.CorrelationId, before.IssuedByUserId), (moved.GeneratedAsOfUtc, moved.IdempotencyKey, moved.CorrelationId, moved.IssuedByUserId));

        var preview = await WithDb(db => new PatientUnmergeService(db, Clock).PreviewAsync(mergeId, default));
        await WithDb(db => new PatientUnmergeService(db, Clock).UnmergeAsync(S.Actor, new UnmergeRequest(mergeId, "They are different people", preview.SourceRowVersion, preview.SurvivorRowVersion, true), "undo-1", default));
        Assert.Equal(Ann, (await RowAsync(id)).PatientId);
    }
}
