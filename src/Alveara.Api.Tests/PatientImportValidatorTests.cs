using System.Text.Json.Nodes;
using Xunit;
using Alveara.Api.Architecture.Patients;
using Alveara.Api.Architecture.Patients.Portability;

namespace Alveara.Api.Tests;

/// <summary>
/// STORY-018, the check of an import file before anything is stored (no database): a file in the documented layout passes and gives back the fields; every kind of incorrect file is rejected with the right
/// stable code at the right place in the file; several mistakes are all reported together so the person can fix them in one go; the practice's own required fields count; and nothing from the file is repeated
/// in a message.
/// </summary>
public class PatientImportValidatorTests
{
    private static PatientImportCheck Check(byte[] bytes, PatientRequirements? requirements = null) =>
        PatientImportValidator.Validate(bytes, SchedulingTestSupport.Clock, requirements ?? PatientRequirements.None);

    [Fact]
    public void A_file_in_the_documented_layout_passes_and_gives_back_the_export_id_and_the_fields()
    {
        var id = Guid.NewGuid();
        var check = Check(PatientImportFiles.Bytes(PatientImportFiles.Valid(id)));

        Assert.Empty(check.Problems);
        Assert.NotNull(check.Parsed);
        Assert.Equal(id, check.Parsed!.ExportId);
        Assert.Equal(id, check.ExportId);
        Assert.Equal(1, check.FormatVersion);
        Assert.Equal(("Eli", "Moss", "1990-05-17", "(555) 010-3344", "TX"), (check.Parsed.Fields.FirstName, check.Parsed.Fields.LastName, check.Parsed.Fields.DateOfBirth, check.Parsed.Fields.Phone, check.Parsed.Fields.State));
        Assert.Null(check.Parsed.Fields.MiddleName);
    }

    public static IEnumerable<object[]> IncorrectFiles()
    {
        static object[] Case(string name, byte[] bytes, string code, string path) => [name, bytes, code, path];
        static byte[] Edit(Action<JsonObject> change) => PatientImportFiles.Changed(change);

        yield return Case("an empty file", [], "file_empty", "$");
        yield return Case("a file over the size limit", new byte[PatientExportFormat.MaxFileBytes + 1], "file_too_large", "$");
        yield return Case("text that is not JSON", PatientImportFiles.Bytes("this is not a patient file"), "not_json", "$");
        yield return Case("JSON with a trailing comma", PatientImportFiles.Bytes("{\"format\": \"alveara.patient-export\",}"), "not_json", "$");
        yield return Case("JSON that is not an object", PatientImportFiles.Bytes("[1, 2, 3]"), "wrong_type", "$");
        yield return Case("another format name", Edit(f => f["format"] = "someone.else"), "wrong_format", "format");
        yield return Case("a format that is not text", Edit(f => f["format"] = 7), "wrong_format", "format");
        yield return Case("a newer version", Edit(f => f["formatVersion"] = 2), "unsupported_version", "formatVersion");
        yield return Case("a version written as text", Edit(f => f["formatVersion"] = "1"), "unsupported_version", "formatVersion");
        yield return Case("a version that is not a whole number", Edit(f => f["formatVersion"] = 1.5), "unsupported_version", "formatVersion");
        yield return Case("an export id that is not a GUID", Edit(f => f["exportId"] = "not-a-guid"), "export_id_invalid", "exportId");
        yield return Case("an empty GUID as the export id", Edit(f => f["exportId"] = Guid.Empty.ToString()), "export_id_invalid", "exportId");
        yield return Case("an export time that is not a UTC time", Edit(f => f["exportedAtUtc"] = "yesterday"), "exported_at_invalid", "exportedAtUtc");
        yield return Case("a time without the Z", Edit(f => f["exportedAtUtc"] = "2026-10-26T14:05:11"), "exported_at_invalid", "exportedAtUtc");
        yield return Case("a missing top-level member", Edit(f => f.Remove("source")), "missing_member", "source");
        yield return Case("an unknown top-level member", Edit(f => f["notes"] = "hello"), "unknown_member", "notes");
        yield return Case("a top-level member of the wrong type", Edit(f => f["patient"] = "Eli Moss"), "wrong_type", "patient");
        yield return Case("a missing patient member", Edit(f => f["patient"]!.AsObject().Remove("city")), "missing_member", "patient.city");
        yield return Case("an unknown patient member", Edit(f => f["patient"]!.AsObject()["socialSecurityNumber"] = "123"), "unknown_member", "patient.socialSecurityNumber");
        yield return Case("a patient member that is a number", Edit(f => f["patient"]!.AsObject()["postalCode"] = 78701), "wrong_type", "patient.postalCode");
        yield return Case("a source id that is not a GUID", Edit(f => f["source"]!.AsObject()["sourcePatientId"] = "x"), "source_patient_id_invalid", "source.sourcePatientId");
        yield return Case("a source system that is not text", Edit(f => f["source"]!.AsObject()["system"] = 5), "wrong_type", "source.system");
        yield return Case("a required field that is null", Edit(f => f["patient"]!.AsObject()["firstName"] = null), "field_invalid", "patient.firstName");
        yield return Case("a date of birth in another form", Edit(f => f["patient"]!.AsObject()["dateOfBirth"] = "05/17/1990"), "field_invalid", "patient.dateOfBirth");
        yield return Case("a date of birth in the future", Edit(f => f["patient"]!.AsObject()["dateOfBirth"] = "2999-01-01"), "field_invalid", "patient.dateOfBirth");
        yield return Case("a phone number with too few digits", Edit(f => f["patient"]!.AsObject()["phone"] = "123"), "field_invalid", "patient.phone");
        yield return Case("an email that is not an email", Edit(f => f["patient"]!.AsObject()["email"] = "not an email"), "field_invalid", "patient.email");
        yield return Case("a name over the length limit", Edit(f => f["patient"]!.AsObject()["lastName"] = new string('x', 81)), "field_invalid", "patient.lastName");
        yield return Case("a member written twice",
            PatientImportFiles.Bytes(PatientImportFiles.Valid().ToJsonString().Replace("\"formatVersion\":1", "\"formatVersion\":1,\"formatVersion\":1")), "duplicate_member", "formatVersion");
    }

