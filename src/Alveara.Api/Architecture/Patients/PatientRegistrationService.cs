using System.Globalization;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Patients;

/// <summary>The fields a front-desk user types in. Everything is nullable so a missing field is a validation message, not a binding error.</summary>
public record RegisterPatientRequest(
    string? FirstName, string? MiddleName, string? LastName, string? DateOfBirth, string? Sex,
    string? Phone, string? Email, string? AddressLine1, string? AddressLine2, string? City, string? State, string? PostalCode);

/// <summary>A registration was refused. <see cref="Code"/> is stable for the UI; <see cref="FieldErrors"/> lists each field to fix.</summary>
public sealed class PatientRegistrationException(string code, string message, int statusCode, IReadOnlyDictionary<string, string>? fieldErrors = null, Guid? existingPatientId = null)
    : Exception(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
    public IReadOnlyDictionary<string, string> FieldErrors { get; } = fieldErrors ?? new Dictionary<string, string>();
    public Guid? ExistingPatientId { get; } = existingPatientId;
}

public record PatientRegistrationResult(Patient Patient, bool Created);

public static class PatientAuditEvents
{
    public const string Registered = "PatientRegistered";
}

/// <summary>
/// STORY-003: registers a patient. The order of concerns is deliberate:
/// 1. validate (every missing/invalid field reported at once - nothing partial is ever stored);
/// 2. replay: the same idempotency key returns the patient it already created;
/// 3. duplicate: the same person (name + birth date) is refused with a pointer to the existing record;
/// 4. one SaveChanges stages the patient AND its audit entry together, so a registration can never
///    exist without its audit trail (and an audit failure leaves no patient behind).
/// A race between two callers is settled by the database's unique indexes, not by the friendly pre-checks.
/// Audit details never contain patient names or contact data - the entry points at the patient by id.
/// </summary>
public class PatientRegistrationService(AlveraDbContext db, IPracticeClock clock)
{
    private const int MaxIdempotencyKeyLength = 100;

    public async Task<PatientRegistrationResult> RegisterAsync(RegisterPatientRequest request, string? idempotencyKey, Guid actor, CancellationToken ct)
    {
        var key = idempotencyKey?.Trim();
        if (string.IsNullOrEmpty(key) || key.Length > MaxIdempotencyKeyLength)
            throw new PatientRegistrationException("idempotency_key_required",
                $"An idempotency key (1-{MaxIdempotencyKeyLength} characters) is required so a retry never registers the patient twice.", 400);

        var clean = Validate(request);

        var replay = await db.Patients.AsNoTracking().SingleOrDefaultAsync(p => p.RegistrationKey == key, ct);
        if (replay is not null) return new PatientRegistrationResult(replay, Created: false);

        var duplicateKey = Patient.BuildDuplicateKey(clean.FirstName, clean.LastName, clean.DateOfBirth);
        var existing = await db.Patients.AsNoTracking().SingleOrDefaultAsync(p => p.DuplicateKey == duplicateKey, ct);
        if (existing is not null)
        {
            // The same request can commit between the replay check above and this one (simultaneous retries):
            // that is still a replay, not a duplicate person.
            if (existing.RegistrationKey == key) return new PatientRegistrationResult(existing, Created: false);
            throw Duplicate(existing.Id);
        }

        var patient = new Patient
        {
            Id = Guid.NewGuid(),
            FirstName = clean.FirstName, MiddleName = clean.MiddleName, LastName = clean.LastName,
            DateOfBirth = clean.DateOfBirth, Sex = clean.Sex,
            Phone = clean.Phone, Email = clean.Email,
            AddressLine1 = clean.AddressLine1, AddressLine2 = clean.AddressLine2,
            City = clean.City, State = clean.State, PostalCode = clean.PostalCode,
            DuplicateKey = duplicateKey,
            RegistrationKey = key,
            CreatedByUserId = actor,
            CreatedAtUtc = clock.UtcNow,
        };
        db.Patients.Add(patient);
        AuditService.Record(db, PatientAuditEvents.Registered, nameof(Patient), patient.Id, actor, "Patient registered.");

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            // Lost a race the pre-checks could not see. Drop our staged rows and report what the winner stored.
            db.ChangeTracker.Clear();
            var sameKey = await db.Patients.AsNoTracking().SingleOrDefaultAsync(p => p.RegistrationKey == key, ct);
            if (sameKey is not null) return new PatientRegistrationResult(sameKey, Created: false);
            var winner = await db.Patients.AsNoTracking().Where(p => p.DuplicateKey == duplicateKey).Select(p => p.Id).SingleOrDefaultAsync(ct);
            throw Duplicate(winner);
        }

        return new PatientRegistrationResult(patient, Created: true);
    }

    public async Task<Patient?> FindAsync(Guid id, CancellationToken ct) => await db.Patients.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, ct);

    private static PatientRegistrationException Duplicate(Guid existingId) =>
        new("duplicate_patient", "A patient with the same name and date of birth is already registered.", 409, existingPatientId: existingId == Guid.Empty ? null : existingId);

    private record CleanPatient(string FirstName, string? MiddleName, string LastName, DateOnly DateOfBirth, string? Sex,
        string Phone, string? Email, string AddressLine1, string? AddressLine2, string City, string State, string PostalCode);

    private CleanPatient Validate(RegisterPatientRequest r)
    {
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
        var sex = Optional("sex", "Sex", r.Sex, 30);
        var phone = Required("phone", "Phone", r.Phone, 40);
        if (phone.Length > 0 && phone.Count(char.IsDigit) < 7) errors["phone"] = "Phone must contain at least 7 digits.";
        var email = Optional("email", "Email", r.Email, 200);
        if (email is not null && !LooksLikeEmail(email)) errors["email"] = "Email must look like name@example.com.";
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
            throw new PatientRegistrationException("validation_failed", "Some fields need attention before the patient can be registered.", 400, errors);

        return new CleanPatient(first, middle, last, dob, sex, phone, email, line1, line2, city, state, postal);
    }

    private static bool LooksLikeEmail(string value)
    {
        var at = value.IndexOf('@');
        return at > 0 && at == value.LastIndexOf('@') && value.IndexOf('.', at) > at + 1 && !value.EndsWith('.') && !value.Contains(' ');
    }
}
