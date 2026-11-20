using System.Globalization;
using Alveara.Api.Architecture.Time;

namespace Alveara.Api.Architecture.Patients;

/// <summary>
/// The identity fields a front-desk user types in, shared by registration and edit. Everything is nullable so a missing field is a validation message, not a binding error.
/// ALV-N025 (F-033): <c>Country</c> is last and optional (blank = the practice's default country), so positional callers are unchanged.
/// </summary>
public record PatientFields(
    string? FirstName, string? MiddleName, string? LastName, string? DateOfBirth, string? Sex,
    string? Phone, string? Email, string? AddressLine1, string? AddressLine2, string? City, string? State, string? PostalCode, string? Country = null);

/// <summary>Validated, trimmed identity fields. ALV-N025: <c>Country</c> is always an ISO alpha-2 code.</summary>
public record CleanPatient(string FirstName, string? MiddleName, string LastName, DateOnly DateOfBirth, string? Sex,
    string Phone, string? Email, string AddressLine1, string? AddressLine2, string City, string State, string PostalCode, string Country = Demographics.UnitedStates);

/// <summary>
/// The one set of field rules for registering AND editing a patient (STORY-003's rules, unchanged unless the practice has chosen to require more). Every missing or
/// invalid field is reported at once so the form can prompt for all of them; nothing partial is ever stored.
///
/// ALV-N025 (F-032, F-033; owner decisions DEM-2, DEM-4): Sex is one of <see cref="Demographics.SexValues"/>; Country an ISO alpha-2 code (blank = the practice's default);
/// State, for the US, a code of <see cref="Demographics.UsStates"/> (a full name or any letter case is stored as the code), elsewhere free text up to 50. A value is held
/// to its list only when it is entered or changed: with <paramref name="existing"/> (an edit), an UNCHANGED old Sex, or an unchanged State in an unchanged Country, is kept as
/// it is, so staff can correct something else first; the patient stays on the demographic-review list until it is fixed.
/// </summary>
public static class PatientInput
{
    public static CleanPatient Validate(PatientFields r, IPracticeClock clock, PatientRequirements? requirements = null, PatientFields? existing = null)
    {
        requirements ??= PatientRequirements.None;
        var errors = new Dictionary<string, string>();
        string Required(string field, string label, string? value, int max)
        {
            var v = value?.Trim();
            if (string.IsNullOrEmpty(v)) { errors[field] = $"{label} is required."; return ""; }
            if (v.Length > max) { errors[field] = $"{label} must be {max} characters or fewer."; return ""; }
            return v;
        }
        string? Optional(string field, string label, string? value, int max)
        {
            var v = value?.Trim();
            if (string.IsNullOrEmpty(v)) return null;
            if (v.Length > max) { errors[field] = $"{label} must be {max} characters or fewer."; return null; }
            return v;
        }

        var first = Required("firstName", "First name", r.FirstName, 80);
        var middle = Optional("middleName", "Middle name", r.MiddleName, 80);
        var last = Required("lastName", "Last name", r.LastName, 80);
        var sex = requirements.RequireSex ? Required("sex", "Sex", r.Sex, 30) : Optional("sex", "Sex", r.Sex, 30);
        var phone = Required("phone", "Phone", r.Phone, 40);
        if (phone.Length > 0 && DigitsOf(phone).Length < 7) errors["phone"] = "Phone must contain at least 7 digits.";
        var email = requirements.RequireEmail ? Required("email", "Email", r.Email, 200) : Optional("email", "Email", r.Email, 200);
        if (!string.IsNullOrEmpty(email) && !LooksLikeEmail(email)) errors["email"] = "Email must look like name@example.com.";
        var line1 = Required("addressLine1", "Address", r.AddressLine1, 200);
        var line2 = Optional("addressLine2", "Address line 2", r.AddressLine2, 200);
        var city = Required("city", "City", r.City, 100);
        var state = Required("state", "State", r.State, 50);
        var postal = Required("postalCode", "Postal code", r.PostalCode, 20);

        // ALV-N025: the lists - each checked only when the value is new or changed (an edit keeps an untouched old value)
        static bool Same(string? a, string? b) => string.Equals(a?.Trim() ?? "", b?.Trim() ?? "", StringComparison.Ordinal);
        var country = string.IsNullOrWhiteSpace(r.Country) ? requirements.DefaultCountry : Demographics.CanonicalCountry(r.Country);
        if (country is null) { errors["country"] = "Country must be a country from the list (a two-letter ISO code)."; country = requirements.DefaultCountry; }
        var countryChanged = existing is null || !Same(existing.Country ?? Demographics.UnitedStates, country);
        if (!string.IsNullOrEmpty(sex) && (existing is null || !Same(existing.Sex, sex)))
        {
            if (Demographics.CanonicalSex(sex) is { } canonicalSex) sex = canonicalSex;
            else errors["sex"] = "Sex must be one of: " + string.Join(", ", Demographics.SexValues) + ".";
        }
        if (state.Length > 0 && country == Demographics.UnitedStates && (existing is null || countryChanged || !Same(existing.State, state)))
        {
            if (Demographics.CanonicalUsState(state) is { } code) state = code;
            else errors["state"] = "Choose a US state, district, territory or armed-forces code from the list.";
        }

        var dob = default(DateOnly);
        var dobText = r.DateOfBirth?.Trim();
        if (string.IsNullOrEmpty(dobText)) errors["dateOfBirth"] = "Date of birth is required.";
        else if (!DateOnly.TryParseExact(dobText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out dob))
            errors["dateOfBirth"] = "Date of birth must be a real date in the form yyyy-MM-dd.";
        else if (dob > DateOnly.FromDateTime(clock.ToPracticeLocal(clock.UtcNow).DateTime))
            errors["dateOfBirth"] = "Date of birth cannot be in the future.";
        else if (dob.Year < 1900)
            errors["dateOfBirth"] = "Date of birth cannot be before 1900.";

        if (errors.Count > 0)
            throw new PatientException("validation_failed", "Some fields need attention before the patient can be saved.", 400, errors);

        return new CleanPatient(first, middle, last, dob, sex, phone, string.IsNullOrEmpty(email) ? null : email, line1, line2, city, state, postal, country);
    }

    public static string DigitsOf(string value) => new(value.Where(char.IsDigit).ToArray());

    private static bool LooksLikeEmail(string value)
    {
        var at = value.IndexOf('@');
        return at > 0 && at == value.LastIndexOf('@') && value.IndexOf('.', at) > at + 1 && !value.EndsWith('.') && !value.Contains(' ');
    }
}
