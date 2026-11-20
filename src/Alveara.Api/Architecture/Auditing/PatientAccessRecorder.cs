using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Patients;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Auditing;

/// <summary>
/// ALV-N025 (PD-20, OQ-1, SES-2): the patient-access trail's record opens and print requests. PHI-free details; the entity is the patient, the actor the account.
/// - A record open is written once per patient per session: the first open inserts (SessionId, PatientId) into PatientRecordOpens and writes PatientRecordOpened in the
///   same save; a repeat finds the row and writes nothing; two first opens at once meet the key, and the loser writes nothing. A cookie from before this release has no
///   session id (SES-2): each of its opens is written, without a row. Only the workspace load records an open - never searches, lists or background reads.
/// - A print request (an in-application Print button, before the browser's dialog) writes PatientMaterialPrinted with the material type, the resource id and the
///   outcome "requested": it records the request, not that paper came out.
/// </summary>
public sealed class PatientAccessRecorder(AlveraDbContext db)
{
    public static readonly IReadOnlyList<string> MaterialTypes = ["TreatmentPlanPatientCopy", "Statement", "Receipt", "Prescription", "ReadableRecord", "Report"];

    /// <summary>True when this call wrote the open; false when this session had already opened the record.</summary>
    public async Task<bool> RecordOpenAsync(Guid patientId, Guid userId, Guid? sessionId, DateTimeOffset nowUtc, CancellationToken ct)
    {
        if (sessionId is not { } sid)
        {
            AuditService.Record(db, PatientAccessEvents.RecordOpened, nameof(Patient), patientId, userId, "Patient record opened (a session from before the access trail; not de-duplicated).", timestampUtc: nowUtc);
            await db.SaveChangesAsync(ct);
            return true;
        }
        if (await db.PatientRecordOpens.AsNoTracking().AnyAsync(o => o.SessionId == sid && o.PatientId == patientId, ct)) return false;
        db.PatientRecordOpens.Add(new PatientRecordOpen { SessionId = sid, PatientId = patientId, UserId = userId, FirstOpenedAtUtc = nowUtc });
        AuditService.Record(db, PatientAccessEvents.RecordOpened, nameof(Patient), patientId, userId, "Patient record opened.", correlationId: sid, timestampUtc: nowUtc);
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            db.ChangeTracker.Clear();                                                                 // a simultaneous first open of the same session wrote it
            return false;
        }
    }

    /// <summary>Records a print request. Refuses an unknown material type (400) or patient (404).</summary>
    public async Task RecordPrintAsync(Guid patientId, string? materialType, Guid? resourceId, Guid userId, DateTimeOffset nowUtc, CancellationToken ct)
    {
        var type = MaterialTypes.FirstOrDefault(t => string.Equals(t, materialType?.Trim(), StringComparison.Ordinal))
            ?? throw new PatientException("material_type_invalid", "The printed material must be one of: " + string.Join(", ", MaterialTypes) + ".", 400,
                new Dictionary<string, string> { ["materialType"] = "Not a printed material this system knows." });
        if (!await db.Patients.AsNoTracking().AnyAsync(p => p.Id == patientId, ct)) throw new PatientException("patient_not_found", "That patient was not found.", 404);
        AuditService.Record(db, PatientAccessEvents.MaterialPrinted, nameof(Patient), patientId, userId,
            $"Print requested: {type}{(resourceId is { } r ? $" {r}" : "")}; outcome requested.", timestampUtc: nowUtc);
        await db.SaveChangesAsync(ct);
    }
}
