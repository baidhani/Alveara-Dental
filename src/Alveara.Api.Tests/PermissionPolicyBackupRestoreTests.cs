using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Alveara.Api.Architecture.Backup;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Permissions;
using Alveara.Api.Data;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N017, the recovery drill for the permission policy against a real SQL Server restore: policy overrides (valid and invalid), the immutable change history, the delegations, the policy version and the
/// database guards (the history and version triggers and the last-administrator trigger) are backed up and restored into an isolated target, and the restored database holds exactly what the backup held. Policy
/// changes made after the backup are not in the restore (it returns the state of the backup), and the restored database still refuses what the live one refuses.
/// </summary>
[Collection(ParallelismCollections.SerialServer)]
public class PermissionPolicyBackupRestoreTests : SafetyTestBase
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

    private AlveraDbContext RestoredDb(RestoreDrillRecord drill) =>
        new(new DbContextOptionsBuilder<AlveraDbContext>().UseSqlServer(_env.RestoreProvider.ConnectionStringFor(drill.TargetDatabase!)).Options);

    private async Task SeedPolicyAsync()
    {
        await using var db = Fixture.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync();
        for (var i = 0; i < 3; i++) await db.Database.ExecuteSqlAsync($"UPDATE [PermissionPolicyState] SET [Version] = [Version] + 1, [UpdatedAtUtc] = SYSDATETIMEOFFSET() WHERE [Id] = 1");
        db.RolePermissionOverrides.AddRange(
            new RolePermissionOverride { Id = Guid.NewGuid(), Role = "Dentist", Permission = nameof(Permission.ViewBilling), Effect = OverrideEffects.Revoke, Reason = "Dentists no longer see billing", ChangedByUserId = S.Actor, ChangedAtUtc = DateTimeOffset.UtcNow },
            new RolePermissionOverride { Id = Guid.NewGuid(), Role = "Billing", Permission = nameof(Permission.ImportDocuments), Effect = OverrideEffects.Grant, Reason = "Billing imports statements", ChangedByUserId = S.Actor, ChangedAtUtc = ChangedAtUtcAddOne() },
            new RolePermissionOverride { Id = Guid.NewGuid(), Role = "Wizard", Permission = "NoSuchPermission", Effect = OverrideEffects.Revoke, Reason = "invalid on purpose", ChangedByUserId = S.Actor, ChangedAtUtc = DateTimeOffset.UtcNow });
        db.RolePermissionHistories.AddRange(
            new RolePermissionHistory { Id = Guid.NewGuid(), PolicyVersion = 1, Kind = PolicyChangeKinds.Revoked, Role = "Dentist", Permission = nameof(Permission.ViewBilling), OldEffective = true, NewEffective = false, Reason = "Dentists no longer see billing", ChangedByUserId = S.Actor, ChangedAtUtc = DateTimeOffset.UtcNow },
            new RolePermissionHistory { Id = Guid.NewGuid(), PolicyVersion = 2, Kind = PolicyChangeKinds.Granted, Role = "Billing", Permission = nameof(Permission.ImportDocuments), OldEffective = false, NewEffective = true, Reason = "Billing imports statements", ChangedByUserId = S.Actor, ChangedAtUtc = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    private static DateTimeOffset ChangedAtUtcAddOne() => DateTimeOffset.UtcNow.AddMinutes(1);

    [Fact]
    public async Task Every_policy_table_survives_a_backup_and_restore_with_its_rows_version_and_guards()
    {
        await SeedPolicyAsync();
        var record = await BackupAsync();

        // life goes on after the backup: another change is committed. The restore returns the state of the backup, not of today.
        await using (var db = Fixture.CreateContext())
        {
            await db.Database.ExecuteSqlAsync($"UPDATE [PermissionPolicyState] SET [Version] = [Version] + 1 WHERE [Id] = 1");
            await db.Database.ExecuteSqlAsync($"DELETE FROM [RolePermissionOverrides] WHERE [Role] = N'Dentist'");
        }

        var drill = await DrillAsync(record.Id);
        Assert.Equal("Succeeded", drill.Outcome);
        var checks = JsonSerializer.Deserialize<List<ValidationCheck>>(drill.ValidationJson!, BackupManifest.Json)!;
        Assert.All(checks.Where(c => c.Blocking), c => Assert.True(c.Passed, c.Name + ": " + c.Detail));

        await using var restored = RestoredDb(drill);
        Assert.Equal(3L, (await restored.PermissionPolicyStates.AsNoTracking().SingleAsync()).Version);               // the version of the backup, not the later one
        var overrides = await restored.RolePermissionOverrides.AsNoTracking().OrderBy(o => o.Role).ToListAsync();
        Assert.Equal(new[] { "Billing", "Dentist", "Wizard" }, overrides.Select(o => o.Role).ToArray());              // the later delete is not in the restore
        Assert.Equal(("Revoke", "Dentists no longer see billing"), (overrides.Single(o => o.Role == "Dentist").Effect, overrides.Single(o => o.Role == "Dentist").Reason));
        Assert.Equal("Wizard", overrides.Single(o => o.Permission == "NoSuchPermission").Role);                       // invalid data is restored faithfully, not repaired
        Assert.Equal(2, await restored.RolePermissionHistories.CountAsync());
        Assert.Equal(new long[] { 1, 2 }, await restored.RolePermissionHistories.OrderBy(h => h.PolicyVersion).Select(h => h.PolicyVersion).ToArrayAsync());
        var delegations = await restored.PermissionDelegations.AsNoTracking().ToListAsync();
        Assert.Equal(5, delegations.Count);
        Assert.All(delegations, d => Assert.Equal(("OfficeManager", "ImportDocuments"), (d.DelegateRole, d.Permission)));

        // the restored database still holds the guards
        var triggers = await restored.Database.SqlQuery<string>($"SELECT name AS [Value] FROM sys.triggers WHERE name IN ('TR_RolePermissionHistories_Immutable', 'TR_PermissionPolicyState_Guard', 'TR_UserAccounts_LastAdmin')").ToListAsync();
        Assert.Equal(3, triggers.Count);
        var refused = await Assert.ThrowsAnyAsync<Exception>(() => restored.Database.ExecuteSqlAsync($"DELETE FROM [RolePermissionHistories]"));
        Assert.Contains("51204", refused.ToString());

        // and the live database is untouched by the drill
        await using var live = Fixture.CreateContext();
        Assert.Equal(4L, (await live.PermissionPolicyStates.AsNoTracking().SingleAsync()).Version);
    }

    /// <summary>ALV-N018: changes written through the policy write service (an override, a delegation-table edit and a delegated grant) are restored with their history, audit entries and receipts, and a change after the backup is not.</summary>
    [Fact]
    public async Task Policy_changes_written_through_the_write_service_survive_a_backup_and_restore_with_their_history_and_audit()
    {
        await using (var db = Fixture.CreateContext())
        {
            var writes = new PolicyWriteService(db, PolicyTestSupport.Defaults());
            async Task<long> Version() => await db.PermissionPolicyStates.AsNoTracking().Select(s => s.Version).SingleAsync();
            await writes.SetOverrideAsync(S.Actor, new SetOverrideRequest("Dentist", nameof(Permission.ViewBilling), OverrideEffects.Revoke, "Dentists no longer see billing", await Version(), true, null), "restore-1", default);
            await writes.SetDelegationsAsync(S.Actor, new SetDelegationsRequest(
                [new("OfficeManager", nameof(Permission.ImportDocuments), "Dentist"), new("OfficeManager", nameof(Permission.ImportDocuments), "FrontDesk")], "Only two roles are delegated", await Version(), true), "restore-2", default);
            await writes.DelegatedGrantAsync(S.Actor, Role.OfficeManager, new DelegatedGrantRequest("FrontDesk", nameof(Permission.ImportDocuments), true, "Front desk scans intake forms", await Version(), true), "restore-3", default);
        }
        var record = await BackupAsync();
        await using (var db = Fixture.CreateContext())
            await new PolicyWriteService(db, PolicyTestSupport.Defaults()).SetOverrideAsync(S.Actor, new SetOverrideRequest("Dentist", nameof(Permission.ViewBilling), "Revert", "After the backup", await db.PermissionPolicyStates.AsNoTracking().Select(s => s.Version).SingleAsync(), true, null), "restore-4", default);

        var drill = await DrillAsync(record.Id);
        Assert.Equal("Succeeded", drill.Outcome);
        await using var restored = RestoredDb(drill);
        Assert.Equal(3L, (await restored.PermissionPolicyStates.AsNoTracking().SingleAsync()).Version);               // the later revert is not in the restore
        var overrides = await restored.RolePermissionOverrides.AsNoTracking().ToListAsync();
        Assert.Equal(new[] { "Dentist:ViewBilling:Revoke", "FrontDesk:ImportDocuments:Grant" }, overrides.Select(o => $"{o.Role}:{o.Permission}:{o.Effect}").OrderBy(x => x, StringComparer.Ordinal).ToArray());
        Assert.Equal(2, await restored.PermissionDelegations.CountAsync());
        var history = await restored.RolePermissionHistories.AsNoTracking().OrderBy(h => h.PolicyVersion).ThenBy(h => h.Kind).ToListAsync();
        Assert.Equal(new long[] { 1, 2, 2, 2, 3 }, history.Select(h => h.PolicyVersion).ToArray());                  // one override, three delegation rows removed, one delegated grant
        Assert.Single(history, h => h.ViaDelegation);
        Assert.Equal(3, await restored.AuditLogEntries.CountAsync(a => a.EventType == AuditEventTypes.PermissionGranted || a.EventType == AuditEventTypes.PermissionRevoked || a.EventType == AuditEventTypes.DelegationChanged));
        var snapshot = PolicySnapshotBuilder.Build(3, overrides, await restored.PermissionDelegations.AsNoTracking().ToListAsync());
        Assert.DoesNotContain(Permission.ViewBilling, snapshot.EffectiveFor(Role.Dentist));
        Assert.Contains(Permission.ImportDocuments, snapshot.EffectiveFor(Role.FrontDesk));
    }

    [Fact]
    public async Task The_restored_policy_resolves_to_the_same_effective_permissions_as_the_live_policy_did_at_backup_time()
    {
        await SeedPolicyAsync();
        var record = await BackupAsync();
        var drill = await DrillAsync(record.Id);
        await using var restored = RestoredDb(drill);
        var snapshot = PolicySnapshotBuilder.Build(3, await restored.RolePermissionOverrides.AsNoTracking().ToListAsync(), await restored.PermissionDelegations.AsNoTracking().ToListAsync());
        Assert.True(snapshot.Degraded);                                                                               // the invalid row names an unknown role
        var valid = PolicySnapshotBuilder.Build(3, (await restored.RolePermissionOverrides.AsNoTracking().ToListAsync()).Where(o => o.Role != "Wizard"), await restored.PermissionDelegations.AsNoTracking().ToListAsync());
        Assert.DoesNotContain(Permission.ViewBilling, valid.EffectiveFor(Role.Dentist));
        Assert.Contains(Permission.ImportDocuments, valid.EffectiveFor(Role.Billing));
        Assert.Equal(5, valid.Delegations.Count);
    }
}
