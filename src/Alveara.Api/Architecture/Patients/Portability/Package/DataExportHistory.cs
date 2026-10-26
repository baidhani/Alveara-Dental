namespace Alveara.Api.Architecture.Patients.Portability.Package;

/// <summary>What was exported: a whole package (the ZIP with the manifest, data files, readable record and documents) or the readable record on its own.</summary>
public static class DataExportKind
{
    public const string Package = "Package";
    public const string ReadableRecord = "ReadableRecord";
    public static readonly IReadOnlyList<string> All = [Package, ReadableRecord];
}

/// <summary>Whose data: one patient, or every patient the limits allow.</summary>
public static class DataExportScope
{
    public const string OnePatient = "OnePatient";
    public const string AllPatients = "AllPatients";
    public static readonly IReadOnlyList<string> All = [OnePatient, AllPatients];
}

/// <summary>How an export ended. <see cref="Started"/> is the only state that changes: it becomes one of the other three, once. A row still <see cref="Started"/> after its run is over is an interrupted export and is shown as such.</summary>
public static class DataExportResult
{
    public const string Started = "Started";
    /// <summary>Everything in scope was written and every check passed.</summary>
    public const string Complete = "Complete";
    /// <summary>A package was delivered but says in its manifest what is missing (a document file that could not be read, a domain that could not be exported).</summary>
    public const string Partial = "Partial";
    /// <summary>Nothing was delivered.</summary>
    public const string Failed = "Failed";
    public static readonly IReadOnlyList<string> All = [Started, Complete, Partial, Failed];
}

/// <summary>
/// ALV-018-C01: one row for every export of patient data: who asked, when, what (package or readable record), for whom (one patient or all), how it ended and how big the file was. It is the practice's record of what left
/// the system, so it never holds exported content: no names, no contact details, no values from a record, only ids, counts, sizes and a hash of the delivered file. A row is written as <c>Started</c> BEFORE any data is read,
/// so an export that is interrupted still leaves a trace, and is finished once. The database refuses to delete a row, to change what it recorded, or to change a finished row (errors 51250 to 51253).
/// </summary>
public class DataExportHistory
{
    public Guid Id { get; set; }
    /// <summary>The export's own id: the package manifest carries it, so a file found later can be traced to this row.</summary>
    public Guid ExportId { get; set; }
    public required string Kind { get; set; }
    public required string Scope { get; set; }
    /// <summary>The patient's id for a one-patient export, null for all patients. It is deliberately not named <c>PatientId</c> and has no foreign key: this is a record of what happened, which a merge must not rewrite (it names the record that was asked for).</summary>
    public Guid? SubjectRecordId { get; set; }
    public bool IncludesDocumentFiles { get; set; }
    public Guid RequestedByUserId { get; set; }
    public DateTimeOffset RequestedAtUtc { get; set; }
    public required string Result { get; set; }
    public DateTimeOffset? FinishedAtUtc { get; set; }
    public int PatientCount { get; set; }
    public int FileCount { get; set; }
    public long SizeBytes { get; set; }
    /// <summary>SHA-256 of the delivered file in lower-case hex, so a file shown later can be matched to this export; null until it ends with a file.</summary>
    public string? Sha256 { get; set; }
    /// <summary>How many things the package manifest lists as missing or unavailable (0 for a complete export).</summary>
    public int ProblemCount { get; set; }
}
