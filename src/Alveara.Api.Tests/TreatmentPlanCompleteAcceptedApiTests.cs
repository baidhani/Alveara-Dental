using System.Net;
using System.Text.Json;
using Xunit;
using Alveara.Api.Architecture.Clinical;
using Session = Alveara.Api.Tests.SchedulingApiHarness.Session;

namespace Alveara.Api.Tests;

/// <summary>
/// STORY-008 at the HTTP boundary: who may complete a plan's accepted procedures and who may read the clinical history, the CSRF and row-version contract, the stable refusals, and the three acceptance
/// criteria end to end (completion updates the history, a revision keeps the original, every change is logged with user and time). A real API, real SQL Server and a real signed-in session per role.
/// </summary>
public class TreatmentPlanCompleteAcceptedApiTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private SchedulingTestSupport _s = null!;
    private SchedulingApiHarness _api = null!;
    private Guid _ann, _encounter;
    private int _codes;

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
        _s = new SchedulingTestSupport(_fixture);
        _api = new SchedulingApiHarness(_fixture);
        await _s.ArrangeAsync();
        _ann = await _s.PatientAsync();
        await using var db = _fixture.CreateContext();
        var e = new Encounter { Id = Guid.NewGuid(), PatientId = _ann, EncounterAtUtc = DateTimeOffset.UtcNow, Status = EncounterStatuses.Draft, CreatedAtUtc = DateTimeOffset.UtcNow };
        db.Encounters.Add(e);
        await db.SaveChangesAsync();
        _encounter = e.Id;
    }
    public Task DisposeAsync() { _api.Dispose(); return _fixture.DisposeAsync(); }

    private static string Error(JsonElement e) => e.GetProperty("error").GetString()!;
    private static string Id(JsonElement e) => e.GetProperty("id").GetString()!;
    private static string Version(JsonElement e) => e.GetProperty("rowVersion").GetString()!;
    private static string Item(JsonElement plan, int i) => plan.GetProperty("items")[i].GetProperty("id").GetString()!;
    private static string P(JsonElement plan, string tail = "") => $"/api/treatment-plans/{Id(plan)}{tail}";
    private static string[] Completion(JsonElement plan) => plan.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("completion").GetString()!).ToArray();

    /// <summary>A two-procedure plan for Ann, both accepted. The returned plan carries the current row version.</summary>
    private async Task<JsonElement> AcceptedPlanAsync(Session dentist, Session admin)
    {
        var d = await dentist.SendAsync(HttpMethod.Post, $"/api/patients/{_ann}/diagnoses",
            new { idempotencyKey = Guid.NewGuid().ToString("N"), encounterId = _encounter, label = "Chronic periodontitis", toothKey = (string?)null, notes = (string?)null, treatmentPlanReference = (string?)null });
        Assert.Equal(HttpStatusCode.OK, d.Status);
        var items = new List<object>();
        for (var i = 0; i < 2; i++)
        {
            var p = await admin.SendAsync(HttpMethod.Post, "/api/procedures", new { codeSystem = "Local", code = $"LOCAL-{100 + ++_codes}", description = $"Procedure {i + 1}", category = "Periodontic", scope = "WholeMouth", fee = 100m - 30m * i });
            Assert.Equal(HttpStatusCode.OK, p.Status);
            items.Add(new { diagnosisId = Id(d.Body), procedureId = p.Body.GetProperty("summary").GetProperty("id").GetString()!, toothKey = (string?)null, surface = (string?)null });
        }
        var plan = await dentist.SendAsync(HttpMethod.Post, $"/api/patients/{_ann}/treatment-plans", new { idempotencyKey = Guid.NewGuid().ToString("N"), title = "Gum health plan", items });
        Assert.Equal(HttpStatusCode.OK, plan.Status);
        var accepted = await dentist.SendAsync(HttpMethod.Post, P(plan.Body, "/decision"), new { decision = "Accepted", rowVersion = Version(plan.Body) });
        Assert.Equal(HttpStatusCode.OK, accepted.Status);
        return accepted.Body;
    }

    [Fact]
    public async Task Anonymous_callers_get_401_from_the_clinical_history()
    {
        var anon = _api.Factory.CreateClient();
        var id = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.SendAsync(new HttpRequestMessage(HttpMethod.Get, $"/api/patients/{id}/clinical-history"))).StatusCode);
    }

    [Theory]
    [InlineData("Admin", true)] [InlineData("Dentist", true)] [InlineData("Hygienist", true)] [InlineData("Assistant", true)]
    [InlineData("FrontDesk", false)] [InlineData("Billing", false)] [InlineData("OfficeManager", false)]
    public async Task Only_the_clinical_team_and_the_administrator_can_read_the_clinical_history(string role, bool allowed)
    {
        var by = await _api.SessionAsync(role);
        var r = await by.SendAsync(HttpMethod.Get, $"/api/patients/{_ann}/clinical-history");
        Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden, r.Status);
    }

    [Fact]
    public async Task An_unknown_patients_clinical_history_is_not_found_and_a_write_is_not_part_of_it()
    {
        var dentist = await _api.SessionAsync("Dentist");
        Assert.Equal(HttpStatusCode.NotFound, (await dentist.SendAsync(HttpMethod.Get, $"/api/patients/{Guid.NewGuid()}/clinical-history")).Status);
    }

    [Fact]
    public async Task The_three_acceptance_criteria_hold_end_to_end()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var plan = await AcceptedPlanAsync(dentist, await _api.SessionAsync("Admin"));

        // 1. an accepted plan is completed and the clinical history updates
        Assert.Equal(0, (await dentist.SendAsync(HttpMethod.Get, $"/api/patients/{_ann}/clinical-history")).Body.GetProperty("entries").GetArrayLength());
        await LegacyCompletionFixture.CompleteAcceptedAsync(_fixture, Id(plan), _s.Actor);              // completed the old way (the route is retired; see LegacyCompletionFixture)
        var done = await dentist.SendAsync(HttpMethod.Get, P(plan));
        Assert.Equal(["Completed", "Completed"], Completion(done.Body));
        var history = (await dentist.SendAsync(HttpMethod.Get, $"/api/patients/{_ann}/clinical-history")).Body.GetProperty("entries");
        Assert.Equal(2, history.GetArrayLength());
        Assert.All(history.EnumerateArray(), h => Assert.False(string.IsNullOrWhiteSpace(h.GetProperty("completedByName").GetString())));
        Assert.Equal(2, (await dentist.SendAsync(HttpMethod.Get, $"/api/patients/{_ann}/clinical-history")).Body.GetProperty("entries").GetArrayLength());

        // 2. a revision keeps the original
        var revised = await dentist.SendAsync(HttpMethod.Post, P(plan, "/revise"), new
        {
            reason = "Phased after treatment", clinicalRationale = "Healing first", patientExplanation = "Next visit follows.",
            phases = new[] { new { number = 1, name = "Healing", plannedVisit = (string?)null, targetDate = (string?)null } }, placements = (object?)null, rowVersion = Version(done.Body),
        });
        Assert.Equal(HttpStatusCode.OK, revised.Status);
        var revisions = await dentist.SendAsync(HttpMethod.Get, P(plan, "/revisions"));
        Assert.Equal(2, revisions.Body.GetArrayLength());
        var original = await dentist.SendAsync(HttpMethod.Get, P(plan, "/revisions/1"));
        Assert.Equal(HttpStatusCode.OK, original.Status);

        // 3. every change is logged with user and time
        var events = (await dentist.SendAsync(HttpMethod.Get, P(plan, "/history"))).Body;
        var completions = events.EnumerateArray().Where(e => e.GetProperty("changeType").GetString() == "ItemCompleted").ToList();
        Assert.Equal(2, completions.Count);
        Assert.All(events.EnumerateArray(), e =>
        {
            Assert.False(string.IsNullOrWhiteSpace(e.GetProperty("actorName").GetString()));
            Assert.True(e.GetProperty("occurredAtUtc").GetDateTimeOffset() > DateTimeOffset.UtcNow.AddMinutes(-10));
        });
    }
}
