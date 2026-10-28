using Xunit;
using Alveara.Api.Architecture.Imports;
using Alveara.Api.Architecture.Patients;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-018-C02 slice 4a, the fields of an appointment file without a database: which are required, how a header is matched (ignoring capitals, spaces and punctuation), that each field is suggested for at most one
/// column, how a mapping is checked (the same checks as for patients), and that the mapping step reaches the right catalog for each kind of file.
/// </summary>
public class AppointmentImportFieldsTests
{
    private static readonly string[] Columns = ["Patient First", "Patient Last", "DOB", "Provider", "Operatory", "Type", "Start", "Duration", "Notes", "Extra"];

    private static Dictionary<string, string?> FullMapping() => new()
    {
        ["Patient First"] = "patientFirstName", ["Patient Last"] = "patientLastName", ["DOB"] = "patientDateOfBirth", ["Provider"] = "provider", ["Operatory"] = "operatory", ["Type"] = "appointmentType",
        ["Start"] = "start", ["Duration"] = "durationMinutes", ["Notes"] = "notes", ["Extra"] = null,
    };

    [Fact]
    public void The_patient_the_provider_the_place_the_type_and_the_start_are_required_and_the_duration_and_notes_are_not()
    {
        Assert.Equal(["patientFirstName", "patientLastName", "patientDateOfBirth", "provider", "operatory", "appointmentType", "start"], AppointmentImportFields.All.Where(f => f.Required).Select(f => f.Key));
        Assert.Equal(["durationMinutes", "notes"], AppointmentImportFields.All.Where(f => !f.Required).Select(f => f.Key));
    }

    [Fact]
    public void Headers_are_matched_ignoring_capitals_spaces_and_punctuation_and_a_column_with_no_match_is_left_out()
    {
        var suggested = AppointmentImportFields.Suggest(Columns);

        var expected = FullMapping().Where(kv => kv.Value is not null).ToDictionary(kv => kv.Key, kv => kv.Value!);
        Assert.Equal(expected.OrderBy(kv => kv.Key), suggested.OrderBy(kv => kv.Key));
        Assert.DoesNotContain("Extra", suggested.Keys);
        Assert.Equal("start", AppointmentImportFields.Suggest(["START_TIME"])["START_TIME"]);
        Assert.Equal("operatory", AppointmentImportFields.Suggest(["Room"])["Room"]);
    }

    [Fact]
    public void A_sound_mapping_has_no_problems_and_each_kind_of_bad_mapping_is_named()
    {
        Assert.Empty(AppointmentImportFields.CheckMapping(FullMapping(), Columns));

        var bad = FullMapping();
        bad["Ghost"] = "start";                                                                  // the file has no such column
        bad["Extra"] = "room";                                                                   // there is no such field
        bad["Notes"] = "provider";                                                               // already filled by "Provider"
        bad.Remove("Operatory");                                                                 // a required field left unfilled
        var codes = AppointmentImportFields.CheckMapping(bad, Columns).Select(p => p.Code).ToList();

        Assert.Contains("unknown_column", codes);
        Assert.Contains("unknown_field", codes);
        Assert.Contains("field_mapped_twice", codes);
        Assert.Contains("required_field_unmapped", codes);
    }

    [Fact]
    public void The_mapping_step_reaches_the_right_catalog_for_each_kind_of_file()
    {
        Assert.Equal(AppointmentImportFields.All, ImportFields.For(ImportSourceKind.Appointments, PatientRequirements.None));
        Assert.Contains(ImportFields.For(ImportSourceKind.Patients, PatientRequirements.None), f => f.Key == "firstName");
        Assert.Equal("provider", ImportFields.Suggest(ImportSourceKind.Appointments, ["Provider"])["Provider"]);
        Assert.Equal("firstName", ImportFields.Suggest(ImportSourceKind.Patients, ["First Name"])["First Name"]);
        Assert.Contains(ImportFields.CheckMapping(ImportSourceKind.Appointments, new Dictionary<string, string?> { ["Provider"] = "provider" }, ["Provider"], PatientRequirements.None), p => p.Code == "required_field_unmapped");
    }
}
