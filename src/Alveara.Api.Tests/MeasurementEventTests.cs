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
        var json = "{\"count\":1,\"durationMs\":10,\"outcome\":\"success\",\"category\":\"x\",\"role\":\"dentist\",\"release\":\"r0\"}";
        MeasurementEventValidator.ValidateProperties(json); // must not throw
    }

    // --- N002-R01-04 regression: the validator previously checked only top-level key names,
    // so an allow-listed key's *value* could be an arbitrary nested object, array, oversized
    // string, or out-of-range/non-enumerated value. Each of these reproduces the exact bypass
    // the reviewer found and confirms it is now rejected. ---

    [Fact]
    public async Task Rejects_a_nested_object_smuggled_under_an_allow_listed_key()
    {
        // Reproduces the reviewer's exact bypass: RecordAsync("...", 0, new { category = new { patientName = "..." } })
        await using var db = _fixture.CreateContext();
        var sink = new MeasurementEventSink(db);

        var ex = await Assert.ThrowsAsync<MeasurementEventValidationException>(() =>
            sink.RecordAsync("review.synthetic-privacy", schemaVersion: 1,
                new { category = new { patientName = "SYNTHETIC_TEST_ONLY" } }));
        Assert.Contains("nested object/array", ex.Message);
    }

    [Fact]
    public void Rejects_an_array_value_under_an_allow_listed_key()
    {
        var ex = Assert.Throws<MeasurementEventValidationException>(() =>
            MeasurementEventValidator.ValidateProperties("{\"category\":[\"a\",\"b\"]}"));
        Assert.Contains("nested object/array", ex.Message);
    }

    [Fact]
    public void Rejects_an_oversized_string_value()
    {
        var tooLong = new string('a', 41);
        Assert.Throws<MeasurementEventValidationException>(() =>
            MeasurementEventValidator.ValidateProperties($"{{\"category\":\"{tooLong}\"}}"));
    }

    [Fact]
    public void Rejects_a_negative_count()
    {
        Assert.Throws<MeasurementEventValidationException>(() =>
            MeasurementEventValidator.ValidateProperties("{\"count\":-1}"));
    }

    [Fact]
    public void Rejects_an_outcome_value_outside_the_bounded_enumeration()
    {
        // The previous validator accepted "outcome":"ok" — not a defined outcome value.
        Assert.Throws<MeasurementEventValidationException>(() =>
            MeasurementEventValidator.ValidateProperties("{\"outcome\":\"ok\"}"));
    }

    [Fact]
    public async Task Rejects_schema_version_zero()
    {
        // Reproduces the reviewer's finding: RecordAsync(..., schemaVersion: 0, ...) previously succeeded.
        await using var db = _fixture.CreateContext();
        var sink = new MeasurementEventSink(db);

        await Assert.ThrowsAsync<MeasurementEventValidationException>(() =>
            sink.RecordAsync("review.synthetic-privacy", schemaVersion: 0, new { count = 1 }));
    }

    [Theory]
    [InlineData("")]
    [InlineData("NoDotsAtAll")]
    [InlineData("Has.Uppercase")]
    [InlineData("trailing.dot.")]
    public void Rejects_malformed_event_names(string eventName)
    {
        Assert.Throws<MeasurementEventValidationException>(() => MeasurementEventValidator.ValidateEventName(eventName));
    }

    [Fact]
    public void Accepts_a_well_formed_dotted_lowercase_event_name()
    {
        MeasurementEventValidator.ValidateEventName("appointment.scheduled"); // must not throw
    }
}
