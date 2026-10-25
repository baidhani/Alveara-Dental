namespace Alveara.Api.Architecture.Patients.Merge;

/// <summary>
/// STORY-017: the demographic fields a merge can reconcile. Name and date of birth are not among them: they make the unique identity key, which the retired record keeps, so the surviving record keeps its own.
/// A field the survivor has empty is filled from the retired record; a field both have, with different values, is a conflict the administrator answers.
/// </summary>
internal static class PatientMergeFields
{
    /// <summary><see cref="Clear"/> empties an optional field again (an undo puts back a field the retired record had filled in); a required field has none.</summary>
    internal sealed record Field(string Name, Func<Patient, string?> Get, Action<Patient, string> Set, bool Required, Action<Patient>? Clear = null);

    public static readonly IReadOnlyList<Field> All =
    [
        new("MiddleName", p => p.MiddleName, (p, v) => p.MiddleName = v, false, p => p.MiddleName = null),
        new("Sex", p => p.Sex, (p, v) => p.Sex = v, false, p => p.Sex = null),
        new("Phone", p => p.Phone, (p, v) => p.Phone = v, true),
        new("Email", p => p.Email, (p, v) => p.Email = v, false, p => p.Email = null),
        new("AddressLine1", p => p.AddressLine1, (p, v) => p.AddressLine1 = v, true),
        new("AddressLine2", p => p.AddressLine2, (p, v) => p.AddressLine2 = v, false, p => p.AddressLine2 = null),
        new("City", p => p.City, (p, v) => p.City = v, true),
        new("State", p => p.State, (p, v) => p.State = v, true),
        new("PostalCode", p => p.PostalCode, (p, v) => p.PostalCode = v, true),
    ];

    /// <summary>A value with only its meaning left: trimmed, and blank is no value.</summary>
    public static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Two phone numbers are the same when their digits are; everything else is compared ignoring case.</summary>
    public static bool Same(Field f, string a, string b) =>
        f.Name == "Phone" ? PatientInput.DigitsOf(a) == PatientInput.DigitsOf(b) : string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    public static (List<MergeFieldConflict> Conflicts, List<MergeAutoFill> AutoFills) Compare(Patient source, Patient survivor)
    {
        var conflicts = new List<MergeFieldConflict>(); var fills = new List<MergeAutoFill>();
        foreach (var f in All)
        {
            var s = Clean(f.Get(source)); var v = Clean(f.Get(survivor));
            if (s is null) continue;
            if (v is null) fills.Add(new MergeAutoFill(f.Name, s));
            else if (!Same(f, s, v)) conflicts.Add(new MergeFieldConflict(f.Name, v, s));
        }
        return (conflicts, fills);
    }
}
