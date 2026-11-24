using System.Security.Claims;
using Alveara.Api.Architecture.Configuration;
using Alveara.Api.Architecture.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Alveara.Api.Controllers;

public record DesignatePracticeOwnerRequest(Guid StaffProfileId, bool Confirm);

/// <summary>ALV-N030 (brief section 4): read the Practice Owner, and designate the first one (only while none has ever been designated). ManageUsers.</summary>
[ApiController]
[Route("api/practice-owner")]
[Authorize]
[RequirePermission(Permission.ManageUsers)]
public class PracticeOwnerController(PracticeOwnerService owners) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct) => Ok(new { owner = await owners.CurrentAsync(ct) });

    [HttpPost("initial")]
    [RequireCsrfToken]
    public async Task<IActionResult> DesignateInitial([FromBody] DesignatePracticeOwnerRequest request, CancellationToken ct)
    {
        try
        {
            var owner = await owners.DesignateInitialAsync(request.StaffProfileId, request.Confirm, Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!), ct);
            return StatusCode(StatusCodes.Status201Created, owner);
        }
        catch (ConfigurationException ex)
        {
            return StatusCode(ex.StatusCode, new { error = ex.Code, message = ex.Message });
        }
    }
}
