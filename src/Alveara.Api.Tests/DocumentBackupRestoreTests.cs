using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Backup;
using Alveara.Api.Architecture.Documents;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Storage;
using Alveara.Api.Controllers;
using Alveara.Api.Data;
using Xunit;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-010-C01, the recovery drill for documents against a real SQL Server restore: documents imported through the real service (a PNG, a PDF, a text file, a confirmed duplicate, one with an encounter and a tooth, an
/// inactive one with its reason) are backed up and restored into an isolated target, and the restored database and files prove that every file came back byte for byte with its recorded hash, every record, history row
/// and status change came back, the history numbers are intact, the storage check finds nothing wrong and the recovered application serves the documents. A backup that lacks a document's file, or holds a file that
/// no longer matches the hash recorded at import, fails the restore validation instead of being trusted.
/// </summary>
[Collection(ParallelismCollections.SerialServer)]
public class DocumentBackupRestoreTests : DocumentTestBase, IAsyncLifetime
{
    private BackupTestEnvironment _env = null!;
    private readonly List<string> _restored = [];
    private string _unusedRoot = "";                                                                      // the folder the base class made before this class pointed the documents at the backup environment

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _unusedRoot = _storageRoot;
        _env = new BackupTestEnvironment(Fixture);
        await _env.ResetBackupStateAsync();
        await _env.ConfigureRecoveryKeyAsync();
        _storageRoot = _env.BlobRoot;                                                                       // the documents live where the backup looks for the documents asset class
        _blobs = new RecordingBlobStorage(new LocalDiskBlobStorage(_env.BlobRoot));
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        foreach (var name in _restored) await _env.RestoreProvider.DropDatabaseAsync(name, default);
        _env.Dispose();
        await Fixture.DisposeAsync();
        if (_unusedRoot.Length > 0) TestStorageCleanup.Delete(_unusedRoot);
    }

    private sealed record Expected(Guid Id, byte[] Bytes, string Sha256, string Name, string Status, string? Reason, string? Tooth, Guid? Encounter, Guid Category, string? Description, Guid? DuplicateOf);

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

    private static IReadOnlyList<ValidationCheck> ChecksOf(RestoreDrillRecord drill) => JsonSerializer.Deserialize<List<ValidationCheck>>(drill.ValidationJson!, BackupManifest.Json)!;

    private AlveraDbContext RestoredDb(RestoreDrillRecord drill) =>
        new(new DbContextOptionsBuilder<AlveraDbContext>().UseSqlServer(_env.RestoreProvider.ConnectionStringFor(drill.TargetDatabase!)).Options);

    private async Task<Guid> EncounterAsync()
    {
        var e = new Alveara.Api.Architecture.Clinical.Encounter { Id = Guid.NewGuid(), PatientId = Ann, EncounterAtUtc = DateTimeOffset.UtcNow, Status = Alveara.Api.Architecture.Clinical.EncounterStatuses.Draft, CreatedAtUtc = DateTimeOffset.UtcNow };
        await WithDb(async db => { db.Encounters.Add(e); await db.SaveChangesAsync(); return 0; });
        return e.Id;
    }

    private static byte[] Pdf() => [.. "%PDF-1.7\n"u8.ToArray(), .. Enumerable.Repeat((byte)'p', 700)];

    /// <summary>A representative library: five documents of different kinds, one of them inactive, and a storage check so results exist to be restored.</summary>
    private async Task<List<Expected>> SeedLibraryAsync()
    {
        var encounter = await EncounterAsync();
        var png = Png(4000, 3);
        var made = new List<(DocumentView Doc, byte[] Bytes)>();
        async Task<DocumentView> Add(byte[] bytes, string name, Guid? category = null, string? description = null, Guid? confirm = null, Guid? enc = null, string? tooth = null)
        {
            var r = await ImportAsync(Request(bytes, name, category: category, description: description) with { ConfirmDuplicateOf = confirm, EncounterId = enc, ToothKey = tooth });
            made.Add((r.Document, bytes));
            return r.Document;
        }
        var first = await Add(png, "bitewing.png", DocumentCategorySeeds.Radiograph, "Bitewing, left side.", enc: encounter, tooth: "16");
        await Add(Pdf(), "referral.pdf", DocumentCategorySeeds.ReferralLetter, "To the periodontist.");
        await Add("plain notes, nothing verified about this type"u8.ToArray(), "notes.txt", DocumentCategorySeeds.Correspondence);
        await Add(png, "bitewing again.png", DocumentCategorySeeds.Radiograph, "Same picture, added on purpose.", confirm: first.Id);
        var wrong = await Add(Png(900, 9), "wrong-patient-scan.png", DocumentCategorySeeds.Identification);
        await WithDb(async db => { await new DocumentStatusService(db, Clock).InactivateAsync(wrong.Id, wrong.RowVersion, "Scanned under the wrong patient.", S.Actor, default); return 0; });
        await WithDb(db => new DocumentIntegrityService(db, _blobs, Clock).CheckAllAsync(S.Actor, default));

        await using var read = Fixture.CreateContext();
        var rows = await read.Documents.AsNoTracking().ToDictionaryAsync(d => d.Id);
        return made.Select(m => { var d = rows[m.Doc.Id]; return new Expected(d.Id, m.Bytes, d.Sha256, d.OriginalFileName, d.Status, d.InactiveReason, d.ToothKey, d.EncounterId, d.CategoryId, d.Description, d.DuplicateOfDocumentId); }).ToList();
    }

    // ---------- the drill ----------

    [Fact]
    public async Task Documents_survive_a_backup_and_a_restore_byte_for_byte_with_their_records_history_and_status_changes()
    {
        var expected = await SeedLibraryAsync();
        var record = await BackupAsync();

        // life goes on after the backup: a file is lost, a document is added and another is made inactive. The restore returns the state of the backup, not of today.
        File.Delete(Path.Combine(_env.BlobRoot, (await BlobOfAsync(expected[0].Id)).ToString("N")));
        var later = (await ImportAsync(Request(Png(5000, 1), "after-the-backup.png"))).Document;
        await WithDb(async db => { var row = await db.Documents.AsNoTracking().SingleAsync(d => d.Id == expected[2].Id); await new DocumentStatusService(db, Clock).InactivateAsync(row.Id, Convert.ToBase64String(row.RowVersion), "Changed after the backup.", S.Actor, default); return 0; });

        var drill = await DrillAsync(record.Id);
        Assert.Equal("Succeeded", drill.Outcome);
        var checks = ChecksOf(drill);
        Assert.All(checks.Where(c => c.Blocking), c => Assert.True(c.Passed, c.Name + ": " + c.Detail));
        Assert.Contains(checks, c => c.Name == "restored_documents_have_their_files" && c.Passed);
        var restoredFiles = Path.Combine(drill.TargetDirectory!, "extracted", ManagedAssetClasses.Documents);

        await using var restored = RestoredDb(drill);
        var rows = await restored.Documents.AsNoTracking().ToDictionaryAsync(d => d.Id);
        Assert.Equal(expected.Count, rows.Count);
        Assert.DoesNotContain(later.Id, rows.Keys);                                                                   // added after the backup: not in it
        foreach (var e in expected)
        {
            var d = rows[e.Id];
            Assert.Equal((e.Name, e.Status, e.Reason, e.Tooth, e.Encounter, e.Category, e.Description, e.DuplicateOf), (d.OriginalFileName, d.Status, d.InactiveReason, d.ToothKey, d.EncounterId, d.CategoryId, d.Description, d.DuplicateOfDocumentId));
            var file = Path.Combine(restoredFiles, d.BlobId.ToString("N"));
            Assert.True(File.Exists(file), $"{e.Name}: the file did not come back");
            var bytes = await File.ReadAllBytesAsync(file);
            Assert.Equal(e.Bytes, bytes);                                                                              // byte for byte
            Assert.Equal(e.Sha256, Convert.ToHexStringLower(SHA256.HashData(bytes)));                                  // and the hash recorded at import still matches
            Assert.Equal(e.Bytes.Length, d.SizeBytes);
        }

        // the history is intact: numbers 1..N with no gap, the counter agrees, and the reason of the inactivation is kept
        var numbers = await restored.DocumentEvents.AsNoTracking().OrderBy(x => x.EventNumber).Select(x => x.EventNumber).ToListAsync();
        Assert.Equal(Enumerable.Range(1, numbers.Count).Select(i => (long)i), numbers);
        Assert.Equal(numbers.Count, (await restored.DocumentEventCounters.AsNoTracking().SingleAsync()).LastEventNumber);
        var change = await restored.DocumentStatusChanges.AsNoTracking().SingleAsync();
        Assert.Equal(("Inactive", "Scanned under the wrong patient."), (change.ToStatus, change.Reason));
        Assert.Equal(expected.Count, await restored.DocumentIntegrity.AsNoTracking().CountAsync());
        Assert.Equal(1, await restored.DocumentEvents.AsNoTracking().CountAsync(x => x.Kind == DocumentEventKinds.ImportedAsDuplicate));

        // the storage check, run against the restored database and files, finds everything intact and nothing unreferenced
        var report = await new DocumentIntegrityService(restored, new LocalDiskBlobStorage(restoredFiles), Clock).CheckAllAsync(S.Actor, default);
        Assert.Equal((expected.Count, expected.Count, 0, 0, 0), (report.Checked, report.Ok, report.Missing, report.Corrupt, report.Skipped));
        Assert.Empty(report.Orphans);
    }

    private async Task<Guid> BlobOfAsync(Guid documentId) { await using var db = Fixture.CreateContext(); return (await db.Documents.AsNoTracking().SingleAsync(d => d.Id == documentId)).BlobId; }

    [Fact]
    public async Task A_backup_that_lacks_a_documents_file_fails_the_restore_validation_instead_of_being_trusted()
    {
        var expected = await SeedLibraryAsync();
        File.Delete(Path.Combine(_env.BlobRoot, (await BlobOfAsync(expected[1].Id)).ToString("N")));                // the file is gone before the backup is taken, so the set does not contain it
        var record = await BackupAsync();

        var drill = await DrillAsync(record.Id);
        Assert.Equal(("Failed", "restore_validation_failed"), (drill.Outcome, drill.FailureCode));
        var check = ChecksOf(drill).Single(c => c.Name == "restored_documents_have_their_files");
        Assert.False(check.Passed);
        Assert.Contains("1 document", check.Detail);
        Assert.DoesNotContain("referral", check.Detail, StringComparison.OrdinalIgnoreCase);                         // counts and ids, never a file name
    }

    [Fact]
    public async Task A_backup_whose_file_no_longer_matches_the_hash_recorded_at_import_fails_the_restore_validation()
    {
        var expected = await SeedLibraryAsync();
        var path = Path.Combine(_env.BlobRoot, (await BlobOfAsync(expected[0].Id)).ToString("N"));
        var bytes = File.ReadAllBytes(path); bytes[^1] ^= 0xFF; File.WriteAllBytes(path, bytes);                      // damaged before the backup: the set holds the damaged copy, with a matching manifest hash
        var record = await BackupAsync();

        var drill = await DrillAsync(record.Id);
        Assert.Equal(("Failed", "restore_validation_failed"), (drill.Outcome, drill.FailureCode));
        Assert.False(ChecksOf(drill).Single(c => c.Name == "restored_documents_have_their_files").Passed);
    }

    [Fact]
    public async Task An_empty_library_restores_and_the_check_passes_because_there_is_nothing_to_lose()
    {
        var record = await BackupAsync();
        var drill = await DrillAsync(record.Id);
        Assert.Equal("Succeeded", drill.Outcome);
        var check = ChecksOf(drill).Single(c => c.Name == "restored_documents_have_their_files");
        Assert.True(check.Passed);
    }

    [Fact]
    public async Task The_recovered_application_serves_the_restored_documents_and_their_storage_report_is_clean()
    {
        var expected = await SeedLibraryAsync();
        const string password = "recovered-app-password-1";
        string username;
        await using (var seed = _env.NewDb())
        {
            var accounts = new AccountService(seed, _env.DataProtection);
            username = $"doc-drill-{Guid.NewGuid():N}";
            var created = await accounts.RegisterAsync(username, password);
            await accounts.ChangeRoleAsync(created.Id, Role.Admin, S.Actor);
            await accounts.SetAccountEnabledAsync(created.Id, true, S.Actor);
        }
        var record = await BackupAsync();
        var drill = await DrillAsync(record.Id);
        Assert.Equal("Succeeded", drill.Outcome);
        var extracted = Path.Combine(drill.TargetDirectory!, "extracted");

        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Alveara", _env.RestoreProvider.ConnectionStringFor(drill.TargetDatabase!));
            builder.UseSetting("StorageRoot", Path.Combine(extracted, ManagedAssetClasses.Documents));
            builder.UseSetting("DataProtectionKeysPath", Path.Combine(extracted, ManagedAssetClasses.DataProtectionKeys));
            builder.UseSetting("Backup:Root", Path.Combine(_env.Root, "recovered-app-backup"));
            builder.UseSetting("DataProtection:ApplicationName", BackupTestEnvironment.DataProtectionApplicationName);
            builder.UseSetting("AdminBootstrapSecret", IdentityTestHelpers.TestBootstrapSecret);
        });
        var client = factory.CreateClient();
        Assert.True((await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(username, password))).IsSuccessStatusCode);

        var library = await client.GetFromJsonAsync<JsonElement>($"/api/patients/{Ann}/documents?status=All");
        var entries = library.GetProperty("entries").EnumerateArray().Where(e => e.GetProperty("kind").GetString() == "Document").ToList();
        Assert.Equal(expected.Count, entries.Count);
        foreach (var e in expected)
        {
            var served = await client.GetAsync($"/api/documents/{e.Id}/content?download=true");
            Assert.Equal(HttpStatusCode.OK, served.StatusCode);
            Assert.Equal(e.Bytes, await served.Content.ReadAsByteArrayAsync());
        }
        var thumb = await client.GetAsync($"/api/documents/{expected[0].Id}/thumbnail");
        Assert.Equal(HttpStatusCode.OK, thumb.StatusCode);

        var csrf = (await client.GetFromJsonAsync<JsonElement>("/api/auth/csrf-token")).GetProperty("token").GetString()!;
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/document-storage/check");
        request.Headers.Add("X-CSRF-Token", csrf);
        var storage = JsonDocument.Parse(await (await client.SendAsync(request)).Content.ReadAsStringAsync()).RootElement;
        Assert.Equal((expected.Count, expected.Count, 0, 0), (storage.GetProperty("checked").GetInt32(), storage.GetProperty("ok").GetInt32(), storage.GetProperty("missing").GetInt32(), storage.GetProperty("corrupt").GetInt32()));
        Assert.Equal(0, storage.GetProperty("orphans").GetArrayLength());
    }
}
