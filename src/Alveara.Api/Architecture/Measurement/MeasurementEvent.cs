using System.Text.Json;
using System.Text.RegularExpressions;

namespace Alveara.Api.Architecture.Measurement;

/// <summary>
/// One versioned, privacy-minimized operational measurement event, per ALV-N002. These are
/// observational only — never the authoritative source for clinical or financial state — and
/// exist so later reporting stories (ALV-N006) can measure the project's stated success metrics
/// without becoming a shadow copy of the patient chart.
/// </summary>
public class MeasurementEvent
{
    public Guid Id { get; set; }

    /// <summary>e.g. "appointment.scheduled", "procedure.completed" — a stable, documented name.</summary>
    public required string EventName { get; set; }

    /// <summary>
    /// The event *schema* version (not the app version) — bumped whenever this event's property
    /// shape changes, so a later report can tell "the metric changed" apart from "the definition
    /// changed" (per the Engineering Reference's measurement-events rule). Must be >= 1; there is
    /// no meaningful "version 0" schema.
    /// </summary>
    public required int SchemaVersion { get; set; }

    /// <summary>
    /// Minimal, allow-listed, type-and-shape-validated properties only (see
    /// <see cref="MeasurementEventValidator"/>) — never a clinical note, patient name, nested
    /// object, or other free-text/PHI-shaped payload.
    /// </summary>
    public required string PropertiesJson { get; set; }

    public DateTimeOffset OccurredAtUtc { get; set; }
}

/// <summary>
/// Enforces the privacy-minimization rule at write time. This is not just a top-level key
/// allow-list (N002-R01-04 found that alone lets a nested object or arbitrary free text through
/// under an allowed key, e.g. <c>{"category": {"patientName": "..."}}</c>) — every allowed key
/// also has a fixed expected JSON kind (string or number, never object/array), and where the key
/// is categorical, a fixed bounded set of accepted values, so a caller cannot smuggle free text
/// through a "safe-looking" key either.
/// </summary>
public static class MeasurementEventValidator
{
    private static readonly Regex EventNamePattern = new(@"^[a-z][a-z0-9-]*(\.[a-z][a-z0-9-]*)+$", RegexOptions.Compiled);
    private const int MaxEventNameLength = 60;
    private const int MaxStringValueLength = 40;

    private static readonly IReadOnlySet<string> AllowedOutcomes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "success", "failure", "skipped",
    };

    private static readonly IReadOnlySet<string> AllowedRoles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "admin", "dentist", "hygienist", "assistant", "front_desk", "billing", "office_manager",
    };

    private static readonly Regex CategoryPattern = new(@"^[a-z][a-z0-9_]{0,39}$", RegexOptions.Compiled);
    private static readonly Regex ReleasePattern = new(@"^r[0-9]$", RegexOptions.Compiled);

    /// <summary>Per-key validators. Each one receives the raw JSON value and returns whether it is acceptable — including rejecting the wrong JSON kind (object/array/etc.) outright.</summary>
    private static readonly Dictionary<string, Func<JsonElement, bool>> PropertyValidators = new(StringComparer.OrdinalIgnoreCase)
    {
        ["count"] = v => v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) && n >= 0,
        ["durationMs"] = v => v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d) && d >= 0,
        ["outcome"] = v => v.ValueKind == JsonValueKind.String && AllowedOutcomes.Contains(v.GetString() ?? string.Empty),
        ["category"] = v => v.ValueKind == JsonValueKind.String && CategoryPattern.IsMatch(v.GetString() ?? string.Empty),
        ["role"] = v => v.ValueKind == JsonValueKind.String && AllowedRoles.Contains(v.GetString() ?? string.Empty),
        ["release"] = v => v.ValueKind == JsonValueKind.String && ReleasePattern.IsMatch(v.GetString() ?? string.Empty),
    };

    public static void ValidateEventName(string eventName)
    {
        if (string.IsNullOrWhiteSpace(eventName) || eventName.Length > MaxEventNameLength || !EventNamePattern.IsMatch(eventName))
        {
            throw new MeasurementEventValidationException(
                $"Event name '{eventName}' must be dotted.lowercase.segments (e.g. 'appointment.scheduled'), <= {MaxEventNameLength} chars.");
        }
    }

    public static void ValidateSchemaVersion(int schemaVersion)
    {
        if (schemaVersion < 1)
        {
            throw new MeasurementEventValidationException($"Schema version must be >= 1; got {schemaVersion}.");
        }
    }

    public static void ValidateProperties(string propertiesJson)
    {
        using var doc = JsonDocument.Parse(propertiesJson);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new MeasurementEventValidationException("Measurement event properties must be a JSON object.");
        }

        foreach (var property in doc.RootElement.EnumerateObject())
        {
            if (!PropertyValidators.TryGetValue(property.Name, out var validate))
            {
                throw new MeasurementEventValidationException(
                    $"Property '{property.Name}' is not on the measurement-event allow-list. " +
                    "Add it to MeasurementEventValidator only after confirming it cannot carry PHI.");
            }

            if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
            {
                throw new MeasurementEventValidationException(
                    $"Property '{property.Name}' must be a plain string or number, not a nested object/array.");
            }

            if (property.Value.ValueKind == JsonValueKind.String && (property.Value.GetString()?.Length ?? 0) > MaxStringValueLength)
            {
                throw new MeasurementEventValidationException(
                    $"Property '{property.Name}' exceeds the maximum allowed length of {MaxStringValueLength} characters.");
            }

            if (!validate(property.Value))
            {
                throw new MeasurementEventValidationException(
                    $"Property '{property.Name}' has an unacceptable value for its type/bounded-value rule.");
            }
        }
    }
}

public sealed class MeasurementEventValidationException(string message) : Exception(message);
