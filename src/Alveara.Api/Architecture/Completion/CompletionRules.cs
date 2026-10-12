using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Alveara.Api.Architecture.Treatment;

namespace Alveara.Api.Architecture.Completion;

/// <summary>What a caller supplies to complete a selected set of one plan's procedures. Everything is optional here so the rules can name every missing or wrong field in one answer.</summary>
public sealed record CompleteProceduresRequest(string? IdempotencyKey, string? RowVersion, Guid? EncounterId, Guid? ProviderId, DateTimeOffset? PerformedAtUtc, IReadOnlyList<Guid>? ItemIds);

/// <summary>The request after the rules have cleaned it: a trimmed key, the performed time normalized to UTC milliseconds and the selected procedures sorted and free of duplicates.</summary>
public sealed record CleanCompletion(string Key, Guid EncounterId, Guid ProviderId, DateTimeOffset PerformedAtUtc, IReadOnlyList<Guid> ItemIds);

/// <summary>What a caller supplies to correct a completed procedure: the number of the latest amendment they saw (0 when none), their own key, what to correct and why. Everything is optional here so every wrong field is named at once.</summary>
public sealed record AmendCompletedProcedureRequest(string? IdempotencyKey, int? ExpectedAmendmentNumber, Guid? ProviderId, DateTimeOffset? PerformedAtUtc, Guid? EncounterId, string? Reason);

/// <summary>The amendment after cleaning: a trimmed key, the performed time in UTC milliseconds (when given) and the trimmed reason.</summary>
public sealed record CleanAmendment(string Key, int ExpectedAmendmentNumber, Guid? ProviderId, DateTimeOffset? PerformedAtUtc, Guid? EncounterId, string Reason);

/// <summary>
/// ALV-008-C01: the pure rules of completing a set, so they can be tested without a database: what a request must look like, how the performed time is judged against the practice clock, how an item's accepted time
/// is read from its history, and the canonical form (and hash) of a request that makes a retry recognisable.
/// </summary>
public static class CompletionRules
{
    /// <summary>How far ahead of the practice clock a performed time may be: enough for two computers' clocks to disagree, not enough to record care that has not happened. The boundary is inclusive.</summary>
    public static readonly TimeSpan ClockTolerance = TimeSpan.FromMinutes(5);
    public const int MaxItems = 50;

    /// <summary>A time cut to whole milliseconds in UTC, so the same instant is always the same value in the hash and in the database.</summary>
    public static DateTimeOffset NormalizeTime(DateTimeOffset time)
    {
        var utc = time.UtcDateTime;
        return new DateTimeOffset(utc.Ticks - utc.Ticks % TimeSpan.TicksPerMillisecond, TimeSpan.Zero);
    }

    public static bool IsAheadOfClock(DateTimeOffset performed, DateTimeOffset now) => performed > now + ClockTolerance;

    /// <summary>Checks the request without touching the database and names every problem at once; returns the cleaned request or throws <c>validation_failed</c> (400).</summary>
    public static CleanCompletion Clean(CompleteProceduresRequest request, DateTimeOffset now)
    {
        var errors = new Dictionary<string, string>();
        var key = request.IdempotencyKey?.Trim();
        if (string.IsNullOrEmpty(key)) errors["idempotencyKey"] = "A save key is required, so a retry never completes the same procedures twice.";
        else if (key.Length > CompletionLimits.KeyMax) errors["idempotencyKey"] = $"Keep the save key to {CompletionLimits.KeyMax} characters or fewer.";
        else if (key.Any(char.IsControl)) errors["idempotencyKey"] = "The save key cannot contain control characters.";

        if (request.EncounterId is null || request.EncounterId == Guid.Empty) errors["encounterId"] = "Choose the encounter this care belongs to.";
        if (request.ProviderId is null || request.ProviderId == Guid.Empty) errors["providerId"] = "Choose the provider who performed the care.";

        DateTimeOffset performed = default;
        if (request.PerformedAtUtc is null) errors["performedAtUtc"] = "Say when the procedures were performed.";
        else
        {
            performed = NormalizeTime(request.PerformedAtUtc.Value);
            if (IsAheadOfClock(performed, now)) errors["performedAtUtc"] = $"The performed time cannot be more than {(int)ClockTolerance.TotalMinutes} minutes ahead of the practice clock.";
        }

        var ids = request.ItemIds;
        if (ids is null || ids.Count == 0) errors["itemIds"] = "Choose at least one procedure to complete.";
        else if (ids.Count > MaxItems) errors["itemIds"] = $"Complete at most {MaxItems} procedures at a time.";
        else if (ids.Any(i => i == Guid.Empty)) errors["itemIds"] = "A selected procedure is not valid.";
        else if (ids.Distinct().Count() != ids.Count) errors["itemIds"] = "The same procedure is selected more than once.";

        if (errors.Count > 0) throw TreatmentPlanRules.Invalid(errors);
        return new CleanCompletion(key!, request.EncounterId!.Value, request.ProviderId!.Value, performed, ids!.OrderBy(i => i).ToList());
    }

