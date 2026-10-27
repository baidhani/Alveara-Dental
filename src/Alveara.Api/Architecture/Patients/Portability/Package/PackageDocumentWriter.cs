using System.IO.Compression;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Alveara.Api.Architecture.Storage;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Patients.Portability.Package;

/// <summary>A document as the package needs it: where its file is stored and what was recorded about it at import. Read straight from the table; the column names are the property names.</summary>
public sealed class PackageDocumentRow
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    public string OriginalFileName { get; set; } = "";
    public Guid BlobId { get; set; }
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = "";
}

/// <summary>The patient's name columns, used only to name a folder.</summary>
public sealed class PackagePatientRow
{
    public Guid Id { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
}

/// <summary>What a package is going to hold of documents: a folder for every patient in scope and every document of those patients.</summary>
public sealed record PackageDocumentPlan(IReadOnlyList<ManifestPatientFolder> Folders, IReadOnlyList<PackageDocumentRow> Documents)
{
    public long RecordedBytes => Documents.Sum(d => d.SizeBytes);
}

/// <summary>
/// ALV-018-C01: puts the patients' document files into a package, checking each against the hash recorded when it was imported. Every document is listed in the manifest whatever happens to its file (with its recorded
/// hash and size, the hash found at export and a status), so a reader can tell exactly what is and is not there. Each file is read once, into a temporary file, hashing as it goes, and only then copied into the ZIP, so
/// a file that fails half way never leaves a broken entry. A file whose hash differs from the recorded one is still included as found and marked HashMismatch (the reader is told, rather than the export silently
/// refusing or silently passing); a file missing from storage is marked Missing and has no entry; a file that cannot be read just now is marked Unreadable. Each of those is a package problem, so the package is Partial.
/// A "manifest only" export lists the documents (NotIncluded) and reads no file. The size of all files counts towards <see cref="DataExportOptions.MaxPackageBytes"/>: over the limit, the export is refused
/// (413 <c>package_too_large</c>), never cut short. Failure modes handled: a missing or unreadable file, a changed file, an over-limit package, cancellation; a failing write to the temporary folder (a full disk)
/// is NOT blamed on the document and ends the export. Not handled: a file changed in storage between the check and the copy (the hash is of the bytes copied, so the manifest stays truthful about them).
/// </summary>
public sealed class PackageDocumentWriter(AlveraDbContext db, IBlobStorage blobs, IOptions<DataExportOptions> options, ILogger<PackageDocumentWriter> log)
{
    private readonly DataExportOptions _options = options.Value;

    /// <summary>The patients in scope with their folder names, and their documents. <paramref name="patientIdsSql"/> is built by the caller from GUIDs and constants, never from user text (see <see cref="ExportTableFilters"/>).</summary>
    public async Task<PackageDocumentPlan> PlanAsync(string patientIdsSql, string folderNaming, CancellationToken ct)
    {
        FolderNaming.Require(folderNaming);
        var previous = db.Database.GetCommandTimeout();
        db.Database.SetCommandTimeout(_options.ReadTimeoutSeconds);                                      // an explicit limit on these reads
        try
        {
#pragma warning disable EF1002 // patientIdsSql is made of GUID literals and model names only, see ExportTableFilters
            var patients = await db.Database.SqlQueryRaw<PackagePatientRow>($"SELECT [Id], [FirstName], [LastName] FROM [Patients] WHERE [Id] IN ({patientIdsSql}) ORDER BY [Id]").ToListAsync(ct);
            var documents = await db.Database.SqlQueryRaw<PackageDocumentRow>($"SELECT [Id], [PatientId], [OriginalFileName], [BlobId], [SizeBytes], [Sha256] FROM [Documents] WHERE [PatientId] IN ({patientIdsSql}) ORDER BY [PatientId], [Id]").ToListAsync(ct);
#pragma warning restore EF1002
            var folders = patients.Select(p => new ManifestPatientFolder(p.Id, PackagePaths.PatientFolder(p.Id, p.FirstName, p.LastName, folderNaming))).ToList();
            return new PackageDocumentPlan(folders, documents);
        }
        finally
        {
            db.Database.SetCommandTimeout(previous);
        }
    }

    /// <summary>Refuses a plan whose files together are over the package limit.</summary>
    public void EnsureFits(PackageDocumentPlan plan)
    {
        if (plan.RecordedBytes > _options.MaxPackageBytes) throw TooLarge(plan.RecordedBytes);
    }

