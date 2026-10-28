using Xunit;
using Alveara.Api.Architecture.Imports;
using Alveara.Api.Architecture.Patients;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-018-C02 slice 2, the patient fields an import can fill, without a database: which are required (the front desk's own rules, plus email and sex only when the practice requires them); how a header is matched
/// (ignoring capitals, spaces and punctuation, against common names) and that each field is suggested for at most one column; and the checks of a mapping: a column the file does not have, a field that does not
/// exist, a field filled twice, a required field nothing fills; an empty target means the column is ignored.
/// </summary>
public class PatientImportFieldsTests
{
    private static readonly string[] Columns = ["First Name", "LAST_NAME", "dob", "Phone #", "E-mail", "Street Address", "City", "State", "ZIP", "Notes"];

    private static Dictionary<string, string?> FullMapping() => new()
    {
        ["First Name"] = "firstName", ["LAST_NAME"] = "lastName", ["dob"] = "dateOfBirth", ["Phone #"] = "phone", ["E-mail"] = "email", ["Street Address"] = "addressLine1",
        ["City"] = "city", ["State"] = "state", ["ZIP"] = "postalCode", ["Notes"] = null,
    };

    [Fact]
    public void The_front_desks_required_fields_are_required_and_email_and_sex_only_when_the_practice_requires_them()
    {
        var none = PatientImportFields.For(PatientRequirements.None).Where(f => f.Required).Select(f => f.Key).ToList();
        Assert.Equal(["firstName", "lastName", "dateOfBirth", "phone", "addressLine1", "city", "state", "postalCode"], none);

        Assert.Contains("email", PatientImportFields.For(new PatientRequirements(RequireEmail: true)).Where(f => f.Required).Select(f => f.Key));
        Assert.Contains("sex", PatientImportFields.For(new PatientRequirements(RequireSex: true)).Where(f => f.Required).Select(f => f.Key));
        Assert.Equal(12, PatientImportFields.For(PatientRequirements.None).Count);
    }

    [Fact]
    public void Headers_are_matched_ignoring_capitals_spaces_and_punctuation()
    {
        var suggested = PatientImportFields.Suggest(Columns);

        var expected = new Dictionary<string, string>
        {
            ["First Name"] = "firstName", ["LAST_NAME"] = "lastName", ["dob"] = "dateOfBirth", ["Phone #"] = "phone", ["E-mail"] = "email", ["Street Address"] = "addressLine1",
            ["City"] = "city", ["State"] = "state", ["ZIP"] = "postalCode",
        };
        Assert.Equal(expected.OrderBy(kv => kv.Key), suggested.OrderBy(kv => kv.Key));
        Assert.DoesNotContain("Notes", suggested.Keys);                                                  // a column with no obvious match is left for the operator
        Assert.Equal("firstname", PatientImportFields.Normalize(" First_Name "));
    }

    [Fact]
    public void A_field_is_suggested_for_at_most_one_column_the_first_that_matches()
    {
        var suggested = PatientImportFields.Suggest(["Address", "Street", "Address 2"]);

        Assert.Equal("addressLine1", suggested["Address"]);
        Assert.DoesNotContain("Street", suggested.Keys);
        Assert.Equal("addressLine2", suggested["Address 2"]);
    }

    [Fact]
    public void A_sound_mapping_has_no_problems_and_an_empty_target_ignores_the_column()
    {
        Assert.Empty(PatientImportFields.CheckMapping(FullMapping(), Columns, PatientRequirements.None));
    }

    [Fact]
    public void Each_kind_of_bad_mapping_is_reported_with_its_stable_code()
    {
        var mapping = FullMapping();
        mapping["Ghost"] = "city";                                                                        // the file has no such column
        mapping["Notes"] = "nickname";                                                                    // there is no such field
        mapping["City"] = "firstName";                                                                    // firstName is already filled by "First Name"
        mapping.Remove("State");                                                                          // state is now unfilled

        var codes = PatientImportFields.CheckMapping(mapping, Columns, PatientRequirements.None).Select(p => p.Code).ToList();

        Assert.Contains("unknown_column", codes);
        Assert.Contains("unknown_field", codes);
        Assert.Contains("field_mapped_twice", codes);
        Assert.Contains("required_field_unmapped", codes);
        Assert.Equal(codes.Count, codes.Count(c => c is "unknown_column" or "unknown_field" or "field_mapped_twice" or "required_field_unmapped"));
    }

    [Fact]
    public void An_unmapped_required_field_is_named_and_email_becomes_required_when_the_practice_says_so()
    {
        var withoutEmail = FullMapping();
        withoutEmail["E-mail"] = null;

        Assert.Empty(PatientImportFields.CheckMapping(withoutEmail, Columns, PatientRequirements.None));
        var problem = Assert.Single(PatientImportFields.CheckMapping(withoutEmail, Columns, new PatientRequirements(RequireEmail: true)));
        Assert.Equal("required_field_unmapped", problem.Code);
        Assert.Contains("Email", problem.Message);
    }

    [Fact]
    public void A_problem_message_never_repeats_anything_but_the_names_the_operator_sent()
    {
        var problems = PatientImportFields.CheckMapping(new Dictionary<string, string?> { ["Ghost"] = "city" }, Columns, PatientRequirements.None);

        Assert.All(problems, p => Assert.DoesNotContain("Ann", p.Message));
        Assert.Contains(problems, p => p.Code == "unknown_column" && p.Column == "Ghost");
    }
}
