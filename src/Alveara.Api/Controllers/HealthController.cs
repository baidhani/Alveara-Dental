using Microsoft.AspNetCore.Mvc;

namespace Alveara.Api.Controllers;

/// <summary>
/// Liveness check consumed by the client shell's disconnected-state detection
/// (see alveara-client/src/hooks/useConnectionStatus.ts). Deliberately reports
/// only process liveness, not database/dependency health, so the shell's
/// "local server unavailable" banner reflects reality rather than a deeper
/// dependency failure this story does not yet own.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new { status = "ok" });
}
