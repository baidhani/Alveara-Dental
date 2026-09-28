using System.Diagnostics;

namespace Alveara.Api.Architecture.Logging;

/// <summary>
/// N002-R03-02: an unhandled exception reaching the default hosting/diagnostics logging path can
/// carry raw database exception text — including, for a SQL Server unique-constraint violation,
/// the literal conflicting value — straight into ordinary logs. This middleware is the single
/// place every unhandled request-pipeline exception passes through: it logs only the exception's
/// type and a correlation id, never <see cref="Exception.Message"/> or <see cref="Exception.ToString"/>,
/// then returns a generic problem response. Registered first in the pipeline so no downstream
/// middleware's own exception logging (e.g. the built-in developer exception page) sees the raw
/// exception first.
/// </summary>
public sealed class SafeExceptionHandlingMiddleware(RequestDelegate next, ILogger<SafeExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            var correlationId = Activity.Current?.Id ?? context.TraceIdentifier;
            logger.LogError("Unhandled request exception: {ExceptionType} (correlation {CorrelationId})", ex.GetType().Name, correlationId);

            if (context.Response.HasStarted)
            {
                throw;
            }

            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new { error = "internal_error", correlationId });
        }
    }
}
