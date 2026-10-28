using Alveara.Api.Architecture.Patients;

namespace Alveara.Api.Architecture.Imports;

/// <summary>A field of a patient that a column of the file can fill. <see cref="Key"/> is the name the front desk's own rules use (<see cref="PatientFields"/>), so a field error means the same thing everywhere.</summary>
public sealed record ImportField(string Key, string Label, bool Required);

/// <summary>
/// ALV-018-C02: the patient fields an import can fill, which of them the practice requires, and how a file's column names are matched to them. A header is matched ignoring capitals, spaces and punctuation
/// ("First Name", "first_name" and "FIRSTNAME" are the same) against a short list of common names ("dob", "birth date", "zip"); a suggestion is only that: the operator confirms or changes the mapping, and the
/// server checks it (<see cref="CheckMapping"/>). A field is required when the front desk's rules require it, so a file that does not map one of them is refused up front instead of failing every row.
/// </summary>
public static class PatientImportFields
{
    private static readonly (string Key, string Label, bool Always, bool IfEmail, bool IfSex, string[] Names)[] Catalog =
    [
        ("firstName", "First name", true, false, false, ["firstname", "first", "givenname", "forename"]),
        ("middleName", "Middle name", false, false, false, ["middlename", "middle", "middleinitial"]),
        ("lastName", "Last name", true, false, false, ["lastname", "last", "surname", "familyname"]),
        ("dateOfBirth", "Date of birth (yyyy-MM-dd)", true, false, false, ["dateofbirth", "dob", "birthdate", "birthday", "born"]),
        ("sex", "Sex", false, false, true, ["sex", "gender"]),
        ("phone", "Phone", true, false, false, ["phone", "phonenumber", "telephone", "mobile", "cell", "homephone"]),
        ("email", "Email", false, true, false, ["email", "emailaddress"]),
        ("addressLine1", "Address", true, false, false, ["addressline1", "address", "address1", "street", "streetaddress"]),
        ("addressLine2", "Address line 2", false, false, false, ["addressline2", "address2", "apt", "unit", "suite"]),
        ("city", "City", true, false, false, ["city", "town"]),
        ("state", "State", true, false, false, ["state", "province"]),
        ("postalCode", "Postal code", true, false, false, ["postalcode", "zip", "zipcode", "postcode"]),
    ];

    public static IReadOnlyList<ImportField> For(PatientRequirements requirements) =>
        Catalog.Select(f => new ImportField(f.Key, f.Label, f.Always || (f.IfEmail && requirements.RequireEmail) || (f.IfSex && requirements.RequireSex))).ToList();

    /// <summary>Header names lower-cased with everything but letters and digits removed.</summary>
    public static string Normalize(string name) => new(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    /// <summary>The suggested field for each column that has an obvious match; each field is suggested for at most one column (the first that matches), and a column with no match is left out.</summary>
    public static IReadOnlyDictionary<string, string> Suggest(IReadOnlyList<string> columns)
    {
        var suggestion = new Dictionary<string, string>();
        var taken = new HashSet<string>();
        foreach (var column in columns)
        {
            var normalized = Normalize(column);
            var match = Catalog.FirstOrDefault(f => !taken.Contains(f.Key) && f.Names.Contains(normalized));
            if (match.Key is null) continue;
            suggestion[column] = match.Key;
            taken.Add(match.Key);
        }

        return suggestion;
    }

    /// <summary>
    /// Checks a mapping of column name to field key (an empty or missing target means the column is ignored). Returns the problems, none for a sound mapping: a column the file does not have
    /// (<c>unknown_column</c>), a field that does not exist (<c>unknown_field</c>), a field filled by two columns (<c>field_mapped_twice</c>) and a required field no column fills (<c>required_field_unmapped</c>).
    /// </summary>
    public static IReadOnlyList<ImportProblem> CheckMapping(IReadOnlyDictionary<string, string?> mapping, IReadOnlyList<string> columns, PatientRequirements requirements)
    {
        var problems = new List<ImportProblem>();
        var fields = For(requirements);
        var known = fields.Select(f => f.Key).ToHashSet();
        var used = new Dictionary<string, string>();
        foreach (var (column, target) in mapping)
        {
            if (!columns.Contains(column)) { problems.Add(new ImportProblem("unknown_column", null, column, "The file has no column with this name.")); continue; }
            if (string.IsNullOrWhiteSpace(target)) continue;
            if (!known.Contains(target)) { problems.Add(new ImportProblem("unknown_field", null, column, $"\"{target}\" is not a patient field.")); continue; }
            if (used.TryGetValue(target, out var first)) problems.Add(new ImportProblem("field_mapped_twice", null, column, $"The field \"{target}\" is already filled by the column \"{first}\"."));
            else used[target] = column;
        }

        foreach (var field in fields.Where(f => f.Required && !used.ContainsKey(f.Key)))
            problems.Add(new ImportProblem("required_field_unmapped", null, null, $"No column fills \"{field.Label}\", which is required."));
        return problems;
    }
}
