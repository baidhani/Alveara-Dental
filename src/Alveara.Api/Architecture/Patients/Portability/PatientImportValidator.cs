using System.Globalization;
using System.Text.Json;
using Alveara.Api.Architecture.Time;

namespace Alveara.Api.Architecture.Patients.Portability;

/// <summary>One thing wrong with an import file: a stable <see cref="Code"/>, where in the file (<see cref="Path"/>, such as <c>patient.dateOfBirth</c>) and a plain message the person can act on.</summary>
public sealed record PatientImportProblem(string Code, string Path, string Message);

/// <summary>The identity and fields of a file that passed every check; only a valid file produces one.</summary>
public sealed record ParsedPatientImport(Guid ExportId, PatientFields Fields);

/// <summary>What checking a file found. <see cref="ExportId"/> and <see cref="FormatVersion"/> are filled in when the file got far enough to say them, so a rejection can still be traced in the log.</summary>
public sealed record PatientImportCheck(IReadOnlyList<PatientImportProblem> Problems, ParsedPatientImport? Parsed, Guid? ExportId, int? FormatVersion);

/// <summary>
/// STORY-018: decides, before anything is stored, whether a file is a patient export this system reads. The whole file is checked and every problem is reported at once so the person can fix the file and send
/// it again: the size and that it is UTF-8 JSON, the format name and version, the export id and time, the exact set of members at each level (none missing, none unknown, none repeated, each of the right
/// type) and every patient field under the SAME rules registration uses (<see cref="PatientInput"/>, including the practice's own required fields). Pure and deterministic: no database, no clock beyond the
/// one given (for the date-of-birth rule). The failure modes it handles: an empty, oversized, non-JSON, wrong-format, wrong-version, incomplete, over-complete or invalid file; it cannot know whether the patient
/// already exists (the import service asks that).
/// </summary>
public static class PatientImportValidator
{
    private static readonly string[] TopMembers = ["format", "formatVersion", "exportId", "exportedAtUtc", "source", "patient"];
    private static readonly string[] SourceMembers = ["system", "sourcePatientId"];
    private static readonly string[] PatientMembers =
        ["firstName", "middleName", "lastName", "dateOfBirth", "sex", "phone", "email", "addressLine1", "addressLine2", "city", "state", "postalCode"];

