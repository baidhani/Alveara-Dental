using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Alveara.Api.Architecture.Patients;
using Alveara.Api.Architecture.Patients.Merge;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-017-C01, undoing a merge against a real SQL Server and the real chart of <see cref="MergeChartTestBase"/>. After a merge and its undo every registered column of both patients holds exactly what it held before the
/// merge, nothing disagrees with the record it points at, the retired record is active and unmarked again, the money is the same, field values that were not edited since are restored and the ones that were are kept,
/// the original lineage stays and the undo has its own (user and time, and two audit entries), and the record can be merged away again later. It is refused, changing nothing, for a merge without a ledger, one already
/// undone, a survivor that was itself merged away, a stale record, no confirmation or reason, and records made after the merge that depend on the rows that would move back; a fault planted at four points of an undo
/// leaves nothing half done and the same request then succeeds; and the preview, which does the work and rolls it back, promises exactly what the undo does.
/// </summary>
public class PatientUnmergeServiceTests : MergeChartTestBase
{
    private async Task<MergeResult> MergeAsync(Guid source, Guid survivor, string key = "merge-1")
    {
        var preview = await WithDb(db => new PatientMergeService(db, Clock).PreviewAsync(source, survivor, default));
        return await WithDb(db => new PatientMergeService(db, Clock).MergeAsync(S.Actor, new MergeRequest(source, survivor, "Registered twice", preview.SourceRowVersion, preview.SurvivorRowVersion,
            preview.Conflicts.ToDictionary(c => c.Field, _ => "Source"), true, true), key, default));
    }

    private async Task<UnmergeRequest> RequestAsync(Guid mergeId, string reason = "They are different people", bool? confirmed = true)
    {
        var preview = await WithDb(db => new PatientUnmergeService(db, Clock).PreviewAsync(mergeId, default));
        return new UnmergeRequest(mergeId, reason, preview.SourceRowVersion, preview.SurvivorRowVersion, confirmed);
    }

    private Task<UnmergeResult> UnmergeAsync(UnmergeRequest request, string key = "undo-1") => WithDb(db => new PatientUnmergeService(db, Clock).UnmergeAsync(S.Actor, request, key, default));
    private async Task<PatientException> RefusedAsync(Func<Task> action) => await Assert.ThrowsAsync<PatientException>(action);
    private async Task<int> Scalar(string sql) { await using var db = Fixture.CreateContext(); return await db.Database.SqlQueryRaw<int>(sql).SingleAsync(); }
    private async Task SetAsync(Guid patient, string column, string? value) { await using var db = Fixture.CreateContext(); await db.Database.ExecuteSqlRawAsync($"UPDATE [Patients] SET [{column}] = @v WHERE [Id] = @p", new SqlParameter("@v", (object?)value ?? DBNull.Value), new SqlParameter("@p", patient)); }
    private async Task<(string? Phone, string? Email, Guid? Household)> FieldsOfAsync(Guid patient) { await using var db = Fixture.CreateContext(); var p = await db.Patients.AsNoTracking().SingleAsync(x => x.Id == patient); return (p.Phone, p.Email, p.HouseholdId); }

    private async Task AssertSameCountsAsync(Dictionary<string, int> ann, Dictionary<string, int> bo)
    {
        var a = await CountsAsync(Ann); var b = await CountsAsync(Bo);
        foreach (var column in PatientOwnedData.All.Where(c => c.Table != "PatientHistory"))
        {
            var k = $"{column.Table}.{column.Column}";
            Assert.True(a[k] == ann[k] && b[k] == bo[k], $"{k}: retired {a[k]} (was {ann[k]}), survivor {b[k]} (was {bo[k]})");
        }
        Assert.Equal(0, await AgreementViolationsAsync());
    }

    // ---------- the round trip ----------

