using System.Globalization;
using Alveara.Api.Architecture.Time;
using Alveara.Api.TestSupport;
using Microsoft.Extensions.Configuration;

namespace Alveara.Api.ChainHost;

/// <summary>
/// Entry point of the chain host. Settings come from the same environment variables the production API reads (connection string, storage, key ring, backup folder), plus:
/// <c>CHAIN_CLOCK_MODE</c> (required: <c>injected</c> or <c>real</c>; ALV-N030), <c>CHAIN_CLOCK_UTC</c> (ISO 8601 instant; default 2026-01-15T15:00:00Z; injected mode only)
/// and <c>CHAIN_URL</c> (default http://127.0.0.1:5072; loopback only). ALV-N030: <c>ASPNETCORE_ENVIRONMENT=Test</c> with <c>TestSupport__Enabled=true</c> adds the
/// account-fixture route (<see cref="TestSupportRegistration"/>); a requested route that may not be enabled stops the start (exit 3). It prints one ready line and runs until it is stopped (Ctrl+C or the runner).
/// ALV-N021 A2: <c>CHAIN_CONTROL_TOKEN</c>, <c>CHAIN_RUN_ID</c> and <c>CHAIN_CLOCK_LOG</c> switch on the clock-advance route for one chain run (<see cref="ClockAdvanceStartupFilter"/>).
/// </summary>
public static class ChainHostMain
{
    public const string DefaultClockUtc = "2026-01-15T15:00:00Z";

    /// <summary>
    /// ALV-N021 A2-R02 (review CR-1, decision 02 T-2): <c>CHAIN_CLOCK_UTC</c> is a documented machine value (ISO 8601 instant), so it is read with the invariant culture -
    /// never the process culture, which on a Thai (Buddhist calendar) or dotted-time machine reads the same text differently or not at all. A value without an offset is UTC;
    /// the result is always UTC.
    /// </summary>
    public static bool TryParseClockUtc(string? value, out DateTimeOffset utc)
    {
        var ok = DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed);
        utc = ok ? parsed.ToUniversalTime() : default;
        return ok;
    }

    public const string DefaultUrl = "http://127.0.0.1:5072";
    public const int ExitBadSetting = 2;
    public const int ExitRefused = 3;

    /// <summary>ALV-N030 (brief 17.3): <c>CHAIN_CLOCK_MODE</c> must be stated - <c>injected</c> or <c>real</c>.</summary>
    public static bool TryParseClockMode(string? value, out bool injected)
    {
        injected = string.Equals(value, "injected", StringComparison.Ordinal);
        return injected || string.Equals(value, "real", StringComparison.Ordinal);
    }

    /// <summary>
    /// ALV-N030 (brief 17.2; guards G4, G7): decides, before anything starts, whether this process may run. A non-loopback <c>CHAIN_URL</c> is always refused (this is
    /// a test host); with <c>TestSupport:Enabled=true</c>, a non-<c>Test</c> environment or any non-loopback configured address is refused too. Null means the start may go ahead.
    /// </summary>
    public static string? StartRefusal(string chainUrl, TestSupportDecision decision)
    {
        if (!TestSupportRegistration.IsLoopback(chainUrl))
            return $"CHAIN_URL '{chainUrl}' is not a loopback address (127.0.0.1, ::1 or localhost); the chain host never listens beyond this machine.";
        return decision.MustRefuseStart ? $"test support was requested but may not be enabled: {decision.Refusal}." : null;
    }

    public static async Task<int> Main(string[] args)
    {
        var url = Environment.GetEnvironmentVariable("CHAIN_URL") ?? DefaultUrl;
        if (!TryParseClockMode(Environment.GetEnvironmentVariable("CHAIN_CLOCK_MODE"), out var injected))
        {
            Console.Error.WriteLine("CHAIN_CLOCK_MODE must be 'injected' or 'real'.");
            return ExitBadSetting;
        }
        if (!TryParseClockUtc(Environment.GetEnvironmentVariable("CHAIN_CLOCK_UTC") ?? DefaultClockUtc, out var start))
        {
            Console.Error.WriteLine("CHAIN_CLOCK_UTC is not a valid ISO 8601 instant.");
            return ExitBadSetting;
        }

        var environmentName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") is { Length: > 0 } env ? env : "Development";
        // The same sources the product reads its settings and listen addresses from: environment variables (also ASPNETCORE_-prefixed) and the command line (--urls)
        var config = new ConfigurationBuilder().AddEnvironmentVariables().AddEnvironmentVariables("ASPNETCORE_").AddCommandLine(args).Build();
        var decision = TestSupportRegistration.Evaluate(environmentName, config, [url]);
        if (StartRefusal(url, decision) is { } refusal)
        {
            Console.Error.WriteLine($"ChainHost refused to start: {refusal}");
            return ExitRefused;
        }

        var zoneId = Environment.GetEnvironmentVariable("PracticeTimeZone") ?? "America/Chicago";
        // ALV-N021 A2: the run's control settings (set only by Invoke-AccumulatedChain.ps1); without CHAIN_CONTROL_TOKEN the clock-advance route stays off
        var control = new ChainControlOptions(Environment.GetEnvironmentVariable("CHAIN_RUN_ID"), Environment.GetEnvironmentVariable("CHAIN_CONTROL_TOKEN"), Environment.GetEnvironmentVariable("CHAIN_CLOCK_LOG"));
        var clock = injected ? new ChainPracticeClock(start, TimeZoneInfo.FindSystemTimeZoneById(zoneId)) : null;
        using var factory = new ChainHostFactory(clock, control, new ChainHostSettings(environmentName, decision));
        var listen = new Uri(url);
        factory.UseKestrel(kestrel =>
        {
            // Bound to exactly the loopback address CHAIN_URL names, never a wildcard default
            if (string.Equals(listen.Host, "localhost", StringComparison.OrdinalIgnoreCase)) kestrel.ListenLocalhost(listen.Port);
            else kestrel.Listen(System.Net.IPAddress.Parse(listen.Host.Trim('[', ']')), listen.Port);
        });
        factory.StartServer();
        Console.WriteLine($"ChainHost ready on {url} environment={environmentName} clockMode={(injected ? "injected" : "real")}"
            + (injected ? $" clockUtc={start:O} clockAdvance={(control.AdvanceEnabled ? "on" : "off")}" : "") + $" testSupport={(decision.Enabled ? "on" : "off")}");

        using var stop = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => stop.Cancel();
        try { await Task.Delay(Timeout.Infinite, stop.Token); } catch (OperationCanceledException) { }
        return 0;
    }
}
