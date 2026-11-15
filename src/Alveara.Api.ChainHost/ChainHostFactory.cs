using Alveara.Api.Architecture.Time;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Alveara.Api.ChainHost;

/// <summary>
/// The production application with exactly one difference: <see cref="IPracticeClock"/> is the injected <see cref="ChainPracticeClock"/> (and the clock-mode report is added). Nothing else is
/// replaced, so the chain exercises the real services, database access, authentication and background jobs. ALV-N021 A2 adds the clock-advance route (<see cref="ClockAdvanceStartupFilter"/>),
/// which is OFF unless the chain run passes its control token in <paramref name="control"/>.
/// </summary>
public sealed class ChainHostFactory(ChainPracticeClock clock, ChainControlOptions? control = null) : WebApplicationFactory<Program>
{
    public ChainPracticeClock Clock { get; } = clock;
    public ChainControlOptions Control { get; } = control ?? ChainControlOptions.Disabled;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IPracticeClock>();
            services.AddSingleton<IPracticeClock>(Clock);
            services.AddSingleton(Clock);
            services.AddSingleton(Control);
            services.AddTransient<IStartupFilter, ClockModeStartupFilter>();
            services.AddTransient<IStartupFilter, ClockAdvanceStartupFilter>();
        });
    }
}
