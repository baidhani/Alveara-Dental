using System.Data.Common;
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

/// <summary>
/// Who and what a package is for. A one-patient scope carries the patient's id as a SQL fragment built from a GUID and constants (see <see cref="ExportTableFilters"/>); an all-patients scope carries no ids (the
/// builder collects everyone not merged away itself, see <see cref="ExportReadSession"/>). <paramref name="Checkpoint"/> is called before anything is read and between the parts of a long export; it throws to stop it.
/// </summary>
public sealed record PackageScope(string Kind, string PatientIdsSql, int PatientCount, Guid? SubjectRecordId, PackageChoices? Choices = null, Func<CancellationToken, Task>? Checkpoint = null);

/// <summary>What the person exporting asked for: whether the document files travel in the package (default yes; false lists the documents in the manifest only) and how patient folders are named (null means the practice default from <see cref="DataExportOptions"/>).</summary>
public sealed record PackageChoices(bool IncludeDocumentFiles = true, string? FolderNaming = null)
{
    public static PackageChoices Default { get; } = new();
}

/// <summary>A finished package: the ZIP, open at its start in a temporary file that is deleted when the stream is closed, and what the manifest says about it.</summary>
public sealed class BuiltPackage(Stream content, string result, int fileCount, int problemCount, string sha256, int patientCount) : IAsyncDisposable
{
    public Stream Content { get; } = content;
    public string Result { get; } = result;
    public int FileCount { get; } = fileCount;
    public int ProblemCount { get; } = problemCount;
    public string Sha256 { get; } = sha256;
    public int PatientCount { get; } = patientCount;
    public long SizeBytes => Content.Length;
    public ValueTask DisposeAsync() => Content.DisposeAsync();
}

/// <summary>
/// ALV-018-C01: assembles the export package for the patients in a <see cref="PackageScope"/>. It reads each domain of <see cref="ExportDomainCatalog"/> (every table by the rule in <see cref="ExportTableFilters"/>) into its
/// own temporary file first, so a domain that fails half way is dropped whole and never leaves a broken data file in the package; a failed domain is listed in the manifest as Unavailable with the error class and the package is
/// Partial, not Complete. The manifest is written last and carries a SHA-256 of every other file. Everything is assembled in temporary files that are deleted when closed, so patient data is never left on disk, and a failure
/// or cancellation deletes them and rethrows. The reading itself is held by an <see cref="ExportReadSession"/> (for all patients: one export at a time, the same patients in every table, one snapshot when the database
/// allows it; the manifest says <c>readConsistency</c>). The patients' document files are added by <see cref="PackageDocumentWriter"/> unless the export asked for the manifest only. The size of the document files is
/// checked against the package limit BEFORE anything is assembled and the size of the package again after every domain, so an over-limit export is refused (413), never cut short. Failure modes handled: a domain whose
/// tables cannot be reached or whose read fails (Unavailable), one patient's readable page that cannot be made (listed, the rest go on), a stopped checkpoint (permission withdrawn), a cancelled request, an over-limit
/// patient count or size, a second all-patients export, a full disk. Not handled: a change to the data made while the export reads when the database does not allow snapshots (the manifest says so).
/// </summary>
public sealed class PatientDataPackageBuilder(AlveraDbContext db, IPracticeClock clock, IOptions<DataExportOptions> options, ReadableRecordLoader readable, PackageDocumentWriter documents, ILogger<PatientDataPackageBuilder> log)
{
    private const int CheckpointEveryPatients = 250;
    private readonly DataExportOptions _options = options.Value;

