using System.Net;
using System.Text.Json;
using Xunit;
using static Alveara.Api.Tests.SchedulingTestSupport;
using Session = Alveara.Api.Tests.SchedulingApiHarness.Session;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-006-C01 at the HTTP boundary: who may read the condition catalogue and a tooth's timeline, who may add, retire and reactivate a condition (the permission that already governs the
/// practice's clinical configuration - dentist and administrator) and who may link a finding, the CSRF and row-version contract, and the stable refusal codes. A real API, real SQL Server
/// and a real signed-in session per role.
/// </summary>
public class OdontogramCatalogueApiTests : IAsyncLifetime
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

    private static string Error(JsonElement e) => e.GetProperty("error").GetString()!;
    private const string Types = "/api/odontogram/condition-types";
    private string Chart => $"/api/patients/{_ann}/odontogram";
    private static JsonElement Type(JsonElement list, string code) => list.EnumerateArray().Single(t => t.GetProperty("code").GetString() == code);

    private async Task<JsonElement> CreateAsync(Session by, string code = "Fracture", string label = "Tooth fracture", string scope = "Surface", string appliesTo = "Both", string effect = "None")
    {
        var r = await by.SendAsync(HttpMethod.Post, Types, new { code, label, scope, appliesTo, toothEffect = effect });
        Assert.Equal(HttpStatusCode.OK, r.Status);
        return r.Body;
    }

    private async Task<JsonElement> RecordAsync(Session by, string tooth, string? surface, string condition, string state = "Diagnosed")
    {
        var r = await by.SendAsync(HttpMethod.Post, Chart + "/findings", new { toothKey = tooth, surface, condition, state });
        Assert.Equal(HttpStatusCode.OK, r.Status);
        return r.Body;
    }

    // ---------- authentication and roles ----------

    [Fact]
    public async Task Anonymous_callers_get_401_from_every_new_endpoint()
    {
        var anon = _api.Factory.CreateClient();
        var id = Guid.NewGuid();
        var calls = new (HttpMethod Method, string Url)[]
        {
            (HttpMethod.Get, Types), (HttpMethod.Post, Types), (HttpMethod.Get, $"{Types}/{id}/history"), (HttpMethod.Post, $"{Types}/{id}/retire"), (HttpMethod.Post, $"{Types}/{id}/reactivate"),
            (HttpMethod.Get, $"{Chart}/teeth/16/history"), (HttpMethod.Post, $"/api/odontogram/findings/{id}/links"),
        };
        foreach (var (method, url) in calls) Assert.Equal(HttpStatusCode.Unauthorized, (await anon.SendAsync(new HttpRequestMessage(method, url))).StatusCode);
    }

    [Theory]
    [InlineData("Admin", true)]
    [InlineData("Dentist", true)]
    [InlineData("Hygienist", true)]
    [InlineData("Assistant", true)]
    [InlineData("FrontDesk", false)]
    [InlineData("Billing", false)]
    [InlineData("OfficeManager", false)]
    public async Task Only_the_clinical_team_and_the_administrator_can_read_the_catalogue_and_a_teeth_timeline(string role, bool allowed)
    {
        var dentist = await _api.SessionAsync("Dentist");
        var list = await CreateAsync(dentist);
        var id = Type(list, "Fracture").GetProperty("id").GetString();
        var by = await _api.SessionAsync(role);
        var responses = new[] { await by.SendAsync(HttpMethod.Get, Types), await by.SendAsync(HttpMethod.Get, $"{Types}/{id}/history"), await by.SendAsync(HttpMethod.Get, $"{Chart}/teeth/16/history") };
        Assert.All(responses, r => Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden, r.Status));
    }

    [Theory]
    [InlineData("Admin", true)]
    [InlineData("Dentist", true)]
    [InlineData("Hygienist", false)]        // may record findings, may not change the practice's catalogue
    [InlineData("Assistant", false)]
    [InlineData("FrontDesk", false)]
    [InlineData("Billing", false)]
    [InlineData("OfficeManager", false)]
    public async Task Only_the_roles_that_manage_clinical_templates_can_add_retire_and_reactivate_a_condition(string role, bool allowed)
    {
        var dentist = await _api.SessionAsync("Dentist");
        var existing = Type(await CreateAsync(dentist, "Sealant", "Sealant", "Surface"), "Sealant");
        var by = await _api.SessionAsync(role);
        var statuses = new[]
        {
            (await by.SendAsync(HttpMethod.Post, Types, new { code = $"New{role}", label = "New", scope = "Surface", appliesTo = "Both", toothEffect = "None" })).Status,
            (await by.SendAsync(HttpMethod.Post, $"{Types}/{existing.GetProperty("id").GetString()}/retire", new { reason = "x", rowVersion = existing.GetProperty("rowVersion").GetString() })).Status,
            (await by.SendAsync(HttpMethod.Post, $"{Types}/{existing.GetProperty("id").GetString()}/reactivate", new { rowVersion = existing.GetProperty("rowVersion").GetString() })).Status,
        };
        if (allowed) Assert.DoesNotContain(HttpStatusCode.Forbidden, statuses);
        else
        {
            Assert.All(statuses, s => Assert.Equal(HttpStatusCode.Forbidden, s));
            Assert.Equal(7, (await dentist.SendAsync(HttpMethod.Get, Types)).Body.GetArrayLength());                        // nothing changed
            Assert.True(Type((await dentist.SendAsync(HttpMethod.Get, Types)).Body, "Sealant").GetProperty("isActive").GetBoolean());
        }
    }

    [Theory]
    [InlineData("Admin", true)]
    [InlineData("Dentist", true)]
    [InlineData("Hygienist", true)]
    [InlineData("Assistant", false)]
    [InlineData("FrontDesk", false)]
    [InlineData("Billing", false)]
    public async Task Only_roles_that_manage_clinical_notes_can_link_a_finding(string role, bool allowed)
    {
        var dentist = await _api.SessionAsync("Dentist");
        var id = (await RecordAsync(dentist, "16", "O", "Caries")).GetProperty("findings")[0].GetProperty("id").GetString();
        var by = await _api.SessionAsync(role);
        var r = await by.SendAsync(HttpMethod.Post, $"/api/odontogram/findings/{id}/links", new { linkType = "Diagnosis", reference = "DX-1" });
        Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden, r.Status);
        Assert.Equal(allowed ? 1 : 0, await _s.CountAsync(db => db.ToothFindingLinks));
    }

    // ---------- the workflow over HTTP ----------

    [Fact]
    public async Task A_dentist_adds_a_condition_records_it_retires_it_and_it_stops_new_findings_but_the_old_one_stays()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var hygienist = await _api.SessionAsync("Hygienist");
        var created = Type(await CreateAsync(dentist), "Fracture");
        Assert.Equal(("Tooth fracture", "Surface", "Both", "None", true), (created.GetProperty("label").GetString(), created.GetProperty("scope").GetString(), created.GetProperty("appliesTo").GetString(), created.GetProperty("toothEffect").GetString(), created.GetProperty("isActive").GetBoolean()));

        var chart = await RecordAsync(hygienist, "16", "O", "Fracture");                           // the hygienist records with the dentist's new condition
        var finding = chart.GetProperty("findings")[0];
        Assert.Equal(("Fracture", "Tooth fracture", "Surface"), (finding.GetProperty("condition").GetString(), finding.GetProperty("conditionLabel").GetString(), finding.GetProperty("conditionScope").GetString()));

        var noReason = await dentist.SendAsync(HttpMethod.Post, $"{Types}/{created.GetProperty("id").GetString()}/retire", new { rowVersion = created.GetProperty("rowVersion").GetString() });
        Assert.Equal((HttpStatusCode.BadRequest, "reason_required"), (noReason.Status, Error(noReason.Body)));
        var retired = await dentist.SendAsync(HttpMethod.Post, $"{Types}/{created.GetProperty("id").GetString()}/retire", new { reason = "Not used", rowVersion = created.GetProperty("rowVersion").GetString() });
        Assert.False(Type(retired.Body, "Fracture").GetProperty("isActive").GetBoolean());

        var blocked = await hygienist.SendAsync(HttpMethod.Post, Chart + "/findings", new { toothKey = "26", surface = "O", condition = "Fracture", state = "Diagnosed" });
        Assert.Equal((HttpStatusCode.Conflict, "condition_inactive"), (blocked.Status, Error(blocked.Body)));
        Assert.Equal("Tooth fracture", (await hygienist.SendAsync(HttpMethod.Get, Chart)).Body.GetProperty("findings")[0].GetProperty("conditionLabel").GetString());   // the old finding still reads as it did

        var history = (await dentist.SendAsync(HttpMethod.Get, $"{Types}/{created.GetProperty("id").GetString()}/history")).Body;
        Assert.Equal(new[] { "Created", "Retired" }, history.EnumerateArray().Select(e => e.GetProperty("changeType").GetString()!));
    }

    [Fact]
    public async Task A_teeth_timeline_and_a_finding_link_are_readable_over_the_api()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var assistant = await _api.SessionAsync("Assistant");
        var finding = (await RecordAsync(dentist, "55", "O", "Caries")).GetProperty("findings")[0];
        var id = finding.GetProperty("id").GetString();
        var linked = await dentist.SendAsync(HttpMethod.Post, $"/api/odontogram/findings/{id}/links", new { linkType = "Procedure", reference = "PROC-77" });
        var link = linked.Body.GetProperty("findings")[0].GetProperty("links")[0];
        Assert.Equal(("Procedure", "PROC-77"), (link.GetProperty("linkType").GetString(), link.GetProperty("reference").GetString()));
        Assert.Equal(finding.GetProperty("rowVersion").GetString(), linked.Body.GetProperty("findings")[0].GetProperty("rowVersion").GetString());     // linking does not change the finding's version

        var h = (await assistant.SendAsync(HttpMethod.Get, $"{Chart}/teeth/55/history")).Body;
        Assert.Equal("55", h.GetProperty("toothKey").GetString());
        Assert.Equal(new[] { "Recorded", "Linked" }, h.GetProperty("events").EnumerateArray().Select(e => e.GetProperty("changeType").GetString()!));
        Assert.Equal("Dr. Okafor".Length > 0, !string.IsNullOrWhiteSpace(h.GetProperty("events")[0].GetProperty("actorName").GetString()));
    }

    // ---------- the contract ----------

    [Fact]
    public async Task Every_change_needs_a_csrf_token_and_a_stale_or_missing_version_is_refused_with_stable_codes()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var t = Type(await CreateAsync(dentist), "Fracture");
        var id = t.GetProperty("id").GetString();
        var version = t.GetProperty("rowVersion").GetString();
        var finding = (await RecordAsync(dentist, "16", "O", "Caries")).GetProperty("findings")[0].GetProperty("id").GetString();

        Assert.Equal(HttpStatusCode.BadRequest, (await dentist.SendAsync(HttpMethod.Post, Types, new { code = "Other", label = "x", scope = "Surface", appliesTo = "Both" }, csrf: false)).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await dentist.SendAsync(HttpMethod.Post, $"{Types}/{id}/retire", new { reason = "x", rowVersion = version }, csrf: false)).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await dentist.SendAsync(HttpMethod.Post, $"/api/odontogram/findings/{finding}/links", new { linkType = "Diagnosis", reference = "DX-1" }, csrf: false)).Status);

        var noVersion = await dentist.SendAsync(HttpMethod.Post, $"{Types}/{id}/retire", new { reason = "x" });
        Assert.Equal((HttpStatusCode.BadRequest, "row_version_required"), (noVersion.Status, Error(noVersion.Body)));
        var bad = await dentist.SendAsync(HttpMethod.Post, $"{Types}/{id}/retire", new { reason = "x", rowVersion = "not-base64!" });
        Assert.Equal((HttpStatusCode.BadRequest, "row_version_invalid"), (bad.Status, Error(bad.Body)));

        await dentist.SendAsync(HttpMethod.Post, $"{Types}/{id}/retire", new { reason = "First", rowVersion = version });
        var stale = await dentist.SendAsync(HttpMethod.Post, $"{Types}/{id}/reactivate", new { rowVersion = version });
        Assert.Equal((HttpStatusCode.Conflict, "concurrency_conflict"), (stale.Status, Error(stale.Body)));
        Assert.Equal("ConditionType", stale.Body.GetProperty("entityType").GetString());
    }

    [Fact]
    public async Task Refusals_have_stable_codes_and_field_errors_a_client_can_show()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var bad = await dentist.SendAsync(HttpMethod.Post, Types, new { code = "x", label = "", scope = "Area", appliesTo = "Adult", toothEffect = "Gone" });
        Assert.Equal((HttpStatusCode.BadRequest, "validation_failed"), (bad.Status, Error(bad.Body)));
        Assert.Equal(new[] { "appliesTo", "code", "label", "scope", "toothEffect" }, bad.Body.GetProperty("fieldErrors").EnumerateObject().Select(p => p.Name).Order().ToArray());

        await CreateAsync(dentist);
        var dup = await dentist.SendAsync(HttpMethod.Post, Types, new { code = "FRACTURE", label = "Tooth fracture", scope = "Surface", appliesTo = "Both", toothEffect = "None" });
        Assert.Equal((HttpStatusCode.Conflict, "condition_exists"), (dup.Status, Error(dup.Body)));

        var unknown = await dentist.SendAsync(HttpMethod.Post, Chart + "/findings", new { toothKey = "16", surface = "O", condition = "Cavity", state = "Diagnosed" });
        Assert.Equal((HttpStatusCode.BadRequest, "validation_failed"), (unknown.Status, Error(unknown.Body)));
        Assert.True(unknown.Body.GetProperty("fieldErrors").TryGetProperty("condition", out _));
        var implantOnPrimary = await dentist.SendAsync(HttpMethod.Post, Chart + "/findings", new { toothKey = "54", condition = "Implant", state = "Existing" });
        Assert.True(implantOnPrimary.Body.GetProperty("fieldErrors").TryGetProperty("condition", out _));

        await RecordAsync(dentist, "16", null, "Missing", "Existing");
        var absent = await dentist.SendAsync(HttpMethod.Post, Chart + "/findings", new { toothKey = "16", surface = "O", condition = "Caries", state = "Diagnosed" });
        Assert.Equal((HttpStatusCode.Conflict, "tooth_absent"), (absent.Status, Error(absent.Body)));

        Assert.Equal("condition_not_found", Error((await dentist.SendAsync(HttpMethod.Get, $"{Types}/{Guid.NewGuid()}/history")).Body));
        var badTooth = await dentist.SendAsync(HttpMethod.Get, $"{Chart}/teeth/19/history");
        Assert.Equal((HttpStatusCode.BadRequest, "validation_failed"), (badTooth.Status, Error(badTooth.Body)));
        Assert.Equal("patient_not_found", Error((await dentist.SendAsync(HttpMethod.Get, $"/api/patients/{Guid.NewGuid()}/odontogram/teeth/16/history")).Body));
        var badLink = await dentist.SendAsync(HttpMethod.Post, $"/api/odontogram/findings/{Guid.NewGuid()}/links", new { linkType = "Note", reference = "" });
        Assert.Equal((HttpStatusCode.BadRequest, "validation_failed"), (badLink.Status, Error(badLink.Body)));
        var noFinding = await dentist.SendAsync(HttpMethod.Post, $"/api/odontogram/findings/{Guid.NewGuid()}/links", new { linkType = "Diagnosis", reference = "DX-1" });
        Assert.Equal((HttpStatusCode.NotFound, "finding_not_found"), (noFinding.Status, Error(noFinding.Body)));
    }

    [Fact]
    public async Task The_chart_response_carries_each_condition_label_scope_and_links_so_a_screen_needs_no_list_of_its_own()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var chart = await RecordAsync(dentist, "16", null, "Crown", "Existing");
        var f = chart.GetProperty("findings")[0];
        Assert.Equal(new[] { "id", "toothKey", "surface", "condition", "conditionLabel", "conditionScope", "state", "status", "recordedByName", "recordedAtUtc", "updatedByName", "updatedAtUtc", "rowVersion", "links" }.Order(),
            f.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal(("Crown", "WholeTooth", 0), (f.GetProperty("conditionLabel").GetString(), f.GetProperty("conditionScope").GetString(), f.GetProperty("links").GetArrayLength()));
    }
}
