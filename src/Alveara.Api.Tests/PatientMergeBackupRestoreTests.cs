using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Alveara.Api.Architecture.Backup;
using Alveara.Api.Architecture.Patients.Merge;
using Alveara.Api.Data;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>
/// STORY-017, the recovery drill for a merge against a real SQL Server restore: a merged chart is backed up and restored into an isolated target, and the restored database holds exactly what the backup held: the
/// retired patient in place and marked merged, the lineage with every move and answer, the chart on the surviving patient, the audit entries with who and when, and the guards (the merge-scope and merged-record triggers),
/// so the restored database refuses what the live one refuses. A merge made after the backup is not in the restore, and the live database is untouched by the drill.
/// </summary>
[Collection(ParallelismCollections.SerialServer)]
public class PatientMergeBackupRestoreTests : TreatmentPlanTestBase
{
    private BackupTestEnvironment _env = null!;
    private readonly List<string> _restored = [];

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _env = new BackupTestEnvironment(Fixture);
        await _env.ResetBackupStateAsync();
        await _env.ConfigureRecoveryKeyAsync();
    }

    public override async Task DisposeAsync()
    {
        foreach (var name in _restored) await _env.RestoreProvider.DropDatabaseAsync(name, default);
        _env.Dispose();
        await base.DisposeAsync();
    }

    private async Task<MergeResult> MergeAsync(Guid source, Guid survivor, string reason)
    {
        var preview = await WithDb(db => new PatientMergeService(db, Clock).PreviewAsync(source, survivor, default));
        return await WithDb(db => new PatientMergeService(db, Clock).MergeAsync(S.Actor, new MergeRequest(source, survivor, reason, preview.SourceRowVersion, preview.SurvivorRowVersion, null, true, true), Guid.NewGuid().ToString("N"), default));
    }

    private async Task<BackupRecord> BackupAsync()
    {
        await using var db = _env.NewDb();
        return await _env.NewBackupService(db).RunBackupAsync(BackupKind.Manual, $"manual:{Guid.NewGuid():N}", S.Actor, default);
    }

    private async Task<RestoreDrillRecord> DrillAsync(Guid recordId)
    {
        await using var db = _env.NewDb();
        var drill = await _env.NewRestoreService(db).RestoreDrillAsync(recordId, _env.Key.EncryptedPrivateKeyPem, BackupTestEnvironment.Passphrase, S.Actor, default);
        if (drill.TargetDatabase is not null) _restored.Add(drill.TargetDatabase);
        return drill;
    }

    [Fact]
    public async Task A_merged_chart_survives_a_backup_and_restore_with_its_lineage_its_audit_and_its_guards_and_a_later_merge_is_not_in_it()
    {
        var diagnosis = await DiagnosisAsync(Ann, "Gingivitis");
        var procedure = await ProcedureAsync(code: $"LOCAL-{Guid.NewGuid():N}"[..14]);
        await CreateAsync([Item(diagnosis, procedure.Summary.Id)], title: "Ann's plan", patient: Ann);
        var merged = await MergeAsync(Ann, Bo, "Registered twice");
        var plansOfSurvivor = await WithDb(db => db.TreatmentPlans.CountAsync(p => p.PatientId == Bo));
        Assert.True(plansOfSurvivor > 0);

        var record = await BackupAsync();
        var later = (await S.PatientAsync("Cy", "Kim"), await S.PatientAsync("Dee", "Kim"));
        await MergeAsync(later.Item1, later.Item2, "Merged after the backup");                                          // life goes on after the backup

        var drill = await DrillAsync(record.Id);
        Assert.Equal("Succeeded", drill.Outcome);
        var checks = JsonSerializer.Deserialize<List<ValidationCheck>>(drill.ValidationJson!, BackupManifest.Json)!;
        Assert.All(checks.Where(c => c.Blocking), c => Assert.True(c.Passed, c.Name + ": " + c.Detail));

        await using var restored = new AlveraDbContext(new DbContextOptionsBuilder<AlveraDbContext>().UseSqlServer(_env.RestoreProvider.ConnectionStringFor(drill.TargetDatabase!)).Options);

        // the retired patient is in place, marked merged and inactive; the lineage holds who, when, why, every move and every answer
        var retired = await restored.Patients.AsNoTracking().SingleAsync(p => p.Id == Ann);
        Assert.False(retired.IsActive); Assert.Equal(merged.MergedAtUtc, retired.MergedAtUtc);
        var lineage = await restored.PatientMerges.AsNoTracking().SingleAsync();                                          // only the merge made before the backup
        Assert.Equal((merged.MergeId, Ann, Bo, S.Actor, "Registered twice", merged.MergedAtUtc), (lineage.Id, lineage.SourcePatientId, lineage.SurvivorPatientId, lineage.MergedByUserId, lineage.Reason, lineage.MergedAtUtc));
        Assert.Equal(PatientOwnedData.All.Count, await restored.PatientMergeMoves.CountAsync(m => m.MergeId == lineage.Id));
        Assert.Equal(merged.Moves.Sum(m => m.Rows), await restored.PatientMergeMoves.Where(m => m.MergeId == lineage.Id).SumAsync(m => m.RowsMoved));
        Assert.Equal(merged.Resolutions.Count, await restored.PatientMergeResolutions.CountAsync(x => x.MergeId == lineage.Id));

        // the chart is on the surviving patient, and the audit entries say who and when
        Assert.Equal(plansOfSurvivor, await restored.TreatmentPlans.CountAsync(p => p.PatientId == Bo));
        Assert.Equal(0, await restored.TreatmentPlans.CountAsync(p => p.PatientId == Ann));
        var audit = await restored.AuditLogEntries.AsNoTracking().Where(a => a.EventType == "PatientMerged").ToListAsync();
        Assert.Equal(2, audit.Count);
        Assert.All(audit, a => { Assert.Equal(S.Actor, a.PerformedByUserAccountId); Assert.Equal(lineage.Id, a.CorrelationId); });

        // the restored database holds the guards and refuses what the live one refuses
        Assert.Equal(PatientOwnedData.Tables.Count, await restored.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM sys.triggers WHERE name LIKE 'TR[_]%[_]MergeScope'").SingleAsync());
        Assert.Equal(3, await restored.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM sys.triggers WHERE name IN ('TR_Patients_Merged', 'TR_PatientMerges_Survivor', 'TR_PatientMerges_Immutable')").SingleAsync());
        Assert.Contains("51233", (await Assert.ThrowsAnyAsync<Exception>(() => restored.Database.ExecuteSqlAsync($"UPDATE [Patients] SET [LastName] = N'Changed' WHERE [Id] = {Ann}"))).ToString());
        Assert.Contains("51230", (await Assert.ThrowsAnyAsync<Exception>(() => restored.Database.ExecuteSqlAsync($"DELETE FROM [PatientMergeMoves]"))).ToString());
        Assert.Contains("51086", (await Assert.ThrowsAnyAsync<Exception>(() => restored.Database.ExecuteSqlAsync($"UPDATE [TreatmentPlans] SET [PatientId] = {Ann} WHERE [PatientId] = {Bo}"))).ToString());   // no merge value: an immutable record stays put

        // and the live database is untouched by the drill: both merges are there
        await using var live = Fixture.CreateContext();
        Assert.Equal(2, await live.PatientMerges.CountAsync());
    }

    [Fact]
    public async Task The_ledger_the_undo_records_and_the_dismissals_survive_a_restore_and_a_merge_can_be_undone_in_the_restored_database_without_touching_the_live_one()
    {
        var diagnosis = await DiagnosisAsync(Ann, "Gingivitis");
        var procedure = await ProcedureAsync(code: $"LOCAL-{Guid.NewGuid():N}"[..14]);
        await CreateAsync([Item(diagnosis, procedure.Summary.Id)], title: "Ann's plan", patient: Ann);
        var plansOfAnn = await WithDb(db => db.TreatmentPlans.CountAsync(p => p.PatientId == Ann));
        var merged = await MergeAsync(Ann, Bo, "Registered twice");
        var other = (await S.PatientAsync("Cy", "Kim"), await S.PatientAsync("Dee", "Kim"));
        var dismissed = await WithDb(db => new PatientDuplicateReviewService(db, Clock).DismissAsync(S.Actor, new DismissRequest(other.Item1, other.Item2, "Twins"), default));

        var drill = await DrillAsync((await BackupAsync()).Id);
        Assert.Equal("Succeeded", drill.Outcome);
        await using var restored = new AlveraDbContext(new DbContextOptionsBuilder<AlveraDbContext>().UseSqlServer(_env.RestoreProvider.ConnectionStringFor(drill.TargetDatabase!)).Options);
        Assert.Equal(merged.Moves.Sum(m => m.Rows), await restored.PatientMergeMovedRows.CountAsync(r => r.MergeId == merged.MergeId));      // the ledger came back whole
        Assert.True((await restored.PatientMerges.AsNoTracking().SingleAsync()).HasLedger);
        Assert.Equal(dismissed.Id, (await restored.PatientDuplicateDismissals.AsNoTracking().SingleAsync()).Id);                               // and the dismissal
        Assert.Equal(4, await restored.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM sys.triggers WHERE name IN ('TR_PatientMergeMovedRows_Immutable', 'TR_PatientUnmerges_Immutable', 'TR_PatientDuplicateDismissals_Guard', 'TR_PatientUnmerges_Active')").SingleAsync());                       // the guards came with it

        // the merge is undone in the restored database, which is a different database from the live one
        var preview = await new PatientUnmergeService(restored, Clock).PreviewAsync(merged.MergeId, default);
        Assert.True(preview.CanUndo);
        var done = await new PatientUnmergeService(restored, Clock).UnmergeAsync(S.Actor, new UnmergeRequest(merged.MergeId, "Undone in the restored copy", preview.SourceRowVersion, preview.SurvivorRowVersion, true), "restored-undo", default);
        Assert.Equal(merged.Moves.Sum(m => m.Rows), done.RowsMovedBack);
        restored.ChangeTracker.Clear();
        Assert.Equal(plansOfAnn, await restored.TreatmentPlans.CountAsync(p => p.PatientId == Ann));
        var ann = await restored.Patients.AsNoTracking().SingleAsync(p => p.Id == Ann);
        Assert.True(ann.IsActive); Assert.Null(ann.MergedAtUtc);

        await using var live = Fixture.CreateContext();
        Assert.Equal(0, await live.PatientUnmerges.CountAsync());                                                                              // the live database still has the merge in force
        Assert.NotNull((await live.Patients.AsNoTracking().SingleAsync(p => p.Id == Ann)).MergedAtUtc);
        Assert.Equal(0, await live.TreatmentPlans.CountAsync(p => p.PatientId == Ann));
    }
}
