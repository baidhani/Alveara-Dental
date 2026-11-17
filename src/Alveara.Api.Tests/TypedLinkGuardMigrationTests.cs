using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Alveara.Api.Architecture.Clinical;
using Alveara.Api.Architecture.Documents;
using Alveara.Api.Architecture.Periodontal;
using Alveara.Api.Architecture.Safety;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N022 A3 (owner decision D-1 B), against real SQL Server and through the real migrator on a clone: migration AddTypedClearanceDocumentAndPerioDiagnosisLinks.
/// - Direct persistence (bypassing the services): a clearance pointing to ANOTHER patient's document is refused by the database (51288); a perio link pointing to another
///   patient's diagnosis is refused (51289); a DiagnosisId on a non-diagnosis link is refused (CK); the same-patient cases are accepted.
/// - Down removes only what Up added while no typed link exists, and Up restores it; once a typed link exists Down is refused (51290) and nothing is lost.
/// - Existing legacy free text is untouched by the upgrade.
/// </summary>
public class TypedLinkGuardMigrationTests : SafetyTestBase
{
    private const string Before = "20261117160917_AddDiagnosisFinalizedEncounterGuard";

    private async Task MigrateToAsync(string target)
    {
        await using var db = Fixture.CreateContext();
        await db.GetInfrastructure().GetRequiredService<IMigrator>().MigrateAsync(target);
    }

    private Task<int> CountAsync(string sql) => WithDb(db => db.Database.SqlQuery<int>(System.Runtime.CompilerServices.FormattableStringFactory.Create(sql)).SingleAsync());
    private async Task<int> Number(Func<Task> action) => Assert.IsType<SqlException>((await Assert.ThrowsAnyAsync<Exception>(action)) switch { DbUpdateException d => d.InnerException, var e => e }).Number;

    private async Task<Guid> DocumentAsync(Guid patient)
    {
        var id = Guid.NewGuid();
        await WithDb(async db =>
        {
            db.Add(new PatientDocument
            {
                Id = id, PatientId = patient, CategoryId = DocumentCategorySeeds.Radiograph, DocumentDate = new DateOnly(2030, 1, 10), OriginalFileName = "letter.pdf", DetectedType = "pdf",
                ContentType = "application/pdf", SizeBytes = 10, Sha256 = new string('a', 64), BlobId = Guid.NewGuid(), ImportedByUserId = S.Actor, ImportedAtUtc = DateTimeOffset.UtcNow,
                IdempotencyKey = $"doc-{Guid.NewGuid():N}", RequestHash = new string('b', 64),
            });
            return await db.SaveChangesAsync();
        });
        return id;
    }

    private async Task<Guid> ClearanceAsync(Guid patient, Guid? documentId = null, string? legacyText = null)
    {
        var id = Guid.NewGuid();
        await WithDb(async db =>
        {
            db.Clearances.Add(new Clearance
            {
                Id = id, PatientId = patient, Kind = "Medical", Reason = "Cardiac clearance", ReasonKey = Guid.NewGuid().ToString("N"), Status = ClearanceStatuses.Received,
                RequestedAtUtc = DateTimeOffset.UtcNow, RequestedByUserId = S.Actor, ReceivedAtUtc = DateTimeOffset.UtcNow, ReceivedByUserId = S.Actor, DocumentId = documentId, DocumentReference = legacyText,
            });
            return await db.SaveChangesAsync();
        });
        return id;
    }

    private async Task<(Guid Exam, Guid Diagnosis)> ExamAndDiagnosisAsync(Guid patient)
    {
        var (exam, dx, enc) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await WithDb(async db =>
        {
            db.Encounters.Add(new Encounter { Id = enc, PatientId = patient, EncounterAtUtc = DateTimeOffset.UtcNow.AddHours(-1), Status = EncounterStatuses.Draft, CreatedAtUtc = DateTimeOffset.UtcNow });
            db.Diagnoses.Add(new Diagnosis { Id = dx, PatientId = patient, EncounterId = enc, IdempotencyKey = $"dx-{Guid.NewGuid():N}", Label = "Periodontitis", Status = DiagnosisStatuses.Active, CreatedAtUtc = DateTimeOffset.UtcNow, CreatedByUserId = S.Actor });
            db.PerioExams.Add(new PerioExam { Id = exam, PatientId = patient, IdempotencyKey = $"exam-{Guid.NewGuid():N}", RecordedAtUtc = DateTimeOffset.UtcNow, RecordedByUserId = S.Actor, ReadingCount = 1 });
            return await db.SaveChangesAsync();
        });
        return (exam, dx);
    }

