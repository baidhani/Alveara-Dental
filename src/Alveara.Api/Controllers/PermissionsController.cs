using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Permissions;

namespace Alveara.Api.Controllers;

public record RemoveInvalidRowBody(string? Reason, string? RowVersion);

/// <summary>
/// ALV-N017: reading the effective permission policy and keeping it healthy. <c>GET effective</c> (<see cref="Permission.ViewPermissionMatrix"/>) lists every role's effective permissions with where each cell comes from
/// (default, granted, revoked), who changed it and why, whether it can be granted or delegated, and the health of the stored rows; <c>GET history</c> (<see cref="Permission.ViewAuditLog"/>) is the immutable change
/// history; <c>GET health</c> and <c>POST invalid-rows/{id}/remove</c> need <see cref="Permission.ManagePermissionGrants"/>, which only an administrator can ever hold. The one write here removes a stored row
/// that is invalid (or names a retired permission) with a written reason; changing the policy itself is not part of this story. Refusals carry a stable <c>error</c>: <c>reason_required</c>,
/// <c>row_version_required</c> (400), <c>row_not_found</c> (404), <c>row_not_removable</c>, <c>row_changed</c> (409).
/// </summary>
[ApiController]
[Authorize]
[Route("api/permissions")]
public class PermissionsController(PolicyAdminService policy) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet("effective")]
    [RequirePermission(Permission.ViewPermissionMatrix)]
    public async Task<IActionResult> Effective(CancellationToken ct) => Ok(await policy.EffectiveAsync(ct));

    [HttpGet("history")]
    [RequirePermission(Permission.ViewAuditLog)]
    public async Task<IActionResult> History([FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct) => Ok(await policy.HistoryAsync(page, pageSize, ct));

    [HttpGet("health")]
    [RequirePermission(Permission.ManagePermissionGrants)]
    public async Task<IActionResult> Health(CancellationToken ct) => Ok(await policy.HealthAsync(ct));

    [HttpPost("invalid-rows/{id:guid}/remove")]
    [RequirePermission(Permission.ManagePermissionGrants)]
    [RequireCsrfToken]
    public async Task<IActionResult> RemoveInvalidRow(Guid id, [FromBody] RemoveInvalidRowBody body, CancellationToken ct)
    {
        try
        {
            return Ok(await policy.RemoveRowAsync(Actor, id, body.Reason, body.RowVersion, ct));
        }
        catch (PolicyAdminException ex)
        {
            return StatusCode(ex.StatusCode, new { error = ex.Code, message = ex.Message });
        }
    }
}
