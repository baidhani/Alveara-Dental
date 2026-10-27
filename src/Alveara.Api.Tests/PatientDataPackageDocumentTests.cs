using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Alveara.Api.Architecture.Documents;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Patients;
using Alveara.Api.Architecture.Patients.Portability.Package;
using Alveara.Api.Architecture.Storage;
using Alveara.Api.Data;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-018-C01, the document files in the package against a real SQL Server and real files on disk: each file is copied under <c>documents/&lt;patient folder&gt;/&lt;document id&gt;/&lt;name&gt;</c> byte for byte with the
/// hash the manifest gives (a round trip); another patient's files are not there; a file that no longer matches its recorded hash is included as found, marked HashMismatch, and makes the package Partial; a missing file
/// is marked Missing, has no entry and makes the package Partial; a manifest-only export lists every document and reads no file; folders are named by id or by name and id as asked, and the manifest maps every patient
/// to its folder; a file name a user typed can never leave its folder; two files with one name both survive; and document files over the package limit are refused, the history row is Failed and nothing is left on disk.
/// </summary>
public class PatientDataPackageDocumentTests : MergeChartTestBase
{
    private string _temp = null!;
    private string _blobRoot = null!;
    private LocalDiskBlobStorage _blobs = null!;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _temp = Path.Combine(Path.GetTempPath(), $"alveara-export-docs-{Guid.NewGuid():N}");
        _blobRoot = Path.Combine(_temp, "blobs");
        Directory.CreateDirectory(_temp);
        _blobs = new LocalDiskBlobStorage(_blobRoot);
    }

    public override async Task DisposeAsync()
    {
        await base.DisposeAsync();
        if (Directory.Exists(_temp)) Directory.Delete(_temp, recursive: true);
    }

    private async Task<(Guid Id, byte[] Content, StoredBlob Blob)> AddDocumentAsync(Guid patient, string fileName, int size = 600)
    {
        var content = RandomNumberGenerator.GetBytes(size);
        var blob = await _blobs.StoreAsync(new MemoryStream(content));
        var id = Guid.NewGuid();
        await using var db = Fixture.CreateContext();
        db.Add(new PatientDocument
        {
            Id = id, PatientId = patient, CategoryId = DocumentCategorySeeds.Radiograph, DocumentDate = new DateOnly(2026, 10, 1), OriginalFileName = fileName, DetectedType = "png", ContentType = "image/png",
            SizeBytes = blob.SizeBytes, Sha256 = blob.Sha256Hash, BlobId = blob.Id, ImportedByUserId = S.Actor, ImportedAtUtc = DateTimeOffset.UtcNow, IdempotencyKey = $"doc-{Guid.NewGuid():N}", RequestHash = new string('b', 64),
        });
        await db.SaveChangesAsync();
        return (id, content, blob);
    }

    private Task<DataPackageFile> ExportAsync(Guid patient, PackageChoices? choices = null, long? maxBytes = null)
    {
        var services = new ServiceCollection().AddScoped(_ => Fixture.CreateContext()).BuildServiceProvider();
        var db = Fixture.CreateContext();
        var options = Options.Create(new DataExportOptions { TempDirectory = _temp, MaxPackageBytes = maxBytes ?? new DataExportOptions().MaxPackageBytes });
        var documents = new PackageDocumentWriter(db, _blobs, options, NullLogger<PackageDocumentWriter>.Instance);
        var builder = new PatientDataPackageBuilder(db, Clock, options, new ReadableRecordLoader(db, Clock), documents, NullLogger<PatientDataPackageBuilder>.Instance);
        return new PatientDataPackageService(db, builder, Clock, services.GetRequiredService<IServiceScopeFactory>(), NullLogger<PatientDataPackageService>.Instance).ExportPatientAsync(patient, S.Actor, default, choices);
    }

    private static Dictionary<string, byte[]> Entries(Stream zip)
    {
        zip.Position = 0;
        using var archive = new ZipArchive(zip, ZipArchiveMode.Read, leaveOpen: true);
        return archive.Entries.ToDictionary(e => e.FullName, e => { using var s = e.Open(); using var m = new MemoryStream(); s.CopyTo(m); return m.ToArray(); });
    }

    private static JsonElement Manifest(Dictionary<string, byte[]> entries) => JsonDocument.Parse(entries["manifest.json"]).RootElement;

    private static JsonElement DocumentEntry(JsonElement manifest, Guid id) => manifest.GetProperty("documents").EnumerateArray().Single(d => d.GetProperty("documentId").GetGuid() == id);

    [Fact]
    public async Task Each_document_file_is_in_the_package_under_its_patient_byte_for_byte_with_the_hash_the_manifest_gives()
    {
        var first = await AddDocumentAsync(Ann, "pano.png");
        var second = await AddDocumentAsync(Ann, "bitewing.png", 1500);
        var others = await AddDocumentAsync(Bo, "not-ann.png");

        await using var file = await ExportAsync(Ann);
        var entries = Entries(file.Content);
        var manifest = Manifest(entries);

        var folder = manifest.GetProperty("patientFolders").EnumerateArray().Single().GetProperty("folder").GetString();
        Assert.Equal(Ann.ToString("D"), folder);                                                                     // id only is the default
        Assert.Equal(first.Content, entries[$"documents/{folder}/{first.Id:D}/pano.png"]);                           // the round trip: what went in is what comes out
        Assert.Equal(second.Content, entries[$"documents/{folder}/{second.Id:D}/bitewing.png"]);
        Assert.DoesNotContain(entries.Keys, k => k.Contains(others.Id.ToString("D")));                               // another patient's file is not here
        Assert.Equal("Complete", manifest.GetProperty("result").GetString());
        Assert.Equal(0, manifest.GetProperty("problems").GetArrayLength());
        Assert.True(manifest.GetProperty("documentFilesIncluded").GetBoolean());
        Assert.Equal(2, manifest.GetProperty("documents").GetArrayLength());
        var entry = DocumentEntry(manifest, first.Id);
        Assert.Equal(("Included", first.Blob.Sha256Hash, first.Blob.Sha256Hash), (entry.GetProperty("status").GetString(), entry.GetProperty("sha256").GetString(), entry.GetProperty("actualSha256").GetString()));
        Assert.Equal($"documents/{folder}/{first.Id:D}/pano.png", entry.GetProperty("path").GetString());
        var listed = manifest.GetProperty("files").EnumerateArray().Single(f => f.GetProperty("path").GetString() == entry.GetProperty("path").GetString());
        Assert.Equal((first.Content.Length, Convert.ToHexStringLower(SHA256.HashData(first.Content))), (listed.GetProperty("bytes").GetInt32(), listed.GetProperty("sha256").GetString()));
        var page = System.Text.Encoding.UTF8.GetString(entries["readable-record.html"]);
        Assert.Contains("in this patient's own folder", page);
    }

    [Fact]
    public async Task A_file_that_no_longer_matches_its_recorded_hash_is_included_as_found_marked_and_makes_the_package_partial()
    {
        var good = await AddDocumentAsync(Ann, "good.png");
        var damaged = await AddDocumentAsync(Ann, "damaged.png");
        var changed = RandomNumberGenerator.GetBytes(600);
        await File.WriteAllBytesAsync(Path.Combine(_blobRoot, damaged.Blob.Id.ToString("N")), changed);               // the file on disk is no longer what was imported

        await using var file = await ExportAsync(Ann);
        var entries = Entries(file.Content);
        var manifest = Manifest(entries);

        Assert.Equal("Partial", file.Result);
        var entry = DocumentEntry(manifest, damaged.Id);
        Assert.Equal("HashMismatch", entry.GetProperty("status").GetString());
        Assert.Equal(damaged.Blob.Sha256Hash, entry.GetProperty("sha256").GetString());                              // what was recorded
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(changed)), entry.GetProperty("actualSha256").GetString());   // what was found
        Assert.Equal(changed, entries[entry.GetProperty("path").GetString()!]);                                       // included as found, not hidden and not repaired
        Assert.Equal("Included", DocumentEntry(manifest, good.Id).GetProperty("status").GetString());                  // one bad file does not touch the others
        var problem = Assert.Single(manifest.GetProperty("problems").EnumerateArray());
        Assert.Equal(("document_hash_mismatch", damaged.Id.ToString("D")), (problem.GetProperty("code").GetString(), problem.GetProperty("where").GetString()));
        var row = await WithDb(db => db.DataExportHistory.AsNoTracking().SingleAsync(h => h.ExportId == file.ExportId));
        Assert.Equal((DataExportResult.Partial, 1), (row.Result, row.ProblemCount));
    }

    [Fact]
    public async Task A_file_missing_from_storage_is_marked_missing_has_no_entry_and_makes_the_package_partial()
    {
        var present = await AddDocumentAsync(Ann, "present.png");
        var gone = await AddDocumentAsync(Ann, "gone.png");
        await _blobs.DeleteAsync(gone.Blob.Id);

        await using var file = await ExportAsync(Ann);
        var entries = Entries(file.Content);
        var manifest = Manifest(entries);

        Assert.Equal("Partial", file.Result);
        var entry = DocumentEntry(manifest, gone.Id);
        Assert.Equal("Missing", entry.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, entry.GetProperty("path").ValueKind);
        Assert.Equal(JsonValueKind.Null, entry.GetProperty("actualSha256").ValueKind);
        Assert.DoesNotContain(entries.Keys, k => k.Contains(gone.Id.ToString("D")));
        Assert.Equal("Included", DocumentEntry(manifest, present.Id).GetProperty("status").GetString());
        Assert.Equal("document_missing", Assert.Single(manifest.GetProperty("problems").EnumerateArray()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_manifest_only_export_lists_every_document_reads_no_file_and_is_still_complete()
    {
        await AddDocumentAsync(Ann, "pano.png");
        var gone = await AddDocumentAsync(Ann, "gone.png");
        await _blobs.DeleteAsync(gone.Blob.Id);                                                                       // proves the file is not read: a missing file is no problem here

        await using var file = await ExportAsync(Ann, new PackageChoices(IncludeDocumentFiles: false));
        var entries = Entries(file.Content);
        var manifest = Manifest(entries);

        Assert.Equal("Complete", file.Result);
        Assert.DoesNotContain(entries.Keys, k => k.StartsWith("documents/"));
        Assert.False(manifest.GetProperty("documentFilesIncluded").GetBoolean());
        Assert.Equal(2, manifest.GetProperty("documents").GetArrayLength());
        Assert.All(manifest.GetProperty("documents").EnumerateArray(), d =>
        {
            Assert.Equal("NotIncluded", d.GetProperty("status").GetString());
            Assert.Equal(JsonValueKind.Null, d.GetProperty("path").ValueKind);
            Assert.Equal(64, d.GetProperty("sha256").GetString()!.Length);                                           // the recorded hash is still listed
        });
        Assert.Contains("without the document files", System.Text.Encoding.UTF8.GetString(entries["readable-record.html"]));
        Assert.Contains("WITHOUT the document files", System.Text.Encoding.UTF8.GetString(entries["README.txt"]));
        var row = await WithDb(db => db.DataExportHistory.AsNoTracking().SingleAsync(h => h.ExportId == file.ExportId));
        Assert.False(row.IncludesDocumentFiles);
    }

    [Fact]
    public async Task Folders_are_named_by_name_and_id_when_asked_and_the_manifest_maps_the_patient_to_it()
    {
        var doc = await AddDocumentAsync(Ann, "pano.png");
        var ann = await WithDb(db => db.Patients.AsNoTracking().SingleAsync(p => p.Id == Ann));

        await using var file = await ExportAsync(Ann, new PackageChoices(FolderNaming: FolderNaming.NameAndId));
        var entries = Entries(file.Content);
        var manifest = Manifest(entries);

        var mapping = manifest.GetProperty("patientFolders").EnumerateArray().Single();
        var folder = mapping.GetProperty("folder").GetString()!;
        Assert.Equal(Ann, mapping.GetProperty("patientId").GetGuid());
        Assert.Equal(PackagePaths.PatientFolder(Ann, ann.FirstName, ann.LastName, FolderNaming.NameAndId), folder);
        Assert.EndsWith($"_{Ann:D}", folder);
        Assert.Equal("NameAndId", manifest.GetProperty("folderNaming").GetString());
        Assert.True(entries.ContainsKey($"documents/{folder}/{doc.Id:D}/pano.png"));
    }

    [Fact]
    public async Task The_practice_default_decides_the_naming_when_the_export_does_not_ask_and_a_misspelt_choice_is_refused()
    {
        await AddDocumentAsync(Ann, "pano.png");

        var refused = await Assert.ThrowsAsync<PatientException>(() => ExportAsync(Ann, new PackageChoices(FolderNaming: "names")));

        Assert.Equal(("invalid_folder_naming", 400), (refused.Code, refused.StatusCode));
        Assert.Equal(0, await WithDb(db => db.DataExportHistory.CountAsync()));                                      // refused before anything is recorded
        await using var file = await ExportAsync(Ann);
        Assert.Equal("IdOnly", Manifest(Entries(file.Content)).GetProperty("folderNaming").GetString());
    }

    [Fact]
    public async Task A_file_name_a_user_typed_cannot_leave_its_folder_and_two_files_with_one_name_both_survive()
    {
        var evil = await AddDocumentAsync(Ann, "..\\..\\..\\Windows\\evil.exe");
        var twinA = await AddDocumentAsync(Ann, "scan.png");
        var twinB = await AddDocumentAsync(Ann, "scan.png", 900);

        await using var file = await ExportAsync(Ann);
        var entries = Entries(file.Content);

        Assert.True(entries.ContainsKey($"documents/{Ann:D}/{evil.Id:D}/evil.exe"));
        Assert.DoesNotContain(entries.Keys, k => k.Contains(".."));
        Assert.All(entries.Keys, k => Assert.False(Path.IsPathRooted(k)));
        Assert.Equal(twinA.Content, entries[$"documents/{Ann:D}/{twinA.Id:D}/scan.png"]);
        Assert.Equal(twinB.Content, entries[$"documents/{Ann:D}/{twinB.Id:D}/scan.png"]);
    }

    [Fact]
    public async Task Document_files_over_the_package_limit_are_refused_the_row_is_failed_and_nothing_is_left_on_disk()
    {
        await AddDocumentAsync(Ann, "big.png", 5000);

        var refused = await Assert.ThrowsAsync<PatientException>(() => ExportAsync(Ann, maxBytes: 1000));

        Assert.Equal(("package_too_large", 413), (refused.Code, refused.StatusCode));
        Assert.Equal(DataExportResult.Failed, (await WithDb(db => db.DataExportHistory.AsNoTracking().SingleAsync())).Result);
        Assert.Equal(1, await WithDb(db => db.AuditLogEntries.CountAsync(a => a.EventType == AuditEventTypes.DataExportFailed)));
        Assert.Empty(Directory.GetFiles(_temp));
        await using var withoutFiles = await ExportAsync(Ann, new PackageChoices(IncludeDocumentFiles: false), maxBytes: 1000000);     // asking for the manifest only is the way out
        Assert.Equal("Complete", withoutFiles.Result);
    }

    [Fact]
    public async Task No_temporary_file_is_left_after_a_package_with_documents_is_closed_or_when_a_file_is_missing()
    {
        var gone = await AddDocumentAsync(Ann, "gone.png");
        await AddDocumentAsync(Ann, "here.png");
        await _blobs.DeleteAsync(gone.Blob.Id);

        var file = await ExportAsync(Ann);
        await file.DisposeAsync();

        Assert.Empty(Directory.GetFiles(_temp));
    }
}
