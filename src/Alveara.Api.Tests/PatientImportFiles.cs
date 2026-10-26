using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Alveara.Api.Architecture.Patients.Portability;

namespace Alveara.Api.Tests;

/// <summary>STORY-018: builds import files for the tests of the validator, the import service and the import endpoint, and bends one member at a time so each test can say exactly what is wrong with its file.</summary>
internal static class PatientImportFiles
{
    /// <summary>A file the validator accepts, as a JSON tree a test can change.</summary>
    public static JsonObject Valid(Guid? exportId = null, string first = "Eli", string last = "Moss", string dateOfBirth = "1990-05-17")
    {
        var document = new PatientExportDocument(
            PatientExportFormat.FormatName, PatientExportFormat.CurrentVersion, exportId ?? Guid.NewGuid(), "2026-10-26T14:05:11Z",
            new PatientExportSource(PatientExportFormat.SystemName, Guid.NewGuid()),
            new PatientExportPatient(first, null, last, dateOfBirth, "Male", "(555) 010-3344", null, "9 Oak Road", null, "Austin", "TX", "78701"));
        return JsonNode.Parse(JsonSerializer.Serialize(document, PatientExportFormat.JsonOptions))!.AsObject();
    }

    public static byte[] Bytes(JsonObject file) => Encoding.UTF8.GetBytes(file.ToJsonString());

    public static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    /// <summary>A valid file after <paramref name="change"/> has been applied to its tree.</summary>
    public static byte[] Changed(Action<JsonObject> change, Guid? exportId = null)
    {
        var file = Valid(exportId);
        change(file);
        return Bytes(file);
    }

    /// <summary>An exported file (as bytes) with the identity of a different person and a new export id: what another practice would import.</summary>
    public static byte[] AsAnotherPerson(byte[] exported, string? first = null, string? last = null, string? dateOfBirth = null)
    {
        var file = JsonNode.Parse(exported)!.AsObject();
        file["exportId"] = Guid.NewGuid().ToString();
        var patient = file["patient"]!.AsObject();
        if (first is not null) patient["firstName"] = first;
        if (last is not null) patient["lastName"] = last;
        if (dateOfBirth is not null) patient["dateOfBirth"] = dateOfBirth;
        return Bytes(file);
    }
}
