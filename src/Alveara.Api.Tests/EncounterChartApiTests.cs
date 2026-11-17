using System.Net;
using System.Text.Json;
using Xunit;
using Session = Alveara.Api.Tests.SchedulingApiHarness.Session;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N022 A1 at the HTTP boundary: the two new encounter routes (a chart item's status, "confirm current") are protected like every other clinical write - 401 without a
/// session, 403 without ManageClinicalNotes, CSRF required - and a refused allergy add returns the stable 409 <c>duplicate_item</c> body with <c>existingItem</c>
/// (id, kind, status), which the screen needs to offer "update" or "reactivate or update". Behaviour is tested in <see cref="EncounterChartRoutingTests"/>.
/// </summary>
public class EncounterChartApiTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private SchedulingTestSupport _s = null!;
    private SchedulingApiHarness _api = null!;
    private Guid _ann;

    public Task InitializeAsync() => _fixture.InitializeOwnerAsync(async () =>
    {
        _s = new SchedulingTestSupport(_fixture);
        _api = new SchedulingApiHarness(_fixture);
        await _s.ArrangeAsync();
        _ann = await _s.PatientAsync();
    });
    public Task DisposeAsync() { _api.Dispose(); return _fixture.DisposeAsync(); }

    private static string Version(JsonElement e) => e.GetProperty("rowVersion").GetString()!;
    private static string Url(JsonElement e, string tail) => $"/api/encounters/{e.GetProperty("id").GetString()}{tail}";
    private static JsonElement Section(JsonElement e, string kind) => e.GetProperty("sections").EnumerateArray().Single(s => s.GetProperty("kind").GetString() == kind);

    /// <summary>A draft encounter with one allergy added in it (so it is on the chart): the encounter and the item.</summary>
    private async Task<(JsonElement Encounter, JsonElement Item)> DraftWithAllergyAsync(Session dentist)
    {
        var started = await dentist.SendAsync(HttpMethod.Post, $"/api/patients/{_ann}/encounters", new { });
        var added = await dentist.SendAsync(HttpMethod.Post, Url(started.Body, "/entries"), new { kind = "Allergy", name = "Penicillin", reaction = "Hives", rowVersion = Version(started.Body) });
        Assert.Equal(HttpStatusCode.OK, added.Status);
        return (added.Body, Section(added.Body, "Allergy").GetProperty("chartItems")[0]);
    }

    [Fact]
    public async Task The_new_routes_need_a_session()
    {
        var anon = _api.Factory.CreateClient();
        var id = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.SendAsync(new HttpRequestMessage(HttpMethod.Post, $"/api/encounters/{id}/entries/{id}/status"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.SendAsync(new HttpRequestMessage(HttpMethod.Post, $"/api/encounters/{id}/sections/Allergy/confirm-current"))).StatusCode);
    }

    [Theory]
    [InlineData("Admin", true)]
    [InlineData("Dentist", true)]
    [InlineData("Hygienist", true)]
    [InlineData("Assistant", false)]
    [InlineData("FrontDesk", false)]
    [InlineData("Billing", false)]
    [InlineData("OfficeManager", false)]
    public async Task Only_roles_that_manage_clinical_notes_can_change_a_chart_item_status_or_confirm_a_list_from_an_encounter(string role, bool allowed)
    {
        var dentist = await _api.SessionAsync("Dentist");
        var (e, item) = await DraftWithAllergyAsync(dentist);
        var by = await _api.SessionAsync(role);

        var status = await by.SendAsync(HttpMethod.Post, Url(e, $"/entries/{item.GetProperty("id").GetString()}/status"),
            new { status = "Resolved", rowVersion = Version(e), itemRowVersion = item.GetProperty("rowVersion").GetString() });
        var current = status.Status == HttpStatusCode.OK ? Version(status.Body) : Version(e);
        var confirm = await by.SendAsync(HttpMethod.Post, Url(e, "/sections/Allergy/confirm-current"), new { rowVersion = current });

        if (allowed)
        {
            Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (status.Status, confirm.Status));
            Assert.Equal("Resolved", Section(status.Body, "Allergy").GetProperty("chartItems")[0].GetProperty("status").GetString());
            Assert.Equal("Reviewed", Section(confirm.Body, "Allergy").GetProperty("reviewedHere").GetString());
        }
        else
        {
            Assert.Equal((HttpStatusCode.Forbidden, HttpStatusCode.Forbidden), (status.Status, confirm.Status));
            var now = (await dentist.SendAsync(HttpMethod.Get, Url(e, ""))).Body;
            Assert.Equal(Version(e), Version(now)); // nothing changed
            Assert.Equal("Active", Section(now, "Allergy").GetProperty("chartItems")[0].GetProperty("status").GetString());
        }
    }

    [Fact]
    public async Task The_new_routes_need_a_csrf_token()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var (e, item) = await DraftWithAllergyAsync(dentist);
        var status = await dentist.SendAsync(HttpMethod.Post, Url(e, $"/entries/{item.GetProperty("id").GetString()}/status"),
            new { status = "Resolved", rowVersion = Version(e), itemRowVersion = item.GetProperty("rowVersion").GetString() }, csrf: false);
        var confirm = await dentist.SendAsync(HttpMethod.Post, Url(e, "/sections/Allergy/confirm-current"), new { rowVersion = Version(e) }, csrf: false);
        Assert.Equal((HttpStatusCode.BadRequest, HttpStatusCode.BadRequest), (status.Status, confirm.Status));
    }

    [Fact]
    public async Task A_refused_duplicate_allergy_returns_409_duplicate_item_pointing_to_the_existing_item()
    {
        var dentist = await _api.SessionAsync("Dentist");
        var (e, item) = await DraftWithAllergyAsync(dentist);

        var dup = await dentist.SendAsync(HttpMethod.Post, Url(e, "/entries"), new { kind = "Allergy", name = " penicillin ", reaction = "Rash", rowVersion = Version(e) });
        Assert.Equal((HttpStatusCode.Conflict, "duplicate_item"), (dup.Status, dup.Body.GetProperty("error").GetString()));
        var existing = dup.Body.GetProperty("existingItem");
        Assert.Equal((item.GetProperty("id").GetString(), "Allergy", "Active"),
            (existing.GetProperty("id").GetString(), existing.GetProperty("kind").GetString(), existing.GetProperty("status").GetString()));

        // other refusals do not carry it
        var other = await dentist.SendAsync(HttpMethod.Post, Url(e, "/entries"), new { kind = "Allergy", name = "  ", rowVersion = Version(e) });
        Assert.False(other.Body.TryGetProperty("existingItem", out _));
    }
}