    private Task LinkAsync(Guid exam, Guid patient, string type, string reference, Guid? diagnosisId) => WithDb(async db =>
    {
        db.PerioExamLinks.Add(new PerioExamLink { Id = Guid.NewGuid(), ExamId = exam, PatientId = patient, LinkType = type, Reference = reference, DiagnosisId = diagnosisId, CreatedAtUtc = DateTimeOffset.UtcNow, CreatedByUserId = S.Actor });
        return await db.SaveChangesAsync();
    });

    // ---------- direct persistence ----------

    [Fact]
    public async Task A_clearance_pointing_to_another_patients_document_is_refused_by_the_database_and_its_own_is_accepted()
    {
        var bos = await DocumentAsync(Bo);
        Assert.Equal(ClearanceService.DocumentSamePatientGuard, await Number(() => ClearanceAsync(Ann, bos)));
        var own = await DocumentAsync(Ann);
        var id = await ClearanceAsync(Ann, own);
        Assert.Equal(own, (await WithDb(db => db.Clearances.AsNoTracking().SingleAsync(c => c.Id == id))).DocumentId);
        // an UPDATE to another patient's document is refused too
        Assert.Equal(ClearanceService.DocumentSamePatientGuard, await Number(() => WithDb(db => db.Database.ExecuteSqlRawAsync("UPDATE [Clearances] SET [DocumentId] = {0} WHERE [Id] = {1}", bos, id))));
    }

    [Fact]
    public async Task A_perio_link_to_another_patients_diagnosis_is_refused_by_the_database_and_a_diagnosis_id_needs_a_diagnosis_link()
    {
        var (exam, _) = await ExamAndDiagnosisAsync(Ann);
        var (_, bosDx) = await ExamAndDiagnosisAsync(Bo);
        Assert.Equal(PerioSessionService.DiagnosisSamePatientGuard, await Number(() => LinkAsync(exam, Ann, "Diagnosis", bosDx.ToString(), bosDx)));
        var (_, ownDx) = await ExamAndDiagnosisAsync(Ann);
        Assert.Equal(547, await Number(() => LinkAsync(exam, Ann, "Encounter", "enc-1", ownDx)));                // CK_PerioExamLinks_DiagnosisIdType
        await LinkAsync(exam, Ann, "Diagnosis", ownDx.ToString(), ownDx);
        Assert.Equal(1, await WithDb(db => db.PerioExamLinks.CountAsync(l => l.DiagnosisId == ownDx)));
    }

    // ---------- migration down / up ----------

    [Fact]
    public async Task The_migration_rolls_back_and_forward_while_no_typed_link_exists_and_keeps_legacy_text()
    {
        var legacy = await ClearanceAsync(Ann, null, "ER Doctor");
        Assert.Equal(2, await CountAsync("SELECT COUNT(*) AS [Value] FROM sys.triggers WHERE name IN ('TR_Clearances_DocumentSamePatient','TR_PerioExamLinks_DiagnosisSamePatient')"));
        await MigrateToAsync(Before);
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) AS [Value] FROM sys.triggers WHERE name IN ('TR_Clearances_DocumentSamePatient','TR_PerioExamLinks_DiagnosisSamePatient')"));
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) AS [Value] FROM sys.columns WHERE name IN ('DocumentId','DiagnosisId') AND object_id IN (OBJECT_ID('Clearances'),OBJECT_ID('ClearanceVersions'),OBJECT_ID('PerioExamLinks'))"));
        await using (var db = Fixture.CreateContext()) await db.Database.MigrateAsync();
        await using var check = Fixture.CreateContext();
        Assert.Empty(await check.Database.GetPendingMigrationsAsync());
        Assert.Equal(("ER Doctor", (Guid?)null), await check.Clearances.AsNoTracking().Where(c => c.Id == legacy).Select(c => ValueTuple.Create(c.DocumentReference, c.DocumentId)).SingleAsync());
    }

    [Fact]
    public async Task Rolling_back_is_refused_once_a_typed_link_exists_and_nothing_is_lost()
    {
        var doc = await DocumentAsync(Ann);
        var id = await ClearanceAsync(Ann, doc);
        Assert.Equal(51290, await Number(() => MigrateToAsync(Before)));
        Assert.Equal(doc, (await WithDb(db => db.Clearances.AsNoTracking().SingleAsync(c => c.Id == id))).DocumentId);
        Assert.Equal(2, await CountAsync("SELECT COUNT(*) AS [Value] FROM sys.triggers WHERE name IN ('TR_Clearances_DocumentSamePatient','TR_PerioExamLinks_DiagnosisSamePatient')"));
    }
}
