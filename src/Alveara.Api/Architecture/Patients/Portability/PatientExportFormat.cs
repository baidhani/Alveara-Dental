using System.Text.Json;
using System.Text.Json.Serialization;

namespace Alveara.Api.Architecture.Patients.Portability;

/// <summary>
/// STORY-018: the file one patient's record is exported to, and the one an import reads. The layout is documented in <c>docs/PATIENT_PORTABILITY.md</c>; this is its single definition in code so the export
/// and the import cannot drift apart. The names are camelCase JSON, nulls are written (a reader can tell "no value" from "missing"), and the document carries its own format name and version so any
/// reader can tell what it is before it trusts anything else in it.
/// </summary>
public static class PatientExportFormat
{
    /// <summary>The name written in <c>format</c>; an import accepts nothing else.</summary>
    public const string FormatName = "alveara.patient-export";

    /// <summary>The only layout version this system writes and reads. A reader that meets another version refuses the file instead of guessing.</summary>
    public const int CurrentVersion = 1;

    /// <summary>The system name written in <c>source.system</c> (informational).</summary>
    public const string SystemName = "Alveara Dental";

    /// <summary>The largest file an import reads (a single patient is a few hundred bytes; this bounds what is parsed).</summary>
    public const int MaxFileBytes = 256 * 1024;

    /// <summary>The time form written in <c>exportedAtUtc</c>: always UTC, to the second.</summary>
    public const string TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";

    public static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };
}

/// <summary>The whole export file.</summary>
public sealed record PatientExportDocument(string Format, int FormatVersion, Guid ExportId, string ExportedAtUtc, PatientExportSource Source, PatientExportPatient Patient);

/// <summary>Where the record came from. <see cref="SourcePatientId"/> is informational: an import never uses it as an id.</summary>
public sealed record PatientExportSource(string System, Guid SourcePatientId);

/// <summary>
/// The patient's identity and contact fields, exactly the ones registration holds and with its names and text forms (<see cref="PatientFields"/>): the date is <c>yyyy-MM-dd</c>, optional fields may be null.
/// Household and guarantor links, clinical and financial data, files and user account data are deliberately not here.
/// </summary>
public sealed record PatientExportPatient(
    string FirstName, string? MiddleName, string LastName, string DateOfBirth, string? Sex,
    string Phone, string? Email, string AddressLine1, string? AddressLine2, string City, string State, string PostalCode);

/// <summary>What an export hands back to the caller: the bytes of the file and the name to save it under.</summary>
public sealed record PatientExportFile(Guid ExportId, string FileName, byte[] Content);
