using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Alveara.Api.Architecture.Time;
using Alveara.Api.ChainHost;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N021 A2 (owner decision of 2026-11-15, case 3.09): the chain host's test-only clock-advance route, against a real SQL Server because the host starts its background jobs. It moves only the run's
/// injected clock, forward, for the holder of the run's control token, and records each advance; every refusal leaves the clock and the evidence unchanged. A chain host started without a token has
/// no such route (404), and the production application never has it.
/// </summary>
public class ChainClockAdvanceTests : IAsyncLifetime
{
    private const string Token = "test-control-token-0123456789";
    private static readonly DateTimeOffset Start = new(2026, 1, 15, 15, 0, 0, TimeSpan.Zero);
    private static TimeZoneInfo Chicago => TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");
    private readonly TestDatabaseFixture _fixture = new();
    private readonly string _log = Path.Combine(Path.GetTempPath(), $"chain-clock-{Guid.NewGuid():N}.jsonl");

    public Task InitializeAsync() => _fixture.InitializeAsync();
    public async Task DisposeAsync()
    {
        await _fixture.DisposeAsync();
        if (File.Exists(_log)) File.Delete(_log);
    }

    private void Configure(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Alveara", _fixture.ConnectionString);
        builder.UseSetting("AdminBootstrapSecret", IdentityTestHelpers.TestBootstrapSecret);
    }

    private static Task<HttpResponseMessage> AdvanceAsync(HttpClient client, string body, string? token = Token)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, ClockAdvanceStartupFilter.Route) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        if (token is not null) request.Headers.Add(ClockAdvanceStartupFilter.TokenHeader, token);
        return client.SendAsync(request);
    }

    private string[] LogLines() => File.Exists(_log) ? File.ReadAllLines(_log).Where(l => l.Length > 0).ToArray() : [];

    [Fact]
    public async Task A_forward_advance_moves_only_the_injected_clock_and_is_recorded_with_the_run_old_time_and_new_time()
    {
        var clock = new ChainPracticeClock(Start, Chicago);
        await using var factory = new ChainHostFactory(clock, new ChainControlOptions("run-123", Token, _log)).WithWebHostBuilder(Configure);
        var client = factory.CreateClient();

        var response = await AdvanceAsync(client, """{"minutes":90}""");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var record = await response.Content.ReadFromJsonAsync<ClockAdvanceRecord>();
        Assert.Equal(("run-123", Start, 90, Start.AddMinutes(90)), (record!.RunId, record.PreviousUtc, record.AdvancedByMinutes, record.UtcNow));
        Assert.Equal(Start.AddMinutes(90), clock.UtcNow);
        Assert.Equal(Start.AddMinutes(90), factory.Services.GetRequiredService<IPracticeClock>().UtcNow);   // the clock the application uses
        Assert.Equal(Start.AddMinutes(90), (await client.GetFromJsonAsync<ClockModeReport>(ClockModeStartupFilter.Route))!.UtcNow);
        var line = Assert.Single(LogLines());
        var logged = JsonSerializer.Deserialize<ClockAdvanceRecord>(line, JsonSerializerOptions.Web)!;
        Assert.Equal((record.RunId, record.PreviousUtc, record.AdvancedByMinutes, record.UtcNow), (logged.RunId, logged.PreviousUtc, logged.AdvancedByMinutes, logged.UtcNow));

        Assert.Equal(HttpStatusCode.OK, (await AdvanceAsync(client, """{"minutes":30}""")).StatusCode);   // advances add up, each recorded
        Assert.Equal(Start.AddMinutes(120), clock.UtcNow);
        Assert.Equal(2, LogLines().Length);
    }

    [Fact]
    public async Task Zero_negative_fractional_oversized_missing_extra_or_malformed_advances_are_refused_and_change_nothing()
    {
        var clock = new ChainPracticeClock(Start, Chicago);
        await using var factory = new ChainHostFactory(clock, new ChainControlOptions("run-123", Token, _log)).WithWebHostBuilder(Configure);
        var client = factory.CreateClient();
        string[] bodies =
        [
            """{"minutes":0}""", """{"minutes":-5}""", """{"minutes":1.5}""", """{"minutes":"60"}""", """{"minutes":null}""",
            $$"""{"minutes":{{ClockAdvanceStartupFilter.MaxMinutes + 1}}}""", """{"minutes":2147483648}""", """{}""", """{"seconds":60}""",
            """{"minutes":5,"extra":1}""", """[60]""", "60", "not json", "",
        ];

        foreach (var body in bodies)
        {
            var response = await AdvanceAsync(client, body);
            Assert.True(HttpStatusCode.BadRequest == response.StatusCode, $"{body}: {response.StatusCode}");
            Assert.Contains("invalid_advance", await response.Content.ReadAsStringAsync());
        }

        Assert.Equal(Start, clock.UtcNow);
        Assert.Empty(LogLines());
        Assert.Equal(HttpStatusCode.OK, (await AdvanceAsync(client, $$"""{"minutes":{{ClockAdvanceStartupFilter.MaxMinutes}}}""")).StatusCode);   // the bound itself is allowed
    }

    [Fact]
    public async Task Without_the_runs_token_or_with_the_wrong_method_the_clock_cannot_be_moved()
    {
        var clock = new ChainPracticeClock(Start, Chicago);
        await using var factory = new ChainHostFactory(clock, new ChainControlOptions("run-123", Token, _log)).WithWebHostBuilder(Configure);
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Forbidden, (await AdvanceAsync(client, """{"minutes":60}""", token: null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await AdvanceAsync(client, """{"minutes":60}""", token: "wrong-token")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await AdvanceAsync(client, """{"minutes":60}""", token: Token + "x")).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.GetAsync(ClockAdvanceStartupFilter.Route)).StatusCode);

        Assert.Equal(Start, clock.UtcNow);
        Assert.Empty(LogLines());
    }

    [Fact]
    public async Task A_chain_host_started_without_a_control_token_has_no_advance_route()
    {
        var clock = new ChainPracticeClock(Start, Chicago);
        await using var factory = new ChainHostFactory(clock).WithWebHostBuilder(Configure);   // as the walkthrough runner starts it

        var response = await AdvanceAsync(factory.CreateClient(), """{"minutes":60}""");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(Start, clock.UtcNow);
    }

    [Fact]
    public async Task The_production_application_has_no_clock_advance_route_and_keeps_the_real_clock()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(Configure);
        var before = factory.Services.GetRequiredService<IPracticeClock>().UtcNow;

        var response = await AdvanceAsync(factory.CreateClient(), """{"minutes":600}""");

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("previousUtc", await response.Content.ReadAsStringAsync());
        Assert.IsType<PracticeClock>(factory.Services.GetRequiredService<IPracticeClock>());
        Assert.True(factory.Services.GetRequiredService<IPracticeClock>().UtcNow - before < TimeSpan.FromMinutes(5));   // real time, not moved ten hours
    }
}