    [Theory]
    [MemberData(nameof(IncorrectFiles))]
    public void An_incorrect_file_is_rejected_with_the_right_code_at_the_right_place_and_gives_back_no_fields(string name, byte[] bytes, string code, string path)
    {
        var check = Check(bytes);

        Assert.True(check.Problems.Any(p => p.Code == code && p.Path == path), $"{name}: expected {code} at {path}, got {string.Join("; ", check.Problems.Select(p => $"{p.Code} at {p.Path}"))}");
        Assert.Null(check.Parsed);                                                                      // a rejected file yields nothing to store
        Assert.All(check.Problems, p => Assert.False(string.IsNullOrWhiteSpace(p.Message), "every problem says what to do"));
    }

    [Fact]
    public void Several_mistakes_are_all_reported_together_so_they_can_be_fixed_in_one_go()
    {
        var check = Check(PatientImportFiles.Changed(f =>
        {
            f["formatVersion"] = 9;
            f["exportId"] = "nope";
            f["patient"]!.AsObject()["phone"] = "12";
            f["patient"]!.AsObject()["lastName"] = null;
            f["patient"]!.AsObject()["dateOfBirth"] = "tomorrow";
        }));

        var codes = check.Problems.Select(p => (p.Code, p.Path)).ToList();
        Assert.Contains(("unsupported_version", "formatVersion"), codes);
        Assert.Contains(("export_id_invalid", "exportId"), codes);
        Assert.Contains(("field_invalid", "patient.phone"), codes);
        Assert.Contains(("field_invalid", "patient.lastName"), codes);
        Assert.Contains(("field_invalid", "patient.dateOfBirth"), codes);
        Assert.Equal(9, check.FormatVersion);                                                           // what the file said is kept, so the rejection can be traced
    }

    [Fact]
    public void A_field_the_practice_requires_is_required_in_the_file_too()
    {
        var file = PatientImportFiles.Bytes(PatientImportFiles.Valid());                                // no email, a sex given

        Assert.Empty(Check(file).Problems);
        var strict = Check(file, new PatientRequirements(RequireEmail: true));
        Assert.Contains(strict.Problems, p => p.Code == "field_invalid" && p.Path == "patient.email");
        Assert.Empty(Check(file, new PatientRequirements(RequireSex: true)).Problems);                  // sex is present, so the other requirement is met
    }

    [Fact]
    public void A_message_never_repeats_what_the_file_said()
    {
        var check = Check(PatientImportFiles.Changed(f =>
        {
            f["format"] = "secret-format-name-12345";
            f["patient"]!.AsObject()["email"] = "private.person@example.invalid is not valid";
            f["patient"]!.AsObject()["phone"] = "9";
        }));

        Assert.NotEmpty(check.Problems);
        Assert.All(check.Problems, p =>
        {
            Assert.DoesNotContain("secret-format-name-12345", p.Message);
            Assert.DoesNotContain("private.person", p.Message);
        });
    }
}
