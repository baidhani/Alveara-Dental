using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Patients.Portability.Package;

/// <summary>A package ready to hand to the caller: the ZIP stream (a temporary file deleted when the stream is closed, so dispose it after sending), the name to save it under and what was recorded about it.</summary>
public sealed class DataPackageFile(BuiltPackage built, Guid exportId, string fileName) : IAsyncDisposable
{
    public Guid ExportId { get; } = exportId;
    public string FileName { get; } = fileName;
    public Stream Content => built.Content;
    public long SizeBytes => built.SizeBytes;
    public string Sha256 => built.Sha256;
    public string Result => built.Result;
    public int ProblemCount => built.ProblemCount;
    public int PatientCount => built.PatientCount;
    public ValueTask DisposeAsync() => built.DisposeAsync();
}

/// <summary>
/// ALV-018-C01: exports one patient's whole record, or every patient's, as a package (see <see cref="PatientDataPackageBuilder"/>). Who may do it is decided by the permission on the controller. The order is deliberate,
/// as in the STORY-018 export: the history row is saved as Started BEFORE any data is read, so even an export that is interrupted leaves a trace; the package is built; then (all patients only) the permission is checked
/// once more; then the history row is finished (result, totals, file hash) and the audit entry (the user and the time, the export id and counts, never a name or a value from a record) is saved in the SAME save; only
/// then is the package handed back, so a package can never leave the system without its history row and its log entry. If building, the last permission check or that save fails, the temporary package is deleted, the
/// history row is marked Failed and a DataExportFailed entry is logged, and the original error goes to the caller: no file is returned. For all patients the permission is also checked before anything is read and between
/// the parts of the export, each time freshly (see <see cref="ExportAuthorization"/>), so an export whose permission is withdrawn part way stops and delivers nothing. Failure modes handled: an unknown patient (404
/// <c>patient_not_found</c>), a patient merged away (409 <c>patient_merged</c>), too many patients (413), a package over the size limit (413), a second all-patients export (409 <c>export_in_progress</c>), permission
/// withdrawn (403 <c>permission_revoked</c>), a domain that cannot be read (the package is Partial and says so), a missing or changed document file (Partial), an unknown folder-naming choice (400), a failing history or
/// audit store (no file), cancellation. No external service is called; each database read has a time limit.
/// </summary>
public class PatientDataPackageService(AlveraDbContext db, PatientDataPackageBuilder builder, IExportAuthorization authorization, IPracticeClock clock, IServiceScopeFactory scopes, ILogger<PatientDataPackageService> log)
{
    public async Task<DataPackageFile> ExportPatientAsync(Guid patientId, Guid actor, CancellationToken ct, PackageChoices? choices = null)
    {
        choices = ValidChoices(choices);
        var patient = await db.Patients.AsNoTracking().Select(p => new { p.Id, p.MergedAtUtc }).SingleOrDefaultAsync(p => p.Id == patientId, ct)
            ?? throw new PatientException("patient_not_found", "That patient was not found.", 404);
        if (patient.MergedAtUtc is not null)
            throw new PatientException("patient_merged", "That record was merged into another patient record and is no longer exported; export the record it was merged into.", 409);

        var scope = new PackageScope(DataExportScope.OnePatient, $"SELECT CONVERT(uniqueidentifier, '{patientId:D}')", 1, patientId, choices);
        return await RunAsync(scope, patientId, actor, ct, recheckPermission: false, AuditEventTypes.PatientDataPackageExported, nameof(Patient), patientId);
    }

    /// <summary>Every patient who has not been merged away, up to the configured limits. Over a limit the export is refused, never cut short.</summary>
    public async Task<DataPackageFile> ExportAllAsync(Guid actor, CancellationToken ct, PackageChoices? choices = null)
    {
        choices = ValidChoices(choices);
        var scope = new PackageScope(DataExportScope.AllPatients, "", 0, null, choices, token => authorization.EnsureStillAllowedAsync(actor, Permission.ExportAllPatientData, token));
        return await RunAsync(scope, null, actor, ct, recheckPermission: true, AuditEventTypes.AllPatientDataPackageExported, "PatientData", Guid.Empty);
    }