    [Fact]
    public async Task A_merge_and_its_undo_leave_every_row_of_both_charts_where_it_was_and_the_record_active_again_with_its_own_lineage_and_audit()
    {
        await GiveRetiringChartAsync(); await GiveSurvivingChartAsync();
        await SetAsync(Ann, "Phone", "(555) 010-0001"); await SetAsync(Bo, "Phone", "(555) 010-0002"); await SetAsync(Ann, "Email", "ann@example.org");       // a conflict the merge answers with the retired value, and an empty field it fills
        var originalSurvivor = await FieldsOfAsync(Bo);
        var before = (Ann: await CountsAsync(Ann), Bo: await CountsAsync(Bo));
        var account = await Bill(s => s.GetAccountAsync(Ann, default));
        var merged = await MergeAsync(Ann, Bo);
        Assert.Equal(("(555) 010-0001", "ann@example.org"), ((await FieldsOfAsync(Bo)).Phone, (await FieldsOfAsync(Bo)).Email));

        var request = await RequestAsync(merged.MergeId);
        var done = await UnmergeAsync(request);
        Assert.False(done.Replayed);
        Assert.Equal(merged.Moves.Sum(m => m.Rows), done.RowsMovedBack);
        Assert.Equal(new[] { "Email", "Household", "Phone" }, done.FieldsRestored.Order().ToArray()); Assert.Empty(done.FieldsKept);

        await AssertSameCountsAsync(before.Ann, before.Bo);                                                                           // every registered column, both patients
        Assert.Equal(originalSurvivor, await FieldsOfAsync(Bo));                                                                       // the survivor's own phone is back, the filled email and household are gone
        var back = await Bill(s => s.GetAccountAsync(Ann, default));
        Assert.Equal((account.BalanceOwed, account.Entries.Count), (back.BalanceOwed, back.Entries.Count));                            // the money, to the cent, on the retired patient's own account again
        await using var db = Fixture.CreateContext();
        var retired = await db.Patients.AsNoTracking().SingleAsync(p => p.Id == Ann);
        Assert.True(retired.IsActive); Assert.Null(retired.MergedAtUtc);
        Assert.Equal(1, await db.PatientMerges.CountAsync());                                                                          // the original lineage stays
        var unmerge = await db.PatientUnmerges.AsNoTracking().SingleAsync();
        Assert.Equal((merged.MergeId, S.Actor, "They are different people", "undo-1"), (unmerge.MergeId, unmerge.UnmergedByUserId, unmerge.Reason, unmerge.UnmergeKey));
        var audit = await db.AuditLogEntries.AsNoTracking().Where(a => a.EventType == "PatientUnmerged").ToListAsync();
        Assert.Equal(2, audit.Count);
        Assert.All(audit, a => { Assert.Equal(S.Actor, a.PerformedByUserAccountId); Assert.Equal(unmerge.Id, a.CorrelationId); Assert.Equal("They are different people", a.Reason); Assert.True(a.TimestampUtc > DateTimeOffset.UtcNow.AddMinutes(-5)); Assert.DoesNotContain("555", a.Details); });
        Assert.Equal(new[] { Ann, Bo }.Order().ToArray(), audit.Select(a => a.TargetUserAccountId).Order().ToArray());
        Assert.Contains(await db.PatientHistory.AsNoTracking().Where(h => h.PatientId == Ann).ToListAsync(), h => h.ChangeType == "Activated" && h.ChangedByUserId == S.Actor);

        var history = await WithDb(d => new PatientMergeService(d, Clock).HistoryAsync(Ann, default));                                  // the lineage page: nothing current to show
        Assert.Null(history.MergedInto); Assert.Empty((await WithDb(d => new PatientMergeService(d, Clock).HistoryAsync(Bo, default))).Absorbed);
        var log = Assert.Single(await WithDb(d => new PatientMergeService(d, Clock).LogAsync(10, default)));
        Assert.NotNull(log.UndoneAtUtc); Assert.False(log.CanUndo); Assert.False(string.IsNullOrWhiteSpace(log.UndoneByName));
        Assert.False((await WithDb(d => new PatientMergeService(d, Clock).PreviewAsync(Ann, Bo, default))).Blockers.Any());           // and the pair can be reviewed and merged again
    }

