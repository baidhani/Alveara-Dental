using Alveara.Api.Architecture.Time;
using Microsoft.AspNetCore.Builder;

namespace Alveara.Api.ChainHost;

/// <summary>
/// The one route the chain host adds to the production application: <c>GET /api/chain/clock-mode</c>. It reports which clock the running host really uses, read from the service container, so the evidence
/// states a measured mode and not an assumed one. The production application serves no such report (an unknown route gets its client page or a 404).
/// </summary>
public sealed class ClockModeStartupFilter : IStartupFilter
{
    public const string Route = "/api/chain/clock-mode";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use(async (context, pipeline) =>
        {
            if (context.Request.Method == HttpMethods.Get && context.Request.Path == Route)
            {
                var clock = context.RequestServices.GetRequiredService<IPracticeClock>();
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsJsonAsync(new ClockModeReport(clock is ChainPracticeClock ? "injected" : "real", clock.GetType().Name, clock.UtcNow));
                return;
            }
            await pipeline();
        });
        next(app);
    };
}

public sealed record ClockModeReport(string Mode, string ClockType, DateTimeOffset UtcNow);
