using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Patients.Portability.Package;

/// <summary>Who and what a package is for: the patients in scope as a SQL fragment built from GUIDs and constants (see <see cref="ExportTableFilters"/>), how many they are, and the single patient for a one-patient export.</summary>
public sealed record PackageScope(string Kind, string PatientIdsSql, int PatientCount, Guid? SubjectRecordId, PackageChoices? Choices = null);

/// <summary>What the person exporting asked for: whether the document files travel in the package (default yes; false lists the documents in the manifest only) and how patient folders are named (null means the practice default from <see cref="DataExportOptions"/>).</summary>
public sealed record PackageChoices(bool IncludeDocumentFiles = true, string? FolderNaming = null)
{
    public static PackageChoices Default { get; } = new();
}

/// <summary>A finished package: the ZIP, open at its start in a temporary file that is deleted when the stream is closed, and what the manifest says about it.</summary>
public sealed class BuiltPackage(Stream content, string result, int fileCount, int problemCount, string sha256) : IAsyncDisposable
{
    public Stream Content { get; } = content;
    public string Result { get; } = result;
    public int FileCount { get; } = fileCount;
    public int ProblemCount { get; } = problemCount;
    public string Sha256 { get; } = sha256;
    public long SizeBytes => Content.Length;
    public ValueTask DisposeAsync() => Content.DisposeAsync();
}

/// <summary>
/// ALV-018-C01: assembles the export package for the patients in a <see cref="PackageScope"/>. It reads each domain of <see cref="ExportDomainCatalog"/> (every table by the rule in <see cref="ExportTableFilters"/>) into its
/// own temporary file first, so a domain that fails half way is dropped whole and never leaves a broken data file in the package; a failed domain is listed in the manifest as Unavailable with the error class and the package is
/// Partial, not Complete. The manifest is written last and carries a SHA-256 of every other file. Everything is assembled in temporary files that are deleted when closed, so patient data is never left on disk, and a failure
/// or cancellation deletes them and rethrows. Failure modes handled: a domain whose tables cannot be reached by any rule or whose read fails (Unavailable), a cancelled request, a full disk. Not handled: a change made to
/// the data by someone else while the export reads (each table is read at one moment; the manifest time says when the package was started). The patients' document files are added by
/// <see cref="PackageDocumentWriter"/> (each checked against its recorded hash; a missing or changed file makes the package Partial) unless the export asked for the manifest only; the size of all of them is checked
/// against the package limit BEFORE anything is assembled, so an over-limit export is refused rather than cut short.
/// </summary>
public sealed class PatientDataPackageBuilder(AlveraDbContext db, IPracticeClock clock, IOptions<DataExportOptions> options, ReadableRecordLoader readable, PackageDocumentWriter documents, ILogger<PatientDataPackageBuilder> log)
{
    private readonly DataExportOptions _options = options.Value;

