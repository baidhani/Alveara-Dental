using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Alveara.Api.Architecture.Documents;
using Alveara.Api.Data;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// STORY-010, the database on its own: rows written straight to the tables, around any service, so the tests prove the database refuses an incorrect document or history row by itself, never deletes or changes an
/// imported file, keeps the history append-only with issued numbers that only move forward by one, locks the built-in blocked extensions, keeps a duplicate inside one patient and one hash, seeds what the story
/// promises exactly once, and refuses to roll back over recorded documents.
/// </summary>
public class DocumentSchemaTests : SafetyTestBase
{
    private const string BeforeDocuments = "20261014024801_AddPrescriptionWorkflow";
    private static readonly string HashA = new('a', 64);
    private static readonly string HashB = new('b', 64);

    private PatientDocument NewDocument(Action<PatientDocument>? change = null, Guid? patient = null)
    {
        var row = new PatientDocument
        {
            Id = Guid.NewGuid(), PatientId = patient ?? Ann, CategoryId = DocumentCategorySeeds.Radiograph, DocumentDate = new DateOnly(2026, 10, 1), Description = "Bitewing, left side.",
            OriginalFileName = "bitewing.png", DetectedType = "png", ContentType = "image/png", SizeBytes = 2048, Sha256 = HashA, BlobId = Guid.NewGuid(), ImportedByUserId = S.Actor,
            ImportedAtUtc = DateTimeOffset.UtcNow, IdempotencyKey = $"doc-{Guid.NewGuid():N}", RequestHash = HashA,
        };
        change?.Invoke(row);
        return row;
    }

    private async Task<PatientDocument> SavedAsync(Action<PatientDocument>? change = null, Guid? patient = null)
    {
        var d = NewDocument(change, patient);
        await AddAsync(d);
        return d;
    }

    private async Task AddAsync(params object[] rows)
    {
        await using var db = Fixture.CreateContext();
        db.AddRange(rows);
        await db.SaveChangesAsync();
    }

    /// <summary>Issues the next event number: one UPDATE on the counter in the caller's transaction (its row lock is held until that transaction ends), then a read of it.</summary>
    private static async Task<long> IssueAsync(AlveraDbContext db)
    {
        await db.Database.ExecuteSqlAsync($"UPDATE [DocumentEventCounter] SET [LastEventNumber] = [LastEventNumber] + 1 WHERE [Id] = 1");
        return await db.Database.SqlQuery<long>($"SELECT [LastEventNumber] AS [Value] FROM [DocumentEventCounter] WHERE [Id] = 1").SingleAsync();
    }

