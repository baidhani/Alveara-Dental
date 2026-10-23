using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Alveara.Api.Architecture.Permissions;

namespace Alveara.Api.Architecture.Identity;

/// <summary>
/// Like <see cref="RequirePermissionAttribute"/>, but the caller needs ANY ONE of the listed permissions (ALV-011-C01: a visit's provider/operatory may be
/// reassigned by the front office or by the chairside team). Same responses: 401 without a role claim, 403 <c>permission_denied</c> otherwise, with
/// <c>required</c> naming the permissions that would have allowed it.
/// </summary>
public class RequireAnyPermissionAttribute(params Permission[] permissions) : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var roleClaim = context.HttpContext.User.FindFirstValue(ClaimTypes.Role);
        if (roleClaim is null || !Enum.TryParse<Role>(roleClaim, out var role))
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        // ALV-N017: decided by the one effective-permission seam, like <see cref="RequirePermissionAttribute"/>.
        var effective = context.HttpContext.RequestServices.GetRequiredService<IEffectivePermissions>();
        var refusal = PermissionResults.Refusal(await effective.CheckAnyAsync(role, permissions, context.HttpContext.RequestAborted), string.Join(" or ", permissions));
        if (refusal is not null)
        {
            context.Result = refusal;
            return;
        }

        await next();
    }
}
