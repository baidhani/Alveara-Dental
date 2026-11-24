using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Startup;
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
public class SetupController(DeploymentSetupService setup, StartupModeState startup, ISetupRestart restart) : ControllerBase
{
    public const string RateLimitPolicy = "SetupAttempts";

    /// <summary>
    /// ALV-N030 (brief 16.2a): mapped in every mode. The whole body is one of exactly four, decided by this process's start-up mode, so the client can show
    /// the right screen before any sign-in: 503 <c>{mode:"installation_incomplete"}</c>, 503 <c>{mode:"upgrade_required"}</c>,
    /// 200 <c>{mode:"setup", setupAvailable:true}</c> or 200 <c>{mode:"normal", setupAvailable:false}</c>. Nothing else is ever in it.
    /// </summary>
    [HttpGet("status")]
    [EnableRateLimiting("AuthAttempts")]
    public IActionResult Status()
    {
        Response.Headers.CacheControl = "no-store";
        return startup.Mode switch
        {
            StartupMode.NotInstalled => StatusCode(StatusCodes.Status503ServiceUnavailable, new { mode = "installation_incomplete" }),
            StartupMode.UpgradeRequired => StatusCode(StatusCodes.Status503ServiceUnavailable, new { mode = "upgrade_required" }),
            StartupMode.Setup => Ok(new { mode = "setup", setupAvailable = true }),
            _ => Ok(new { mode = "normal", setupAvailable = false }),
        };
    }

    [HttpPost("deployment-administrator")]
    [EnableRateLimiting(RateLimitPolicy)]
    public async Task<IActionResult> CreateDeploymentAdministrator([FromBody] CompleteSetupRequest? request, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        try
        {
            var account = await setup.CompleteAsync(request?.Token, request?.Username, request?.Password, ct);
            // ALV-N030 (brief 16.2b): only after the 201 has been sent does the process ask to be restarted (exit 75); a failed setup never gets here
            Response.OnCompleted(() => { restart.RequestRestart(); return Task.CompletedTask; });
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