    private async Task<DocumentEvent> EventAsync(Guid documentId, string kind, Action<DocumentEvent>? change = null, Guid? patient = null)
    {
        await using var db = Fixture.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync();
        var e = new DocumentEvent { EventNumber = await IssueAsync(db), Kind = kind, DocumentId = documentId, PatientId = patient ?? Ann, ByUserId = S.Actor, AtUtc = DateTimeOffset.UtcNow };
        change?.Invoke(e);
        db.DocumentEvents.Add(e);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return e;
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

    // ---------- what the migration seeds ----------

    [Fact]
    public async Task The_migration_seeds_the_nine_categories_the_fifteen_blocked_extensions_the_counter_and_the_default_size_once()
    {
        await using var db = Fixture.CreateContext();
        var categories = await db.DocumentCategories.AsNoTracking().OrderBy(c => c.SortOrder).ToListAsync();
        Assert.Equal(["Radiograph", "Photo", "Referral letter", "Medical clearance", "Lab report", "Insurance card", "Identification", "Correspondence", "Other"], categories.Select(c => c.Name));
        Assert.Equal(["radiograph", "photo", "referral_letter", "medical_clearance", "lab_report", "insurance_card", "identification", "correspondence", "other"], categories.Select(c => c.Code));
        Assert.All(categories, c => Assert.True(c.IsActive));

        var blocked = await db.BlockedFileExtensions.AsNoTracking().ToListAsync();
        Assert.Equal(15, blocked.Count);
        Assert.All(blocked, b => Assert.True(b.IsBuiltIn));
        Assert.Equal(DocumentFileRules.BuiltInBlockedExtensions.OrderBy(x => x), blocked.Select(b => b.Extension).OrderBy(x => x));

        Assert.Equal(0, (await db.DocumentEventCounters.AsNoTracking().SingleAsync()).LastEventNumber);
        Assert.Equal(DocumentFileRules.DefaultMaxUploadBytes, (await db.DocumentSettings.AsNoTracking().SingleAsync()).MaxUploadBytes);
    }

    // ---------- what is accepted ----------

    [Fact]
    public async Task A_document_with_and_without_a_description_and_a_confirmed_duplicate_are_accepted_and_the_same_file_may_be_filed_for_two_patients()
    {
        var first = await SavedAsync();
        await AddAsync(
            NewDocument(d => d.Description = null),
            NewDocument(d => { d.DuplicateOfDocumentId = first.Id; d.Sha256 = HashA; }),
            NewDocument(patient: Bo));
        Assert.Equal(4, await CountAsync<PatientDocument>());
    }

    [Fact]
    public async Task Events_with_issued_numbers_are_accepted_and_a_recategorization_names_both_categories()
    {
        var doc = await SavedAsync();
        var copy = await SavedAsync(d => d.DuplicateOfDocumentId = doc.Id);
        await EventAsync(doc.Id, DocumentEventKinds.Imported);
        await EventAsync(copy.Id, DocumentEventKinds.ImportedAsDuplicate, e => e.RelatedDocumentId = doc.Id);
        await EventAsync(doc.Id, DocumentEventKinds.Categorized, e => { e.FromCategoryId = DocumentCategorySeeds.Radiograph; e.ToCategoryId = DocumentCategorySeeds.Photo; });
        await EventAsync(doc.Id, DocumentEventKinds.MetadataEdited);
        await EventAsync(doc.Id, DocumentEventKinds.Opened);
        await using var db = Fixture.CreateContext();
        Assert.Equal([1L, 2, 3, 4, 5], await db.DocumentEvents.AsNoTracking().OrderBy(e => e.EventNumber).Select(e => e.EventNumber).ToListAsync());
    }

    [Fact]
    public async Task The_category_and_the_description_of_a_document_may_change_and_a_category_may_be_renamed_and_deactivated()
    {
        var doc = await SavedAsync();
        await using (var db = Fixture.CreateContext())
        {
            var d = await db.Documents.SingleAsync(x => x.Id == doc.Id);
            d.CategoryId = DocumentCategorySeeds.Photo; d.Description = "Retaken."; d.DocumentDate = new DateOnly(2026, 10, 2);
            var c = await db.DocumentCategories.SingleAsync(x => x.Id == DocumentCategorySeeds.Other);
            c.Name = "Miscellaneous"; c.IsActive = false;
            await db.SaveChangesAsync();
        }
        await using var check = Fixture.CreateContext();
        Assert.Equal(DocumentCategorySeeds.Photo, (await check.Documents.AsNoTracking().SingleAsync(x => x.Id == doc.Id)).CategoryId);
        Assert.False((await check.DocumentCategories.AsNoTracking().SingleAsync(x => x.Id == DocumentCategorySeeds.Other)).IsActive);
    }

    // ---------- an incorrect row is refused by the database ----------

    public static TheoryData<string> IncorrectDocuments => new()
    {
        "hash-short", "hash-upper", "request-hash-bad", "key-blank", "name-blank", "name-control", "description-blank", "type-unknown", "type-wrong-case", "size-zero", "size-over-a-gigabyte",
        "date-too-early", "date-too-late", "content-type-blank", "imported-too-early",
    };

    [Theory]
    [MemberData(nameof(IncorrectDocuments))]
    public async Task The_database_refuses_an_incorrect_document(string problem)
    {
        var doc = NewDocument(d =>
        {
            switch (problem)
            {
                case "hash-short": d.Sha256 = new string('a', 63); break;
                case "hash-upper": d.Sha256 = new string('A', 64); break;
                case "request-hash-bad": d.RequestHash = new string('g', 64); break;
                case "key-blank": d.IdempotencyKey = "  "; break;
                case "name-blank": d.OriginalFileName = " "; break;
                case "name-control": d.OriginalFileName = "a\u0001.pdf"; break;
                case "description-blank": d.Description = "   "; break;
                case "type-unknown": d.DetectedType = "exe"; break;
                case "type-wrong-case": d.DetectedType = "PDF"; break;
                case "size-zero": d.SizeBytes = 0; break;
                case "size-over-a-gigabyte": d.SizeBytes = DocumentFileRules.MaxUploadLimitBytes + 1; break;
                case "date-too-early": d.DocumentDate = new DateOnly(1800, 1, 1); break;
                case "date-too-late": d.DocumentDate = new DateOnly(2300, 1, 1); break;
                case "content-type-blank": d.ContentType = " "; break;
                case "imported-too-early": d.ImportedAtUtc = new DateTimeOffset(1999, 1, 1, 0, 0, 0, TimeSpan.Zero); break;
            }
        });
        await Refusal(() => AddAsync(doc));
        Assert.Equal(0, await CountAsync<PatientDocument>());
    }

    [Fact]
    public async Task A_document_needs_a_real_patient_category_and_importer_and_one_key_makes_one_document_per_patient_and_one_blob_belongs_to_one_document()
    {
        await Refusal(() => AddAsync(NewDocument(d => d.PatientId = Guid.NewGuid())));
        await Refusal(() => AddAsync(NewDocument(d => d.CategoryId = Guid.NewGuid())));
        await Refusal(() => AddAsync(NewDocument(d => d.ImportedByUserId = Guid.NewGuid())));
        var first = await SavedAsync(d => d.IdempotencyKey = "same-key");
        await Refusal(() => AddAsync(NewDocument(d => d.IdempotencyKey = "same-key")));
        await AddAsync(NewDocument(d => d.IdempotencyKey = "same-key", Bo));                         // another patient may reuse the key
        await Refusal(() => AddAsync(NewDocument(d => d.BlobId = first.BlobId)));
        Assert.Equal(2, await CountAsync<PatientDocument>());
    }

    [Fact]
    public async Task A_document_cannot_be_its_own_duplicate()
    {
        var id = Guid.NewGuid();
        await Refusal(() => AddAsync(NewDocument(d => { d.Id = id; d.DuplicateOfDocumentId = id; })));
    }

    public static TheoryData<string> IncorrectEvents => new()
    {
        "kind-unknown", "duplicate-without-related", "related-on-import", "related-is-itself", "categorized-without-categories", "categories-on-import", "categorized-same-category", "number-zero", "time-too-early",
    };

    [Theory]
    [MemberData(nameof(IncorrectEvents))]
    public async Task The_database_refuses_an_incorrect_history_row(string problem)
    {
        var doc = await SavedAsync();
        var other = await SavedAsync();
        var kind = problem switch
        {
            "kind-unknown" => "Deleted",
            "duplicate-without-related" or "related-is-itself" => DocumentEventKinds.ImportedAsDuplicate,
            "categorized-without-categories" or "categorized-same-category" => DocumentEventKinds.Categorized,
            _ => DocumentEventKinds.Imported,
        };
        await Refusal(() => EventAsync(doc.Id, kind, e =>
        {
            switch (problem)
            {
                case "related-on-import": e.RelatedDocumentId = other.Id; break;
                case "related-is-itself": e.RelatedDocumentId = doc.Id; break;
                case "categories-on-import": e.FromCategoryId = DocumentCategorySeeds.Photo; e.ToCategoryId = DocumentCategorySeeds.Other; break;
                case "categorized-same-category": e.FromCategoryId = DocumentCategorySeeds.Photo; e.ToCategoryId = DocumentCategorySeeds.Photo; break;
                case "number-zero": e.EventNumber = 0; break;
                case "time-too-early": e.AtUtc = new DateTimeOffset(1999, 1, 1, 0, 0, 0, TimeSpan.Zero); break;
            }
        }));
        Assert.Equal(0, await CountAsync<DocumentEvent>());
    }

    [Fact]
    public async Task The_settings_and_the_counter_hold_one_row_and_the_size_limit_stays_between_one_megabyte_and_one_gigabyte()
    {
        await Refusal(() => AddAsync(new DocumentSettings { Id = 2, MaxUploadBytes = DocumentFileRules.DefaultMaxUploadBytes }));
        await Refusal(() => AddAsync(new DocumentEventCounter { Id = 2, LastEventNumber = 0 }));
        Assert.Equal(51190, (await SqlRefusalAsync($"DELETE FROM [DocumentSettings]")).Number);
        await SqlRefusalAsync($"UPDATE [DocumentSettings] SET [MaxUploadBytes] = {DocumentFileRules.MinUploadLimitBytes - 1}");
        await SqlRefusalAsync($"UPDATE [DocumentSettings] SET [MaxUploadBytes] = {DocumentFileRules.MaxUploadLimitBytes + 1}");
        await using (var db = Fixture.CreateContext())
        {
            await db.Database.ExecuteSqlAsync($"UPDATE [DocumentSettings] SET [MaxUploadBytes] = {DocumentFileRules.MinUploadLimitBytes}");
            await db.Database.ExecuteSqlAsync($"UPDATE [DocumentSettings] SET [MaxUploadBytes] = {DocumentFileRules.MaxUploadLimitBytes}");
        }
    }

    // ---------- the triggers ----------

    [Fact]
    public async Task A_document_is_never_deleted()
    {
        var doc = await SavedAsync();
        Assert.Equal(51182, (await SqlRefusalAsync($"DELETE FROM [Documents] WHERE [Id] = {doc.Id}")).Number);
        Assert.Equal(1, await CountAsync<PatientDocument>());
    }

    [Theory]
    [InlineData("PatientId")]
    [InlineData("BlobId")]
    [InlineData("Sha256")]
    [InlineData("SizeBytes")]
    [InlineData("OriginalFileName")]
    [InlineData("DetectedType")]
    [InlineData("ContentType")]
    [InlineData("ImportedByUserId")]
    [InlineData("ImportedAtUtc")]
    [InlineData("IdempotencyKey")]
    [InlineData("RequestHash")]
    [InlineData("DuplicateOfDocumentId")]
    public async Task The_file_of_a_document_never_changes(string column)
    {
        var other = await SavedAsync();
        var doc = await SavedAsync();
        await using var db = Fixture.CreateContext();
        var e = await Refusal(async () =>
        {
            switch (column)
            {
                case "PatientId": await db.Database.ExecuteSqlAsync($"UPDATE [Documents] SET [PatientId] = {Bo} WHERE [Id] = {doc.Id}"); break;
                case "BlobId": await db.Database.ExecuteSqlAsync($"UPDATE [Documents] SET [BlobId] = {Guid.NewGuid()} WHERE [Id] = {doc.Id}"); break;
                case "Sha256": await db.Database.ExecuteSqlAsync($"UPDATE [Documents] SET [Sha256] = {HashB} WHERE [Id] = {doc.Id}"); break;
                case "SizeBytes": await db.Database.ExecuteSqlAsync($"UPDATE [Documents] SET [SizeBytes] = 1 WHERE [Id] = {doc.Id}"); break;
                case "OriginalFileName": await db.Database.ExecuteSqlAsync($"UPDATE [Documents] SET [OriginalFileName] = 'other.png' WHERE [Id] = {doc.Id}"); break;
                case "DetectedType": await db.Database.ExecuteSqlAsync($"UPDATE [Documents] SET [DetectedType] = 'pdf' WHERE [Id] = {doc.Id}"); break;
                case "ContentType": await db.Database.ExecuteSqlAsync($"UPDATE [Documents] SET [ContentType] = 'text/html' WHERE [Id] = {doc.Id}"); break;
                case "ImportedByUserId": await db.Database.ExecuteSqlAsync($"UPDATE [Documents] SET [ImportedByUserId] = {Other} WHERE [Id] = {doc.Id}"); break;
                case "ImportedAtUtc": await db.Database.ExecuteSqlAsync($"UPDATE [Documents] SET [ImportedAtUtc] = '2020-01-01' WHERE [Id] = {doc.Id}"); break;
                case "IdempotencyKey": await db.Database.ExecuteSqlAsync($"UPDATE [Documents] SET [IdempotencyKey] = 'changed' WHERE [Id] = {doc.Id}"); break;
                case "RequestHash": await db.Database.ExecuteSqlAsync($"UPDATE [Documents] SET [RequestHash] = {HashB} WHERE [Id] = {doc.Id}"); break;
                default: await db.Database.ExecuteSqlAsync($"UPDATE [Documents] SET [DuplicateOfDocumentId] = {other.Id} WHERE [Id] = {doc.Id}"); break;
            }
        });
        Assert.Equal(51183, e.Number);
    }

    [Fact]
    public async Task A_duplicate_must_name_a_document_of_the_same_patient_with_the_same_hash()
    {
        var mine = await SavedAsync();
        var bos = await SavedAsync(patient: Bo);
        var different = await SavedAsync(d => d.Sha256 = HashB);
        Assert.Equal(51184, (await Refusal(() => AddAsync(NewDocument(d => d.DuplicateOfDocumentId = bos.Id)))).Number);       // another patient's document
        Assert.Equal(51184, (await Refusal(() => AddAsync(NewDocument(d => d.DuplicateOfDocumentId = different.Id)))).Number); // a different file
        await AddAsync(NewDocument(d => d.DuplicateOfDocumentId = mine.Id));
    }

    [Fact]
    public async Task The_history_is_append_only()
    {
        var doc = await SavedAsync();
        var ev = await EventAsync(doc.Id, DocumentEventKinds.Imported);
        Assert.Equal(51185, (await SqlRefusalAsync($"UPDATE [DocumentEvents] SET [Kind] = 'Opened' WHERE [EventNumber] = {ev.EventNumber}")).Number);
        Assert.Equal(51185, (await SqlRefusalAsync($"DELETE FROM [DocumentEvents] WHERE [EventNumber] = {ev.EventNumber}")).Number);
        Assert.Equal(1, await CountAsync<DocumentEvent>());
    }

    [Fact]
    public async Task A_history_row_needs_an_issued_number_and_the_patient_of_its_document_and_the_related_document_the_same_patient()
    {
        var doc = await SavedAsync();
        var bos = await SavedAsync(patient: Bo);
        // a number that was never issued (the counter is still 0)
        Assert.Equal(51186, (await Refusal(() => AddAsync(new DocumentEvent { EventNumber = 1, Kind = DocumentEventKinds.Imported, DocumentId = doc.Id, PatientId = Ann, ByUserId = S.Actor, AtUtc = DateTimeOffset.UtcNow }))).Number);
        // the wrong patient
        Assert.Equal(51186, (await Refusal(() => EventAsync(doc.Id, DocumentEventKinds.Imported, patient: Bo))).Number);
        // a related document of another patient
        Assert.Equal(51186, (await Refusal(() => EventAsync(doc.Id, DocumentEventKinds.ImportedAsDuplicate, e => e.RelatedDocumentId = bos.Id))).Number);
        Assert.Equal(0, await CountAsync<DocumentEvent>());
    }

    [Fact]
    public async Task A_rolled_back_event_leaves_no_gap_and_a_number_is_never_reused()
    {
        var doc = await SavedAsync();
        await using (var db = Fixture.CreateContext())
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            Assert.Equal(1, await IssueAsync(db));
            await tx.RollbackAsync();
        }
        var first = await EventAsync(doc.Id, DocumentEventKinds.Imported);
        var second = await EventAsync(doc.Id, DocumentEventKinds.Opened);
        Assert.Equal((1L, 2L), (first.EventNumber, second.EventNumber));
        await Refusal(() => AddAsync(new DocumentEvent { EventNumber = 2, Kind = DocumentEventKinds.Opened, DocumentId = doc.Id, PatientId = Ann, ByUserId = S.Actor, AtUtc = DateTimeOffset.UtcNow }));   // reuse
        Assert.Equal(51186, (await Refusal(() => AddAsync(new DocumentEvent { EventNumber = 3, Kind = DocumentEventKinds.Opened, DocumentId = doc.Id, PatientId = Ann, ByUserId = S.Actor, AtUtc = DateTimeOffset.UtcNow }))).Number);   // not issued yet
    }