    /// <summary>Adds the files (unless <paramref name="includeFiles"/> is false) and returns every document's manifest entry; the files and problems found are added to the two lists.</summary>
    public async Task<IReadOnlyList<ManifestDocument>> WriteAsync(ZipArchive zip, PackageDocumentPlan plan, bool includeFiles, Guid exportId, List<ManifestFile> files, List<ManifestProblem> problems, CancellationToken ct)
    {
        var folders = plan.Folders.ToDictionary(f => f.PatientId, f => f.Folder);
        var listed = new List<ManifestDocument>(plan.Documents.Count);
        long written = 0;
        foreach (var document in plan.Documents)
        {
            ct.ThrowIfCancellationRequested();
            if (!includeFiles)
            {
                listed.Add(Entry(document, DocumentFileStatus.NotIncluded, null, null));
                continue;
            }
            var path = DataPackageFormat.DocumentPath(folders[document.PatientId], document.Id, PackagePaths.DocumentFileName(document.OriginalFileName));
            var (status, actual, bytes) = await CopyAsync(zip, document, path, written, exportId, ct);
            var inPackage = status is DocumentFileStatus.Included or DocumentFileStatus.HashMismatch;
            if (inPackage)
            {
                files.Add(new ManifestFile(path, bytes, actual!));
                written += bytes;
            }
            if (status != DocumentFileStatus.Included)
            {
                problems.Add(new ManifestProblem(ProblemCode(status), document.Id.ToString("D"), ProblemMessage(status)));
                log.LogWarning("{Event} export={ExportId} document={DocumentId} status={Status}", "data_package_document_problem", exportId, document.Id, status);
            }
            listed.Add(Entry(document, status, inPackage ? path : null, actual));
        }
        return listed;
    }

    private async Task<(string Status, string? Sha256, long Bytes)> CopyAsync(ZipArchive zip, PackageDocumentRow document, string path, long writtenSoFar, Guid exportId, CancellationToken ct)
    {
        Stream source;
        try
        {
            source = await blobs.OpenReadAsync(document.BlobId, ct);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return (DocumentFileStatus.Missing, null, 0);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.LogError("{Event} document={DocumentId} error_class={ErrorClass}", "data_package_document_unreadable", document.Id, ex.GetType().Name);
            return (DocumentFileStatus.Unreadable, null, 0);
        }

        await using var staged = _options.OpenTempFile(exportId, "document");                          // deleted when closed, on every path out of here
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using (source)
        {
            var buffer = new byte[81920];
            while (true)
            {
                int read;
                try
                {
                    read = await source.ReadAsync(buffer, ct);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    log.LogError("{Event} document={DocumentId} error_class={ErrorClass}", "data_package_document_unreadable", document.Id, ex.GetType().Name);
                    return (DocumentFileStatus.Unreadable, null, 0);
                }
                if (read == 0) break;
                hash.AppendData(buffer, 0, read);
                await staged.WriteAsync(buffer.AsMemory(0, read), ct);                                  // a failing write is the server's problem, not the document's: it ends the export
            }
        }

        if (writtenSoFar + staged.Length > _options.MaxPackageBytes) throw TooLarge(writtenSoFar + staged.Length);
        var actual = Convert.ToHexStringLower(hash.GetHashAndReset());
        staged.Position = 0;
        var entry = zip.CreateEntry(path, CompressionLevel.NoCompression);                              // scans and images are already compressed
        await using (var target = entry.Open()) await staged.CopyToAsync(target, ct);
        var matches = staged.Length == document.SizeBytes && string.Equals(actual, document.Sha256, StringComparison.OrdinalIgnoreCase);
        return (matches ? DocumentFileStatus.Included : DocumentFileStatus.HashMismatch, actual, staged.Length);
    }

    private static ManifestDocument Entry(PackageDocumentRow d, string status, string? path, string? actual) =>
        new(d.Id, d.PatientId, status, d.OriginalFileName, d.SizeBytes, d.Sha256, path, actual);

    private PatientException TooLarge(long bytes) =>
        new("package_too_large", $"The document files come to {bytes} bytes, over the limit of {_options.MaxPackageBytes} bytes for one package. Export without the document files instead.", 413);

    private static string ProblemCode(string status) => status switch
    {
        DocumentFileStatus.HashMismatch => "document_hash_mismatch",
        DocumentFileStatus.Missing => "document_missing",
        _ => "document_unreadable",
    };

    private static string ProblemMessage(string status) => status switch
    {
        DocumentFileStatus.HashMismatch => "A document file does not match the hash recorded when it was imported. It is included as found; do not rely on it.",
        DocumentFileStatus.Missing => "A document file is missing from storage, so it is not in this package.",
        _ => "A document file could not be read just now, so it is not in this package.",
    };
}