    [Fact]
    public async Task After_an_undo_the_record_can_be_merged_away_again_and_that_merge_can_be_undone_in_turn()
    {
        await GiveRetiringChartAsync(); await GiveSurvivingChartAsync();
        var before = (Ann: await CountsAsync(Ann), Bo: await CountsAsync(Bo));
        var first = await MergeAsync(Ann, Bo, "merge-1");
        await UnmergeAsync(await RequestAsync(first.MergeId), "undo-1");
        var second = await MergeAsync(Ann, Bo, "merge-2");
        Assert.NotEqual(first.MergeId, second.MergeId);
        Assert.Equal(2, await Scalar("SELECT COUNT(*) AS [Value] FROM [PatientMerges]"));
        var after = await CountsAsync(Bo);
        Assert.Equal(before.Ann["TreatmentPlans.PatientId"] + before.Bo["TreatmentPlans.PatientId"], after["TreatmentPlans.PatientId"]);
        Assert.Equal("already_unmerged", (await RefusedAsync(() => UnmergeAsync(new UnmergeRequest(first.MergeId, "again", null, null, true), "undo-again"))).Code);
        await UnmergeAsync(await RequestAsync(second.MergeId), "undo-2");
        await AssertSameCountsAsync(before.Ann, before.Bo);
        Assert.Equal(2, await Scalar("SELECT COUNT(*) AS [Value] FROM [PatientUnmerges]"));
    }

    [Fact]
    public async Task A_field_edited_after_the_merge_is_kept_while_the_untouched_ones_are_put_back()
    {
        await GiveRetiringChartAsync(); await GiveSurvivingChartAsync();
        await SetAsync(Ann, "Phone", "(555) 010-0001"); await SetAsync(Bo, "Phone", "(555) 010-0002"); await SetAsync(Ann, "Email", "ann@example.org");
        var merged = await MergeAsync(Ann, Bo);
        await SetAsync(Bo, "Phone", "(555) 010-9999");                                                                                  // someone corrects the survivor's phone after the merge

        var preview = await WithDb(db => new PatientUnmergeService(db, Clock).PreviewAsync(merged.MergeId, default));
        Assert.Equal(new[] { "Email", "Household" }, preview.FieldsToRestore.Order().ToArray()); Assert.Equal(new[] { "Phone" }, preview.FieldsKept.ToArray());
        var done = await UnmergeAsync(await RequestAsync(merged.MergeId));
        Assert.Equal(new[] { "Phone" }, done.FieldsKept.ToArray());
        var bo = await FieldsOfAsync(Bo);
        Assert.Equal(("(555) 010-9999", null, null), (bo.Phone, bo.Email, bo.Household));                                                // the later edit survives; the filled email and household went back
    }

    // ---------- the preview ----------

    [Fact]
    public async Task The_preview_does_the_work_and_rolls_it_back_so_it_changes_nothing_and_promises_what_the_undo_does()
    {
        await GiveRetiringChartAsync(); await GiveSurvivingChartAsync();
        var merged = await MergeAsync(Ann, Bo);
        var afterMerge = (Ann: await CountsAsync(Ann), Bo: await CountsAsync(Bo));
        var preview = await WithDb(db => new PatientUnmergeService(db, Clock).PreviewAsync(merged.MergeId, default));
        Assert.True(preview.CanUndo); Assert.Empty(preview.Blockers);
        Assert.Equal(afterMerge.Ann, await CountsAsync(Ann)); Assert.Equal(afterMerge.Bo, await CountsAsync(Bo));                       // nothing moved
        Assert.Equal(0, await Scalar("SELECT COUNT(*) AS [Value] FROM [PatientUnmerges]"));
        Assert.Equal(1, await Scalar($"SELECT COUNT(*) AS [Value] FROM [Patients] WHERE [Id] = '{Ann}' AND [MergedAtUtc] IS NOT NULL"));
        var done = await UnmergeAsync(new UnmergeRequest(merged.MergeId, "Different people", preview.SourceRowVersion, preview.SurvivorRowVersion, true));
        Assert.Equal(preview.Moves.Sum(m => m.Rows), done.RowsMovedBack);
        Assert.Equal(preview.FieldsToRestore.Order().ToArray(), done.FieldsRestored.Order().ToArray());
        foreach (var m in preview.Moves) Assert.Equal(m.Rows, merged.Moves.Single(x => x.Table == m.Table && x.Column == m.Column).Rows);
    }

