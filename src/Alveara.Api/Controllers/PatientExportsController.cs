using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Patients;
using Alveara.Api.Architecture.Patients.Portability.Package;
using Alveara.Api.Architecture.Permissions;

namespace Alveara.Api.Controllers;

/// <summary>What a person may ask for when starting a package. Both parts are optional: no body means document files included and the practice's folder naming.</summary>
public sealed record PackageRequest(bool? IncludeDocumentFiles, string? FolderNaming);

/// <summary>
/// ALV-018-C01: taking patient data out of the system as a package. Three downloads, each its own permission and each a POST with a CSRF token (every export writes a history row and an audit entry, and a bulk one is
/// expensive, so a plain link must not be able to start one): one patient's package (<see cref="Permission.ExportPatientData"/>), every patient's (<see cref="Permission.ExportAllPatientData"/>) and one patient's
/// readable record alone (<see cref="Permission.ExportPatientReadableRecord"/>). The history list and the overview are open to anyone who holds one of the three; the history shows only the caller's own exports unless
/// the caller holds the bulk permission. Downloads are sent with <c>no-store</c> because they hold personal details, the temporary package is deleted when the response ends, and the headers
/// <c>X-Export-Id</c> and <c>X-Export-Result</c> let the screen warn about a Partial package without opening it. Failures return the stable <c>error</c> code the screens map to a message; nothing is sent when an export fails.
/// </summary>
[ApiController]
[Route("api/patient-exports")]
[Authorize]
public class PatientExportsController(
    PatientDataPackageService packages, PatientReadableRecordService readableRecords, ExportHistoryService history, IEffectivePermissions permissions, ILogger<PatientExportsController> log) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpPost("patients/{patientId:guid}/package")]
    [RequirePermission(Permission.ExportPatientData)]
    [RequireCsrfToken]
    public Task<IActionResult> OnePatientPackage(Guid patientId, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] PackageRequest? request, CancellationToken ct) =>
        DeliverPackageAsync(() => packages.ExportPatientAsync(patientId, Actor, ct, ChoicesOf(request)));

    [HttpPost("all-patients/package")]
    [RequirePermission(Permission.ExportAllPatientData)]
    [RequireCsrfToken]
    public Task<IActionResult> AllPatientsPackage([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] PackageRequest? request, CancellationToken ct) =>
        DeliverPackageAsync(() => packages.ExportAllAsync(Actor, ct, ChoicesOf(request)));

    [HttpPost("patients/{patientId:guid}/readable-record")]
    [RequirePermission(Permission.ExportPatientReadableRecord)]
    [RequireCsrfToken]
    public async Task<IActionResult> ReadableRecord(Guid patientId, CancellationToken ct)
    {
        try
        {
            var file = await readableRecords.ExportAsync(patientId, Actor, ct);
            AddDownloadHeaders(file.ExportId, DataExportResult.Complete);
            Response.Headers.ContentSecurityPolicy = "default-src 'none'; style-src 'unsafe-inline'; sandbox";          // a downloaded page is never given the application's origin or scripts
            return File(file.Content, "text/html; charset=utf-8", file.FileName);
        }
        catch (PatientException ex)
        {
            return StatusCode(ex.StatusCode, new { error = ex.Code, message = ex.Message });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ExportFailed(ex);
        }
    }

    [HttpGet("history")]
    [RequireAnyPermission(Permission.ExportPatientData, Permission.ExportAllPatientData, Permission.ExportPatientReadableRecord, RequiredText = nameof(Permission.ExportPatientData))]
    public async Task<IActionResult> History([FromQuery] int? limit, [FromQuery] DateTimeOffset? before, CancellationToken ct)
    {
        var take = limit ?? ExportHistoryService.DefaultLimit;
        if (take < 1 || take > ExportHistoryService.MaxLimit)
            return BadRequest(new { error = "invalid_limit", message = $"The limit must be between 1 and {ExportHistoryService.MaxLimit}." });
        var seeEveryone = Enum.TryParse<Role>(User.FindFirstValue(ClaimTypes.Role), out var role) && await permissions.CheckAsync(role, Permission.ExportAllPatientData, ct) == PermissionOutcome.Allowed;
        return Ok(await history.ListAsync(Actor, seeEveryone, take, before, ct));
    }

    [HttpGet("overview")]
    [RequireAnyPermission(Permission.ExportPatientData, Permission.ExportAllPatientData, Permission.ExportPatientReadableRecord, RequiredText = nameof(Permission.ExportPatientData))]
    public async Task<IActionResult> Overview(CancellationToken ct) => Ok(await history.OverviewAsync(ct));

    private static PackageChoices ChoicesOf(PackageRequest? request) => new(request?.IncludeDocumentFiles ?? true, request?.FolderNaming);

    private async Task<IActionResult> DeliverPackageAsync(Func<Task<DataPackageFile>> export)
    {
        try
        {
            var file = await export();
            Response.RegisterForDisposeAsync(file);                                                          // the temporary package is deleted when the response ends, sent or abandoned
            AddDownloadHeaders(file.ExportId, file.Result);
            Response.Headers["X-Export-Problems"] = file.ProblemCount.ToString();
            Response.Headers["X-Package-Sha256"] = file.Sha256;
            return File(file.Content, "application/zip", file.FileName);
        }
        catch (PatientException ex)
        {
            return StatusCode(ex.StatusCode, new { error = ex.Code, message = ex.Message });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ExportFailed(ex);
        }
    }

    private void AddDownloadHeaders(Guid exportId, string result)
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["X-Export-Id"] = exportId.ToString();
        Response.Headers["X-Export-Result"] = result;
    }

    /// <summary>An unexpected failure: the service has already marked the export Failed and logged it; the caller gets a stable code and no detail, and nothing was sent.</summary>
    private IActionResult ExportFailed(Exception ex)
    {
        log.LogError("{Event} error_class={ErrorClass}", "patient_export_request_failed", ex.GetType().Name);
        return StatusCode(500, new { error = "export_failed", message = "The export could not be completed. Nothing was delivered." });
    }
}
