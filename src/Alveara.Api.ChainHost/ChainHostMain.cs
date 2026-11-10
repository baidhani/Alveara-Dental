using Alveara.Api.Architecture.Time;

namespace Alveara.Api.ChainHost;

/// <summary>
/// Entry point of the chain host. Settings come from the same environment variables the production API reads (connection string, storage, key ring, backup folder, bootstrap secret), plus:
/// <c>CHAIN_CLOCK_UTC</c> (ISO 8601 instant; default 2026-01-15T15:00:00Z) and <c>CHAIN_URL</c> (default http://localhost:5072). It prints one ready line and runs until it is stopped (Ctrl+C or the runner).
/// </summary>
public static class ChainHostMain
{
    public const string DefaultClockUtc = "2026-01-15T15:00:00Z";

    public static async Task<int> Main(string[] args)
    {
        var url = Environment.GetEnvironmentVariable("CHAIN_URL") ?? "http://localhost:5072";
        if (!DateTimeOffset.TryParse(Environment.GetEnvironmentVariable("CHAIN_CLOCK_UTC") ?? DefaultClockUtc, null, System.Globalization.DateTimeStyles.AssumeUniversal, out var start))
        {
            Console.Error.WriteLine("CHAIN_CLOCK_UTC is not a valid ISO 8601 instant.");
            return 2;
        }

        var zoneId = Environment.GetEnvironmentVariable("PracticeTimeZone") ?? "America/Chicago";
        using var factory = new ChainHostFactory(new ChainPracticeClock(start, TimeZoneInfo.FindSystemTimeZoneById(zoneId)));
        factory.UseKestrel(new Uri(url).Port);
        factory.StartServer();
        Console.WriteLine($"ChainHost ready on {url} clockMode=injected clockUtc={start:O}");

        using var stop = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => stop.Cancel();
        try { await Task.Delay(Timeout.Infinite, stop.Token); } catch (OperationCanceledException) { }
        return 0;
    }
}
