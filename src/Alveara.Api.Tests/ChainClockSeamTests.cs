using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Alveara.Api.Architecture.Time;
using Alveara.Api.ChainHost;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N019 R02 (review correction 01, decisions O-1 and PD-32): the chain clock seam, against a real SQL Server because the host starts its background jobs. The production host, built the way the
/// product starts it, registers the real <see cref="PracticeClock"/> and serves no chain report (the product answers an unknown route with its client page or a 404, never JSON). The chain host, which runs the same application, uses only its injected <see cref="ChainPracticeClock"/>: it is the one
/// <see cref="IPracticeClock"/> registered, its "now" is the injected instant, and the one added route reports the mode the host really has. The injected clock itself keeps a fixed "now", moves only forward, and leaves
/// every time-zone conversion to the real practice clock.
/// </summary>
public class ChainClockSeamTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Start = new(2026, 1, 15, 15, 0, 0, TimeSpan.Zero);
    private static TimeZoneInfo Chicago => TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");
    private readonly TestDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();
    public Task DisposeAsync() => _fixture.DisposeAsync();

    private void Configure(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Alveara", _fixture.ConnectionString);
        builder.UseSetting("AdminBootstrapSecret", IdentityTestHelpers.TestBootstrapSecret);
    }

    // ---------- the production host ----------

    [Fact]
    public async Task The_production_host_registers_the_real_practice_clock_and_serves_no_chain_report()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(Configure);

        var clock = factory.Services.GetRequiredService<IPracticeClock>();
        Assert.IsType<PracticeClock>(clock);
        Assert.IsNotType<ChainPracticeClock>(clock);
        Assert.Single(factory.Services.GetServices<IPracticeClock>());
        var response = await factory.CreateClient().GetAsync(ClockModeStartupFilter.Route);
        // production answers an unknown GET with its client page (200 HTML) or a 404; what it never does is return the chain's JSON report
        Assert.NotEqual("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain("\"mode\"", await response.Content.ReadAsStringAsync());
    }

    // ---------- the chain host ----------

    [Fact]
    public async Task The_chain_host_uses_only_its_injected_clock_and_reports_the_mode_it_really_has()
    {
        var injected = new ChainPracticeClock(Start, Chicago);
        await using var factory = new ChainHostFactory(injected).WithWebHostBuilder(Configure);

        Assert.Same(injected, factory.Services.GetRequiredService<IPracticeClock>());
        Assert.Single(factory.Services.GetServices<IPracticeClock>());                              // the real clock registration was replaced, not added to
        var report = await factory.CreateClient().GetFromJsonAsync<ClockModeReport>(ClockModeStartupFilter.Route);
        Assert.Equal(("injected", nameof(ChainPracticeClock), Start), (report!.Mode, report.ClockType, report.UtcNow));

        injected.Advance(TimeSpan.FromHours(2));
        var later = await factory.CreateClient().GetFromJsonAsync<ClockModeReport>(ClockModeStartupFilter.Route);
        Assert.Equal(Start.AddHours(2), later!.UtcNow);
    }

    [Fact]
    public async Task The_chain_host_still_serves_the_real_application()
    {
        await using var factory = new ChainHostFactory(new ChainPracticeClock(Start, Chicago)).WithWebHostBuilder(Configure);

        var health = await factory.CreateClient().GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }

    // ---------- the injected clock ----------

    [Fact]
    public void The_injected_clock_keeps_a_fixed_now_that_only_advance_moves_and_never_runs_backwards()
    {
        var clock = new ChainPracticeClock(Start, Chicago);

        Assert.Equal((Start, Start), (clock.UtcNow, clock.UtcNow));                                  // the same instant on every read
        clock.Advance(TimeSpan.FromMinutes(90));
        Assert.Equal(Start.AddMinutes(90), clock.UtcNow);
        clock.Advance(TimeSpan.Zero);
        Assert.Equal(Start.AddMinutes(90), clock.UtcNow);
        Assert.Throws<ArgumentOutOfRangeException>(() => clock.Advance(TimeSpan.FromTicks(-1)));
        Assert.Equal(Start.AddMinutes(90), clock.UtcNow);                                            // a refused step changes nothing
    }

    [Fact]
    public void The_injected_clock_leaves_every_time_zone_conversion_to_the_real_practice_clock()
    {
        var injected = new ChainPracticeClock(Start, Chicago);
        var real = new PracticeClock(Chicago);

        Assert.Equal(Chicago, injected.PracticeTimeZone);
        Assert.Equal(real.ToPracticeLocal(Start), injected.ToPracticeLocal(Start));
        Assert.Equal(new DateTimeOffset(2026, 1, 15, 9, 0, 0, TimeSpan.FromHours(-6)), injected.ToPracticeLocal(Start));       // winter: UTC-6
        var local = new DateTime(2026, 7, 1, 10, 30, 0);
        Assert.Equal(real.FromPracticeLocal(local), injected.FromPracticeLocal(local));
        Assert.Throws<LocalTimeConversionException>(() => injected.FromPracticeLocal(new DateTime(2026, 3, 8, 2, 30, 0)));     // the spring-forward gap is still refused
    }

    [Fact]
    public async Task Reading_the_clock_from_many_threads_while_advancing_it_never_goes_backwards()
    {
        var clock = new ChainPracticeClock(Start, Chicago);
        var writer = Task.Run(() => { for (var i = 0; i < 1000; i++) clock.Advance(TimeSpan.FromSeconds(1)); });
        var readers = Enumerable.Range(0, 4).Select(_ => Task.Run(() => { var last = Start; for (var i = 0; i < 5000; i++) { var now = clock.UtcNow; Assert.True(now >= last); last = now; } })).ToArray();

        await Task.WhenAll(readers.Append(writer));

        Assert.Equal(Start.AddSeconds(1000), clock.UtcNow);
    }
}
