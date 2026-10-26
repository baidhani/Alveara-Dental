using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Patients;
using Alveara.Api.Architecture.Patients.Portability;

namespace Alveara.Api.Controllers;

/// <summary>
/// STORY-018: taking one patient's record out of the system as a file (and, in a later step, bringing one in). The export needs <see cref="Permission.ExportPatientData"/> (the administrator by default); the file is
/// sent as a download that no cache may keep, because it holds personal details. Failures return the stable <c>error</c> code the screens map to a message.
/// </summary>
[ApiController]
[Route("api/patients")]
[Authorize]
public class PatientPortabilityController(PatientExportService exports) : ControllerBase
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
}
