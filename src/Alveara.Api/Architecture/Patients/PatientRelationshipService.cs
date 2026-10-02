using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Patients;

/// <summary>
/// ALV-003-C01: household membership and the guarantor (responsible party), kept as TWO independent links on the patient:
/// a household can contain several people with different guarantors, and a guarantor need not live in the household.
///
/// Guarantor rules (each refusal is an <c>invalid_relationship</c> 400 with a message the form shows):
/// - null (or the patient themselves) means "responsible for themselves";
/// - the guarantor must exist and be active;
/// - the guarantor must be responsible for themselves - no chains (A guaranteed by B guaranteed by C) and so no cycles;
/// - a patient who is already guarantor for others cannot be given a guarantor of their own.
/// Household rules: a patient is in at most one household; joining names an existing patient as the anchor (creating the
/// household if the anchor has none); a patient in a different household must leave it first; an inactive anchor is refused.
/// Every change checks the row version, writes a history row and a PHI-free audit entry in one save.
/// </summary>
public class PatientRelationshipService(AlveraDbContext db, IPracticeClock clock)
{
    public async Task<Patient> SetGuarantorAsync(Guid id, Guid? guarantorId, string? rowVersion, Guid actor, CancellationToken ct)
    {
        var patient = await db.Patients.SingleOrDefaultAsync(p => p.Id == id, ct) ?? throw NotFound();
        PatientWrite.ApplyExpectedVersion(db, patient, rowVersion);

        var target = guarantorId == id ? null : guarantorId;
        if (target is not null)
        {
            var guarantor = await db.Patients.AsNoTracking().SingleOrDefaultAsync(p => p.Id == target, ct)
                ?? throw Invalid("The chosen guarantor was not found.");
            if (!guarantor.IsActive) throw Invalid("An inactive patient cannot be a guarantor. Choose an active patient.");
            if (guarantor.GuarantorPatientId is not null)
                throw Invalid("That patient has a guarantor of their own. A guarantor must be responsible for themselves.");
            if (await db.Patients.AnyAsync(p => p.GuarantorPatientId == id, ct))
                throw Invalid("This patient is already the guarantor for other patients, so they cannot be given a guarantor of their own.");
        }

        if (patient.GuarantorPatientId == target) { db.ChangeTracker.Clear(); return await db.Patients.AsNoTracking().SingleAsync(p => p.Id == id, ct); }

        var now = clock.UtcNow;
        PatientWrite.History(db, id, now, actor, "GuarantorChanged", "guarantorPatientId", patient.GuarantorPatientId?.ToString(), target?.ToString());
        patient.GuarantorPatientId = target;
        PatientWrite.Touch(patient, now, actor);
        PatientWrite.Audit(db, PatientAuditEventsV2.GuarantorChanged, id, actor, target is null ? "Guarantor cleared (self-responsible)." : "Guarantor set.");
        await PatientWrite.SaveAsync(db, id, ct);
        return patient;
    }

    /// <summary>Joins the household of <paramref name="anchorId"/> (null = leave the current household).</summary>
    public async Task<Patient> SetHouseholdAsync(Guid id, Guid? anchorId, string? relationship, string? rowVersion, Guid actor, CancellationToken ct)
    {
        var patient = await db.Patients.SingleOrDefaultAsync(p => p.Id == id, ct) ?? throw NotFound();
        PatientWrite.ApplyExpectedVersion(db, patient, rowVersion);
        var now = clock.UtcNow;

        if (anchorId is null)
        {
            if (patient.HouseholdId is null) { db.ChangeTracker.Clear(); return await db.Patients.AsNoTracking().SingleAsync(p => p.Id == id, ct); }
            var leaving = patient.HouseholdId.Value;
            PatientWrite.History(db, id, now, actor, "HouseholdChanged", "householdId", leaving.ToString(), null);
            patient.HouseholdId = null;
            patient.HouseholdRelationship = null;
            PatientWrite.Touch(patient, now, actor);
            PatientWrite.Audit(db, PatientAuditEventsV2.HouseholdChanged, id, actor, "Left household.");
            // A household nobody belongs to carries no meaning; remove the empty grouping (the history above keeps the trail).
            if (!await db.Patients.AnyAsync(p => p.HouseholdId == leaving && p.Id != id, ct))
                db.Households.Remove(await db.Households.SingleAsync(h => h.Id == leaving, ct));
            await PatientWrite.SaveAsync(db, id, ct);
            return patient;
        }

        if (anchorId == id) throw Invalid("A patient cannot be joined to their own household. Choose another member of the household.");
        if (!HouseholdRelationships.IsValid(relationship))
            throw new PatientException("validation_failed", "Choose how this patient relates to the household.", 400,
                new Dictionary<string, string> { ["relationship"] = $"Relationship must be one of: {string.Join(", ", HouseholdRelationships.All)}." });

        var anchor = await db.Patients.SingleOrDefaultAsync(p => p.Id == anchorId, ct) ?? throw Invalid("The household member you chose was not found.");
        if (!anchor.IsActive) throw Invalid("An inactive patient cannot anchor a household. Choose an active patient.");
        if (patient.HouseholdId is not null && patient.HouseholdId != anchor.HouseholdId)
            throw new PatientException("already_in_household", "This patient already belongs to a different household. Remove them from it first.", 409);

        var householdId = anchor.HouseholdId;
        if (householdId is null)
        {
            var household = new Household { Id = Guid.NewGuid(), CreatedAtUtc = now, CreatedByUserId = actor };
            db.Households.Add(household);
            householdId = household.Id;
            anchor.HouseholdId = householdId;
            anchor.HouseholdRelationship = "Self"; // the anchor is the household's head until someone says otherwise
            PatientWrite.Touch(anchor, now, actor);
            PatientWrite.History(db, anchor.Id, now, actor, "HouseholdChanged", "householdId", null, householdId.ToString());
        }

        if (patient.HouseholdId == householdId && patient.HouseholdRelationship == relationship) { db.ChangeTracker.Clear(); return await db.Patients.AsNoTracking().SingleAsync(p => p.Id == id, ct); }

        if (patient.HouseholdId != householdId)
            PatientWrite.History(db, id, now, actor, "HouseholdChanged", "householdId", patient.HouseholdId?.ToString(), householdId.ToString());
        if (patient.HouseholdRelationship != relationship)
            PatientWrite.History(db, id, now, actor, "HouseholdChanged", "householdRelationship", patient.HouseholdRelationship, relationship);
        patient.HouseholdId = householdId;
        patient.HouseholdRelationship = relationship;
        PatientWrite.Touch(patient, now, actor);
        PatientWrite.Audit(db, PatientAuditEventsV2.HouseholdChanged, id, actor, "Household membership set.");
        if (anchor.Id != id && db.Entry(anchor).State == EntityState.Modified)
            PatientWrite.Audit(db, PatientAuditEventsV2.HouseholdChanged, anchor.Id, actor, "Household created.");
        await PatientWrite.SaveAsync(db, id, ct);
        return patient;
    }

    private static PatientException Invalid(string message) => new("invalid_relationship", message, 400);
    private static PatientException NotFound() => new("patient_not_found", "That patient was not found.", 404);
}