    /// <summary>SHA-256, lower-case hexadecimal, of the canonical request: version tag, sorted item ids, encounter, provider and the performed time in UTC milliseconds. The same request always hashes the same.</summary>
    public static string RequestHash(CleanCompletion c)
    {
        var canonical = string.Join('|', "v1", string.Join(',', c.ItemIds.OrderBy(i => i).Select(i => i.ToString("N"))), c.EncounterId.ToString("N"), c.ProviderId.ToString("N"),
            c.PerformedAtUtc.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    /// <summary>Checks an amendment request without touching the database and names every problem at once; returns the cleaned request or throws <c>validation_failed</c> (400).</summary>
    public static CleanAmendment CleanForAmendment(AmendCompletedProcedureRequest request, DateTimeOffset now)
    {
        var errors = new Dictionary<string, string>();
        var key = request.IdempotencyKey?.Trim();
        if (string.IsNullOrEmpty(key)) errors["idempotencyKey"] = "A save key is required, so a retry never records the same correction twice.";
        else if (key.Length > CompletionLimits.KeyMax) errors["idempotencyKey"] = $"Keep the save key to {CompletionLimits.KeyMax} characters or fewer.";
        else if (key.Any(char.IsControl)) errors["idempotencyKey"] = "The save key cannot contain control characters.";

        if (request.ExpectedAmendmentNumber is null or < 0) errors["expectedAmendmentNumber"] = "Say which amendment you last saw (0 if there is none), so a correction made by someone else is never overwritten.";

        var reason = request.Reason?.Trim();
        if (string.IsNullOrEmpty(reason)) errors["reason"] = "Say why the procedure is being corrected.";
        else if (reason.Length > CompletionLimits.ReasonMax) errors["reason"] = $"Keep the reason to {CompletionLimits.ReasonMax} characters or fewer.";
        else if (reason.Any(char.IsControl)) errors["reason"] = "The reason cannot contain control characters or line breaks.";

        if (request.ProviderId is null && request.PerformedAtUtc is null && request.EncounterId is null) errors["correction"] = "Choose what to correct: the provider, the performed time or the encounter.";
        if (request.ProviderId == Guid.Empty) errors["providerId"] = "Choose the provider who performed the care.";
        if (request.EncounterId == Guid.Empty) errors["encounterId"] = "Choose the encounter this care belongs to.";

        DateTimeOffset? performed = null;
        if (request.PerformedAtUtc is { } given)
        {
            performed = NormalizeTime(given);
            if (IsAheadOfClock(performed.Value, now)) errors["performedAtUtc"] = $"The performed time cannot be more than {(int)ClockTolerance.TotalMinutes} minutes ahead of the practice clock.";
        }

        if (errors.Count > 0) throw TreatmentPlanRules.Invalid(errors);
        return new CleanAmendment(key!, request.ExpectedAmendmentNumber!.Value, request.ProviderId, performed, request.EncounterId, reason!);
    }

    /// <summary>SHA-256, lower-case hexadecimal, of the canonical amendment: version tag, the amendment last seen, each correction (or a dash when not given) and the reason.</summary>
    public static string AmendmentHash(CleanAmendment c)
    {
        var canonical = string.Join('|', "v1", "amend", c.ExpectedAmendmentNumber.ToString(CultureInfo.InvariantCulture), c.ProviderId?.ToString("N") ?? "-",
            c.PerformedAtUtc?.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture) ?? "-", c.EncounterId?.ToString("N") ?? "-", c.Reason);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    /// <summary>
    /// When the acceptance now in force was recorded: the latest state row that turned the procedure from not accepted to Accepted. Scheduling a procedure writes later rows that are also Accepted; they do not
    /// restart the clock. The time is when staff recorded the answer, not when the patient may have given it.
    /// </summary>
    public static DateTimeOffset? AcceptedAt(IEnumerable<TreatmentPlanItemState> history)
    {
        DateTimeOffset? accepted = null;
        var wasAccepted = false;
        foreach (var row in history.OrderBy(s => s.Sequence))
        {
            var isAccepted = row.Acceptance == ItemAcceptance.Accepted;
            if (isAccepted && !wasAccepted) accepted = row.RecordedAtUtc;
            wasAccepted = isAccepted;
        }
        return accepted;
    }
}
