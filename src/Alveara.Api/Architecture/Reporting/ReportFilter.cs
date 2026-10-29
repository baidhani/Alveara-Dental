using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Reporting;

/// <summary>ALV-N006: the limits of a report run. Every value has a default; the section name is <c>Reporting</c>.</summary>
public sealed class ReportOptions
{
    public const string SectionName = "Reporting";

    /// <summary>The longest date range one report may cover (inclusive days). A longer range is refused with a clear message, never silently shortened.</summary>
    public int MaxRangeDays { get; set; } = 366;

    public int MaxProviders { get; set; } = 50;

    /// <summary>The most source rows one report may read. Over it the run is refused (413 <c>report_too_large</c>) so a huge range cannot exhaust memory and a number is never computed from a cut-off set.</summary>
    public int MaxSourceRows { get; set; } = 200_000;

    /// <summary>The fewest records each denominator must hold for a metric to be compared with a baseline: below it a percentage would say more than the numbers can support.</summary>
    public int MinimumComparisonSample { get; set; } = 30;

    /// <summary>The most data lines (summary lines plus table rows) one exported file may hold. Over it the export is refused (413 <c>export_too_large</c>), never cut short.</summary>
    public int MaxExportRows { get; set; } = 50_000;

    public int DefaultDrillRows { get; set; } = 100;
    public int MaxDrillRows { get; set; } = 500;
}

/// <summary>A report request that is refused. <see cref="Code"/> is stable (the screens map it to words); the message never echoes a value from the request.</summary>
public sealed class ReportException(string code, string message, int statusCode) : Exception(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
}

/// <summary>What the caller sent: dates as text (<c>yyyy-MM-dd</c>, practice-local), the booked providers to include (none means all) and an optional location.</summary>
public sealed record ReportFilterInput(string? From, string? To, IReadOnlyList<Guid>? ProviderIds, Guid? LocationId);

/// <summary>A checked filter: whole practice-local days turned into a half-open UTC range [StartUtc, EndUtc).</summary>
public sealed record ReportFilter(DateOnly From, DateOnly To, int Days, DateTimeOffset StartUtc, DateTimeOffset EndUtc, IReadOnlyList<Guid> ProviderIds, Guid? LocationId, string TimeZoneId);

/// <summary>
/// ALV-N006: turns the caller's filter into a <see cref="ReportFilter"/> or refuses it with a stable code. Dates are practice-local days, converted through the practice's own time zone (never the server's): a day
/// starts at local midnight (at the first instant that exists when midnight falls in a daylight-saving gap, at the earlier of the two when it is ambiguous) and the range ends at the next local midnight after the last day.
/// Provider and location ids must exist (inactive ones may be asked for: their history is still reportable). Failure modes handled: missing, malformed, inverted and over-long ranges, too many or unknown providers,
/// an unknown location. No database change is made.
/// </summary>
public sealed class ReportFilterResolver(AlveraDbContext db, IPracticeClock clock, IOptions<ReportOptions> options)
{
    private readonly ReportOptions _options = options.Value;

    public async Task<ReportFilter> ResolveAsync(ReportFilterInput input, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(input.From) || string.IsNullOrWhiteSpace(input.To))
            throw new ReportException("dates_required", "Give both a first and a last date.", 400);
        var from = ParseDate(input.From);
        var to = ParseDate(input.To);
        if (from > to) throw new ReportException("invalid_range", "The first date must not be after the last date.", 400);
        var days = to.DayNumber - from.DayNumber + 1;
        if (days > _options.MaxRangeDays)
            throw new ReportException("range_too_large", $"A report can cover at most {_options.MaxRangeDays} days. Choose a shorter range.", 400);

        var providers = (input.ProviderIds ?? []).Distinct().ToArray();
        if (providers.Length > _options.MaxProviders)
            throw new ReportException("too_many_providers", $"At most {_options.MaxProviders} providers can be chosen.", 400);
        if (providers.Length > 0 && await db.ProviderProfiles.AsNoTracking().CountAsync(p => providers.Contains(p.Id), ct) != providers.Length)
            throw new ReportException("unknown_provider", "One of the chosen providers does not exist.", 400);
        if (input.LocationId is { } location && !await db.PracticeLocations.AsNoTracking().AnyAsync(l => l.Id == location, ct))
            throw new ReportException("unknown_location", "The chosen location does not exist.", 400);

        return new ReportFilter(from, to, days, StartOfDayUtc(from), StartOfDayUtc(to.AddDays(1)), providers, input.LocationId, clock.PracticeTimeZone.Id);
    }

    /// <summary>Parses <c>yyyy-MM-dd</c> only, within years 1900 to 2200 (so adding a day can never overflow).</summary>
    public static DateOnly ParseDate(string? text)
    {
        if (!DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) || date.Year < 1900 || date.Year > 2200)
            throw new ReportException("invalid_date", "Dates must be written as year-month-day, for example 2026-10-28.", 400);
        return date;
    }

    /// <summary>The UTC instant at which the practice-local day <paramref name="date"/> begins.</summary>
    public DateTimeOffset StartOfDayUtc(DateOnly date)
    {
        var zone = clock.PracticeTimeZone;
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        while (zone.IsInvalidTime(local)) local = local.AddMinutes(30);      // midnight does not exist (a spring-forward gap): the day starts at the first instant that does
        var offset = zone.IsAmbiguousTime(local) ? zone.GetAmbiguousTimeOffsets(local).Max() : zone.GetUtcOffset(local);   // the larger offset is the earlier instant
        return new DateTimeOffset(local, offset).ToUniversalTime();
    }
}