    public async Task<BuiltPackage> BuildAsync(Guid exportId, Guid actor, PackageScope scope, CancellationToken ct)
    {
        var choices = scope.Choices ?? PackageChoices.Default;
        var naming = FolderNaming.Require(choices.FolderNaming ?? _options.DefaultFolderNaming);
        var allPatients = scope.Kind == DataExportScope.AllPatients;
        Directory.CreateDirectory(_options.ResolveTempDirectory());
        await using var session = await ExportReadSession.OpenAsync(db, scope, _options, log, ct);
        await CheckpointAsync(scope, ct);                                                                  // still allowed to do this, before anything is read
        var plan = await documents.PlanAsync(session.PatientIdsSql, naming, ct);
        if (choices.IncludeDocumentFiles) documents.EnsureFits(plan);                                      // refused before any file is made
        var zipStream = _options.OpenTempFile(exportId, "package");
        try
        {
            var filters = new ExportTableFilters(db.Model, session.PatientIdsSql);
            var files = new List<ManifestFile>();
            var problems = new List<ManifestProblem>();
            var domains = new List<ManifestDomain>();
            using (var zip = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                var generatedAt = clock.UtcNow.UtcDateTime.ToString(PatientExportFormat.TimestampFormat);
                files.Add(await AddBytesAsync(zip, DataPackageFormat.ReadmePath, Encoding.UTF8.GetBytes(PackageReadme.Text(exportId, naming, choices.IncludeDocumentFiles, allPatients, session.ReadConsistency)), ct));
                var pages = await AddReadableRecordsAsync(zip, scope, plan, exportId, choices.IncludeDocumentFiles, files, problems, ct);
                foreach (var domain in ExportDomainCatalog.Domains)
                {
                    ct.ThrowIfCancellationRequested();
                    var (entry, file) = await ReadDomainAsync(domain, filters, session, exportId, ct);
                    domains.Add(entry);
                    if (file is null)
                    {
                        problems.Add(new("domain_unavailable", domain.Key, $"'{domain.Title}' could not be read, so it is not in this package."));
                    }
                    else
                    {
                        await using (file)
                        {
                            var zipEntry = zip.CreateEntry(DataPackageFormat.DataPath(domain.Key), CompressionLevel.Optimal);
                            await using (var target = zipEntry.Open()) await file.CopyToAsync(target, ct);
                        }
                        files.Add(new(DataPackageFormat.DataPath(domain.Key), entry.Bytes!.Value, entry.Sha256!));
                    }
                    EnsureWithinLimit(zipStream);
                    await CheckpointAsync(scope, ct);                                                      // between the parts of a long export
                }

                var listedDocuments = await documents.WriteAsync(zip, plan, choices.IncludeDocumentFiles, exportId, files, problems, ct);
                var result = problems.Count == 0 ? DataExportResult.Complete : DataExportResult.Partial;
                var manifest = new DataPackageManifest(
                    new(DataPackageFormat.FormatName, DataPackageFormat.CurrentVersion), await SchemaVersionAsync(ct), AppVersion(), exportId,
                    generatedAt, actor, new(scope.Kind, session.PatientCount, scope.SubjectRecordId), result,
                    domains, ExportDomainCatalog.Excluded.Select(g => new ManifestExcluded(g.Key, g.Reason, g.Tables)).ToList(), files, pages.OnePatientPath, problems, Fhir: null,
                    naming, choices.IncludeDocumentFiles, plan.Folders, listedDocuments, session.ReadConsistency, pages.Listed);
                await AddBytesAsync(zip, DataPackageFormat.ManifestPath, JsonSerializer.SerializeToUtf8Bytes(manifest, DataPackageFormat.JsonOptions), ct);
                log.LogInformation("{Event} export={ExportId} outcome={Outcome} domains={Domains} problems={Problems} read={ReadMode}", "data_package_built", exportId, result, domains.Count, problems.Count, session.ReadConsistency);
            }

            zipStream.Position = 0;
            var sha = Convert.ToHexStringLower(await SHA256.HashDataAsync(zipStream, ct));
            zipStream.Position = 0;
            var finalResult = problems.Count == 0 ? DataExportResult.Complete : DataExportResult.Partial;
            return new BuiltPackage(zipStream, finalResult, files.Count + 1, problems.Count, sha, session.PatientCount);
        }
        catch
        {
            await zipStream.DisposeAsync();                                                             // deletes the temporary file; the caller records the failure
            throw;
        }
    }

    private static Task CheckpointAsync(PackageScope scope, CancellationToken ct) => scope.Checkpoint is { } check ? check(ct) : Task.CompletedTask;

