using System.Text.Json;
using System.Text.Json.Serialization;

namespace Alveara.Api.Architecture.Patients.Portability.Package;

/// <summary>
/// ALV-018-C01: the layout of the export package, defined once so the writer, the readers of the manifest and the documentation cannot drift apart. The package is one ZIP:
/// <c>README.txt</c> (what it is and how to read it), <c>manifest.json</c> (everything below, written last), <c>data/&lt;domain&gt;.json</c> (one file per domain of <see cref="ExportDomainCatalog"/>), the
/// readable record and <c>documents/</c> (the patients' document files, when asked for). An optional <c>fhir/</c> folder is reserved for a future standard-format export and is never written today. The names are camelCase JSON, nulls are written, and the manifest
/// carries its own format name and version so a reader can tell what it is before it trusts anything else.
/// </summary>
public static class DataPackageFormat
{
    public const string FormatName = "alveara.patient-data-package";
    public const int CurrentVersion = 1;
    public const string ManifestPath = "manifest.json";
    public const string ReadmePath = "README.txt";
    public const string ReadableRecordPath = "readable-record.html";
    public const string DataFolder = "data/";
    public const string DocumentsFolder = "documents/";
    public const string ReadableRecordsFolder = "readable-records/";
    public const string ReservedFhirFolder = "fhir/";

    public static string DataPath(string domainKey) => $"{DataFolder}{domainKey}.json";

    /// <summary>An all-patients package holds one readable record per patient: <c>readable-records/&lt;patient folder&gt;.html</c>.</summary>
    public static string ReadableRecordPathFor(string patientFolder) => $"{ReadableRecordsFolder}{patientFolder}.html";

    /// <summary><c>documents/&lt;patient folder&gt;/&lt;document id&gt;/&lt;file name&gt;</c>: grouped by patient, unique by document id, recognisable by the original file name (cleaned by <see cref="PackagePaths.DocumentFileName"/>).</summary>
    public static string DocumentPath(string patientFolder, Guid documentId, string fileName) => $"{DocumentsFolder}{patientFolder}/{documentId:D}/{fileName}";

    public static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };
}

/// <summary>How a domain ended up in a package: its rows are in the file, or it could not be read (the manifest says why and the package is Partial).</summary>
public static class DomainStatus
{
    public const string Included = "Included";
    public const string Unavailable = "Unavailable";
}

/// <summary>How the tables of a package were read: all inside one database snapshot (one consistent moment), or one table after another (a change made while the export ran may show in some tables and not others; the manifest time says when it started).</summary>
public static class ReadMode
{
    public const string Snapshot = "Snapshot";
    public const string TableByTable = "TableByTable";
}

/// <summary>Where a patient's readable record is in the package.</summary>
public sealed record ManifestReadableRecord(Guid PatientId, string Path);

/// <summary>What became of one document's file: in the package and matching its recorded hash, in the package but not matching (included as found), missing from storage, unreadable just now, or left out because the export was asked for the manifest only.</summary>
public static class DocumentFileStatus
{
    public const string Included = "Included";
    public const string HashMismatch = "HashMismatch";
    public const string Missing = "Missing";
    public const string Unreadable = "Unreadable";
    public const string NotIncluded = "NotIncluded";
}

/// <summary>Which folder holds a patient's files in this package, so a reader never has to parse a folder name.</summary>
public sealed record ManifestPatientFolder(Guid PatientId, string Folder);

/// <summary>One document: the hash and size recorded when it was imported, the hash found in storage at export (null when the file was not read), its status and where it is in the package (null when it is not).</summary>
public sealed record ManifestDocument(Guid DocumentId, Guid PatientId, string Status, string FileName, long SizeBytes, string Sha256, string? Path, string? ActualSha256);

public sealed record ManifestTable(string Name, int Rows);

public sealed record ManifestDomain(string Key, string Title, string Status, string? File, long? Bytes, string? Sha256, IReadOnlyList<ManifestTable> Tables, IReadOnlyList<ManifestTable> LookupTables);

public sealed record ManifestExcluded(string Key, string Reason, IReadOnlyList<string> Tables);

public sealed record ManifestFile(string Path, long Bytes, string Sha256);

/// <summary>Something the package could not do: a stable <see cref="Code"/>, the part it concerns and a plain message. Never a value from a record.</summary>
public sealed record ManifestProblem(string Code, string Where, string Message);

public sealed record ManifestScope(string Kind, int PatientCount, Guid? SubjectRecordId);

public sealed record ManifestPackage(string Name, int Version);

/// <summary>The manifest: what the package contains, what it deliberately leaves out, what it could not include, and a hash of every file in it (the manifest itself excepted).</summary>
public sealed record DataPackageManifest(
    ManifestPackage Package,
    string SchemaVersion,
    string AppVersion,
    Guid ExportId,
    string GeneratedAtUtc,
    Guid GeneratedByUserId,
    ManifestScope Scope,
    string Result,
    IReadOnlyList<ManifestDomain> Domains,
    IReadOnlyList<ManifestExcluded> Excluded,
    IReadOnlyList<ManifestFile> Files,
    string? ReadableRecord,
    IReadOnlyList<ManifestProblem> Problems,
    string? Fhir,
    string FolderNaming,
    bool DocumentFilesIncluded,
    IReadOnlyList<ManifestPatientFolder> PatientFolders,
    IReadOnlyList<ManifestDocument> Documents,
    string ReadConsistency,
    IReadOnlyList<ManifestReadableRecord> ReadableRecords);
