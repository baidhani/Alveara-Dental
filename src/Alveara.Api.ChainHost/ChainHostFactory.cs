using Alveara.Api.Architecture.Time;
using Alveara.Api.TestSupport;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Alveara.Api.ChainHost;

/// <summary>
/// ALV-N030 (brief 17.2, 17.3): how one ChainHost process runs. <see cref="EnvironmentName"/> is the host environment (the runners pass <c>Test</c>);
/// <see cref="TestSupport"/> is the already-made decision on the test-only account route (<see cref="TestSupportRegistration.Evaluate"/>).
/// </summary>
public sealed record ChainHostSettings(string EnvironmentName, TestSupportDecision TestSupport)
{
    /// <summary>What the existing tests and an unconfigured start get: Development, no test support (the behaviour before ALV-N030).</summary>
    public static readonly ChainHostSettings Default = new("Development", new TestSupportDecision(false, false, "not requested"));
}

/// <summary>
/// The production application with the fewest differences a test run needs. Nothing in <c>Alveara.Api</c> is changed (PD-32); everything here is a registration
/// this host adds:
/// <list type="bullet">
/// <item><b>Clock:</b> with a <see cref="ChainPracticeClock"/> (<c>CHAIN_CLOCK_MODE=injected</c>) it replaces <see cref="IPracticeClock"/> and adds the clock-advance route
/// (off without the run's control token, ALV-N021 A2). With none (<c>real</c>) the product's own clock runs and there is no advance route. The clock-mode report is always there.</item>
/// <item><b>Test environment</b> (ALV-N030): the product relaxes the cookie's Secure flag only in Development; under <c>Test</c> this host serves plain HTTP on loopback,
/// so it sets <c>SameAsRequest</c> itself, and it applies the quiet log levels of the product's <c>appsettings.Development.json</c>.</item>
/// <item><b>Test support</b> (ALV-N030): the account-fixture route, only with an enabled <see cref="TestSupportDecision"/> (Test, opted in, loopback).</item>
/// </list>
/// </summary>
public sealed class ChainHostFactory(ChainPracticeClock? clock, ChainControlOptions? control = null, ChainHostSettings? settings = null) : WebApplicationFactory<Program>
{
    public ChainPracticeClock? Clock { get; } = clock;
    public ChainControlOptions Control { get; } = control ?? ChainControlOptions.Disabled;
    public ChainHostSettings Settings { get; } = settings ?? ChainHostSettings.Default;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Settings.EnvironmentName);
        var testEnvironment = Settings.EnvironmentName == TestSupportRegistration.EnvironmentName;
        if (testEnvironment)
        {
            builder.ConfigureLogging(logging =>
            {
                logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
                logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.None);
                logging.AddFilter("Microsoft.AspNetCore.Diagnostics", LogLevel.None);
            });
        }
        builder.ConfigureServices(services =>
        {
            if (Clock is not null)
            {
                services.RemoveAll<IPracticeClock>();
                services.AddSingleton<IPracticeClock>(Clock);
                services.AddSingleton(Clock);
                services.AddSingleton(Control);
                services.AddTransient<IStartupFilter, ClockAdvanceStartupFilter>();
            }
            services.AddTransient<IStartupFilter, ClockModeStartupFilter>();
            if (testEnvironment)
                services.PostConfigure<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme, o => o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest);
            if (Settings.TestSupport.Enabled) services.AddTestSupport(Settings.TestSupport);
        });
    }
}