    public async Task<BuiltPackage> BuildAsync(Guid exportId, Guid actor, PackageScope scope, CancellationToken ct)
    {
        var choices = scope.Choices ?? PackageChoices.Default;
        var naming = FolderNaming.Require(choices.FolderNaming ?? _options.DefaultFolderNaming);
        Directory.CreateDirectory(_options.ResolveTempDirectory());
        var plan = await documents.PlanAsync(scope.PatientIdsSql, naming, ct);
        if (choices.IncludeDocumentFiles) documents.EnsureFits(plan);                                      // refused before any file is made
        var zipStream = _options.OpenTempFile(exportId, "package");
        try
        {
            var filters = new ExportTableFilters(db.Model, scope.PatientIdsSql);
            var connection = db.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync(ct);

            var files = new List<ManifestFile>();
            var problems = new List<ManifestProblem>();
            var domains = new List<ManifestDomain>();
            using (var zip = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                var generatedAt = clock.UtcNow.UtcDateTime.ToString(PatientExportFormat.TimestampFormat);
                files.Add(await AddBytesAsync(zip, DataPackageFormat.ReadmePath, Encoding.UTF8.GetBytes(Readme(exportId, naming, choices.IncludeDocumentFiles)), ct));
                string? readablePath = null;
                if (scope.SubjectRecordId is { } subject)
                {
                    var page = await RenderReadableRecordAsync(subject, exportId, choices.IncludeDocumentFiles, ct);
                    if (page is null) problems.Add(new("readable_record_unavailable", "readable-record", "The readable record could not be made, so it is not in this package."));
                    else { files.Add(await AddBytesAsync(zip, DataPackageFormat.ReadableRecordPath, page, ct)); readablePath = DataPackageFormat.ReadableRecordPath; }
                }
                foreach (var domain in ExportDomainCatalog.Domains)
                {
                    ct.ThrowIfCancellationRequested();
                    var (entry, file) = await ReadDomainAsync(domain, filters, connection, exportId, ct);
                    domains.Add(entry);
                    if (file is null)
                    {
                        problems.Add(new("domain_unavailable", domain.Key, $"'{domain.Title}' could not be read, so it is not in this package."));
                        continue;
                    }
                    await using (file)
                    {
                        var zipEntry = zip.CreateEntry(DataPackageFormat.DataPath(domain.Key), CompressionLevel.Optimal);
                        await using (var target = zipEntry.Open()) await file.CopyToAsync(target, ct);
                    }
                    files.Add(new(DataPackageFormat.DataPath(domain.Key), entry.Bytes!.Value, entry.Sha256!));
                }

                var listedDocuments = await documents.WriteAsync(zip, plan, choices.IncludeDocumentFiles, exportId, files, problems, ct);
                var result = problems.Count == 0 ? DataExportResult.Complete : DataExportResult.Partial;
                var manifest = new DataPackageManifest(
                    new(DataPackageFormat.FormatName, DataPackageFormat.CurrentVersion), await SchemaVersionAsync(ct), AppVersion(), exportId,
                    generatedAt, actor, new(scope.Kind, scope.PatientCount, scope.SubjectRecordId), result,
                    domains, ExportDomainCatalog.Excluded.Select(g => new ManifestExcluded(g.Key, g.Reason, g.Tables)).ToList(), files, readablePath, problems, Fhir: null,
                    naming, choices.IncludeDocumentFiles, plan.Folders, listedDocuments);
                await AddBytesAsync(zip, DataPackageFormat.ManifestPath, JsonSerializer.SerializeToUtf8Bytes(manifest, DataPackageFormat.JsonOptions), ct);
                log.LogInformation("{Event} export={ExportId} outcome={Outcome} domains={Domains} problems={Problems}", "data_package_built", exportId, result, domains.Count, problems.Count);
            }

            zipStream.Position = 0;
            var sha = Convert.ToHexStringLower(await SHA256.HashDataAsync(zipStream, ct));
            zipStream.Position = 0;
            var finalResult = problems.Count == 0 ? DataExportResult.Complete : DataExportResult.Partial;
            return new BuiltPackage(zipStream, finalResult, files.Count + 1, problems.Count, sha);
        }
        catch
        {
            await zipStream.DisposeAsync();                                                             // deletes the temporary file; the caller records the failure
            throw;
        }
    }

    /// <summary>Reads every table of one domain into a temporary file. On any failure but cancellation the domain is reported Unavailable and the partly written file is deleted.</summary>
    private async Task<(ManifestDomain Entry, FileStream? File)> ReadDomainAsync(ExportDomain domain, ExportTableFilters filters, System.Data.Common.DbConnection connection, Guid exportId, CancellationToken ct)
    {
        var file = _options.OpenTempFile(exportId, domain.Key);
        try
        {
            var tables = new List<ManifestTable>();
            var lookups = new List<ManifestTable>();
            await using (var json = new Utf8JsonWriter(file, new JsonWriterOptions { Indented = false }))
            {
                json.WriteStartObject();
                json.WriteString("domain", domain.Key);
                json.WriteStartObject("tables");
                foreach (var table in domain.Tables) tables.Add(await WriteOneAsync(json, connection, filters, table, ct));
                json.WriteEndObject();
                json.WriteStartObject("lookupTables");
                foreach (var table in domain.LookupTables) lookups.Add(await WriteOneAsync(json, connection, filters, table, ct));
                json.WriteEndObject();
                json.WriteEndObject();
            }

            file.Position = 0;
            var sha = Convert.ToHexStringLower(await SHA256.HashDataAsync(file, ct));
            file.Position = 0;
            return (new ManifestDomain(domain.Key, domain.Title, DomainStatus.Included, DataPackageFormat.DataPath(domain.Key), file.Length, sha, tables, lookups), file);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await file.DisposeAsync();
            log.LogError("{Event} domain={Domain} error_class={ErrorClass}", "data_package_domain_failed", domain.Key, ex.GetType().Name);
            return (new ManifestDomain(domain.Key, domain.Title, DomainStatus.Unavailable, null, null, null, [], []), null);
        }
        catch
        {
            await file.DisposeAsync();
            throw;
        }
    }

