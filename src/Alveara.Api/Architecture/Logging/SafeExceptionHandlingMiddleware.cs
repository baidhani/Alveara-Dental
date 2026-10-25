using System.Diagnostics;
using Microsoft.Data.SqlClient;

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
    /// <summary>Error 51235 anywhere in the exception chain.</summary>
    internal static bool IsMergedPatientRefusal(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
            if (e is SqlException { Number: 51235 }) return true;
        return false;
    }

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

            // ALV-017-C01: the database refused a new record for a patient whose record was merged into another (error 51235). Say so plainly; the database's own text is never sent.
            if (IsMergedPatientRefusal(ex))
            {
                context.Response.Clear();
                context.Response.StatusCode = StatusCodes.Status409Conflict;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsJsonAsync(new { error = "patient_merged", message = "This patient record was merged into another record and takes no new records. Open the record it was merged into.", correlationId });
                return;
            }

            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new { error = "internal_error", correlationId });
        }
    }
}
