using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Patients.Portability.Package;

/// <summary>
/// ALV-018-C01: what every export does when it fails after its history row was saved: mark the row Failed and log a DataExportFailed entry (the export id and the error CLASS, never the message, which could hold a
/// value), on a fresh scope because the failed save may have left the original context unusable. It has its own 15 second limit so it cannot hang a request that is already failing. If even this cannot be saved it is
/// logged with its error class and the original error still reaches the caller; it never throws.
/// </summary>
public class ExportFailureRecorder(IServiceScopeFactory scopes, IPracticeClock clock, ILogger<ExportFailureRecorder> log)
{
    public async Task RecordAsync(Guid historyId, Guid exportId, string what, string auditEntity, Guid auditEntityId, Guid actor, Exception cause)
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
                $"{what} export failed (export {exportId}, error class {cause.GetType().Name}).", null, exportId);
            await other.SaveChangesAsync(limit.Token);
        }
        catch (Exception ex)
        {
            log.LogError("{Event} export={ExportId} error_class={ErrorClass} cause={Cause}", "data_export_failure_not_recorded", exportId, ex.GetType().Name, cause.GetType().Name);
        }
    }
}

/// <summary>The one rule for who can be exported: the patient must exist (404) and must not be merged away (409, the record it was merged into is the one to export).</summary>
public static class PatientExportSubject
{
    public static async Task RequireAsync(AlveraDbContext db, Guid patientId, CancellationToken ct)
    {
        var patient = await db.Patients.AsNoTracking().Select(p => new { p.Id, p.MergedAtUtc }).SingleOrDefaultAsync(p => p.Id == patientId, ct)
            ?? throw new PatientException("patient_not_found", "That patient was not found.", 404);
        if (patient.MergedAtUtc is not null)
            throw new PatientException("patient_merged", "That record was merged into another patient record and is no longer exported; export the record it was merged into.", 409);
    }
}
