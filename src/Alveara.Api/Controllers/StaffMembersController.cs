using System.Security.Claims;
using Alveara.Api.Architecture.Configuration;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Alveara.Api.Controllers;

public record AddStaffMemberRequest(string? DisplayName, string? JobTitle, string? Username, string? Role, bool? PracticeOwner = null);
public record LegacyLoginRequest(string? Username, string? Role);

/// <summary>
/// ALV-N030 (brief section 4, R01 CR-1): "Add staff member" (profile and login together) and the legacy-remediation action "Create a login for this legacy
/// staff record". Both need ManagePracticeConfiguration (the profile) AND ManageUsers (the login), the CSRF token and an Idempotency-Key. The temporary
/// password is in the 201 body once and nowhere else; a replay answers 200 <c>already_created</c> without it. Bodies of these routes are never logged.
/// </summary>
[ApiController]
[Route("api/config")]
[Authorize]
public class StaffMembersController(StaffMemberService staffMembers, IEffectivePermissions permissions) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpPost("staff-members")]
    [RequirePermission(Permission.ManagePracticeConfiguration)]
    [RequireCsrfToken]
    public Task<IActionResult> Add([FromBody] AddStaffMemberRequest? request, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, CancellationToken ct) =>
        RunAsync(() => staffMembers.CreateAsync(request?.DisplayName, request?.JobTitle, request?.Username, request?.Role, idempotencyKey, Actor, ct, request?.PracticeOwner ?? false), ct);

    [HttpPost("staff/{id:guid}/legacy-login")]
    [RequirePermission(Permission.ManagePracticeConfiguration)]
    [RequireCsrfToken]
    public Task<IActionResult> LegacyLogin(Guid id, [FromBody] LegacyLoginRequest? request, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, CancellationToken ct) =>
        RunAsync(() => staffMembers.CreateLegacyLoginAsync(id, request?.Username, request?.Role, idempotencyKey, Actor, ct), ct);

    private async Task<IActionResult> RunAsync(Func<Task<StaffMemberResult>> action, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        if (!Enum.TryParse<Role>(User.FindFirstValue(ClaimTypes.Role), out var role)) return Unauthorized();
        if (PermissionResults.Refusal(await permissions.CheckAsync(role, Permission.ManageUsers, ct), nameof(Permission.ManageUsers)) is { } refused) return refused;
        try
        {
            var r = await action();
            return r.Replayed
                ? Ok(new { status = "already_created", staffProfileId = r.StaffProfileId, userAccountId = r.UserAccountId, username = r.Username, role = r.Role, practiceOwner = r.PracticeOwner })
                : StatusCode(StatusCodes.Status201Created, new { staffProfileId = r.StaffProfileId, userAccountId = r.UserAccountId, username = r.Username, role = r.Role, practiceOwner = r.PracticeOwner, temporaryPassword = r.TemporaryPassword });
        }
        catch (ConfigurationException ex)
        {
            return StatusCode(ex.StatusCode, new { error = ex.Code, message = ex.Message });
        }
    }
}
