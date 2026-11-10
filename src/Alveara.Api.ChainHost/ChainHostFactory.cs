using Alveara.Api.Architecture.Time;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Alveara.Api.ChainHost;

/// <summary>
/// The production application with exactly one difference: <see cref="IPracticeClock"/> is the injected <see cref="ChainPracticeClock"/> (and the one clock-mode route is added). Nothing else is
/// replaced, so the chain exercises the real services, database access, authentication and background jobs.
/// </summary>
public sealed class ChainHostFactory(ChainPracticeClock clock) : WebApplicationFactory<Program>
{
    public ChainPracticeClock Clock { get; } = clock;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IPracticeClock>();
            services.AddSingleton<IPracticeClock>(Clock);
            services.AddTransient<IStartupFilter, ClockModeStartupFilter>();
        });
    }
}
