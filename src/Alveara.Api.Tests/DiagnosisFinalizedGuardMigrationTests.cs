using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Alveara.Api.Architecture.Clinical;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N022 A2-R02: the migration AddDiagnosisFinalizedEncounterGuard through the real migrator, on a clone. Down removes only the trigger; Up adds it back; diagnoses
/// recorded on an encounter before it was finalized (existing data) are untouched by the upgrade and stay correctable; after the upgrade a new one is refused.
/// </summary>
public class DiagnosisFinalizedGuardMigrationTests : SafetyTestBase
{
    private const string Before = "20261116145816_RefreshAppointmentMergeScope";
    private const string Trigger = "TR_Diagnoses_EncounterNotFinalized";

    private async Task MigrateToAsync(string target)
    {
        await using var db = Fixture.CreateContext();
        await db.GetInfrastructure().GetRequiredService<IMigrator>().MigrateAsync(target);
    }

    private Task<int> TriggerCountAsync() => WithDb(db => db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM sys.triggers WHERE name = {Trigger}").SingleAsync());

    private async Task<Guid> FinalizedEncounterWithAnEarlierDiagnosisAsync()
    {
        var encounter = Guid.NewGuid();
        await WithDb(async db =>
        {
            db.Encounters.Add(new Encounter { Id = encounter, PatientId = Ann, EncounterAtUtc = DateTimeOffset.UtcNow.AddDays(-1), Status = EncounterStatuses.Draft, CreatedAtUtc = DateTimeOffset.UtcNow });
            db.Diagnoses.Add(new Diagnosis { Id = Guid.NewGuid(), PatientId = Ann, EncounterId = encounter, IdempotencyKey = "earlier", Label = "Recorded while a draft", Status = DiagnosisStatuses.Active, CreatedAtUtc = DateTimeOffset.UtcNow, CreatedByUserId = S.Actor });
            await db.SaveChangesAsync();
            return await db.Database.ExecuteSqlRawAsync("UPDATE [Encounters] SET [Status] = 'Finalized', [FinalizedAtUtc] = SYSDATETIMEOFFSET(), [FinalizedByUserId] = {0} WHERE [Id] = {1}", S.Actor, encounter);
        });
        return encounter;
    }

    private Task InsertOnAsync(Guid encounter, string key) => WithDb(async db =>
    {
        db.Diagnoses.Add(new Diagnosis { Id = Guid.NewGuid(), PatientId = Ann, EncounterId = encounter, IdempotencyKey = key, Label = "Later", Status = DiagnosisStatuses.Active, CreatedAtUtc = DateTimeOffset.UtcNow, CreatedByUserId = S.Actor });
        return await db.SaveChangesAsync();
    });

    [Fact]
    public async Task The_guard_rolls_back_and_forward_and_only_its_trigger_changes()
    {
        Assert.Equal(1, await TriggerCountAsync());
        await MigrateToAsync(Before);
        Assert.Equal(0, await TriggerCountAsync());
        await using (var db = Fixture.CreateContext()) await db.Database.MigrateAsync();
        Assert.Equal(1, await TriggerCountAsync());
        await using var check = Fixture.CreateContext();
        Assert.Empty(await check.Database.GetPendingMigrationsAsync());
    }

    [Fact]
    public async Task Upgrading_keeps_diagnoses_already_on_a_finalized_encounter_and_then_refuses_new_ones()
    {
        await MigrateToAsync(Before);
        var encounter = await FinalizedEncounterWithAnEarlierDiagnosisAsync();
        await InsertOnAsync(encounter, "before-upgrade");                                          // without the guard the database accepted it (the old rule)

        await using (var db = Fixture.CreateContext()) await db.Database.MigrateAsync();

        Assert.Equal(2, await WithDb(db => db.Diagnoses.CountAsync(d => d.EncounterId == encounter))); // existing rows untouched by the upgrade
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => InsertOnAsync(encounter, "after-upgrade"));
        Assert.Equal(DiagnosisService.FinalizedEncounterGuard, Assert.IsType<Microsoft.Data.SqlClient.SqlException>(ex.InnerException).Number);
        Assert.Equal(2, await WithDb(db => db.Diagnoses.CountAsync(d => d.EncounterId == encounter)));
    }
}