    private async Task<ManifestTable> WriteOneAsync(Utf8JsonWriter json, System.Data.Common.DbConnection connection, ExportTableFilters filters, string table, CancellationToken ct)
    {
        var where = filters.WhereFor(table) ?? throw new InvalidOperationException($"No export rule reaches the table {table}.");
        json.WritePropertyName(table);
        var rows = await ExportRowWriter.WriteTableAsync(connection, json, table, where, filters.OrderBy(table), _options.ReadTimeoutSeconds, ct);
        await json.FlushAsync(ct);
        return new ManifestTable(table, rows);
    }

    /// <summary>The readable record of one patient as the bytes of the page, or null when it could not be made (logged with its error class, never silently).</summary>
    private async Task<byte[]?> RenderReadableRecordAsync(Guid patientId, Guid exportId, bool documentFilesIncluded, CancellationToken ct)
    {
        try
        {
            var record = await readable.LoadAsync(patientId, ct);
            return Encoding.UTF8.GetBytes(ReadableRecordHtml.Render(record, new ReadableRecordContext(exportId, PracticeTimeText.Moment(clock, clock.UtcNow), clock.PracticeTimeZone.Id, InsidePackage: true, DocumentFilesIncluded: documentFilesIncluded)));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogError("{Event} export={ExportId} error_class={ErrorClass}", "readable_record_failed", exportId, ex.GetType().Name);
            return null;
        }
    }

    private static async Task<ManifestFile> AddBytesAsync(ZipArchive zip, string path, byte[] content, CancellationToken ct)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        await using (var target = entry.Open()) await target.WriteAsync(content, ct);
        return new ManifestFile(path, content.Length, Convert.ToHexStringLower(SHA256.HashData(content)));
    }

    private async Task<string> SchemaVersionAsync(CancellationToken ct) => (await db.Database.GetAppliedMigrationsAsync(ct)).LastOrDefault() ?? "unknown";

    private static string AppVersion() => (Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown").Split('+')[0];

    private static string Readme(Guid exportId, string folderNaming, bool documentFilesIncluded) => $"""
        ALVEARA DENTAL - PATIENT DATA PACKAGE
        Export id: {exportId}

        This ZIP file is a copy of patient records taken out of Alveara Dental. It holds personal health
        information. Keep it where only people allowed to see these records can reach it, and delete it
        when it is no longer needed.

        WHAT IS IN IT
          manifest.json     What the package holds, what it leaves out and why, how many rows of each table,
                            a SHA-256 hash of every file, and anything that could not be included.
                            Read this first. If "result" is "Partial", the "problems" list says what is missing.
          data/<domain>.json  One file per part of the record (registration, appointments, billing, ...).
                            Each holds the rows of its tables exactly as the system stores them.
          readable-record.html  The patient's record as a person reads it: open it in a web browser, or print it.
                            It lists the documents; the data files are the complete machine-readable copy.
          documents/<patient folder>/<document id>/<file>
                            The patient's document files (scans, images, letters), when the export included them.
                            {(documentFilesIncluded ? "This package includes them." : "This package was made WITHOUT the document files: they are listed in manifest.json only.")}
                            Patient folders are named by {(folderNaming == FolderNaming.IdOnly ? "the patient's id" : "the patient's name and id")};
                            manifest.json gives the folder of every patient, so no one has to read it from the name.
          README.txt        This file.

        WHAT IS NOT IN IT
          The manifest lists every part of the system that is deliberately left out (sign-in accounts,
          security settings, the audit log, practice settings, technical bookkeeping) and the reason.

        CHECKING IT
          Each file's SHA-256 hash is in manifest.json. A file whose hash differs has been changed or damaged.
          For every document, manifest.json gives the hash recorded when it was imported and the hash found
          at export, and a status: Included, HashMismatch (the file differs from what was imported; it is
          included as found, do not rely on it), Missing, Unreadable or NotIncluded.

        This package is in Alveara's own layout (format alveara.patient-data-package, version {DataPackageFormat.CurrentVersion}).
        It cannot yet be imported back into Alveara. A standard-format (FHIR) copy is not included.
        """.Replace("\r\n", "\n");
}
