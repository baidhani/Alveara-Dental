using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Alveara.Api.Architecture.Billing;
using Alveara.Api.Architecture.Clinical;
using Alveara.Api.Architecture.Documents;
using Alveara.Api.Architecture.Periodontal;
using Alveara.Api.Architecture.Patients.Merge;
using Alveara.Api.Architecture.Safety;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N025, migration AddMergeTriggerIntegrityRepairs (merge-integrity addendum, MERGE-ADDENDUM-R02), against real SQL Server:
/// - U-017 reproduced first: on the schema just before the repair, merging a patient whose clearance links a document fails (51288); after it, the merge and its undo
///   succeed and the clearance and its document move together;
/// - U-017's own rule is kept: outside a merge an UPDATE or INSERT pointing a clearance at another patient's document is refused (51288), and inside a merge an INSERT is;
/// - U-016, Clearances.DocumentId (the one directly exposed column): inside a merge an UPDATE is refused (51234) - it was not before the repair - and outside a merge the
///   same statement has the same outcome before and after (a zero-row UPDATE still fires the AFTER UPDATE triggers and makes UPDATE(column) true);
/// - U-016, the four completeness gaps (owner decision 2026-11-19): ClearanceVersions.DocumentId, PerioExamLinks.DiagnosisId, FinancialDocuments.ReplacementReason and
///   .ReplacesDocumentId sit on tables whose immutable trigger is INSTEAD OF UPDATE: inside a merge it re-issues a PatientId-only update, so on a REAL row the value does
///   not change (no 51234 can be raised), and outside a merge it refuses with its own error; the added terms are proven by the definition test (defence in depth);
/// - each repaired trigger is its previous text plus exactly the inserted text, and Down gives every previous text back exactly.
/// </summary>
public class MergeTriggerIntegrityTests : BillingTestBase
{
    private const string BeforeRepair = "20261120011518_AddPatientMrn";

    private static readonly (string Table, string Trigger, string Column)[] Columns =
    [
        ("Clearances", "TR_Clearances_MergeScope", "DocumentId"),
        ("ClearanceVersions", "TR_ClearanceVersions_MergeScope", "DocumentId"),
        ("PerioExamLinks", "TR_PerioExamLinks_MergeScope", "DiagnosisId"),
        ("FinancialDocuments", "TR_FinancialDocuments_MergeScope", "ReplacementReason"),
        ("FinancialDocuments", "TR_FinancialDocuments_MergeScope", "ReplacesDocumentId"),
    ];

    private static readonly string[] Repaired = ["TR_Clearances_MergeScope", "TR_ClearanceVersions_MergeScope", "TR_PerioExamLinks_MergeScope", "TR_FinancialDocuments_MergeScope", "TR_Clearances_DocumentSamePatient"];

    private async Task MigrateToAsync(string? target)
    {
        await using var db = Fixture.CreateContext();
        if (target is null) await db.Database.MigrateAsync();
        else await db.GetInfrastructure().GetRequiredService<IMigrator>().MigrateAsync(target);
    }

    private async Task<Dictionary<string, string>> DefinitionsAsync()
    {
        await using var db = Fixture.CreateContext();
        var result = new Dictionary<string, string>();
        foreach (var t in Repaired)
            result[t] = await db.Database.SqlQuery<string>($"SELECT OBJECT_DEFINITION(OBJECT_ID({t})) AS [Value]").SingleAsync();
        return result;
    }

    /// <summary>Runs one statement on one connection, optionally inside a merge session; the SQL error number it was refused with, or 0.</summary>
    private async Task<int> OutcomeAsync(string sql, bool inMerge)
    {
        await using var db = Fixture.CreateContext();
        await db.Database.OpenConnectionAsync();
        if (inMerge) await db.Database.ExecuteSqlRawAsync("EXEC sp_set_session_context N'alveara.patient_merge', 1");
        try { await db.Database.ExecuteSqlRawAsync(sql); return 0; }
        catch (SqlException e) { return e.Number; }
    }

    private static int SqlNumber(Exception e)
    {
        for (Exception? x = e; x is not null; x = x.InnerException) if (x is SqlException s) return s.Number;
        return 0;
    }

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

