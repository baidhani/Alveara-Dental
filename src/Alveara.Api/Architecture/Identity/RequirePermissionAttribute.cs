using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Alveara.Api.Architecture.Permissions;

namespace Alveara.Api.Architecture.Identity;

/// <summary>
/// ALV-001-C01's permission enforcement point. Reads the caller's role claim (already
/// verified authentic by cookie-auth + the SecurityStamp check in Program.cs) and checks it
/// against <see cref="PermissionMatrix"/> - this is what makes the matrix an actual authorization
/// mechanism rather than documentation. Applied alongside (not instead of) [Authorize]: this
/// attribute assumes the caller is already authenticated and only decides "having gotten in, do
/// they hold this specific permission." A missing role claim (shouldn't happen once [Authorize]
/// has run, but checked defensively) is 401; an authenticated caller whose role lacks the
/// permission is 403 - the same status a caller sees today from [Authorize(Roles=...)], so this
/// is a drop-in replacement, not a behavior change for existing callers.
/// </summary>
public class RequirePermissionAttribute(Permission permission) : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var roleClaim = context.HttpContext.User.FindFirstValue(ClaimTypes.Role);
        if (roleClaim is null || !Enum.TryParse<Role>(roleClaim, out var role))
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        // ALV-N017: what a role holds is decided by the one effective-permission seam (code defaults plus stored policy), not by the static table.
        var permissions = context.HttpContext.RequestServices.GetRequiredService<IEffectivePermissions>();
        var refusal = PermissionResults.Refusal(await permissions.CheckAsync(role, permission, context.HttpContext.RequestAborted), permission.ToString());
        if (refusal is not null)
        {
            context.Result = refusal;
            return;
        }

        await next();
    }
}