    [Fact]
    public async Task The_counter_only_moves_forward_by_one_and_is_never_deleted()
    {
        Assert.Equal(51187, (await SqlRefusalAsync($"UPDATE [DocumentEventCounter] SET [LastEventNumber] = 5")).Number);
        Assert.Equal(51187, (await SqlRefusalAsync($"DELETE FROM [DocumentEventCounter]")).Number);
        await using (var db = Fixture.CreateContext()) Assert.Equal(1, await IssueAsync(db));
        Assert.Equal(51187, (await SqlRefusalAsync($"UPDATE [DocumentEventCounter] SET [LastEventNumber] = 0")).Number);       // never backwards
        Assert.Equal(51187, (await SqlRefusalAsync($"UPDATE [DocumentEventCounter] SET [LastEventNumber] = [LastEventNumber]")).Number);   // never standing still
    }

    [Fact]
    public async Task A_category_is_never_deleted_and_its_code_never_changes()
    {
        Assert.Equal(51188, (await SqlRefusalAsync($"DELETE FROM [DocumentCategories] WHERE [Id] = {DocumentCategorySeeds.Photo}")).Number);
        Assert.Equal(51188, (await SqlRefusalAsync($"UPDATE [DocumentCategories] SET [Code] = 'picture' WHERE [Id] = {DocumentCategorySeeds.Photo}")).Number);
        Assert.Equal(9, await CountAsync<DocumentCategory>());
    }

