using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Alveara.Api.Architecture.Clinical;
using Alveara.Api.Data;
using Xunit;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N022 A2-R02 (review A2-R01 CR-1), against real SQL Server: the database itself refuses a NEW diagnosis on a Finalized encounter (trigger
/// TR_Diagnoses_EncounterNotFinalized, error 51287), so a finalize that commits between the service's check and the insert cannot let one through.
/// - Interleaving: the encounter is finalized after the service passed its check and before its save; the caller gets 409 encounter_finalized and nothing is committed.
/// - Direct persistence: an insert that bypasses the service is refused on a Finalized encounter and accepted on a Draft one.
/// - Insert only: correcting or withdrawing a diagnosis whose encounter was finalized later still works.
/// </summary>
public class DiagnosisFinalizedEncounterGuardTests : SafetyTestBase
{
    private async Task<Guid> EncounterAsync(string status = EncounterStatuses.Draft)
    {
        await using var db = Fixture.CreateContext();
        var e = new Encounter { Id = Guid.NewGuid(), PatientId = Ann, EncounterAtUtc = new DateTimeOffset(2030, 1, 14, 15, 0, 0, TimeSpan.Zero), Status = status, CreatedAtUtc = DateTimeOffset.UtcNow };
        if (status == EncounterStatuses.Finalized) (e.FinalizedAtUtc, e.FinalizedByUserId) = (DateTimeOffset.UtcNow, S.Actor);
        db.Encounters.Add(e);
        await db.SaveChangesAsync();
        return e.Id;
    }

    private Task FinalizeAsync(Guid encounterId) => WithDb(db => db.Database.ExecuteSqlRawAsync(
        "UPDATE [Encounters] SET [Status] = 'Finalized', [FinalizedAtUtc] = SYSDATETIMEOFFSET(), [FinalizedByUserId] = {0} WHERE [Id] = {1}", S.Actor, encounterId));

    private static DiagnosisInput In(Guid encounter, string label = "Gingivitis") => new(encounter, label, null, null, null);

    private async Task<(int Diagnoses, int Versions, int Audit)> CountsAsync()
    {
        await using var db = Fixture.CreateContext();
        return (await db.Diagnoses.CountAsync(), await db.DiagnosisVersions.CountAsync(), await db.AuditLogEntries.CountAsync(a => a.EntityType == nameof(Diagnosis)));
    }

    /// <summary>Finalizes the encounter on its own connection, committed, just BEFORE the service's save runs - i.e. after the service's own status check passed.</summary>
    private sealed class FinalizeBeforeSave(string connectionString, Guid encounterId, Guid actor) : SaveChangesInterceptor
    {
        public bool Fired { get; private set; }
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!Fired)
            {
                Fired = true;
                await using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync(cancellationToken);
                await using var command = new SqlCommand("UPDATE [Encounters] SET [Status] = 'Finalized', [FinalizedAtUtc] = SYSDATETIMEOFFSET(), [FinalizedByUserId] = @actor WHERE [Id] = @id", connection);
                command.Parameters.AddWithValue("@actor", actor);
                command.Parameters.AddWithValue("@id", encounterId);
                Assert.Equal(1, await command.ExecuteNonQueryAsync(cancellationToken));
            }
            return await base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    [Fact]
    public async Task A_finalize_that_lands_after_the_services_check_still_refuses_the_new_diagnosis_and_commits_nothing()
    {
        var encounter = await EncounterAsync();                                                  // a Draft: the service's own check passes
        var interceptor = new FinalizeBeforeSave(Fixture.ConnectionString, encounter, S.Actor);
        await using var db = new AlveraDbContext(new DbContextOptionsBuilder<AlveraDbContext>().UseSqlServer(Fixture.ConnectionString).AddInterceptors(interceptor).Options);

        var refused = await Assert.ThrowsAsync<DiagnosisException>(() => new DiagnosisService(db, Clock).RecordAsync(Ann, "race-key", In(encounter), S.Actor, default));

        Assert.True(interceptor.Fired);                                                          // the finalize really landed between the check and the save
        Assert.Equal(("encounter_finalized", 409), (refused.Code, refused.StatusCode));
        Assert.Equal(("encounterId", "finalized"), (Assert.Single(refused.Problems).Field, refused.Problems[0].Code));
        Assert.DoesNotContain("51287", refused.Message);                                         // no SQL detail reaches the caller
        Assert.Equal((0, 0, 0), await CountsAsync());                                            // no diagnosis, no version, no audit
    }

    [Fact]
    public async Task A_direct_insert_that_bypasses_the_service_is_refused_on_a_finalized_encounter_and_accepted_on_a_draft()
    {
        var finalized = await EncounterAsync(EncounterStatuses.Finalized);
        var draft = await EncounterAsync();
        Diagnosis Row(Guid encounter) => new() { Id = Guid.NewGuid(), PatientId = Ann, EncounterId = encounter, IdempotencyKey = Guid.NewGuid().ToString("N"), Label = "Direct", Status = DiagnosisStatuses.Active, CreatedAtUtc = DateTimeOffset.UtcNow, CreatedByUserId = S.Actor };

        await using (var db = Fixture.CreateContext())
        {
            db.Diagnoses.Add(Row(finalized));
            var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.Equal(DiagnosisService.FinalizedEncounterGuard, Assert.IsType<SqlException>(ex.InnerException).Number);
        }
        Assert.Equal(0, await WithDb(db => db.Diagnoses.CountAsync()));

        await using (var db = Fixture.CreateContext())
        {
            db.Diagnoses.Add(Row(draft));
            await db.SaveChangesAsync();
        }
        Assert.Equal(1, await WithDb(db => db.Diagnoses.CountAsync()));
    }

    [Fact]
    public async Task A_diagnosis_recorded_on_a_draft_can_still_be_corrected_and_withdrawn_after_its_encounter_is_finalized()
    {
        var encounter = await EncounterAsync();
        var d = await WithDb(db => new DiagnosisService(db, Clock).RecordAsync(Ann, "before", In(encounter), S.Actor, default));
        await FinalizeAsync(encounter);

        var corrected = await WithDb(db => new DiagnosisService(db, Clock).CorrectAsync(d.Id, d.RowVersion, new DiagnosisCorrection("Gingivitis, localized", null, null, null, false), "Clarified at review", S.Actor, default));
        Assert.Equal("Gingivitis, localized", corrected.Label);
        var withdrawn = await WithDb(db => new DiagnosisService(db, Clock).WithdrawAsync(d.Id, corrected.RowVersion, "Entered on the wrong patient", S.Actor, default));
        Assert.Equal(DiagnosisStatuses.Withdrawn, withdrawn.Status);
        Assert.Equal(encounter, withdrawn.EncounterId);
    }
}
