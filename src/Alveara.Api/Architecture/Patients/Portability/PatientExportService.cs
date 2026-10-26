using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Patients.Portability;

/// <summary>
/// STORY-018: exports one patient's record to the portable file of <see cref="PatientExportFormat"/>. Who may do it is decided by the permission on the controller; this service decides what the file holds
/// and makes sure the export is logged. The order is deliberate: the file is built, the audit entry (the user and the time, the patient, the export id and format version, never a name or contact detail) is
/// saved, and only then is the file handed back, so an export can never leave the system without its log entry. If the audit entry cannot be saved the exception goes to the caller and no file is returned.
/// Failure modes handled: an unknown patient (404 <c>patient_not_found</c>), a patient who was merged away (409 <c>patient_merged</c>: the record carries on under another one), a failing audit store (no file).
/// Not handled: more than one patient, clinical or financial data, linked files (later stories). No external call is made, so there is no timeout or retry to define.
/// </summary>
public class PatientExportService(AlveraDbContext db, IPracticeClock clock)
{
    public async Task<PatientExportFile> ExportAsync(Guid patientId, Guid actor, CancellationToken ct)
    {
        var patient = await db.Patients.AsNoTracking().SingleOrDefaultAsync(p => p.Id == patientId, ct)
            ?? throw new PatientException("patient_not_found", "That patient was not found.", 404);
        if (patient.MergedAtUtc is not null)
            throw new PatientException("patient_merged", "That record was merged into another patient record and is no longer exported; export the record it was merged into.", 409);

        var exportId = Guid.NewGuid();
        var document = new PatientExportDocument(
            PatientExportFormat.FormatName, PatientExportFormat.CurrentVersion, exportId,
            clock.UtcNow.UtcDateTime.ToString(PatientExportFormat.TimestampFormat),
            new PatientExportSource(PatientExportFormat.SystemName, patient.Id),
            new PatientExportPatient(
                patient.FirstName, patient.MiddleName, patient.LastName, patient.DateOfBirth.ToString("yyyy-MM-dd"), patient.Sex,
                patient.Phone, patient.Email, patient.AddressLine1, patient.AddressLine2, patient.City, patient.State, patient.PostalCode));
        var content = JsonSerializer.SerializeToUtf8Bytes(document, PatientExportFormat.JsonOptions);

        AuditService.Record(db, AuditEventTypes.PatientExported, nameof(Patient), patient.Id, actor,
            $"Patient record exported to a file (format {PatientExportFormat.FormatName}, version {PatientExportFormat.CurrentVersion}, export {exportId}).", null, exportId);
        await db.SaveChangesAsync(ct);                                                                  // the log entry is saved before the file leaves

        return new PatientExportFile(exportId, $"alveara-patient-{exportId:N}.json", content);
    }
}
