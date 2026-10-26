using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Patients;
using Alveara.Api.Architecture.Patients.Portability;

namespace Alveara.Api.Controllers;

/// <summary>
/// STORY-018: taking one patient's record out of the system as a file, and bringing one in from a file. The export needs <see cref="Permission.ExportPatientData"/> (the administrator by default); the file is
/// sent as a download that no cache may keep, because it holds personal details. The import needs <see cref="Permission.ImportPatientData"/> (the administrator and the office manager by default) and a CSRF token;
/// it takes the file itself as the request body. Failures return the stable <c>error</c> code the screens map to a message; a rejected file also lists every problem to correct.
/// </summary>
[ApiController]
[Route("api/patients")]
[Authorize]
public class PatientPortabilityController(PatientExportService exports, PatientImportService imports) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet("{id:guid}/export")]
    [RequirePermission(Permission.ExportPatientData)]
    public async Task<IActionResult> Export(Guid id, CancellationToken ct)
    {
        try
        {
            var file = await exports.ExportAsync(id, Actor, ct);
            Response.Headers.CacheControl = "no-store";
            return File(file.Content, "application/json", file.FileName);
        }
        catch (PatientException ex)
        {
            return StatusCode(ex.StatusCode, new { error = ex.Code, message = ex.Message });
        }
    }

    /// <summary>Brings one patient in from an export file sent as the request body. The file is read up to one byte past the limit, so an oversized file is rejected without reading it all.</summary>
    [HttpPost("import")]
    [RequirePermission(Permission.ImportPatientData)]
    [RequireCsrfToken]
    public async Task<IActionResult> Import(CancellationToken ct)
    {
        try
        {
            var content = await ReadBodyAsync(ct);
            var result = await imports.ImportAsync(content, Actor, ct);
            var body = new { patientId = result.PatientId, created = result.Created, exportId = result.ExportId };
            return result.Created ? Created($"/api/patients/{result.PatientId}", body) : Ok(body);
        }
        catch (PatientImportRejectedException ex)
        {
            return BadRequest(new { error = "import_rejected", message = ex.Message, problems = ex.Problems });
        }
        catch (PatientException ex)
        {
            return StatusCode(ex.StatusCode, new { error = ex.Code, message = ex.Message, existingPatientId = ex.ExistingPatientId, candidates = ex.Candidates });
        }
    }

    private async Task<byte[]> ReadBodyAsync(CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while (buffer.Length <= PatientExportFormat.MaxFileBytes && (read = await Request.Body.ReadAsync(chunk, ct)) > 0) buffer.Write(chunk, 0, read);
        return buffer.ToArray();
    }
}