    private async Task<Guid> ClearanceAsync(Guid patient, Guid? documentId)
    {
        var id = Guid.NewGuid();
        await WithDb(async db =>
        {
            db.Clearances.Add(new Clearance
            {
                Id = id, PatientId = patient, Kind = "Medical", Reason = "Cardiac clearance", ReasonKey = Guid.NewGuid().ToString("N"), Status = ClearanceStatuses.Received,
                RequestedAtUtc = DateTimeOffset.UtcNow, RequestedByUserId = S.Actor, ReceivedAtUtc = DateTimeOffset.UtcNow, ReceivedByUserId = S.Actor, DocumentId = documentId,
            });
            return await db.SaveChangesAsync();
        });
        return id;
    }

    private async Task<MergeResult> MergeAsync(Guid source, Guid survivor)
    {
        var preview = await WithDb(db => new PatientMergeService(db, Clock).PreviewAsync(source, survivor, default));
        return await WithDb(db => new PatientMergeService(db, Clock).MergeAsync(S.Actor, new MergeRequest(source, survivor, "Same person", preview.SourceRowVersion, preview.SurvivorRowVersion, null, true, true), Guid.NewGuid().ToString("N"), default));
    }

    private async Task<(Guid ClearanceOwner, Guid DocumentOwner)> OwnersAsync(Guid clearance, Guid document) => await WithDb(async db =>
        (await db.Clearances.AsNoTracking().Where(c => c.Id == clearance).Select(c => c.PatientId).SingleAsync(),
         await db.Set<PatientDocument>().AsNoTracking().Where(d => d.Id == document).Select(d => d.PatientId).SingleAsync()));

    [Fact]
    public async Task U017_reproduced_before_the_repair_and_after_it_a_patient_with_a_document_linked_clearance_merges_and_unmerges()
    {
        var kept = await S.PatientAsync("Cy", "Kim"); var gone = await S.PatientAsync("Dee", "Kim");
        var document = await DocumentAsync(gone);
        var clearance = await ClearanceAsync(gone, document);

        await MigrateToAsync(BeforeRepair);
        var failed = await Assert.ThrowsAnyAsync<Exception>(() => MergeAsync(gone, kept));               // the pre-fix evidence: the clearance moves before its document
        Assert.Equal(51288, SqlNumber(failed));
        Assert.Equal((gone, gone), await OwnersAsync(clearance, document));                                 // nothing moved

        await MigrateToAsync(null);
        var merged = await MergeAsync(gone, kept);
        Assert.Equal((kept, kept), await OwnersAsync(clearance, document));                                 // they move together; VerifyAsync found no disagreement

        var undo = await WithDb(db => new PatientUnmergeService(db, Clock).PreviewAsync(merged.MergeId, default));
        await WithDb(db => new PatientUnmergeService(db, Clock).UnmergeAsync(S.Actor, new UnmergeRequest(merged.MergeId, "Different people", undo.SourceRowVersion, undo.SurvivorRowVersion, true), Guid.NewGuid().ToString("N"), default));
        Assert.Equal((gone, gone), await OwnersAsync(clearance, document));
    }