    public static PatientImportCheck Validate(byte[] content, IPracticeClock clock, PatientRequirements requirements)
    {
        var problems = new List<PatientImportProblem>();
        PatientImportCheck Rejected(Guid? exportId = null, int? version = null) => new(problems, null, exportId, version);

        if (content.Length == 0)
        {
            problems.Add(new("file_empty", "$", "The file is empty. Choose a file exported from Alveara."));
            return Rejected();
        }
        if (content.Length > PatientExportFormat.MaxFileBytes)
        {
            problems.Add(new("file_too_large", "$", $"The file is larger than {PatientExportFormat.MaxFileBytes / 1024} KB, which is more than one patient export can be. Choose a file exported from Alveara."));
            return Rejected();
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(content, new JsonDocumentOptions { MaxDepth = 8, CommentHandling = JsonCommentHandling.Disallow, AllowTrailingCommas = false });
        }
        catch (JsonException)
        {
            problems.Add(new("not_json", "$", "The file is not valid UTF-8 JSON. Choose a file exported from Alveara and do not edit it in a program that changes its format."));
            return Rejected();
        }

        using (document)
        {
            var root = document.RootElement;
            var top = Members(root, "$", "The file", TopMembers, problems);
            if (top is null) return Rejected();

            if (top.TryGetValue("format", out var format) && !(format.ValueKind == JsonValueKind.String && format.GetString() == PatientExportFormat.FormatName))
                problems.Add(new("wrong_format", "format", $"This is not a patient export file: 'format' must be \"{PatientExportFormat.FormatName}\"."));

            int? version = null;
            if (top.TryGetValue("formatVersion", out var versionElement))
            {
                if (versionElement.ValueKind == JsonValueKind.Number && versionElement.TryGetInt32(out var v)) version = v;
                if (version != PatientExportFormat.CurrentVersion)
                    problems.Add(new("unsupported_version", "formatVersion", $"This system reads format version {PatientExportFormat.CurrentVersion} only{(version is null ? "" : $"; the file is version {version}")}."));
            }

            Guid? exportId = null;
            if (top.TryGetValue("exportId", out var exportIdElement))
            {
                if (exportIdElement.ValueKind == JsonValueKind.String && Guid.TryParse(exportIdElement.GetString(), out var g) && g != Guid.Empty) exportId = g;
                else problems.Add(new("export_id_invalid", "exportId", "'exportId' must be the GUID the export wrote."));
            }

            if (top.TryGetValue("exportedAtUtc", out var exportedAt))
            {
                if (!(exportedAt.ValueKind == JsonValueKind.String
                      && DateTimeOffset.TryParseExact(exportedAt.GetString(), PatientExportFormat.TimestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out _)))
                    problems.Add(new("exported_at_invalid", "exportedAtUtc", "'exportedAtUtc' must be a UTC time such as 2026-10-26T14:05:11Z."));
            }

            if (top.TryGetValue("source", out var source))
            {
                var sourceMembers = Members(source, "source", "'source'", SourceMembers, problems);
                if (sourceMembers is not null)
                {
                    if (sourceMembers.TryGetValue("system", out var system) && system.ValueKind != JsonValueKind.String)
                        problems.Add(new("wrong_type", "source.system", "'source.system' must be text."));
                    if (sourceMembers.TryGetValue("sourcePatientId", out var sourceId) && !(sourceId.ValueKind == JsonValueKind.String && Guid.TryParse(sourceId.GetString(), out _)))
                        problems.Add(new("source_patient_id_invalid", "source.sourcePatientId", "'source.sourcePatientId' must be a GUID."));
                }
            }

            PatientFields? fields = null;
            if (top.TryGetValue("patient", out var patient))
            {
                var before = problems.Count;
                var patientMembers = Members(patient, "patient", "'patient'", PatientMembers, problems);
                if (patientMembers is not null)
                {
                    string? Text(string name)
                    {
                        if (!patientMembers.TryGetValue(name, out var value) || value.ValueKind == JsonValueKind.Null) return null;
                        if (value.ValueKind == JsonValueKind.String) return value.GetString();
                        problems.Add(new("wrong_type", $"patient.{name}", $"'patient.{name}' must be text or null."));
                        return null;
                    }

                    var candidate = new PatientFields(Text("firstName"), Text("middleName"), Text("lastName"), Text("dateOfBirth"), Text("sex"), Text("phone"), Text("email"),
                        Text("addressLine1"), Text("addressLine2"), Text("city"), Text("state"), Text("postalCode"));
                    if (problems.Count == before) fields = candidate;                                     // the field rules run only once the shape of 'patient' is right
                }
            }

            if (fields is not null)
            {
                try
                {
                    PatientInput.Validate(fields, clock, requirements);
                }
                catch (PatientException ex)
                {
                    foreach (var (field, message) in ex.FieldErrors) problems.Add(new("field_invalid", $"patient.{field}", message));
                }
            }

            if (problems.Count > 0) return Rejected(exportId, version);
            return new PatientImportCheck(problems, new ParsedPatientImport(exportId!.Value, fields!), exportId, version);
        }
    }

    /// <summary>The members of a JSON object, checked against the exact set allowed: unknown, repeated and missing members are problems. Null when the element is not an object.</summary>
    private static Dictionary<string, JsonElement>? Members(JsonElement element, string path, string label, string[] allowed, List<PatientImportProblem> problems)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            problems.Add(new("wrong_type", path, $"{label} must be a JSON object."));
            return null;
        }

        string Join(string name) => path == "$" ? name : $"{path}.{name}";
        var map = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var member in element.EnumerateObject())
        {
            if (!allowed.Contains(member.Name, StringComparer.Ordinal)) problems.Add(new("unknown_member", Join(member.Name), $"'{member.Name}' is not part of this format."));
            else if (!map.TryAdd(member.Name, member.Value)) problems.Add(new("duplicate_member", Join(member.Name), $"'{member.Name}' appears more than once."));
        }

        foreach (var name in allowed)
            if (!map.ContainsKey(name)) problems.Add(new("missing_member", Join(name), $"'{name}' is missing."));
        return map;
    }
}
