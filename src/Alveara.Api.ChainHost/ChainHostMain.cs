using System.Globalization;
using Alveara.Api.Architecture.Time;

namespace Alveara.Api.ChainHost;

/// <summary>
/// Entry point of the chain host. Settings come from the same environment variables the production API reads (connection string, storage, key ring, backup folder, bootstrap secret), plus:
/// <c>CHAIN_CLOCK_UTC</c> (ISO 8601 instant; default 2026-01-15T15:00:00Z) and <c>CHAIN_URL</c> (default http://localhost:5072). It prints one ready line and runs until it is stopped (Ctrl+C or the runner).
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

    public static async Task<int> Main(string[] args)
    {
        var url = Environment.GetEnvironmentVariable("CHAIN_URL") ?? "http://localhost:5072";
        if (!TryParseClockUtc(Environment.GetEnvironmentVariable("CHAIN_CLOCK_UTC") ?? DefaultClockUtc, out var start))
        {
            Console.Error.WriteLine("CHAIN_CLOCK_UTC is not a valid ISO 8601 instant.");
            return 2;
        }

        var zoneId = Environment.GetEnvironmentVariable("PracticeTimeZone") ?? "America/Chicago";
        // ALV-N021 A2: the run's control settings (set only by Invoke-AccumulatedChain.ps1); without CHAIN_CONTROL_TOKEN the clock-advance route stays off
        var control = new ChainControlOptions(Environment.GetEnvironmentVariable("CHAIN_RUN_ID"), Environment.GetEnvironmentVariable("CHAIN_CONTROL_TOKEN"), Environment.GetEnvironmentVariable("CHAIN_CLOCK_LOG"));
        using var factory = new ChainHostFactory(new ChainPracticeClock(start, TimeZoneInfo.FindSystemTimeZoneById(zoneId)), control);
        factory.UseKestrel(new Uri(url).Port);
        factory.StartServer();
        Console.WriteLine($"ChainHost ready on {url} clockMode=injected clockUtc={start:O} clockAdvance={(control.AdvanceEnabled ? "on" : "off")}");

        using var stop = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => stop.Cancel();
        try { await Task.Delay(Timeout.Infinite, stop.Token); } catch (OperationCanceledException) { }
        return 0;
    }
}
