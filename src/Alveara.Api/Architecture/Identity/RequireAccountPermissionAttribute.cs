using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Alveara.Api.Architecture.Identity;

/// <summary>
/// ALV-N020-C01: the enforcement point for an <see cref="AccountPermission"/>. Checks the signed-in ACCOUNT (its id claim), never the role: an Admin without the
/// grant is refused like anyone else. Applied alongside [Authorize]; a missing account claim is 401, a caller without the grant 403 with the same body shape
/// as <see cref="RequirePermissionAttribute"/> refusals.
/// </summary>
public class RequireAccountPermissionAttribute(AccountPermission permission) : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (!Guid.TryParse(context.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier), out var accountId))
        {
            context.Result = new UnauthorizedResult();
            return;
        }
        var grants = context.HttpContext.RequestServices.GetRequiredService<AccountPermissionService>();
        if (!await grants.HasAsync(accountId, permission, context.HttpContext.RequestAborted))
        {
            context.Result = new ObjectResult(new { error = "permission_denied", required = permission.ToString() }) { StatusCode = StatusCodes.Status403Forbidden };
            return;
        }
        await next();
    }
}
