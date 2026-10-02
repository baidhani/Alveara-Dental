using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Measurement;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Patients;

/// <summary>
/// The fields a front-desk user types in, plus (ALV-003-C01) the likely-duplicate candidates the user has looked at and chosen
/// to register anyway. The extra member is optional and last, so STORY-003's callers are unchanged.
/// </summary>
public record RegisterPatientRequest(
    string? FirstName, string? MiddleName, string? LastName, string? DateOfBirth, string? Sex,
    string? Phone, string? Email, string? AddressLine1, string? AddressLine2, string? City, string? State, string? PostalCode,
    Guid[]? AcknowledgedDuplicateIds = null)
{
    public PatientFields Fields => new(FirstName, MiddleName, LastName, DateOfBirth, Sex, Phone, Email, AddressLine1, AddressLine2, City, State, PostalCode);
}

/// <summary>A registration was refused (STORY-003's type, kept so its callers and tests are unchanged).</summary>
public sealed class PatientRegistrationException(string code, string message, int statusCode, IReadOnlyDictionary<string, string>? fieldErrors = null, Guid? existingPatientId = null, IReadOnlyList<DuplicateCandidate>? candidates = null)
    : PatientException(code, message, statusCode, fieldErrors, existingPatientId, candidates);

public record PatientRegistrationResult(Patient Patient, bool Created);

public static class PatientAuditEvents
{
    public const string Registered = "PatientRegistered";
}

