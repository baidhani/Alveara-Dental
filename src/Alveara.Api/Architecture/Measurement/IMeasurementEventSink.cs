using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Measurement;

public interface IMeasurementEventSink
{
    Task RecordAsync(string eventName, int schemaVersion, object properties, CancellationToken cancellationToken = default);
}

public sealed class MeasurementEventSink(AlveraDbContext db) : IMeasurementEventSink
{
    public async Task RecordAsync(string eventName, int schemaVersion, object properties, CancellationToken cancellationToken = default)
    {
        MeasurementEventValidator.ValidateEventName(eventName);
        MeasurementEventValidator.ValidateSchemaVersion(schemaVersion);
        var propertiesJson = System.Text.Json.JsonSerializer.Serialize(properties);
        MeasurementEventValidator.ValidateProperties(propertiesJson); // throws if a disallowed/nested/oversized/out-of-range value is present

        db.MeasurementEvents.Add(new MeasurementEvent
        {
            Id = Guid.NewGuid(),
            EventName = eventName,
            SchemaVersion = schemaVersion,
            PropertiesJson = propertiesJson,
            OccurredAtUtc = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(cancellationToken);
    }
}