    [Fact]
    public async Task A_new_category_may_be_added_but_not_with_a_code_or_name_already_in_use()
    {
        await AddAsync(new DocumentCategory { Id = Guid.NewGuid(), Code = "consent_copy", Name = "Consent copy", SortOrder = 100 });
        await Refusal(() => AddAsync(new DocumentCategory { Id = Guid.NewGuid(), Code = "consent_copy", Name = "Another name", SortOrder = 110 }));
        await Refusal(() => AddAsync(new DocumentCategory { Id = Guid.NewGuid(), Code = "another_code", Name = "Consent copy", SortOrder = 110 }));
        await Refusal(() => AddAsync(new DocumentCategory { Id = Guid.NewGuid(), Code = "Bad Code", Name = "Bad code", SortOrder = 120 }));
        Assert.Equal(10, await CountAsync<DocumentCategory>());
    }

    [Fact]
    public async Task A_built_in_blocked_extension_is_never_changed_or_deleted_while_an_added_one_can_be_removed()
    {
        Assert.Equal(51189, (await SqlRefusalAsync($"DELETE FROM [BlockedFileExtensions] WHERE [Extension] = 'exe'")).Number);
        Assert.Equal(51189, (await SqlRefusalAsync($"UPDATE [BlockedFileExtensions] SET [Extension] = 'exe2' WHERE [Extension] = 'exe'")).Number);
        Assert.Equal(51189, (await SqlRefusalAsync($"UPDATE [BlockedFileExtensions] SET [IsBuiltIn] = 0, [AddedByUserId] = {S.Actor} WHERE [Extension] = 'js'")).Number);

        await AddAsync(new BlockedFileExtension { Extension = "swf", IsBuiltIn = false, AddedByUserId = S.Actor, AddedAtUtc = DateTimeOffset.UtcNow });
        await Refusal(() => AddAsync(new BlockedFileExtension { Extension = "swf", IsBuiltIn = false, AddedByUserId = S.Actor, AddedAtUtc = DateTimeOffset.UtcNow }));       // one row per extension
        await Refusal(() => AddAsync(new BlockedFileExtension { Extension = "bad.ext", IsBuiltIn = false, AddedByUserId = S.Actor, AddedAtUtc = DateTimeOffset.UtcNow }));
        await Refusal(() => AddAsync(new BlockedFileExtension { Extension = "NOPE", IsBuiltIn = false, AddedByUserId = S.Actor, AddedAtUtc = DateTimeOffset.UtcNow }));
        await Refusal(() => AddAsync(new BlockedFileExtension { Extension = "anon", IsBuiltIn = false, AddedByUserId = null, AddedAtUtc = DateTimeOffset.UtcNow }));            // an added row names who added it
        await using (var db = Fixture.CreateContext()) await db.Database.ExecuteSqlAsync($"DELETE FROM [BlockedFileExtensions] WHERE [Extension] = 'swf'");
        Assert.Equal(15, await CountAsync<BlockedFileExtension>());
    }

