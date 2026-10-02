using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Alveara.Api.Architecture.Identity;

/// <summary>
/// Like <see cref="RequirePermissionAttribute"/>, but the caller needs ANY ONE of the listed permissions (ALV-011-C01: a visit's provider/operatory may be
/// reassigned by the front office or by the chairside team). Same responses: 401 without a role claim, 403 <c>permission_denied</c> otherwise, with
/// <c>required</c> naming the permissions that would have allowed it.
/// </summary>
public class RequireAnyPermissionAttribute(params Permission[] permissions) : Attribute, IAsyncActionFilter
{
    public Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var roleClaim = context.HttpContext.User.FindFirstValue(ClaimTypes.Role);
        if (roleClaim is null || !Enum.TryParse<Role>(roleClaim, out var role))
        {
            context.Result = new UnauthorizedResult();
            return Task.CompletedTask;
        }

        if (!permissions.Any(p => PermissionMatrix.RoleHas(role, p)))
        {
            context.Result = new ObjectResult(new { error = "permission_denied", required = string.Join(" or ", permissions) })
            {
                StatusCode = StatusCodes.Status403Forbidden,
            };
            return Task.CompletedTask;
        }

        return next();
    }
}