    // ---------- refusals ----------

    [Fact]
    public async Task An_undo_is_refused_without_confirmation_a_reason_or_a_key_for_an_unknown_merge_and_for_a_stale_record_and_changes_nothing()
    {
        await GiveRetiringChartAsync(); await GiveSurvivingChartAsync();
        var merged = await MergeAsync(Ann, Bo);
        var after = (Ann: await CountsAsync(Ann), Bo: await CountsAsync(Bo));
        var request = await RequestAsync(merged.MergeId);

        Assert.Equal(("confirmation_required", 400), await CodeAsync(request with { Confirmed = null }));
        Assert.Equal(("confirmation_required", 400), await CodeAsync(request with { Confirmed = false }));
        Assert.Equal(("reason_invalid", 400), await CodeAsync(request with { Reason = "  " }));
        Assert.Equal(("merge_required", 400), await CodeAsync(request with { MergeId = null }));
        Assert.Equal(("merge_not_found", 404), await CodeAsync(request with { MergeId = Guid.NewGuid() }));
        Assert.Equal("idempotency_key_invalid", (await RefusedAsync(() => UnmergeAsync(request, " "))).Code);
        Assert.Equal("row_version_required", (await RefusedAsync(() => UnmergeAsync(request with { SourceRowVersion = null }))).Code);
        await SetAsync(Bo, "City", "Dallas");                                                                                           // the survivor changes after the review
        Assert.Equal(("patient_changed", 409), await CodeAsync(request));
        Assert.Equal(after.Ann, await CountsAsync(Ann)); Assert.Equal(after.Bo, await CountsAsync(Bo));
        Assert.Equal(0, await Scalar("SELECT COUNT(*) AS [Value] FROM [PatientUnmerges]"));

        async Task<(string, int)> CodeAsync(UnmergeRequest r) { var e = await RefusedAsync(() => UnmergeAsync(r)); return (e.Code, e.StatusCode); }
    }

    [Fact]
    public async Task A_repeat_with_the_same_key_is_the_undo_already_made_and_the_same_key_for_another_merge_is_refused()
    {
        await GiveRetiringChartAsync(); await GiveSurvivingChartAsync();
        var merged = await MergeAsync(Ann, Bo);
        var done = await UnmergeAsync(await RequestAsync(merged.MergeId), "undo-once");
        var counts = await CountsAsync(Bo);
        var again = await UnmergeAsync(new UnmergeRequest(merged.MergeId, "Different words", null, null, true), "undo-once");
        Assert.True(again.Replayed); Assert.Equal(done.UnmergeId, again.UnmergeId); Assert.Equal(done.RowsMovedBack, again.RowsMovedBack);
        Assert.Equal(counts, await CountsAsync(Bo));
        Assert.Equal(1, await Scalar("SELECT COUNT(*) AS [Value] FROM [PatientUnmerges]"));
        Assert.Equal(2, await Scalar("SELECT COUNT(*) AS [Value] FROM [AuditLogEntries] WHERE [EventType] = 'PatientUnmerged'"));
        Assert.Equal("key_reused", (await RefusedAsync(() => UnmergeAsync(new UnmergeRequest(Guid.NewGuid(), "x", null, null, true), "undo-once"))).Code);
    }

