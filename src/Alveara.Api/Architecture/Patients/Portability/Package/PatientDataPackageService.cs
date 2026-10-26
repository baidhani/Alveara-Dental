using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Patients.Portability.Package;

/// <summary>A package ready to hand to the caller: the ZIP stream (a temporary file deleted when the stream is closed, so dispose it after sending), the name to save it under and what was recorded about it.</summary>
public sealed class DataPackageFile(BuiltPackage built, Guid exportId) : IAsyncDisposable
{
    public Guid ExportId { get; } = exportId;
    public string FileName { get; } = $"alveara-patient-package-{exportId:N}.zip";
    public Stream Content => built.Content;
    public long SizeBytes => built.SizeBytes;
    public string Sha256 => built.Sha256;
    public string Result => built.Result;
    public int ProblemCount => built.ProblemCount;
    public ValueTask DisposeAsync() => built.DisposeAsync();
}

/// <summary>
/// ALV-018-C01: exports one patient's whole record as a package (see <see cref="PatientDataPackageBuilder"/>). Who may do it is decided by the permission on the controller. The order is deliberate, as in the
/// STORY-018 export: the history row is saved as Started BEFORE any data is read, so even an export that is interrupted leaves a trace; the package is built; then the history row is finished (result, totals, file hash)
/// and the audit entry (the user and the time, the patient, the export id and counts, never a name or a value from a record) is saved in the SAME save; only then is the package handed back, so a package can never leave
/// the system without its history row and its log entry. If building or that save fails, the temporary package is deleted, the history row is marked Failed and a DataExportFailed entry is logged, and the original
/// error goes to the caller: no file is returned. Failure modes handled: an unknown patient (404 <c>patient_not_found</c>), a patient merged away (409 <c>patient_merged</c>), a domain that cannot be read (the package is
/// Partial and says so), a failing history or audit store (no file), cancellation. Not handled: all patients (a later slice), document files (a later slice). No external service is called; each database read has a time limit.
/// </summary>
public class PatientDataPackageService(AlveraDbContext db, PatientDataPackageBuilder builder, IPracticeClock clock, IServiceScopeFactory scopes, ILogger<PatientDataPackageService> log)
{
    public async Task<DataPackageFile> ExportPatientAsync(Guid patientId, Guid actor, CancellationToken ct)
    {
        var patient = await db.Patients.AsNoTracking().Select(p => new { p.Id, p.MergedAtUtc }).SingleOrDefaultAsync(p => p.Id == patientId, ct)
            ?? throw new PatientException("patient_not_found", "That patient was not found.", 404);
        if (patient.MergedAtUtc is not null)
            throw new PatientException("patient_merged", "That record was merged into another patient record and is no longer exported; export the record it was merged into.", 409);

        var exportId = Guid.NewGuid();
        var history = new DataExportHistory
        {
            Id = Guid.NewGuid(), ExportId = exportId, Kind = DataExportKind.Package, Scope = DataExportScope.OnePatient, SubjectRecordId = patientId,
            IncludesDocumentFiles = false, RequestedByUserId = actor, RequestedAtUtc = clock.UtcNow, Result = DataExportResult.Started,
        };
        db.DataExportHistory.Add(history);
        await db.SaveChangesAsync(ct);                                                                  // recorded as Started before any data is read

        BuiltPackage? built = null;
        try
        {
            var scope = new PackageScope(DataExportScope.OnePatient, $"SELECT CONVERT(uniqueidentifier, '{patientId:D}')", 1, patientId);
            built = await builder.BuildAsync(exportId, actor, scope, ct);

            history.Result = built.Result;
            history.FinishedAtUtc = clock.UtcNow;
            history.PatientCount = 1;
            history.FileCount = built.FileCount;
            history.SizeBytes = built.SizeBytes;
            history.Sha256 = built.Sha256;
            history.ProblemCount = built.ProblemCount;
            AuditService.Record(db, AuditEventTypes.PatientDataPackageExported, nameof(Patient), patientId, actor,
                $"Patient data package exported (export {exportId}, result {built.Result}, {built.FileCount} files, {built.SizeBytes} bytes, {built.ProblemCount} problems).", null, exportId);
            await db.SaveChangesAsync(ct);                                                              // finished and logged before the package leaves

            return new DataPackageFile(built, exportId);
        }
        catch (Exception ex)
        {
            if (built is not null) await built.DisposeAsync();
            await RecordFailureAsync(history.Id, exportId, patientId, actor, ex);
            throw;
        }
    }

    /// <summary>Marks the export Failed and logs it, on a fresh scope (the failed save may have left the original context unusable). If even this cannot be saved it is logged with its error class and the original error still reaches the caller.</summary>
    private async Task RecordFailureAsync(Guid historyId, Guid exportId, Guid patientId, Guid actor, Exception cause)
    {
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var other = scope.ServiceProvider.GetRequiredService<AlveraDbContext>();
            var finished = clock.UtcNow;
            await other.DataExportHistory.Where(h => h.Id == historyId && h.Result == DataExportResult.Started)
                .ExecuteUpdateAsync(s => s.SetProperty(h => h.Result, DataExportResult.Failed).SetProperty(h => h.FinishedAtUtc, finished), limit.Token);
            AuditService.Record(other, AuditEventTypes.DataExportFailed, nameof(Patient), patientId, actor,
                $"Patient data package export failed (export {exportId}, error class {cause.GetType().Name}).", null, exportId);
            await other.SaveChangesAsync(limit.Token);
        }
        catch (Exception ex)
        {
            log.LogError("{Event} export={ExportId} error_class={ErrorClass} cause={Cause}", "data_export_failure_not_recorded", exportId, ex.GetType().Name, cause.GetType().Name);
        }
    }
}