    [Fact]
    public async Task U017_rule_is_kept_outside_a_merge_and_for_an_insert_inside_one()
    {
        var mine = await DocumentAsync(Ann); var theirs = await DocumentAsync(Bo);
        var clearance = await ClearanceAsync(Ann, mine);

        Assert.Equal(51288, await OutcomeAsync($"UPDATE [Clearances] SET [DocumentId] = '{theirs}' WHERE [Id] = '{clearance}'", inMerge: false));
        // inside a merge the merge-scope guard refuses any insert first (51234); to show that U-017's OWN check still covers an insert, that guard is switched off for
        // this one statement inside a transaction that is always rolled back (test isolation only)
        Assert.Equal(51288, await OutcomeAsync(
            $@"BEGIN TRANSACTION; DISABLE TRIGGER [TR_Clearances_MergeScope] ON [Clearances];
               INSERT INTO [Clearances] ([Id], [PatientId], [Kind], [Reason], [ReasonKey], [Status], [RequestedAtUtc], [RequestedByUserId], [DocumentId])
               VALUES ('{Guid.NewGuid()}', '{Ann}', 'Medical', 'x', '{Guid.NewGuid():N}', '{ClearanceStatuses.Requested}', SYSDATETIMEOFFSET(), '{S.Actor}', '{theirs}');
               ROLLBACK TRANSACTION;", inMerge: true));
        Assert.Equal(1, await WithDb(db => db.Database.SqlQuery<int>($"SELECT CONVERT(int, is_disabled) ^ 1 AS [Value] FROM sys.triggers WHERE name = 'TR_Clearances_MergeScope'").SingleAsync()));   // back on
        Assert.Equal(0, await OutcomeAsync($"UPDATE [Clearances] SET [DocumentId] = '{mine}' WHERE [Id] = '{clearance}'", inMerge: false));   // the same patient's document: fine
    }

    [Fact]
    public async Task U016_each_column_is_refused_inside_a_merge_only_after_the_repair_and_unchanged_outside_one()
    {
        string Statement((string Table, string Trigger, string Column) c) => $"UPDATE [{c.Table}] SET [{c.Column}] = [{c.Column}] WHERE 1 = 0";   // zero rows; the triggers still fire

        var after = new Dictionary<string, (int InMerge, int Outside)>();
        foreach (var c in Columns) after[c.Column + c.Table] = (await OutcomeAsync(Statement(c), true), await OutcomeAsync(Statement(c), false));

        await MigrateToAsync(BeforeRepair);
        var before = new Dictionary<string, (int InMerge, int Outside)>();
        foreach (var c in Columns) before[c.Column + c.Table] = (await OutcomeAsync(Statement(c), true), await OutcomeAsync(Statement(c), false));
        await MigrateToAsync(null);

        foreach (var c in Columns.Where(c => c.Table == "Clearances"))   // the four others sit behind an INSTEAD OF UPDATE trigger: see the real-row test below
        {
            var key = c.Column + c.Table;
            Assert.True(after[key].InMerge == 51234, $"{c.Table}.{c.Column} inside a merge after the repair: {after[key].InMerge}");
            Assert.True(before[key].InMerge != 51234, $"{c.Table}.{c.Column} inside a merge before the repair was already {before[key].InMerge}");   // the gap was real
            Assert.True(after[key].Outside == before[key].Outside, $"{c.Table}.{c.Column} outside a merge: {before[key].Outside} before, {after[key].Outside} after");
        }
    }

    /// <summary>Runs one UPDATE on a real row inside a merge session and outside one; inside, the row must be byte-for-byte unchanged (no partial or silent change).</summary>
    private async Task CannotChangeAsync(string table, Guid id, string set, int outsideError)
    {
        async Task<string> RowAsync() => await WithDb(db => db.Database.SqlQueryRaw<string>($"SELECT (SELECT * FROM [{table}] WHERE [Id] = '{id}' FOR JSON PATH, INCLUDE_NULL_VALUES) AS [Value]").SingleAsync());
        var before = await RowAsync();
        Assert.Equal(0, await OutcomeAsync($"UPDATE [{table}] SET {set} WHERE [Id] = '{id}'", inMerge: true));
        Assert.Equal(before, await RowAsync());                                                               // inside a merge: discarded, every column as it was
        Assert.Equal(outsideError, await OutcomeAsync($"UPDATE [{table}] SET {set} WHERE [Id] = '{id}'", inMerge: false));
        Assert.Equal(before, await RowAsync());
    }

    [Fact]
    public async Task U016_a_perio_link_diagnosis_and_a_financial_document_replacement_never_change_inside_a_merge_and_are_refused_outside_one()
    {
        // PerioExamLinks.DiagnosisId (TR_PerioExamLinks_Immutable, 51070 outside a merge)
        var (exam, enc) = (Guid.NewGuid(), Guid.NewGuid());
        var (dxA, dxB, link) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await WithDb(async db =>
        {
            db.Encounters.Add(new Encounter { Id = enc, PatientId = Ann, EncounterAtUtc = DateTimeOffset.UtcNow.AddHours(-1), Status = EncounterStatuses.Draft, CreatedAtUtc = DateTimeOffset.UtcNow });
            foreach (var dx in new[] { dxA, dxB })
                db.Diagnoses.Add(new Diagnosis { Id = dx, PatientId = Ann, EncounterId = enc, IdempotencyKey = $"dx-{Guid.NewGuid():N}", Label = "Periodontitis", Status = DiagnosisStatuses.Active, CreatedAtUtc = DateTimeOffset.UtcNow, CreatedByUserId = S.Actor });
            db.PerioExams.Add(new PerioExam { Id = exam, PatientId = Ann, IdempotencyKey = $"exam-{Guid.NewGuid():N}", RecordedAtUtc = DateTimeOffset.UtcNow, RecordedByUserId = S.Actor, ReadingCount = 1 });
            await db.SaveChangesAsync();
            db.PerioExamLinks.Add(new PerioExamLink { Id = link, ExamId = exam, PatientId = Ann, LinkType = "Diagnosis", Reference = dxA.ToString(), DiagnosisId = dxA, CreatedAtUtc = DateTimeOffset.UtcNow, CreatedByUserId = S.Actor });
            return await db.SaveChangesAsync();
        });
        await CannotChangeAsync("PerioExamLinks", link, $"[DiagnosisId] = '{dxB}'", 51070);

        // FinancialDocuments.ReplacementReason and .ReplacesDocumentId (TR_FinancialDocuments_Immutable, 51169 outside a merge), on a real issued statement
        var plan = await BillablePlanAsync();                                                                 // a statement needs a ledger: one completed, charged procedure
        await Bill(s => s.RecordChargeAsync(Ann, new ChargeInput(plan.Completed, "c-merge-1"), S.Actor, default));
        var preview = await Bill(s => s.PreviewDocumentAsync(Ann, new DocumentPreviewInput("Statement", null), default));
        var issued = await Bill(s => s.IssueDocumentAsync(Ann, new IssueDocumentInput(preview.PreviewToken, "doc-merge-1"), S.Actor, default));
        await CannotChangeAsync("FinancialDocuments", issued.Id, "[ReplacementReason] = N'changed during a merge'", 51169);
        await CannotChangeAsync("FinancialDocuments", issued.Id, $"[ReplacesDocumentId] = '{issued.Id}'", 51169);
    }

    [Fact]
    public async Task U016_a_clearance_version_document_never_changes_inside_a_merge_and_is_refused_outside_one()
    {
        // TR_ClearanceVersions_Immutable is INSTEAD OF UPDATE: inside a merge it replaces any update with one of PatientId only, so a DocumentId change is discarded
        // before the merge guard sees it (the repaired UPDATE([DocumentId]) term is the second line of defence); outside a merge it refuses every update (51054)
        var mine = await DocumentAsync(Ann); var theirs = await DocumentAsync(Bo);
        var clearance = await ClearanceAsync(Ann, mine);
        var version = Guid.NewGuid();
        await WithDb(async db =>
        {
            db.Add(new ClearanceVersion { Id = version, ClearanceId = clearance, PatientId = Ann, VersionNumber = 1, ChangeType = "Received", Kind = "Medical", Reason = "Cardiac clearance", Status = ClearanceStatuses.Received, DocumentId = mine, OccurredAtUtc = DateTimeOffset.UtcNow });
            return await db.SaveChangesAsync();
        });
        await CannotChangeAsync("ClearanceVersions", version, $"[DocumentId] = '{theirs}'", 51054);
        Assert.Contains("UPDATE([DocumentId])", (await DefinitionsAsync())["TR_ClearanceVersions_MergeScope"]);
    }

    [Fact]
    public async Task Each_repaired_trigger_is_its_previous_text_plus_exactly_the_inserted_text_and_down_gives_the_previous_text_back()
    {
        var after = await DefinitionsAsync();
        await MigrateToAsync(BeforeRepair);
        var before = await DefinitionsAsync();
        await MigrateToAsync(null);
        var again = await DefinitionsAsync();

        foreach (var trigger in Repaired.Where(t => t.EndsWith("_MergeScope", StringComparison.Ordinal)))
        {
            var inserted = Columns.Where(c => c.Trigger == trigger).Select(c => Migrations.AddMergeTriggerIntegrityRepairs.Term(c.Column)).ToList();
            Assert.All(inserted, term => Assert.Contains(term, after[trigger]));
            Assert.Equal(before[trigger].Replace("CREATE TRIGGER", "ALTER TRIGGER"), inserted.Aggregate(after[trigger], (d, term) => d.Replace(term, "")).Replace("CREATE TRIGGER", "ALTER TRIGGER"));   // nothing else changed
        }
        var standAside = "\n    " + Migrations.AddMergeTriggerIntegrityRepairs.StandAsideLine;
        Assert.Contains(standAside, after["TR_Clearances_DocumentSamePatient"]);
        Assert.Equal(before["TR_Clearances_DocumentSamePatient"].Replace("CREATE TRIGGER", "ALTER TRIGGER"), after["TR_Clearances_DocumentSamePatient"].Replace(standAside, "").Replace("CREATE TRIGGER", "ALTER TRIGGER"));
        Assert.Equal(after, again);                                                                          // Up is repeatable: the same text again

        await MigrateToAsync(BeforeRepair);
        Assert.Equal(before, await DefinitionsAsync());                                                      // Down: the previous texts exactly
        await MigrateToAsync(null);
    }
}