    private void EnsureWithinLimit(Stream zip)
    {
        if (zip.Length > _options.MaxPackageBytes)
            throw new PatientException("package_too_large", $"The package grew past the limit of {_options.MaxPackageBytes} bytes. Nothing was exported.", 413);
    }

    /// <summary>One patient's readable page for a one-patient package (<c>readable-record.html</c>), or one for every patient of an all-patients package (<c>readable-records/&lt;folder&gt;.html</c>). A page that cannot be made is a problem of that patient, never of the whole export.</summary>
    private async Task<(string? OnePatientPath, IReadOnlyList<ManifestReadableRecord> Listed)> AddReadableRecordsAsync(
        ZipArchive zip, PackageScope scope, PackageDocumentPlan plan, Guid exportId, bool filesIncluded, List<ManifestFile> files, List<ManifestProblem> problems, CancellationToken ct)
    {
        var listed = new List<ManifestReadableRecord>();
        var madeAt = PracticeTimeText.Moment(clock, clock.UtcNow);
        var providers = await readable.ProviderNamesAsync(ct);                                             // read once, not once per patient
        var count = 0;
        foreach (var folder in plan.Folders)
        {
            if (++count % CheckpointEveryPatients == 0) await CheckpointAsync(scope, ct);
            ct.ThrowIfCancellationRequested();
            var path = scope.Kind == DataExportScope.AllPatients ? DataPackageFormat.ReadableRecordPathFor(folder.Folder) : DataPackageFormat.ReadableRecordPath;
            var page = await RenderReadableRecordAsync(folder.PatientId, exportId, madeAt, filesIncluded, providers, ct);
            if (page is null)
            {
                problems.Add(new("readable_record_unavailable", folder.PatientId.ToString("D"), "A patient's readable record could not be made, so it is not in this package."));
                continue;
            }
            files.Add(await AddBytesAsync(zip, path, page, ct));
            listed.Add(new ManifestReadableRecord(folder.PatientId, path));
        }
        return (scope.Kind == DataExportScope.AllPatients ? null : listed.FirstOrDefault()?.Path, listed);
    }

    /// <summary>Reads every table of one domain into a temporary file. On any failure but cancellation the domain is reported Unavailable and the partly written file is deleted.</summary>
    private async Task<(ManifestDomain Entry, FileStream? File)> ReadDomainAsync(ExportDomain domain, ExportTableFilters filters, ExportReadSession session, Guid exportId, CancellationToken ct)
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
                foreach (var table in domain.Tables) tables.Add(await WriteOneAsync(json, session.Connection, session.Transaction, filters, table, ct));
                json.WriteEndObject();
                json.WriteStartObject("lookupTables");
                foreach (var table in domain.LookupTables) lookups.Add(await WriteOneAsync(json, session.Connection, session.Transaction, filters, table, ct));
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

    private async Task<ManifestTable> WriteOneAsync(Utf8JsonWriter json, DbConnection connection, DbTransaction? transaction, ExportTableFilters filters, string table, CancellationToken ct)
    {
        var where = filters.WhereFor(table) ?? throw new InvalidOperationException($"No export rule reaches the table {table}.");
        json.WritePropertyName(table);
        var rows = await ExportRowWriter.WriteTableAsync(connection, json, table, where, filters.OrderBy(table), _options.ReadTimeoutSeconds, transaction, ct);
        await json.FlushAsync(ct);
        return new ManifestTable(table, rows);
    }

    /// <summary>The readable record of one patient as the bytes of the page, or null when it could not be made (logged with its error class, never silently).</summary>
    private async Task<byte[]?> RenderReadableRecordAsync(Guid patientId, Guid exportId, string madeAt, bool documentFilesIncluded, Dictionary<Guid, string> providers, CancellationToken ct)
    {
        try
        {
            var record = await readable.LoadAsync(patientId, ct, providers);
            return Encoding.UTF8.GetBytes(ReadableRecordHtml.Render(record, new ReadableRecordContext(exportId, madeAt, clock.PracticeTimeZone.Id, InsidePackage: true, DocumentFilesIncluded: documentFilesIncluded)));
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
}
