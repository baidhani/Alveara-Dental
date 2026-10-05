using System.Net;
using System.Text.Json;
using Xunit;
using static Alveara.Api.Tests.SchedulingTestSupport;
using Session = Alveara.Api.Tests.SchedulingApiHarness.Session;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-006-C01 R02 at the HTTP boundary: the missing-tooth invariant seen by a client - the reviewer's sequence returns the stable conflict that names the way out and changes nothing, the same
/// tooth can still take an implant, the ordinary workflow of the parent story is untouched, and two simultaneous requests for the same tooth cannot both succeed into an inconsistent chart.
/// A real API, real SQL Server and a real signed-in session per role.
/// </summary>
public class ToothPresenceApiTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private SchedulingTestSupport _s = null!;
    private SchedulingApiHarness _api = null!;
    private Guid _ann;

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
        _s = new SchedulingTestSupport(_fixture);
        _api = new SchedulingApiHarness(_fixture);
        await _s.ArrangeAsync();
        _ann = await _s.PatientAsync();
    }
    public Task DisposeAsync() { _api.Dispose(); return _fixture.DisposeAsync(); }

    private string Chart => $"/api/patients/{_ann}/odontogram";
    private static string Error(JsonElement e) => e.GetProperty("error").GetString()!;
    private Task<(HttpStatusCode Status, JsonElement Body)> Record(Session by, string tooth, string? surface, string condition, string state) =>
        by.SendAsync(HttpMethod.Post, Chart + "/findings", new { toothKey = tooth, surface, condition, state });
    private static JsonElement Finding(JsonElement chart, string tooth, string condition) =>
        chart.GetProperty("findings").EnumerateArray().Single(f => f.GetProperty("toothKey").GetString() == tooth && f.GetProperty("condition").GetString() == condition);

    [Fact]
    public async Task The_reviewers_sequence_over_http_is_refused_with_the_stable_conflict_that_names_the_way_out_and_changes_nothing()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var missing = Finding((await Record(dentist, "16", null, "Missing", "Planned")).Body, "16", "Missing");
        Assert.Equal(HttpStatusCode.OK, (await Record(dentist, "16", "O", "Caries", "Diagnosed")).Status);

        var move = await dentist.SendAsync(HttpMethod.Post, $"/api/odontogram/findings/{missing.GetProperty("id").GetString()}/state", new { state = "Completed", rowVersion = missing.GetProperty("rowVersion").GetString() });
        Assert.Equal((HttpStatusCode.Conflict, "tooth_has_findings"), (move.Status, Error(move.Body)));
        Assert.Contains("Withdraw them", move.Body.GetProperty("message").GetString());
        Assert.Contains("Nothing was changed", move.Body.GetProperty("message").GetString());

        var chart = (await dentist.SendAsync(HttpMethod.Get, Chart)).Body;
        var again = Finding(chart, "16", "Missing");
        Assert.Equal(("Planned", missing.GetProperty("rowVersion").GetString()), (again.GetProperty("state").GetString(), again.GetProperty("rowVersion").GetString()));       // not even the row version moved
        Assert.Equal(2, await _s.CountAsync(db => db.ToothFindingVersions));
    }

    [Fact]
    public async Task Recording_a_missing_tooth_over_a_tooth_with_findings_is_a_409_and_a_finding_on_a_missing_tooth_is_a_409_with_their_own_codes()
    {
        var dentist = await _api.SessionAsync("Dentist");
        Assert.Equal(HttpStatusCode.OK, (await Record(dentist, "26", "D", "Caries", "Existing")).Status);
        var over = await Record(dentist, "26", null, "Missing", "Existing");
        Assert.Equal((HttpStatusCode.Conflict, "tooth_has_findings"), (over.Status, Error(over.Body)));

        Assert.Equal(HttpStatusCode.OK, (await Record(dentist, "36", null, "Missing", "Completed")).Status);
        var onto = await Record(dentist, "36", "O", "Caries", "Diagnosed");
        Assert.Equal((HttpStatusCode.Conflict, "tooth_absent"), (onto.Status, Error(onto.Body)));
        Assert.Equal(2, await _s.CountAsync(db => db.ToothFindings));
    }

    [Fact]
    public async Task The_way_out_and_the_replacement_both_work_over_http_and_the_parents_ordinary_workflow_is_untouched()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var chart = (await Record(dentist, "16", null, "Missing", "Planned")).Body;
        chart = (await Record(dentist, "16", "O", "Caries", "Diagnosed")).Body;
        var caries = Finding(chart, "16", "Caries");
        var withdrawn = await dentist.SendAsync(HttpMethod.Post, $"/api/odontogram/findings/{caries.GetProperty("id").GetString()}/withdraw", new { reason = "Tooth extracted", rowVersion = caries.GetProperty("rowVersion").GetString() });
        var missing = Finding(withdrawn.Body, "16", "Missing");
        var completed = await dentist.SendAsync(HttpMethod.Post, $"/api/odontogram/findings/{missing.GetProperty("id").GetString()}/state", new { state = "Completed", rowVersion = missing.GetProperty("rowVersion").GetString() });
        Assert.Equal(HttpStatusCode.OK, completed.Status);
        Assert.Equal(HttpStatusCode.OK, (await Record(dentist, "16", null, "Implant", "Planned")).Status);                     // a replacement is still allowed

        // the parent story's own workflow: record in a chosen state, plan, complete, on an ordinary tooth
        var parent = Finding((await Record(dentist, "46", "O", "Caries", "Diagnosed")).Body, "46", "Caries");
        var planned = await dentist.SendAsync(HttpMethod.Post, $"/api/odontogram/findings/{parent.GetProperty("id").GetString()}/state", new { state = "Planned", rowVersion = parent.GetProperty("rowVersion").GetString() });
        var next = Finding(planned.Body, "46", "Caries");
        var done = await dentist.SendAsync(HttpMethod.Post, $"/api/odontogram/findings/{next.GetProperty("id").GetString()}/state", new { state = "Completed", rowVersion = next.GetProperty("rowVersion").GetString() });
        Assert.Equal("Completed", Finding(done.Body, "46", "Caries").GetProperty("state").GetString());
    }

    [Fact]
    public async Task Two_simultaneous_requests_for_the_same_tooth_cannot_both_succeed_into_an_inconsistent_chart()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var hygienist = await _api.SessionAsync("Hygienist");
        var teeth = new[] { "11", "12", "13", "14", "15", "17" };
        var results = await Task.WhenAll(teeth.Select(async tooth =>
        {
            var gate = new TaskCompletionSource();
            async Task<string> Try(Session by, string? surface, string condition, string state)
            {
                await gate.Task;
                var r = await Record(by, tooth, surface, condition, state);
                return r.Status == HttpStatusCode.OK ? "ok" : Error(r.Body);
            }
            var a = Task.Run(() => Try(dentist, null, "Missing", "Existing"));
            var b = Task.Run(() => Try(hygienist, tooth[1] <= '3' ? "I" : "O", "Caries", "Existing"));
            gate.SetResult();
            return (tooth, missing: await a, caries: await b);
        }));
        foreach (var (tooth, missing, caries) in results)
        {
            Assert.False(missing == "ok" && caries == "ok", $"tooth {tooth}: both requests succeeded");
            Assert.True(missing == "ok" || caries == "ok", $"tooth {tooth}: neither request succeeded ({missing}, {caries})");
            foreach (var outcome in new[] { missing, caries }.Where(o => o != "ok")) Assert.Contains(outcome, new[] { "tooth_absent", "tooth_has_findings", "tooth_busy" });
        }
        var chart = (await dentist.SendAsync(HttpMethod.Get, Chart)).Body.GetProperty("findings").EnumerateArray().ToList();
        foreach (var tooth in teeth)
        {
            var onTooth = chart.Where(f => f.GetProperty("toothKey").GetString() == tooth).ToList();
            var absent = onTooth.Any(f => f.GetProperty("condition").GetString() == "Missing" && f.GetProperty("state").GetString() is "Existing" or "Completed");
            Assert.False(absent && onTooth.Any(f => f.GetProperty("condition").GetString() == "Caries"), $"tooth {tooth} holds an absence and an ordinary finding");
        }
    }
}