    // ---------- rolling back ----------

    [Fact]
    public async Task The_migration_rolls_back_and_forward_while_no_document_exists_and_refuses_to_roll_back_over_a_document()
    {
        await using (var db = Fixture.CreateContext()) await db.GetInfrastructure().GetRequiredService<IMigrator>().MigrateAsync(BeforeDocuments);
        await using (var db = Fixture.CreateContext())
            Assert.Equal(0, await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM sys.tables WHERE name IN ('Documents','DocumentEvents','DocumentCategories','DocumentSettings','DocumentEventCounter','BlockedFileExtensions')").SingleAsync());
        await using (var db = Fixture.CreateContext()) await db.Database.MigrateAsync();
        await using (var db = Fixture.CreateContext())
        {
            Assert.Equal(6, await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM sys.tables WHERE name IN ('Documents','DocumentEvents','DocumentCategories','DocumentSettings','DocumentEventCounter','BlockedFileExtensions')").SingleAsync());
            Assert.Equal(9, await db.DocumentCategories.CountAsync());                                         // seeded once, again after the roll forward
            Assert.Equal(15, await db.BlockedFileExtensions.CountAsync());
        }

        await SavedAsync();
        var e = await Refusal(async () => { await using var db = Fixture.CreateContext(); await db.GetInfrastructure().GetRequiredService<IMigrator>().MigrateAsync(BeforeDocuments); });
        Assert.Equal(51191, e.Number);
        Assert.Equal(1, await CountAsync<PatientDocument>());                                                   // the document is still there
    }
}
