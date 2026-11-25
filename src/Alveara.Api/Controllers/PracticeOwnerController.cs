using System.Security.Claims;
using Alveara.Api.Architecture.Configuration;
using Alveara.Api.Architecture.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Alveara.Api.Controllers;

public record DesignatePracticeOwnerRequest(Guid StaffProfileId, bool Confirm);
public record TransferPracticeOwnerRequest(Guid ExpectedDesignationId, Guid NewOwnerStaffProfileId, string? Reason, bool Confirm, string? CurrentPassword,
    string? MfaCode, bool DisableFormerOwner);

/// <summary>ALV-N030 (brief section 4): read the Practice Owner, and designate the first one (only while none has ever been designated). ManageUsers.</summary>
[ApiController]
[Route("api/practice-owner")]
[Authorize]
[RequirePermission(Permission.ManageUsers)]
public class PracticeOwnerController(PracticeOwnerService owners, PracticeOwnerTransferService transfers) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct) => Ok(new { owner = await owners.CurrentAsync(ct) });

    /// <summary>
    /// ALV-N030-C01 (brief 18.5): the Deployment Administrator transfers the designation. Body fields are never logged (they hold the password and the code).
    /// 200 with the new designation; refusals use the stable codes of <see cref="PracticeOwnerTransferService"/>.
    /// </summary>
    [HttpPost("transfer")]
    [RequireCsrfToken]
    public async Task<IActionResult> Transfer([FromBody] TransferPracticeOwnerRequest request, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        try
        {
            var r = await transfers.TransferAsync(new PracticeOwnerTransferRequest(request.ExpectedDesignationId, request.NewOwnerStaffProfileId, request.Reason, request.Confirm,
                request.CurrentPassword, request.MfaCode, request.DisableFormerOwner), idempotencyKey, Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!), ct);
            return Ok(new { designationId = r.DesignationId, staffProfileId = r.StaffProfileId, designatedAtUtc = r.DesignatedAtUtc, formerOwnerDisabled = r.FormerOwnerDisabled,
                replayed = r.Replayed });
        }
        catch (ConfigurationException ex)
        {
            return StatusCode(ex.StatusCode, new { error = ex.Code, message = ex.Message });
        }
    }

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
