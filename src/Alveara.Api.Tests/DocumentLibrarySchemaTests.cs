using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Alveara.Api.Architecture.Clinical;
using Alveara.Api.Architecture.Documents;
using Alveara.Api.Data;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-010-C01, the database on its own: what the document library adds to a document (an encounter, a tooth or a region, a status with its reason) and around it (the result of the last storage check, in its own
/// table, and every inactivation and reactivation with its reason). Rows are written straight to the tables, around any service, so the tests prove the database refuses an encounter of another patient, a tooth together with a region,
/// an inactive document with no reason, a storage result without a time and any change to a status change, while STORY-010's own guarantees (the file never changes) still hold, and that a rollback refuses to lose
/// library data.
/// </summary>
public class DocumentLibrarySchemaTests : SafetyTestBase
{
    private const string BeforeLibrary = "20261019231115_AddDocuments";
    private static readonly string HashA = new('a', 64);

    private PatientDocument NewDocument(Action<PatientDocument>? change = null, Guid? patient = null)
    {
        var row = new PatientDocument
        {
            Id = Guid.NewGuid(), PatientId = patient ?? Ann, CategoryId = DocumentCategorySeeds.Radiograph, DocumentDate = new DateOnly(2026, 10, 1), Description = "Bitewing.",
            OriginalFileName = "bitewing.png", DetectedType = "png", ContentType = "image/png", SizeBytes = 2048, Sha256 = HashA, BlobId = Guid.NewGuid(), ImportedByUserId = S.Actor,
            ImportedAtUtc = DateTimeOffset.UtcNow, IdempotencyKey = $"doc-{Guid.NewGuid():N}", RequestHash = HashA,
        };
        change?.Invoke(row);
        return row;
    }

    private async Task<Guid> EncounterAsync(Guid? patient = null)
    {
        var e = new Encounter { Id = Guid.NewGuid(), PatientId = patient ?? Ann, EncounterAtUtc = DateTimeOffset.UtcNow, Status = EncounterStatuses.Draft, CreatedAtUtc = DateTimeOffset.UtcNow };
        await AddAsync(e);
        return e.Id;
    }

    private async Task AddAsync(params object[] rows)
    {
        await using var db = Fixture.CreateContext();
        db.AddRange(rows);
        await db.SaveChangesAsync();
    }

    private static async Task<Microsoft.Data.SqlClient.SqlException> Refusal(Func<Task> action)
    {
        var e = await Assert.ThrowsAnyAsync<Exception>(action);
        return Assert.IsType<Microsoft.Data.SqlClient.SqlException>(e is DbUpdateException { InnerException: not null } d ? d.InnerException : e);
    }

    private async Task<Microsoft.Data.SqlClient.SqlException> SqlRefusalAsync(FormattableString sql)
    {
        await using var db = Fixture.CreateContext();
        return await Refusal(() => db.Database.ExecuteSqlAsync(sql));
    }

    private async Task<int> CountAsync<T>() where T : class { await using var db = Fixture.CreateContext(); return await db.Set<T>().CountAsync(); }

    private DocumentStatusChange Change(Guid documentId, string toStatus, string? reason, Action<DocumentStatusChange>? change = null)
    {
        var row = new DocumentStatusChange { Id = Guid.NewGuid(), DocumentId = documentId, ToStatus = toStatus, Reason = reason, ByUserId = S.Actor, AtUtc = DateTimeOffset.UtcNow };
        change?.Invoke(row);
        return row;
    }

    // ---------- what is accepted ----------

    [Fact]
    public async Task A_new_document_is_active_and_unchecked_with_no_encounter_tooth_or_region()
    {
        var doc = NewDocument();
        await AddAsync(doc);
        await using var db = Fixture.CreateContext();
        var row = await db.Documents.AsNoTracking().SingleAsync();
        Assert.Equal((DocumentStatuses.Active, null, null, null), (row.Status, row.EncounterId, row.ToothKey, row.RegionKey));
        Assert.Equal(0, await db.DocumentIntegrity.CountAsync());                                                // never checked: no result row
    }

    [Fact]
    public async Task A_document_may_name_an_encounter_of_its_patient_and_one_tooth_or_one_region()
    {
        var encounter = await EncounterAsync();
        await AddAsync(NewDocument(d => { d.EncounterId = encounter; d.ToothKey = "16"; }), NewDocument(d => d.RegionKey = "UpperLeft"), NewDocument(d => d.ToothKey = "55"), NewDocument(d => d.EncounterId = encounter));
        Assert.Equal(4, await CountAsync<PatientDocument>());
    }

    [Fact]
    public async Task Every_listed_region_and_both_tooth_numberings_are_accepted()
    {
        var teeth = new[] { "11", "48", "51", "85" };
        var regions = DiagnosisRegions.All;
        await AddAsync([.. teeth.Select(t => NewDocument(d => d.ToothKey = t)), .. regions.Select(r => NewDocument(d => d.RegionKey = r))]);
        Assert.Equal(teeth.Length + regions.Count, await CountAsync<PatientDocument>());
    }

    [Fact]
    public async Task An_inactive_document_says_why_who_and_when()
    {
        await AddAsync(NewDocument(d => { d.Status = DocumentStatuses.Inactive; d.InactiveReason = "Scanned under the wrong patient."; d.InactivatedByUserId = S.Actor; d.InactivatedAtUtc = DateTimeOffset.UtcNow; }));
        Assert.Equal(1, await CountAsync<PatientDocument>());
    }

    [Fact]
    public async Task The_status_encounter_and_tooth_of_a_document_may_change_after_import()
    {
        var doc = NewDocument();
        await AddAsync(doc);
        var encounter = await EncounterAsync();
        var now = DateTimeOffset.UtcNow;
        await using (var db = Fixture.CreateContext())
        {
            var row = await db.Documents.SingleAsync();
            row.EncounterId = encounter; row.ToothKey = "26";
            row.Status = DocumentStatuses.Inactive; row.InactiveReason = "Duplicate scan."; row.InactivatedByUserId = S.Actor; row.InactivatedAtUtc = now;
            await db.SaveChangesAsync();
        }
        await using (var db = Fixture.CreateContext())
        {
            var row = await db.Documents.SingleAsync();
            row.Status = DocumentStatuses.Active; row.InactiveReason = null; row.InactivatedByUserId = null; row.InactivatedAtUtc = null; row.ToothKey = null; row.RegionKey = "Tmj";
            await db.SaveChangesAsync();                                                                // reactivated and moved from a tooth to a region
        }
        await using var check = Fixture.CreateContext();
        var after = await check.Documents.AsNoTracking().SingleAsync();
        Assert.Equal((DocumentStatuses.Active, "Tmj", encounter), (after.Status, after.RegionKey, after.EncounterId));
    }

    [Fact]
    public async Task The_file_of_a_document_still_never_changes_and_a_document_is_still_never_deleted()
    {
        var doc = NewDocument();
        await AddAsync(doc);
        Assert.Equal(51183, (await SqlRefusalAsync($"UPDATE [Documents] SET [Sha256] = {new string('b', 64)} WHERE [Id] = {doc.Id}")).Number);
        Assert.Equal(51182, (await SqlRefusalAsync($"DELETE FROM [Documents] WHERE [Id] = {doc.Id}")).Number);
    }

    // ---------- an incorrect document is refused by the database ----------

    public static TheoryData<string> IncorrectDocuments => new()
    {
        "tooth-unknown", "tooth-letter", "tooth-one-digit", "tooth-lowercase-region", "region-unknown", "region-wrong-case", "tooth-and-region", "status-unknown", "status-wrong-case",
        "inactive-without-reason", "inactive-blank-reason", "inactive-control-reason", "inactive-without-who", "inactive-without-time", "active-with-reason", "active-with-who", "active-with-time",
        "encounter-unknown",
    };

    [Theory]
    [MemberData(nameof(IncorrectDocuments))]
    public async Task The_database_refuses_an_incorrect_document(string problem)
    {
        var now = DateTimeOffset.UtcNow;
        var doc = NewDocument(d =>
        {
            switch (problem)
            {
                case "tooth-unknown": d.ToothKey = "99"; break;
                case "tooth-letter": d.ToothKey = "1A"; break;
                case "tooth-one-digit": d.ToothKey = "6"; break;
                case "tooth-lowercase-region": d.RegionKey = "upperleft"; break;
                case "region-unknown": d.RegionKey = "Nose"; break;
                case "region-wrong-case": d.RegionKey = "TMJ"; break;
                case "tooth-and-region": d.ToothKey = "16"; d.RegionKey = "FullMouth"; break;
                case "status-unknown": d.Status = "Deleted"; break;
                case "status-wrong-case": d.Status = "active"; break;
                case "inactive-without-reason": d.Status = DocumentStatuses.Inactive; d.InactivatedByUserId = S.Actor; d.InactivatedAtUtc = now; break;
                case "inactive-blank-reason": d.Status = DocumentStatuses.Inactive; d.InactiveReason = "   "; d.InactivatedByUserId = S.Actor; d.InactivatedAtUtc = now; break;
                case "inactive-control-reason": d.Status = DocumentStatuses.Inactive; d.InactiveReason = "bad\u0001"; d.InactivatedByUserId = S.Actor; d.InactivatedAtUtc = now; break;
                case "inactive-without-who": d.Status = DocumentStatuses.Inactive; d.InactiveReason = "Why."; d.InactivatedAtUtc = now; break;
                case "inactive-without-time": d.Status = DocumentStatuses.Inactive; d.InactiveReason = "Why."; d.InactivatedByUserId = S.Actor; break;
                case "active-with-reason": d.InactiveReason = "Why."; break;
                case "active-with-who": d.InactivatedByUserId = S.Actor; break;
                case "active-with-time": d.InactivatedAtUtc = now; break;
                case "encounter-unknown": d.EncounterId = Guid.NewGuid(); break;
            }
        });
        await Refusal(() => AddAsync(doc));
        Assert.Equal(0, await CountAsync<PatientDocument>());
    }

    [Fact]
    public async Task A_document_cannot_name_an_encounter_of_another_patient_when_added_or_later()
    {
        var bosEncounter = await EncounterAsync(Bo);
        Assert.Equal(51192, (await Refusal(() => AddAsync(NewDocument(d => d.EncounterId = bosEncounter)))).Number);
        var doc = NewDocument();
        await AddAsync(doc);
        Assert.Equal(51192, (await SqlRefusalAsync($"UPDATE [Documents] SET [EncounterId] = {bosEncounter} WHERE [Id] = {doc.Id}")).Number);
        Assert.Equal(1, await CountAsync<PatientDocument>());
    }

    // ---------- the storage check result ----------

    [Fact]
    public async Task Every_storage_result_is_accepted_for_a_document_and_a_check_can_be_repeated_by_replacing_its_row()
    {
        var docs = new[] { NewDocument(), NewDocument(), NewDocument() };
        await AddAsync([.. docs]);
        var results = new[] { IntegrityStatuses.Ok, IntegrityStatuses.Missing, IntegrityStatuses.Corrupt };
        await AddAsync([.. docs.Select((d, i) => new DocumentIntegrity { DocumentId = d.Id, Status = results[i], CheckedAtUtc = DateTimeOffset.UtcNow, CheckedByUserId = S.Actor })]);
        await using (var db = Fixture.CreateContext())
        {
            var row = await db.DocumentIntegrity.SingleAsync(i => i.DocumentId == docs[1].Id);
            (row.Status, row.CheckedAtUtc) = (IntegrityStatuses.Ok, DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
        }
        await using var check = Fixture.CreateContext();
        Assert.Equal([IntegrityStatuses.Corrupt, IntegrityStatuses.Ok, IntegrityStatuses.Ok], (await check.DocumentIntegrity.AsNoTracking().ToListAsync()).Select(i => i.Status).Order());
    }

    [Fact]
    public async Task The_migration_refuses_to_roll_back_over_a_storage_result()
    {
        var doc = NewDocument();
        await AddAsync(doc);
        await AddAsync(new DocumentIntegrity { DocumentId = doc.Id, Status = IntegrityStatuses.Missing, CheckedAtUtc = DateTimeOffset.UtcNow });
        var e = await Refusal(async () => { await using var db = Fixture.CreateContext(); await db.GetInfrastructure().GetRequiredService<IMigrator>().MigrateAsync(BeforeLibrary); });
        Assert.Equal(51194, e.Number);
        Assert.Equal(1, await CountAsync<DocumentIntegrity>());
    }

    [Fact]
    public async Task A_storage_check_never_moves_the_row_version_of_its_document()
    {
        var doc = NewDocument();
        await AddAsync(doc);
        byte[] before;
        await using (var db = Fixture.CreateContext()) before = (await db.Documents.AsNoTracking().SingleAsync()).RowVersion;
        await AddAsync(new DocumentIntegrity { DocumentId = doc.Id, Status = IntegrityStatuses.Corrupt, CheckedAtUtc = DateTimeOffset.UtcNow });
        await using var after = Fixture.CreateContext();
        Assert.Equal(before, (await after.Documents.AsNoTracking().SingleAsync()).RowVersion);
    }

    [Theory]
    [InlineData("Unchecked")]
    [InlineData("Fine")]
    [InlineData("ok")]
    [InlineData("time-too-early")]
    [InlineData("unknown-document")]
    [InlineData("second-row")]
    [InlineData("unknown-user")]
    public async Task The_database_refuses_an_incorrect_storage_result(string problem)
    {
        var doc = NewDocument();
        await AddAsync(doc);
        if (problem == "second-row") await AddAsync(new DocumentIntegrity { DocumentId = doc.Id, Status = IntegrityStatuses.Ok, CheckedAtUtc = DateTimeOffset.UtcNow });
        var row = new DocumentIntegrity
        {
            DocumentId = problem == "unknown-document" ? Guid.NewGuid() : doc.Id, Status = problem is "second-row" or "time-too-early" or "unknown-document" or "unknown-user" ? IntegrityStatuses.Ok : problem,
            CheckedAtUtc = problem == "time-too-early" ? new DateTimeOffset(1999, 1, 1, 0, 0, 0, TimeSpan.Zero) : DateTimeOffset.UtcNow, CheckedByUserId = problem == "unknown-user" ? Guid.NewGuid() : null,
        };
        await Refusal(() => AddAsync(row));
        Assert.Equal(problem == "second-row" ? 1 : 0, await CountAsync<DocumentIntegrity>());
    }

    // ---------- the status history ----------

    [Fact]
    public async Task An_inactivation_with_its_reason_and_a_reactivation_are_recorded_and_the_new_history_kinds_are_accepted()
    {
        var doc = NewDocument();
        await AddAsync(doc);
        await AddAsync(Change(doc.Id, DocumentStatuses.Inactive, "Scanned under the wrong patient."), Change(doc.Id, DocumentStatuses.Active, null));

        await using var db = Fixture.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync();
        var first = await DocumentEventNumbers.IssueAsync(db, default);
        var second = await DocumentEventNumbers.IssueAsync(db, default);
        db.DocumentEvents.AddRange(
            new DocumentEvent { EventNumber = first, Kind = DocumentEventKinds.Inactivated, DocumentId = doc.Id, PatientId = Ann, ByUserId = S.Actor, AtUtc = DateTimeOffset.UtcNow },
            new DocumentEvent { EventNumber = second, Kind = DocumentEventKinds.Reactivated, DocumentId = doc.Id, PatientId = Ann, ByUserId = S.Actor, AtUtc = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        Assert.Equal(2, await CountAsync<DocumentStatusChange>());
    }

    public static TheoryData<string> IncorrectStatusChanges => new() { "inactivate-without-reason", "inactivate-blank-reason", "inactivate-control-reason", "reactivate-with-reason", "unknown-status", "time-too-early", "unknown-document", "unknown-user" };

    [Theory]
    [MemberData(nameof(IncorrectStatusChanges))]
    public async Task The_database_refuses_an_incorrect_status_change(string problem)
    {
        var doc = NewDocument();
        await AddAsync(doc);
        var row = problem switch
        {
            "inactivate-without-reason" => Change(doc.Id, DocumentStatuses.Inactive, null),
            "inactivate-blank-reason" => Change(doc.Id, DocumentStatuses.Inactive, "  "),
            "inactivate-control-reason" => Change(doc.Id, DocumentStatuses.Inactive, "bad\u0001"),
            "reactivate-with-reason" => Change(doc.Id, DocumentStatuses.Active, "Why."),
            "unknown-status" => Change(doc.Id, "Deleted", null),
            "time-too-early" => Change(doc.Id, DocumentStatuses.Active, null, c => c.AtUtc = new DateTimeOffset(1999, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            "unknown-document" => Change(Guid.NewGuid(), DocumentStatuses.Active, null),
            _ => Change(doc.Id, DocumentStatuses.Active, null, c => c.ByUserId = Guid.NewGuid()),
        };
        await Refusal(() => AddAsync(row));
        Assert.Equal(0, await CountAsync<DocumentStatusChange>());
    }

    [Fact]
    public async Task A_status_change_is_append_only()
    {
        var doc = NewDocument();
        await AddAsync(doc);
        var change = Change(doc.Id, DocumentStatuses.Inactive, "Wrong patient.");
        await AddAsync(change);
        Assert.Equal(51193, (await SqlRefusalAsync($"UPDATE [DocumentStatusChanges] SET [Reason] = 'Changed' WHERE [Id] = {change.Id}")).Number);
        Assert.Equal(51193, (await SqlRefusalAsync($"DELETE FROM [DocumentStatusChanges] WHERE [Id] = {change.Id}")).Number);
        Assert.Equal(1, await CountAsync<DocumentStatusChange>());
    }

    // ---------- rolling back ----------

    [Fact]
    public async Task The_migration_rolls_back_and_forward_while_the_library_holds_nothing_and_refuses_to_roll_back_over_library_data()
    {
        var doc = NewDocument();
        await AddAsync(doc);
        await using (var db = Fixture.CreateContext()) await db.GetInfrastructure().GetRequiredService<IMigrator>().MigrateAsync(BeforeLibrary);
        await using (var db = Fixture.CreateContext())
        {
            Assert.Equal(0, await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM sys.columns WHERE object_id = OBJECT_ID('Documents') AND name IN ('Status','ToothKey','EncounterId')").SingleAsync());
            Assert.Equal(0, await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM sys.tables WHERE name IN ('DocumentStatusChanges','DocumentIntegrity')").SingleAsync());
            Assert.Equal(1, await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM [Documents]").SingleAsync());                       // STORY-010's document stays
        }
        await using (var db = Fixture.CreateContext()) await db.Database.MigrateAsync();
        await using (var db = Fixture.CreateContext())
        {
            Assert.Equal(DocumentStatuses.Active, (await db.Documents.AsNoTracking().SingleAsync()).Status);                                      // the existing document came through as Active and Unchecked
            Assert.Equal(2, await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM sys.tables WHERE name IN ('DocumentStatusChanges','DocumentIntegrity')").SingleAsync());
        }

        await using (var db = Fixture.CreateContext()) await db.Database.ExecuteSqlAsync($"UPDATE [Documents] SET [ToothKey] = '16' WHERE [Id] = {doc.Id}");
        var e = await Refusal(async () => { await using var db = Fixture.CreateContext(); await db.GetInfrastructure().GetRequiredService<IMigrator>().MigrateAsync(BeforeLibrary); });
        Assert.Equal(51194, e.Number);
        Assert.Equal("16", (await Fixture.CreateContext().Documents.AsNoTracking().SingleAsync()).ToothKey);                                         // the data is still there
    }
}
