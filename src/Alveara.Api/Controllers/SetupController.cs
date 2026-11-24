using Alveara.Api.Architecture.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Alveara.Api.Controllers;

/// <summary>The setup page's request. The token travels in this JSON body only - never in a URL.</summary>
public record CompleteSetupRequest(string? Token, string? Username, string? Password);

/// <summary>
/// ALV-N030 (brief section 4): first-run setup. Anonymous; rate-limited by caller address (AuthAttempts) and by the stricter SetupAttempts window.
/// Every token failure answers the same 400 <c>setup_failed</c>; username and password are checked only after the token verifies. Nothing about
/// the installation beyond "is setup available" is disclosed. Request bodies of this controller are never logged.
/// </summary>
[ApiController]
[Route("api/setup")]
public class SetupController(DeploymentSetupService setup) : ControllerBase
{
    public const string RateLimitPolicy = "SetupAttempts";

    [HttpGet("status")]
    [EnableRateLimiting("AuthAttempts")]
    public async Task<IActionResult> Status(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        return Ok(new { setupAvailable = await setup.SetupAvailableAsync(ct) });
    }

    [HttpPost("deployment-administrator")]
    [EnableRateLimiting(RateLimitPolicy)]
    public async Task<IActionResult> CreateDeploymentAdministrator([FromBody] CompleteSetupRequest? request, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        try
        {
            var account = await setup.CompleteAsync(request?.Token, request?.Username, request?.Password, ct);
            return StatusCode(StatusCodes.Status201Created, new { id = account.Id, username = account.Username });
        }
        catch (SetupFailedException)
        {
            return BadRequest(new { error = "setup_failed" });
        }
        catch (SetupInputInvalidException ex)
        {
            return BadRequest(new { error = ex.Code, minimumPasswordLength = DeploymentSetupService.MinimumPasswordLength });
        }
    }
}