/// <summary>
/// STORY-003 / ALV-003-C01: registers a patient. The order of concerns is deliberate:
/// 1. validate (every missing/invalid field reported at once - nothing partial is ever stored);
/// 2. replay: the same idempotency key returns the patient it already created;
/// 3. exact duplicate: the same person (name + birth date) is refused with a pointer to the existing record;
/// 4. likely duplicates: shown to the user, who must acknowledge each one to register anyway (never merged silently);
/// 5. one SaveChanges stages the patient AND its audit entry together, so a registration can never
///    exist without its audit trail (and an audit failure leaves no patient behind).
/// A race between two callers is settled by the database's unique indexes, not by the friendly pre-checks.
/// Audit details never contain patient names or contact data - the entry points at the patient by id.
/// </summary>
public class PatientRegistrationService(
    AlveraDbContext db, IPracticeClock clock,
    PatientDuplicateDetector? detector = null, IMeasurementEventSink? measurements = null, ILogger<PatientRegistrationService>? logger = null)
{
    private const int MaxIdempotencyKeyLength = 100;
    private readonly PatientDuplicateDetector _detector = detector ?? new PatientDuplicateDetector(db);

    /// <summary>Candidates for the "check before you create" panel, without registering anything.</summary>
    public async Task<IReadOnlyList<DuplicateCandidate>> CheckDuplicatesAsync(PatientFields fields, CancellationToken ct)
    {
        var clean = ValidateAsRegistration(fields, await PatientRegistrationSettingsService.LoadRequirementsAsync(db, ct));
        return await _detector.FindAsync(clean, excludePatientId: null, ct);
    }

    public async Task<PatientRegistrationResult> RegisterAsync(RegisterPatientRequest request, string? idempotencyKey, Guid actor, CancellationToken ct)
    {
        var key = idempotencyKey?.Trim();
        if (string.IsNullOrEmpty(key) || key.Length > MaxIdempotencyKeyLength)
            throw new PatientRegistrationException("idempotency_key_required",
                $"An idempotency key (1-{MaxIdempotencyKeyLength} characters) is required so a retry never registers the patient twice.", 400);

        var clean = ValidateAsRegistration(request.Fields, await PatientRegistrationSettingsService.LoadRequirementsAsync(db, ct));

        var replay = await db.Patients.AsNoTracking().SingleOrDefaultAsync(p => p.RegistrationKey == key, ct);
        if (replay is not null) return new PatientRegistrationResult(replay, Created: false);

        var duplicateKey = Patient.BuildDuplicateKey(clean.FirstName, clean.LastName, clean.DateOfBirth);
        var existing = await db.Patients.AsNoTracking().SingleOrDefaultAsync(p => p.DuplicateKey == duplicateKey, ct);
        if (existing is not null)
        {
            // The same request can commit between the replay check above and this one (simultaneous retries):
            // that is still a replay, not a duplicate person.
            if (existing.RegistrationKey == key) return new PatientRegistrationResult(existing, Created: false);
            var shown = await _detector.FindAsync(clean, excludePatientId: null, ct);
            await RecordDuplicateEventAsync("exact_refused", shown.Count, ct);
            throw Duplicate(existing.Id, shown);
        }

        var candidates = await _detector.FindAsync(clean, excludePatientId: null, ct);
        var exactNow = candidates.FirstOrDefault(c => c.Exact);
        if (exactNow is not null)
        {
            // The same person was registered between the exact check above and this scan (a simultaneous registration).
            var winner = await db.Patients.AsNoTracking().SingleAsync(p => p.Id == exactNow.Id, ct);
            if (winner.RegistrationKey == key) return new PatientRegistrationResult(winner, Created: false);
            await RecordDuplicateEventAsync("exact_refused", candidates.Count, ct);
            throw Duplicate(exactNow.Id, candidates);
        }
        var acknowledged = (request.AcknowledgedDuplicateIds ?? []).ToHashSet();
        var overridden = candidates.Count > 0 && candidates.All(c => acknowledged.Contains(c.Id));
        if (candidates.Count > 0 && !overridden)
        {
            await RecordDuplicateEventAsync("likely_warned", candidates.Count, ct);
            throw new PatientRegistrationException("possible_duplicate",
                "These patients may be the same person. Review them, then register anyway only if this is a different person.", 409, candidates: candidates);
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
        AuditService.Record(db, PatientAuditEvents.Registered, nameof(Patient), patient.Id, actor, overridden ? "Patient registered after reviewing possible duplicates." : "Patient registered.");

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
            throw Duplicate(winner, []);
        }

        if (overridden) await RecordDuplicateEventAsync("likely_overridden", candidates.Count, ct);
        return new PatientRegistrationResult(patient, Created: true);
    }

    /// <summary>The shared field rules throw <see cref="PatientException"/>; registration keeps raising its own subtype, as STORY-003's callers expect.</summary>
    private CleanPatient ValidateAsRegistration(PatientFields fields, PatientRequirements requirements)
    {
        try
        {
            return PatientInput.Validate(fields, clock, requirements);
        }
        catch (PatientException ex) when (ex is not PatientRegistrationException)
        {
            throw new PatientRegistrationException(ex.Code, ex.Message, ex.StatusCode, ex.FieldErrors);
        }
    }

    private static PatientRegistrationException Duplicate(Guid existingId, IReadOnlyList<DuplicateCandidate> candidates) =>
        new("duplicate_patient", "A patient with the same name and date of birth is already registered.", 409,
            existingPatientId: existingId == Guid.Empty ? null : existingId, candidates: candidates);

    /// <summary>
    /// The shared privacy-safe measurement convention (duplicate-patient detection is a named success metric). Observational only:
    /// a failure to record must never fail or alter the registration, but it is logged, not swallowed.
    /// </summary>
    private async Task RecordDuplicateEventAsync(string category, int count, CancellationToken ct)
    {
        if (measurements is null) return;
        try
        {
            await measurements.RecordAsync("patient.duplicate-check", 1, new { category, count }, ct);
        }
        catch (Exception ex) when (ex is DbUpdateException or MeasurementEventValidationException or InvalidOperationException)
        {
            logger?.LogWarning(ex, "Could not record the patient.duplicate-check measurement event ({ErrorClass}).", ex.GetType().Name);
        }
    }

    public async Task<Patient?> FindAsync(Guid id, CancellationToken ct) => await db.Patients.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, ct);
}