    [Fact]
    public async Task A_merge_made_before_ledgers_existed_cannot_be_undone_and_neither_can_one_whose_survivor_was_merged_away_since()
    {
        var oldSource = await S.PatientAsync("Cy", "Kim"); var oldSurvivor = await S.PatientAsync("Dee", "Kim");
        var lineage = new PatientMerge { Id = Guid.NewGuid(), MergeKey = "old-1", SourcePatientId = oldSource, SurvivorPatientId = oldSurvivor, MergedByUserId = S.Actor, MergedAtUtc = DateTimeOffset.UtcNow, Reason = "Old merge" };
        await using (var db = Fixture.CreateContext())
        {
            db.PatientMerges.Add(lineage); await db.SaveChangesAsync();
            await db.Database.ExecuteSqlAsync($"UPDATE [Patients] SET [IsActive] = 0, [MergedAtUtc] = {lineage.MergedAtUtc} WHERE [Id] = {oldSource}");
        }
        var old = await RefusedAsync(() => UnmergeAsync(new UnmergeRequest(lineage.Id, "x", null, null, true), "old"));
        Assert.Equal(("unmerge_unavailable", 409), (old.Code, old.StatusCode));
        Assert.Equal("unmerge_unavailable", (await RefusedAsync(() => WithDb(db => new PatientUnmergeService(db, Clock).PreviewAsync(lineage.Id, default)))).Code);

        await GiveRetiringChartAsync(); await GiveSurvivingChartAsync();
        var first = await MergeAsync(Ann, Bo, "first");
        var third = await S.PatientAsync("Eli", "Moss");
        var preview = await WithDb(db => new PatientMergeService(db, Clock).PreviewAsync(Bo, third, default));
        await WithDb(db => new PatientMergeService(db, Clock).MergeAsync(S.Actor, new MergeRequest(Bo, third, "Bo into Eli", preview.SourceRowVersion, preview.SurvivorRowVersion, preview.Conflicts.ToDictionary(c => c.Field, _ => "Survivor"), true, true), "second", default));
        var blocked = await RefusedAsync(() => UnmergeAsync(new UnmergeRequest(first.MergeId, "x", null, null, true), "undo-first"));
        Assert.Equal(("survivor_merged", 409), (blocked.Code, blocked.StatusCode));                                                     // undo the later merge first
    }

    [Fact]
    public async Task Records_made_after_the_merge_that_depend_on_rows_that_would_move_back_stop_the_undo_and_nothing_changes()
    {
        await GiveRetiringChartAsync(); await GiveSurvivingChartAsync();
        var merged = await MergeAsync(Ann, Bo);
        var diagnosis = (await IdsAsync("Diagnoses", Bo)).First();                                                                      // one of the retired patient's, on the survivor now
        var newFinding = await FindingAsync("37", "O", patient: Bo);
        await LinkAsync(diagnosis, newFinding);                                                                                         // a record made after the merge, linking a row of each chart
        var after = (Ann: await CountsAsync(Ann), Bo: await CountsAsync(Bo));

        var preview = await WithDb(db => new PatientUnmergeService(db, Clock).PreviewAsync(merged.MergeId, default));
        Assert.False(preview.CanUndo);
        Assert.Contains(preview.Blockers, b => b.Code == "newer_records" && b.Message.Contains("DiagnosisLinks"));
        var refused = await RefusedAsync(() => UnmergeAsync(new UnmergeRequest(merged.MergeId, "x", preview.SourceRowVersion, preview.SurvivorRowVersion, true)));
        Assert.Equal(("unmerge_blocked", 409), (refused.Code, refused.StatusCode));
        Assert.Equal(after.Ann, await CountsAsync(Ann)); Assert.Equal(after.Bo, await CountsAsync(Bo));                                  // the moves were rolled back with everything else
        Assert.Equal(1, await Scalar($"SELECT COUNT(*) AS [Value] FROM [Patients] WHERE [Id] = '{Ann}' AND [MergedAtUtc] IS NOT NULL"));
        Assert.Equal(0, await Scalar("SELECT COUNT(*) AS [Value] FROM [PatientUnmerges]"));
    }

