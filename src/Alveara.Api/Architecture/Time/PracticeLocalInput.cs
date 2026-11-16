using System.Globalization;

namespace Alveara.Api.Architecture.Time;

/// <summary>
/// ALV-N021 A3 (briefing section 5, BR-2): the one parser for a practice-local time typed into a <c>datetime-local</c> control and sent as plain text
/// (<c>performedAtLocal</c>, <c>dueAtLocal</c>, <c>measuredAtLocal</c>). Used by all five routes that accept such a field, so they behave identically.
/// <list type="bullet">
/// <item>Accepts only the invariant ASCII patterns <c>yyyy-MM-ddTHH:mm</c> and, where a field allows seconds, <c>yyyy-MM-ddTHH:mm:ss</c>. A <c>Z</c>, an offset,
/// spaces, fractional seconds, other separators, non-ASCII digits and every other form are refused (<c>invalid_local_time</c>).</item>
/// <item>The result is a wall-clock value of kind <see cref="DateTimeKind.Unspecified"/>, converted only through <see cref="IPracticeClock.FromPracticeLocal"/>.
/// A wall time that does not exist (DST gap) is <c>local_time_does_not_exist</c>; one that occurs twice (DST overlap) is <c>local_time_ambiguous</c>.</item>
/// <item>Owner decision D-2: the legacy UTC field (<c>performedAtUtc</c>, ...) is still accepted when it is supplied alone; supplying both is
/// <c>ambiguous_time</c>, naming both fields. Neither supplied is not an error here: each route keeps its own existing rule for a missing time.</item>
/// </list>
/// Every refusal is a <see cref="LocalTimeInputException"/> (400) with the field name in <c>fieldErrors</c>; nothing is read or written before it.
/// </summary>
public static class PracticeLocalInput
{
    private static readonly string[] MinutePattern = ["yyyy-MM-dd'T'HH:mm"];
    private static readonly string[] MinuteOrSecondPatterns = ["yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd'T'HH:mm:ss"];

    /// <summary>Parses the exact text; true with an <see cref="DateTimeKind.Unspecified"/> wall-clock value, otherwise false.</summary>
    public static bool TryParse(string? text, bool allowSeconds, out DateTime wallClock)
    {
        wallClock = default;
        if (text is null || text.Length is < 16 or > 19) return false;
        foreach (var c in text)
            if (c > 127) return false;   // ASCII only (the parser below would also accept some non-ASCII digits on some runtimes)
        if (!DateTime.TryParseExact(text, allowSeconds ? MinuteOrSecondPatterns : MinutePattern, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            return false;
        wallClock = DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified);
        return true;
    }

    /// <summary>
    /// The UTC instant a request means: from <paramref name="local"/> through the practice clock, or the legacy <paramref name="legacyUtc"/> when it is the only
    /// one supplied, or null when neither is. Throws <see cref="LocalTimeInputException"/> for every refusal described on the class.
    /// </summary>
    public static DateTimeOffset? Resolve(string? local, DateTimeOffset? legacyUtc, string localField, string utcField, bool allowSeconds, IPracticeClock clock)
    {
        if (local is not null && legacyUtc is not null)
            throw new LocalTimeInputException("ambiguous_time", $"Send either {localField} or {utcField}, not both.",
                new Dictionary<string, string> { [localField] = "Send this or the UTC form, not both.", [utcField] = "Send this or the local form, not both." });
        if (local is null) return legacyUtc;

        if (!TryParse(local, allowSeconds, out var wall))
            throw Field("invalid_local_time", localField, allowSeconds
                ? "Enter the date and time as YYYY-MM-DDTHH:MM or YYYY-MM-DDTHH:MM:SS."
                : "Enter the date and time as YYYY-MM-DDTHH:MM.");
        var zone = clock.PracticeTimeZone;
        if (zone.IsInvalidTime(wall))
            throw Field("local_time_does_not_exist", localField, "That time does not exist on that day in the practice's time zone (the clocks change). Choose another time.");
        if (zone.IsAmbiguousTime(wall))
            throw Field("local_time_ambiguous", localField, "That time happens twice on that day in the practice's time zone (the clocks change). Choose another time.");
        return clock.FromPracticeLocal(wall);
    }

    private const string AliasKey = "alveara.practiceLocalInput.alias";

    /// <summary>
    /// ALV-N021 A3 (owner decision, option 3): records that this request sent the practice-local form, so a later field error the unchanged service reports
    /// under the legacy UTC name (<c>performedAtUtc</c>, <c>measuredAtUtc</c>) is also reported under the local name the caller used (<see cref="WithLocalAlias"/>).
    /// </summary>
    public static void MarkLocalUsed(Microsoft.AspNetCore.Http.HttpContext context, string localField, string utcField) => context.Items[AliasKey] = (localField, utcField);

    /// <summary>
    /// The field errors to return: unchanged for a legacy request; for a request that sent the local form, each error under the legacy UTC name is also
    /// given under the local name, with the same message. Old callers therefore see exactly what they saw before.
    /// </summary>
    public static IReadOnlyDictionary<string, string>? WithLocalAlias(IReadOnlyDictionary<string, string>? errors, Microsoft.AspNetCore.Http.HttpContext context)
    {
        if (errors is null || context.Items[AliasKey] is not (string localField, string utcField) || !errors.TryGetValue(utcField, out var message)) return errors;
        return new Dictionary<string, string>(errors) { [localField] = message };
    }

    private static LocalTimeInputException Field(string code, string field, string message) =>
        new(code, message, new Dictionary<string, string> { [field] = message });
}

/// <summary>A practice-local time field was refused (400); <see cref="FieldErrors"/> names the field(s).</summary>
public sealed class LocalTimeInputException(string code, string message, IReadOnlyDictionary<string, string> fieldErrors) : Exception(message)
{
    public string Code { get; } = code;
    public int StatusCode => 400;
    public IReadOnlyDictionary<string, string> FieldErrors { get; } = fieldErrors;
}