    private static PackageChoices ValidChoices(PackageChoices? choices)
    {
        choices ??= PackageChoices.Default;
        if (choices.FolderNaming is not null && !FolderNaming.IsValid(choices.FolderNaming))
            throw new PatientException("invalid_folder_naming", $"Folder naming must be '{FolderNaming.IdOnly}' or '{FolderNaming.NameAndId}'.", 400);
        return choices;
    }

    private async Task<DataPackageFile> RunAsync(PackageScope scope, Guid? subject, Guid actor, CancellationToken ct, bool recheckPermission, string auditEvent, string auditEntity, Guid auditEntityId)
    {
        var exportId = Guid.NewGuid();
        var history = new DataExportHistory
        {
            Id = Guid.NewGuid(), ExportId = exportId, Kind = DataExportKind.Package, Scope = scope.Kind, SubjectRecordId = subject,
            IncludesDocumentFiles = scope.Choices!.IncludeDocumentFiles, RequestedByUserId = actor, RequestedAtUtc = clock.UtcNow, Result = DataExportResult.Started,
        };
        db.DataExportHistory.Add(history);
        await db.SaveChangesAsync(ct);                                                                  // recorded as Started before any data is read

        BuiltPackage? built = null;
        try
        {
            built = await builder.BuildAsync(exportId, actor, scope, ct);
            if (recheckPermission) await authorization.EnsureStillAllowedAsync(actor, Permission.ExportAllPatientData, ct);      // the last look before it leaves

            history.Result = built.Result;
            history.FinishedAtUtc = clock.UtcNow;
            history.PatientCount = built.PatientCount;
            history.FileCount = built.FileCount;
            history.SizeBytes = built.SizeBytes;
            history.Sha256 = built.Sha256;
            history.ProblemCount = built.ProblemCount;
            AuditService.Record(db, auditEvent, auditEntity, auditEntityId, actor,
                $"Patient data package exported (export {exportId}, {built.PatientCount} patients, result {built.Result}, {built.FileCount} files, {built.SizeBytes} bytes, {built.ProblemCount} problems).", null, exportId);
            await db.SaveChangesAsync(ct);                                                              // finished and logged before the package leaves

            return new DataPackageFile(built, exportId, $"alveara-{(scope.Kind == DataExportScope.AllPatients ? "all-patients" : "patient")}-package-{exportId:N}.zip");
        }
        catch (Exception ex)
        {
            if (built is not null) await built.DisposeAsync();
            await RecordFailureAsync(history.Id, exportId, auditEntity, auditEntityId, actor, ex);
            throw;
        }
    }

    /// <summary>Marks the export Failed and logs it, on a fresh scope (the failed save may have left the original context unusable). If even this cannot be saved it is logged with its error class and the original error still reaches the caller.</summary>
    private async Task RecordFailureAsync(Guid historyId, Guid exportId, string auditEntity, Guid auditEntityId, Guid actor, Exception cause)
    {
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var other = scope.ServiceProvider.GetRequiredService<AlveraDbContext>();
            var finished = clock.UtcNow;
            await other.DataExportHistory.Where(h => h.Id == historyId && h.Result == DataExportResult.Started)
                .ExecuteUpdateAsync(s => s.SetProperty(h => h.Result, DataExportResult.Failed).SetProperty(h => h.FinishedAtUtc, finished), limit.Token);
            AuditService.Record(other, AuditEventTypes.DataExportFailed, auditEntity, auditEntityId, actor,
                $"Patient data package export failed (export {exportId}, error class {cause.GetType().Name}).", null, exportId);
            await other.SaveChangesAsync(limit.Token);
        }
        catch (Exception ex)
        {
            log.LogError("{Event} export={ExportId} error_class={ErrorClass} cause={Cause}", "data_export_failure_not_recorded", exportId, ex.GetType().Name, cause.GetType().Name);
        }
    }
}
