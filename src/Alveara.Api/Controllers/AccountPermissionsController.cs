using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Alveara.Api.Architecture.Identity;

namespace Alveara.Api.Controllers;

public record SetPresentationAdministratorRequest(bool? Granted);

/// <summary>
/// ALV-N020-C01 (review BRIEFING-R03 Q-3): the ordinary, audited path to grant or revoke <see cref="AccountPermission.ManageLoginPresentation"/> for a named
/// account. Only a ManageUsers holder, with a CSRF token on the change. Granting to a disabled or Unassigned account is refused (409); repeating a grant or a
/// revoke changes nothing.
/// </summary>
[ApiController]
[Authorize]
[Route("api/auth/{id:guid}/presentation-administrator")]
public class AccountPermissionsController(AccountPermissionService grants) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    [RequirePermission(Permission.ManageUsers)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) =>
        Ok(new { granted = await grants.HasAsync(id, AccountPermission.ManageLoginPresentation, ct) });

    [HttpPut]
    [RequirePermission(Permission.ManageUsers)]
    [RequireCsrfToken]
    public async Task<IActionResult> Set(Guid id, [FromBody] SetPresentationAdministratorRequest request, CancellationToken ct)
    {
        if (request.Granted is not { } granted)
            return BadRequest(new { error = "granted_required", message = "Say whether the account is the presentation administrator." });
        try
        {
            var changed = granted
                ? await grants.GrantAsync(id, AccountPermission.ManageLoginPresentation, Actor, ct)
                : await grants.RevokeAsync(id, AccountPermission.ManageLoginPresentation, Actor, ct);
            return Ok(new { granted, changed });
        }
        catch (AccountPermissionException ex)
        {
            return StatusCode(ex.StatusCode, new { error = ex.Code, message = ex.Message });
        }
    }
}
