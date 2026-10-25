using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Alveara.Api.Architecture.Patients.Merge;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-017-C01, the database side of an undo, on its own against a real SQL Server: a merge keeps a ledger of the exact rows it moved, by the key each row has afterwards (and the ledger and the undo records are append-only,
/// 51236 and 51237); an undo is recorded only for a merge that still holds (51238); a record is merged away once at a time, and again only after its first merge was undone (51244, 51231); a merged record can come back
/// only through an undone merge, only with the unmerge session value, and only its active flag, merged mark and who and when change (51233); and the migration rolls back and forward and refuses to roll back over a
/// ledger (51245).
/// </summary>
public class PatientUnmergeSchemaTests : MergeChartTestBase
{
    private const string BeforeUnmerge = "20261025200612_GuardRetiredPatientRecords";

    private async Task<(Guid Source, Guid Survivor, Guid MergeId, DateTimeOffset At)> MergedPairAsync(bool service = false)
    {
        var source = await S.PatientAsync($"Src{Guid.NewGuid():N}"[..10], "Merge"); var survivor = await S.PatientAsync($"Sur{Guid.NewGuid():N}"[..10], "Merge");
        if (service)
        {
            var preview = await WithDb(db => new PatientMergeService(db, Clock).PreviewAsync(source, survivor, default));
            var done = await WithDb(db => new PatientMergeService(db, Clock).MergeAsync(S.Actor, new MergeRequest(source, survivor, "Same person", preview.SourceRowVersion, preview.SurvivorRowVersion, null, true, true), Guid.NewGuid().ToString("N"), default));
            return (source, survivor, done.MergeId, done.MergedAtUtc);
        }
        var m = new PatientMerge { Id = Guid.NewGuid(), MergeKey = $"k-{Guid.NewGuid():N}", SourcePatientId = source, SurvivorPatientId = survivor, MergedByUserId = S.Actor, MergedAtUtc = DateTimeOffset.UtcNow, Reason = "Same person" };
        await AddAsync(m);
        await using var db = Fixture.CreateContext();
        await db.Database.ExecuteSqlAsync($"UPDATE [Patients] SET [IsActive] = 0, [MergedAtUtc] = {m.MergedAtUtc} WHERE [Id] = {source}");
        return (source, survivor, m.Id, m.MergedAtUtc);
    }

    private async Task AddAsync(params object[] rows) { await using var db = Fixture.CreateContext(); db.AddRange(rows); await db.SaveChangesAsync(); }

    private PatientUnmerge Unmerge(Guid mergeId, Action<PatientUnmerge>? change = null)
    {
        var u = new PatientUnmerge { Id = Guid.NewGuid(), MergeId = mergeId, UnmergeKey = $"u-{Guid.NewGuid():N}", UnmergedByUserId = S.Actor, UnmergedAtUtc = DateTimeOffset.UtcNow, Reason = "Not the same person after all", RowsMovedBack = 0 };
        change?.Invoke(u);
        return u;
    }

    private static async Task<Microsoft.Data.SqlClient.SqlException> Refusal(Func<Task> action)
    {
        var e = await Assert.ThrowsAnyAsync<Exception>(action);
        return Assert.IsType<Microsoft.Data.SqlClient.SqlException>(e is DbUpdateException { InnerException: not null } d ? d.InnerException : e);
    }

    private async Task<int> Scalar(string sql) { await using var db = Fixture.CreateContext(); return await db.Database.SqlQueryRaw<int>(sql).SingleAsync(); }

    /// <summary>Runs one UPDATE of a patient on one connection with the unmerge session value set, the way the undo service will.</summary>
    private async Task<int> UnmarkAsync(string setSql, int flag = 1)
    {
        await using var db = Fixture.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync("EXEC sp_set_session_context N'alveara.patient_unmerge', {0}", flag);
        var n = await db.Database.ExecuteSqlRawAsync(setSql);
        await tx.CommitAsync();
        return n;
    }

    // ---------- the ledger ----------

    [Fact]
    public async Task A_merge_writes_a_ledger_of_exactly_the_rows_it_moved_by_the_key_each_has_afterwards_and_the_ledger_is_append_only()
    {
        await GiveRetiringChartAsync(); await GiveSurvivingChartAsync();
        var preview = await WithDb(db => new PatientMergeService(db, Clock).PreviewAsync(Ann, Bo, default));
        var before = await CountsAsync(Ann);
        var done = await WithDb(db => new PatientMergeService(db, Clock).MergeAsync(S.Actor, new MergeRequest(Ann, Bo, "Same person", preview.SourceRowVersion, preview.SurvivorRowVersion, preview.Conflicts.ToDictionary(c => c.Field, _ => "Survivor"), true, true), "ledger-1", default));

        await using var db = Fixture.CreateContext();
        Assert.True((await db.PatientMerges.AsNoTracking().SingleAsync(m => m.Id == done.MergeId)).HasLedger);
        var ledger = await db.PatientMergeMovedRows.AsNoTracking().Where(r => r.MergeId == done.MergeId).ToListAsync();
        Assert.Equal(done.Moves.Sum(m => m.Rows), ledger.Count);                                                                    // exactly the rows that moved
        foreach (var column in PatientOwnedData.All)
            Assert.Equal(done.Moves.Single(m => m.Table == column.Table && m.Column == column.Column).Rows, ledger.Count(r => r.TableName == column.Table && r.ColumnName == column.Column));
        Assert.Equal(before["TreatmentPlans.PatientId"], ledger.Count(r => r.TableName == "TreatmentPlans"));
        var recall = Assert.Single(ledger.Where(r => r.TableName == "PatientRecallPlans"));
        Assert.Equal(Bo.ToString().ToUpperInvariant(), recall.RowKey);                                                              // a table keyed by the patient id: the key it has now, on the survivor
        var plansOfSurvivor = (await db.TreatmentPlans.AsNoTracking().Where(p => p.PatientId == Bo).Select(p => p.Id).ToListAsync()).ToHashSet();
        Assert.All(ledger.Where(r => r.TableName == "TreatmentPlans"), r => Assert.Contains(Guid.Parse(r.RowKey), plansOfSurvivor));          // each ledger key is a row the survivor owns now

        Assert.Equal(51236, (await Refusal(() => db.Database.ExecuteSqlAsync($"UPDATE [PatientMergeMovedRows] SET [TableName] = [TableName] WHERE [MergeId] = {done.MergeId}"))).Number);
        Assert.Equal(51236, (await Refusal(() => db.Database.ExecuteSqlAsync($"DELETE FROM [PatientMergeMovedRows] WHERE [MergeId] = {done.MergeId}"))).Number);
        Assert.Equal(547, (await Refusal(() => AddAsync(new PatientMergeMovedRow { MergeId = Guid.NewGuid(), TableName = "TreatmentPlans", ColumnName = "PatientId", RowKey = "x" }))).Number);       // a ledger names a merge
        Assert.Equal(547, (await Refusal(() => AddAsync(new PatientMergeMovedRow { MergeId = done.MergeId, TableName = " ", ColumnName = "PatientId", RowKey = "x" }))).Number);
    }

    // ---------- the undo record ----------

    [Fact]
    public async Task An_undo_is_recorded_once_for_a_merge_that_still_holds_and_never_changed_or_deleted()
    {
        var (_, _, mergeId, _) = await MergedPairAsync();
        var u = Unmerge(mergeId); await AddAsync(u);
        Assert.Equal(2601, (await Refusal(() => AddAsync(Unmerge(mergeId)))).Number);                                                // a merge is undone once
        Assert.Equal(547, (await Refusal(() => AddAsync(Unmerge(Guid.NewGuid())))).Number);
        var (_, _, second, _) = await MergedPairAsync();
        Assert.Equal(2601, (await Refusal(() => AddAsync(Unmerge(second, x => x.UnmergeKey = u.UnmergeKey)))).Number);                 // a key is one undo
        Assert.Equal(547, (await Refusal(() => AddAsync(Unmerge(second, x => x.Reason = "  ")))).Number);
        Assert.Equal(547, (await Refusal(() => AddAsync(Unmerge(second, x => x.RowsMovedBack = -1)))).Number);
        await using var db = Fixture.CreateContext();
        Assert.Equal(51237, (await Refusal(() => db.Database.ExecuteSqlAsync($"UPDATE [PatientUnmerges] SET [Reason] = N'x' WHERE [Id] = {u.Id}"))).Number);
        Assert.Equal(51237, (await Refusal(() => db.Database.ExecuteSqlAsync($"DELETE FROM [PatientUnmerges] WHERE [Id] = {u.Id}"))).Number);
    }

    [Fact]
    public async Task An_undo_is_refused_for_a_merge_whose_record_is_not_merged_any_more()
    {
        var source = await S.PatientAsync("Cy", "Kim"); var survivor = await S.PatientAsync("Dee", "Kim");
        var lineage = new PatientMerge { Id = Guid.NewGuid(), MergeKey = $"k-{Guid.NewGuid():N}", SourcePatientId = source, SurvivorPatientId = survivor, MergedByUserId = S.Actor, MergedAtUtc = DateTimeOffset.UtcNow, Reason = "x" };
        await AddAsync(lineage);                                                                                                       // a lineage, but the record was never marked merged
        Assert.Equal(51238, (await Refusal(() => AddAsync(Unmerge(lineage.Id)))).Number);
    }

    // ---------- one merge at a time ----------

    [Fact]
    public async Task A_record_is_merged_away_once_at_a_time_and_again_only_after_the_first_merge_was_undone_and_a_merged_away_record_is_never_a_survivor_until_then()
    {
        var (source, survivor, first, _) = await MergedPairAsync();
        var other = await S.PatientAsync("Cy", "Kim");
        PatientMerge Lineage(Guid s, Guid t) => new() { Id = Guid.NewGuid(), MergeKey = $"k-{Guid.NewGuid():N}", SourcePatientId = s, SurvivorPatientId = t, MergedByUserId = S.Actor, MergedAtUtc = DateTimeOffset.UtcNow, Reason = "x" };
        Assert.Equal(51244, (await Refusal(() => AddAsync(Lineage(source, other)))).Number);                                          // a second merge of the same record
        Assert.Equal(51231, (await Refusal(() => AddAsync(Lineage(other, source)))).Number);                                          // into a record that is merged away
        await AddAsync(Unmerge(first));                                                                                              // once the first merge is undone
        await AddAsync(Lineage(other, source));                                                                                       // the record may be a survivor
        var third = await S.PatientAsync("Dee", "Kim");
        await AddAsync(Lineage(source, third));                                                                                       // and may be merged away again
        Assert.Equal(3, await Scalar("SELECT COUNT(*) AS [Value] FROM [PatientMerges] WHERE [SourcePatientId] IN ('" + source + "', '" + other + "')"));
    }

    // ---------- coming back ----------

    [Fact]
    public async Task A_merged_record_comes_back_only_through_an_undone_merge_with_the_unmerge_value_and_only_its_active_flag_merged_mark_and_who_and_when_change()
    {
        var (source, _, mergeId, _) = await MergedPairAsync();
        string Unmark(string extra = "") => $"UPDATE [Patients] SET [IsActive] = 1, [MergedAtUtc] = NULL, [UpdatedAtUtc] = SYSDATETIMEOFFSET(){extra} WHERE [Id] = '{source}'";
        await using (var db = Fixture.CreateContext())
            Assert.Equal(51233, (await Refusal(() => db.Database.ExecuteSqlRawAsync(Unmark()))).Number);                              // not on its own
        Assert.Equal(51233, (await Refusal(() => UnmarkAsync(Unmark()))).Number);                                                    // not with the value alone: no undone merge yet
        await AddAsync(Unmerge(mergeId));
        await using (var db = Fixture.CreateContext())
            Assert.Equal(51233, (await Refusal(() => db.Database.ExecuteSqlRawAsync(Unmark()))).Number);                              // not without the value, even for an undone merge
        Assert.Equal(51233, (await Refusal(() => UnmarkAsync(Unmark(), flag: 0))).Number);
        Assert.Equal(51233, (await Refusal(() => UnmarkAsync(Unmark(", [LastName] = N'Changed'")))).Number);                       // nothing else may change with it
        Assert.Equal(51233, (await Refusal(() => UnmarkAsync($"UPDATE [Patients] SET [LastName] = N'Changed' WHERE [Id] = '{source}'"))).Number);
        Assert.Equal(547, (await Refusal(() => UnmarkAsync($"UPDATE [Patients] SET [IsActive] = 1 WHERE [Id] = '{source}'"))).Number);       // active but still marked merged: the check constraint answers first
        Assert.Equal(1, await UnmarkAsync(Unmark()));                                                                                // the undone merge, the value, only the allowed columns
        Assert.Equal(1, await Scalar($"SELECT COUNT(*) AS [Value] FROM [Patients] WHERE [Id] = '{source}' AND [IsActive] = 1 AND [MergedAtUtc] IS NULL"));
    }

    // ---------- the migration ----------

    [Fact]
    public async Task The_migration_rolls_back_and_forward_and_refuses_to_roll_back_over_a_ledger_or_an_undone_merge()
    {
        await using (var db = Fixture.CreateContext()) await db.GetInfrastructure().GetRequiredService<IMigrator>().MigrateAsync(BeforeUnmerge);
        await using (var db = Fixture.CreateContext())
        {
            Assert.Equal(0, await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM sys.tables WHERE name IN ('PatientMergeMovedRows','PatientUnmerges')").SingleAsync());
            Assert.Equal(0, await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM sys.sql_modules WHERE definition LIKE N'%alveara.patient_unmerge%'").SingleAsync());   // the Patients trigger is back as it was
            Assert.Equal(1, await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM sys.indexes WHERE name = 'IX_PatientMerges_SourcePatientId' AND is_unique = 1").SingleAsync());
        }
        await using (var db = Fixture.CreateContext()) await db.Database.MigrateAsync();
        await using (var db = Fixture.CreateContext())
        {
            Assert.Equal(2, await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM sys.tables WHERE name IN ('PatientMergeMovedRows','PatientUnmerges')").SingleAsync());
            Assert.Equal(1, await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM sys.sql_modules WHERE definition LIKE N'%alveara.patient_unmerge%'").SingleAsync());
            Assert.Equal(0, await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM sys.indexes WHERE name = 'IX_PatientMerges_SourcePatientId' AND is_unique = 1").SingleAsync());
        }
        await MergedPairAsync(service: true);                                                                                        // a merge made by the service has a ledger (a pair with no rows has none to hold, so the undo record decides)
        var (_, _, mergeId, _) = await MergedPairAsync();
        await AddAsync(Unmerge(mergeId));
        var e = await Refusal(async () => { await using var db = Fixture.CreateContext(); await db.GetInfrastructure().GetRequiredService<IMigrator>().MigrateAsync(BeforeUnmerge); });
        Assert.Equal(51245, e.Number);
    }
}
