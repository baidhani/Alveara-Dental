using System.Globalization;
using Alveara.Api.Architecture.Time;

namespace Alveara.Api.Architecture.Patients;

/// <summary>The identity fields a front-desk user types in, shared by registration and edit. Everything is nullable so a missing field is a validation message, not a binding error.</summary>
public record PatientFields(
    string? FirstName, string? MiddleName, string? LastName, string? DateOfBirth, string? Sex,
    string? Phone, string? Email, string? AddressLine1, string? AddressLine2, string? City, string? State, string? PostalCode);

/// <summary>Validated, trimmed identity fields.</summary>
public record CleanPatient(string FirstName, string? MiddleName, string LastName, DateOnly DateOfBirth, string? Sex,
    string Phone, string? Email, string AddressLine1, string? AddressLine2, string City, string State, string PostalCode);

/// <summary>
/// The one set of field rules for registering AND editing a patient (STORY-003's rules, unchanged unless the practice has chosen to require more). Every missing or
/// invalid field is reported at once so the form can prompt for all of them; nothing partial is ever stored.
/// </summary>
public static class PatientInput
{
    public static CleanPatient Validate(PatientFields r, IPracticeClock clock, PatientRequirements? requirements = null)
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

        return new CleanPatient(first, middle, last, dob, sex, phone, string.IsNullOrEmpty(email) ? null : email, line1, line2, city, state, postal);
    }

    public static string DigitsOf(string value) => new(value.Where(char.IsDigit).ToArray());

    private static bool LooksLikeEmail(string value)
    {
        var at = value.IndexOf('@');
        return at > 0 && at == value.LastIndexOf('@') && value.IndexOf('.', at) > at + 1 && !value.EndsWith('.') && !value.Contains(' ');
    }
}
