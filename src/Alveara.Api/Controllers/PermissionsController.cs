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
public class PermissionsController(PolicyAdminService policy, PolicyWriteService writes) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private async Task<IActionResult> Run(Func<Task<object>> action)
    {
        try
        {
            return Ok(await action());
        }
        catch (PolicyAdminException ex)
        {
            return StatusCode(ex.StatusCode, new { error = ex.Code, message = ex.Message });
        }
    }

    // ---------- ALV-N018: the writes ----------

    /// <summary>Grant, revoke or revert one non-protected cell of a working role (administrator only). Needs <c>Idempotency-Key</c>, the policy version the caller was shown, a reason and a confirmation (typed for the high-impact list).</summary>
    [HttpPut("overrides")]
    [RequirePermission(Permission.ManagePermissionGrants)]
    [RequireCsrfToken]
    public Task<IActionResult> SetOverride([FromBody] SetOverrideRequest request, [FromHeader(Name = "Idempotency-Key")] string? key, CancellationToken ct) =>
        Run(async () => await writes.SetOverrideAsync(Actor, request, key, ct));

    /// <summary>A delegate (today the office manager) grants or revokes a delegated permission for a delegated role. No permission attribute: the delegation table and the delegate's own permissions decide, and a refusal is <c>delegation_not_allowed</c>.</summary>
    [HttpPost("delegated-grants")]
    [RequireCsrfToken]
    public Task<IActionResult> DelegatedGrant([FromBody] DelegatedGrantRequest request, [FromHeader(Name = "Idempotency-Key")] string? key, CancellationToken ct) =>
        Run(async () =>
        {
            if (!Enum.TryParse<Role>(User.FindFirstValue(ClaimTypes.Role), out var role)) throw new PolicyAdminException("delegation_not_allowed", "You may not change that permission for that role.", 403);
            return await writes.DelegatedGrantAsync(Actor, role, request, key, ct);
        });

    /// <summary>What the caller may delegate and where each delegated cell stands (empty for a role that holds no delegation).</summary>
    [HttpGet("my-delegations")]
    public Task<IActionResult> MyDelegations(CancellationToken ct) =>
        Run(async () => Enum.TryParse<Role>(User.FindFirstValue(ClaimTypes.Role), out var role) ? await writes.MyDelegationsAsync(role, ct) : new { policyVersion = 0L, delegations = Array.Empty<object>() });

    [HttpGet("delegations")]
    [RequirePermission(Permission.ManagePermissionGrants)]
    public Task<IActionResult> Delegations(CancellationToken ct) => Run(async () => await writes.DelegationsAsync(ct));

    [HttpPut("delegations")]
    [RequirePermission(Permission.ManagePermissionGrants)]
    [RequireCsrfToken]
    public Task<IActionResult> SetDelegations([FromBody] SetDelegationsRequest request, [FromHeader(Name = "Idempotency-Key")] string? key, CancellationToken ct) =>
        Run(async () => await writes.SetDelegationsAsync(Actor, request, key, ct));

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
