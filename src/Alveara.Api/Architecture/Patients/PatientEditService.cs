using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Patients;

/// <summary>
/// ALV-003-C01: safe edits of an existing patient's demographics/contact details, and active/inactive state. Every edit
/// carries the row version the user read (stale = 409), uses STORY-003's field rules unchanged, writes one history row per
/// changed field and a PHI-free audit entry in the same save. Saving with nothing changed is a no-op (no history, no audit,
/// no version bump), so a repeated submit is harmless.
/// </summary>
public class PatientEditService(AlveraDbContext db, IPracticeClock clock)
{
    public async Task<Patient> UpdateAsync(Guid id, PatientFields fields, string? rowVersion, Guid actor, CancellationToken ct)
    {
        var patient = await db.Patients.SingleOrDefaultAsync(p => p.Id == id, ct) ?? throw NotFound();
        PatientWrite.ApplyExpectedVersion(db, patient, rowVersion);
        // ALV-N025 (DEM-2): the stored values, so an unchanged old Sex/State is kept rather than held to the new lists; an edit that sends no country keeps the patient's own
        if (string.IsNullOrWhiteSpace(fields.Country)) fields = fields with { Country = patient.Country };
        var existing = new PatientFields(patient.FirstName, patient.MiddleName, patient.LastName, patient.DateOfBirth.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), patient.Sex,
            patient.Phone, patient.Email, patient.AddressLine1, patient.AddressLine2, patient.City, patient.State, patient.PostalCode, patient.Country);
        var clean = PatientInput.Validate(fields, clock, await PatientRegistrationSettingsService.LoadRequirementsAsync(db, ct), existing);

        var now = clock.UtcNow;
        var changed = new List<string>();
        Guid? firstHistory = null;
        void Track(string name, string? oldValue, string? newValue)
        {
            if (string.Equals(oldValue, newValue, StringComparison.Ordinal)) return;
            changed.Add(name);
            var entry = PatientWrite.History(db, id, now, actor, "Updated", name, oldValue, newValue);
            firstHistory ??= entry;
        }

        Track("firstName", patient.FirstName, clean.FirstName); patient.FirstName = clean.FirstName;
        Track("middleName", patient.MiddleName, clean.MiddleName); patient.MiddleName = clean.MiddleName;
        Track("lastName", patient.LastName, clean.LastName); patient.LastName = clean.LastName;
        Track("dateOfBirth", patient.DateOfBirth.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), clean.DateOfBirth.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)); patient.DateOfBirth = clean.DateOfBirth;
        Track("sex", patient.Sex, clean.Sex); patient.Sex = clean.Sex;
        Track("phone", patient.Phone, clean.Phone); patient.Phone = clean.Phone;
        Track("email", patient.Email, clean.Email); patient.Email = clean.Email;
        Track("addressLine1", patient.AddressLine1, clean.AddressLine1); patient.AddressLine1 = clean.AddressLine1;
        Track("addressLine2", patient.AddressLine2, clean.AddressLine2); patient.AddressLine2 = clean.AddressLine2;
        Track("city", patient.City, clean.City); patient.City = clean.City;
        Track("state", patient.State, clean.State); patient.State = clean.State;
        Track("postalCode", patient.PostalCode, clean.PostalCode); patient.PostalCode = clean.PostalCode;
        Track("country", patient.Country, clean.Country); patient.Country = clean.Country;

        if (changed.Count == 0) { db.ChangeTracker.Clear(); return (await db.Patients.AsNoTracking().SingleAsync(p => p.Id == id, ct)); }

        var newKey = Patient.BuildDuplicateKey(clean.FirstName, clean.LastName, clean.DateOfBirth);
        if (newKey != patient.DuplicateKey)
        {
            var other = await db.Patients.AsNoTracking().Where(p => p.DuplicateKey == newKey && p.Id != id).Select(p => p.Id).SingleOrDefaultAsync(ct);
            if (other != Guid.Empty)
                throw new PatientException("duplicate_patient", "Another patient with that name and date of birth is already registered.", 409, existingPatientId: other);
            patient.DuplicateKey = newKey;
        }

        PatientWrite.Touch(patient, now, actor);
        PatientWrite.Audit(db, PatientAuditEventsV2.Updated, id, actor, $"Patient updated: {string.Join(", ", changed)}.", firstHistory);   // ALV-N025 (F-041): the fields; the values are in History
        await PatientWrite.SaveAsync(db, id, ct);
        return patient;
    }

    public async Task<Patient> SetActiveAsync(Guid id, bool isActive, string? rowVersion, Guid actor, CancellationToken ct)
    {
        var patient = await db.Patients.SingleOrDefaultAsync(p => p.Id == id, ct) ?? throw NotFound();
        PatientWrite.ApplyExpectedVersion(db, patient, rowVersion);
        if (patient.IsActive == isActive) { db.ChangeTracker.Clear(); return await db.Patients.AsNoTracking().SingleAsync(p => p.Id == id, ct); }

        if (!isActive)
        {
            var dependents = await db.Patients.CountAsync(p => p.GuarantorPatientId == id && p.IsActive, ct);
            if (dependents > 0)
                throw new PatientException("guarantor_in_use",
                    $"This patient is the guarantor for {dependents} active patient{(dependents == 1 ? "" : "s")}. Give them another guarantor before inactivating.", 409);
        }

        var now = clock.UtcNow;
        patient.IsActive = isActive;
        PatientWrite.Touch(patient, now, actor);
        PatientWrite.History(db, id, now, actor, isActive ? "Activated" : "Inactivated", "isActive", (!isActive).ToString(), isActive.ToString());
        PatientWrite.Audit(db, PatientAuditEventsV2.StatusChanged, id, actor, isActive ? "Patient reactivated." : "Patient inactivated.");
        await PatientWrite.SaveAsync(db, id, ct);
        return patient;
    }

    private static PatientException NotFound() => new("patient_not_found", "That patient was not found.", 404);
}