    // ---------- failing part way ----------

    [Theory]
    [InlineData("while the rows move back", "ToothFindings", "AFTER UPDATE", "IF CONVERT(int, SESSION_CONTEXT(N'alveara.patient_merge')) = 1 THROW 59020, 'a move failed', 1;")]
    [InlineData("when the undo is recorded", "PatientUnmerges", "AFTER INSERT", "THROW 59021, 'the undo could not be recorded', 1;")]
    [InlineData("when the retired record is made active again", "Patients", "AFTER UPDATE", "IF ISNULL(CONVERT(int, SESSION_CONTEXT(N'alveara.patient_unmerge')), 0) = 1 THROW 59022, 'the record could not be reactivated', 1;")]
    [InlineData("when the audit entries are written", "AuditLogEntries", "AFTER INSERT", "IF EXISTS (SELECT 1 FROM inserted WHERE [EventType] = 'PatientUnmerged') THROW 59023, 'the audit store is down', 1;")]
    public async Task An_undo_that_fails_part_way_changes_nothing_and_the_same_request_succeeds_once_the_fault_is_gone(string point, string table, string when, string body)
    {
        await GiveRetiringChartAsync(); await GiveSurvivingChartAsync();
        var merged = await MergeAsync(Ann, Bo);
        var after = (Ann: await CountsAsync(Ann), Bo: await CountsAsync(Bo));
        var request = await RequestAsync(merged.MergeId);
        await using (var db = Fixture.CreateContext()) await db.Database.ExecuteSqlRawAsync($"CREATE TRIGGER [TR_TestFault] ON [{table}] {when} AS BEGIN {body} END");
        try
        {
            var failure = await Assert.ThrowsAnyAsync<Exception>(() => UnmergeAsync(request, "recover"));
            Assert.InRange(Assert.IsType<SqlException>(failure is DbUpdateException { InnerException: not null } d ? d.InnerException : failure).Number, 59020, 59023);
            Assert.Equal(after.Ann, await CountsAsync(Ann)); Assert.Equal(after.Bo, await CountsAsync(Bo));
            Assert.Equal(0, await Scalar("SELECT COUNT(*) AS [Value] FROM [PatientUnmerges]"));
            Assert.Equal(0, await Scalar("SELECT COUNT(*) AS [Value] FROM [AuditLogEntries] WHERE [EventType] = 'PatientUnmerged'"));
            Assert.Equal(1, await Scalar($"SELECT COUNT(*) AS [Value] FROM [Patients] WHERE [Id] = '{Ann}' AND [MergedAtUtc] IS NOT NULL AND [IsActive] = 0"));
            for (var i = 0; i < 3; i++) Assert.Contains("51086", (await Assert.ThrowsAnyAsync<Exception>(() => WithDb(db => db.Database.ExecuteSqlAsync($"UPDATE [TreatmentPlans] SET [PatientId] = {Ann} WHERE [PatientId] = {Bo}")))).ToString());   // no value leaked onto a pooled connection
        }
        finally { await using var db = Fixture.CreateContext(); await db.Database.ExecuteSqlRawAsync("DROP TRIGGER [TR_TestFault]"); }

        var done = await UnmergeAsync(request, "recover");                                                                              // the very same request, once it works
        Assert.False(done.Replayed);
        Assert.Equal(0, await AgreementViolationsAsync());
        Assert.Equal(1, await Scalar($"SELECT COUNT(*) AS [Value] FROM [Patients] WHERE [Id] = '{Ann}' AND [MergedAtUtc] IS NULL AND [IsActive] = 1"));
        Assert.True(point.Length > 0);
    }
}
