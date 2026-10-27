using System.Security.Cryptography;
using System.Text;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Patients.Portability.Package;

/// <summary>A readable record ready to hand to the caller: the page as bytes, the name to save it under and its hash. Small enough to hold in memory (one patient, no documents).</summary>
public sealed record ReadableRecordFile(Guid ExportId, byte[] Content, string Sha256)
{
    public string FileName => $"alveara-readable-record-{ExportId:N}.html";
    public long SizeBytes => Content.Length;
}

/// <summary>
/// ALV-018-C01: the stand-alone "readable record" download: the same function the package calls for the page inside it (<see cref="ReadableRecordLoader"/> and <see cref="ReadableRecordHtml"/>), on its own, for
/// one patient, with no documents (the page says so). Who may do it is the controller's permission (<see cref="Permission.ExportPatientReadableRecord"/>). The order is the package's: the history row is saved as
/// Started (kind ReadableRecord, one patient, no document files) BEFORE anything is read; the page is made; then the row is finished (size and hash) and the audit entry (<c>PatientReadableRecordExported</c>: the user,
/// the time, the patient and the export id, never a name or a value) is saved in the SAME save; only then is the page handed back. If making the page or that save fails, nothing is returned, the row is marked Failed
/// and a DataExportFailed entry is logged (see <see cref="ExportFailureRecorder"/>). Failure modes handled: an unknown patient (404), a patient merged away (409), a failing read, a failing history or audit store.
/// Doing it twice makes two exports with two ids and the same page content (apart from the time it was made).
/// </summary>
public class PatientReadableRecordService(AlveraDbContext db, ReadableRecordLoader loader, IPracticeClock clock, ExportFailureRecorder failures)
{
    public async Task<ReadableRecordFile> ExportAsync(Guid patientId, Guid actor, CancellationToken ct)
    {
        await PatientExportSubject.RequireAsync(db, patientId, ct);
        var exportId = Guid.NewGuid();
        var history = new DataExportHistory
        {
            Id = Guid.NewGuid(), ExportId = exportId, Kind = DataExportKind.ReadableRecord, Scope = DataExportScope.OnePatient, SubjectRecordId = patientId,
            IncludesDocumentFiles = false, RequestedByUserId = actor, RequestedAtUtc = clock.UtcNow, Result = DataExportResult.Started,
        };
        db.DataExportHistory.Add(history);
        await db.SaveChangesAsync(ct);                                                                  // recorded as Started before the record is read

        try
        {
            var record = await loader.LoadAsync(patientId, ct);
            var page = Encoding.UTF8.GetBytes(ReadableRecordHtml.Render(record, new ReadableRecordContext(exportId, PracticeTimeText.Moment(clock, clock.UtcNow), clock.PracticeTimeZone.Id, InsidePackage: false)));
            var sha = Convert.ToHexStringLower(SHA256.HashData(page));

            history.Result = DataExportResult.Complete;
            history.FinishedAtUtc = clock.UtcNow;
            (history.PatientCount, history.FileCount, history.SizeBytes, history.Sha256, history.ProblemCount) = (1, 1, page.Length, sha, 0);
            AuditService.Record(db, AuditEventTypes.PatientReadableRecordExported, nameof(Patient), patientId, actor,
                $"Patient readable record exported (export {exportId}, {page.Length} bytes).", null, exportId);
            await db.SaveChangesAsync(ct);                                                              // finished and logged before the page leaves
            return new ReadableRecordFile(exportId, page, sha);
        }
        catch (Exception ex)
        {
            await failures.RecordAsync(history.Id, exportId, "Patient readable record", nameof(Patient), patientId, actor, ex);
            throw;
        }
    }
}
