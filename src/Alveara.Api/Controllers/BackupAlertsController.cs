using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Alveara.Api.Architecture.Backup;
using Alveara.Api.Architecture.Identity;

namespace Alveara.Api.Controllers;

/// <summary>
/// ALV-N026 (PD-25): the administrators' backup failure alerts - the ones not yet acknowledged (for the banner) and acknowledging one (audited, idempotent). Administrators only, by role.
/// Answers carry the alert's kind, time and delivery state only: no file path, message text or patient.
/// </summary>
[ApiController]
[Authorize(Roles = nameof(Role.Admin))]
public class BackupAlertsController(BackupAlertService alerts) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet("api/backup/alerts/unacknowledged")]
    public async Task<IActionResult> Unacknowledged(CancellationToken ct) => Ok(await alerts.UnacknowledgedFailuresAsync(ct));

    [HttpPost("api/backup/alerts/{alertId:guid}/acknowledge")]
    [RequireCsrfToken]
    public async Task<IActionResult> Acknowledge(Guid alertId, CancellationToken ct)
    {
        try
        {
            return Ok(await alerts.AcknowledgeAsync(alertId, Actor, ct));
        }
        catch (BackupException ex)
        {
            return StatusCode(ex.StatusCode, new { error = ex.Code, message = ex.Message });
        }
    }
}
