using System.Text.Json;

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
    /// changed" (per the Engineering Reference's measurement-events rule).
    /// </summary>
    public required int SchemaVersion { get; set; }

    /// <summary>
    /// Minimal, allow-listed properties only (see <see cref="MeasurementEventValidator"/>) —
    /// never a clinical note, patient name, or other PHI payload.
    /// </summary>
    public required string PropertiesJson { get; set; }

    public DateTimeOffset OccurredAtUtc { get; set; }
}

/// <summary>
/// Enforces the privacy-minimization rule at write time: a measurement event's property keys must
/// come from a small allow-list, so a future caller can't accidentally attach a patient name or
/// clinical note "just this once."
/// </summary>
public static class MeasurementEventValidator
{
    private static readonly HashSet<string> AllowedPropertyKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "count", "durationMs", "outcome", "category", "role", "release",
    };

    public static void ValidateProperties(string propertiesJson)
    {
        using var doc = JsonDocument.Parse(propertiesJson);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new MeasurementEventValidationException("Measurement event properties must be a JSON object.");
        }

        foreach (var property in doc.RootElement.EnumerateObject())
        {
            if (!AllowedPropertyKeys.Contains(property.Name))
            {
                throw new MeasurementEventValidationException(
                    $"Property '{property.Name}' is not on the measurement-event allow-list. " +
                    "Add it to MeasurementEventValidator only after confirming it cannot carry PHI.");
            }
        }
    }
}

public sealed class MeasurementEventValidationException(string message) : Exception(message);
