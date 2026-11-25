using Alveara.Api.Architecture.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Alveara.Api.Controllers;

/// <summary>The recovery page's request. The token travels in this JSON body only - never in a URL.</summary>
public record RecoverDeploymentAdministratorRequest(string? Token, string? Username, string? NewPassword);

/// <summary>
/// ALV-N030-C01 (brief 18.6): Deployment Administrator recovery with a console-issued token. Anonymous; rate-limited like first-run setup (SetupAttempts:
/// 5 per 5 minutes per address). Answered in Normal mode only: outside it the start-up mode middleware answers 503 for this route as for every other.
/// Every token failure and a wrong username answer the same 400 <c>recovery_failed</c>. Request bodies of this controller are never logged.
/// </summary>
[ApiController]
[Route("api/recovery")]
public class RecoveryController(DeploymentRecoveryService recovery) : ControllerBase
{
    [HttpPost("deployment-administrator")]
    [EnableRateLimiting(SetupController.RateLimitPolicy)]
    public async Task<IActionResult> Recover([FromBody] RecoverDeploymentAdministratorRequest? request, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        try
        {
            var (account, method) = await recovery.RecoverAsync(request?.Token, request?.Username, request?.NewPassword, ct);
            return Ok(new { username = account.Username, method = method.ToString() });
        }
        catch (RecoveryFailedException)
        {
            return BadRequest(new { error = "recovery_failed" });
        }
        catch (SetupInputInvalidException ex)
        {
            return BadRequest(new { error = ex.Code, minimumPasswordLength = DeploymentSetupService.MinimumPasswordLength });
        }
        catch (AdminGuardBusyException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "admin_guard_busy", message = "Another administrator change is in progress. Try again." });
        }
    }
}
