using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Alveara.Api.Architecture.Identity;

/// <summary>
/// ALV-001-C01: "Cookie-authenticated state-changing operations are protected against CSRF."
/// Applied to [Authorize]-protected state-changing actions (never to login/register/bootstrap,
/// which aren't relying on an existing cookie session for authorization). Validates the
/// double-submit token via ASP.NET Core's built-in antiforgery service; a missing or invalid
/// token returns 400, never a silent pass-through.
/// </summary>
public class RequireCsrfTokenAttribute : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var antiforgery = context.HttpContext.RequestServices.GetRequiredService<IAntiforgery>();
        try
        {
            await antiforgery.ValidateRequestAsync(context.HttpContext);
        }
        catch (AntiforgeryValidationException)
        {
            context.Result = new BadRequestObjectResult(new { error = "csrf_token_invalid", message = "Missing or invalid CSRF token." });
            return;
        }

        await next();
    }
}
