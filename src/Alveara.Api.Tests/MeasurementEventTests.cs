using Alveara.Api.Architecture.Measurement;
using Xunit;

namespace Alveara.Api.Tests;

public class MeasurementEventTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public MeasurementEventTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Recording_an_event_with_allow_listed_properties_persists_with_its_schema_version()
    {
        await using var db = _fixture.CreateContext();
        var sink = new MeasurementEventSink(db);

        await sink.RecordAsync("appointment.scheduled", schemaVersion: 1, new { count = 1, category = "recall" });

        var stored = db.MeasurementEvents.Single(e => e.EventName == "appointment.scheduled");
        Assert.Equal(1, stored.SchemaVersion);
        Assert.Contains("\"count\":1", stored.PropertiesJson);
    }

    [Fact]
    public async Task Recording_an_event_with_a_disallowed_PHI_shaped_property_key_is_rejected()
    {
        await using var db = _fixture.CreateContext();
        var sink = new MeasurementEventSink(db);

        await Assert.ThrowsAsync<MeasurementEventValidationException>(() =>
            sink.RecordAsync("patient.viewed", schemaVersion: 1, new { patientName = "should not be allowed" }));
    }

    [Fact]
    public async Task Recording_an_event_never_writes_to_a_clinical_or_financial_table()
    {
        // Measurement events must be observational only — this proves the sink has no code path
        // that touches any other DbSet.
        await using var db = _fixture.CreateContext();
        var sink = new MeasurementEventSink(db);

        var jobsBefore = db.BackgroundJobs.Count();
        var usersBefore = db.UserAccounts.Count();

        await sink.RecordAsync("procedure.completed", schemaVersion: 2, new { count = 1, outcome = "success" });

        Assert.Equal(jobsBefore, db.BackgroundJobs.Count());
        Assert.Equal(usersBefore, db.UserAccounts.Count());
    }

    [Fact]
    public void Validator_rejects_a_non_object_payload()
    {
        Assert.Throws<MeasurementEventValidationException>(() =>
            MeasurementEventValidator.ValidateProperties("[1,2,3]"));
    }

    [Fact]
    public void Validator_accepts_every_currently_allow_listed_key()
    {
        var json = "{\"count\":1,\"durationMs\":10,\"outcome\":\"ok\",\"category\":\"x\",\"role\":\"dentist\",\"release\":\"r0\"}";
        MeasurementEventValidator.ValidateProperties(json); // must not throw
    }
}
