using System.Net;
using System.Text.Json;
using Xunit;
using Session = Alveara.Api.Tests.SchedulingApiHarness.Session;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N005 at the HTTP boundary: who may read the procedure catalog (everyone who can see billing: dentist, front desk, billing, practice manager, administrator) and who may change it (billing,
/// practice manager, administrator), the CSRF and row-version contract, field-by-field refusals, and the stable codes the screen relies on. A real API, real SQL Server and a real
/// signed-in session per role.
/// </summary>
public class ProcedureCatalogApiTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private SchedulingTestSupport _s = null!;
    private SchedulingApiHarness _api = null!;

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
        _s = new SchedulingTestSupport(_fixture);
        _api = new SchedulingApiHarness(_fixture);
        await _s.ArrangeAsync();
    }
    public Task DisposeAsync() { _api.Dispose(); return _fixture.DisposeAsync(); }

    private const string Url = "/api/procedures";
    private static string Error(JsonElement e) => e.GetProperty("error").GetString()!;
    private static string Id(JsonElement detail) => detail.GetProperty("summary").GetProperty("id").GetString()!;
    private static string Version(JsonElement detail) => detail.GetProperty("summary").GetProperty("rowVersion").GetString()!;

    private static object Body(string code = "LOCAL-100", string description = "Office visit", decimal fee = 50m) =>
        new { codeSystem = "Local", code, description, category = "Diagnostic", scope = "WholeMouth", fee };

    private async Task<JsonElement> CreateAsync(Session by, string code = "LOCAL-100", decimal fee = 50m)
    {
        var r = await by.SendAsync(HttpMethod.Post, Url, Body(code, fee: fee));
        Assert.Equal(HttpStatusCode.OK, r.Status);
        return r.Body;
    }

    // ---------- authentication and roles ----------

    [Fact]
    public async Task Anonymous_callers_get_401_from_every_endpoint()
    {
        var anon = _api.Factory.CreateClient();
        var id = Guid.NewGuid();
        var calls = new (HttpMethod Method, string Url)[]
        {
            (HttpMethod.Get, Url), (HttpMethod.Get, $"{Url}/active"), (HttpMethod.Get, $"{Url}/{id}"), (HttpMethod.Get, $"{Url}/{id}/history"), (HttpMethod.Get, $"{Url}/{id}/usage"),
            (HttpMethod.Get, $"{Url}/{id}/snapshot"), (HttpMethod.Get, $"{Url}/versions/{id}"), (HttpMethod.Post, Url), (HttpMethod.Post, $"{Url}/{id}/revise"),
            (HttpMethod.Post, $"{Url}/{id}/inactivate"), (HttpMethod.Post, $"{Url}/{id}/reactivate"),
        };
        foreach (var (method, url) in calls) Assert.Equal(HttpStatusCode.Unauthorized, (await anon.SendAsync(new HttpRequestMessage(method, url))).StatusCode);
    }

    [Theory]
    [InlineData("Admin", true)]
    [InlineData("OfficeManager", true)]
    [InlineData("Billing", true)]
    [InlineData("Dentist", true)]
    [InlineData("FrontDesk", true)]
    [InlineData("Hygienist", false)]
    [InlineData("Assistant", false)]
    public async Task Whoever_can_see_billing_can_read_the_catalog(string role, bool allowed)
    {
        var created = await CreateAsync(await _api.SessionAsync("Admin"));
        var by = await _api.SessionAsync(role);
        var responses = new[]
        {
            await by.SendAsync(HttpMethod.Get, Url), await by.SendAsync(HttpMethod.Get, $"{Url}/active"), await by.SendAsync(HttpMethod.Get, $"{Url}/{Id(created)}"),
            await by.SendAsync(HttpMethod.Get, $"{Url}/{Id(created)}/history"), await by.SendAsync(HttpMethod.Get, $"{Url}/{Id(created)}/usage"), await by.SendAsync(HttpMethod.Get, $"{Url}/{Id(created)}/snapshot"),
        };
        Assert.All(responses, r => Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden, r.Status));
    }

    [Theory]
    [InlineData("Admin", true)]
    [InlineData("OfficeManager", true)]
    [InlineData("Billing", true)]
    [InlineData("Dentist", false)]
    [InlineData("FrontDesk", false)]
    [InlineData("Hygienist", false)]
    [InlineData("Assistant", false)]
    public async Task Only_billing_the_practice_manager_and_the_administrator_can_change_the_catalog(string role, bool allowed)
    {
        var existing = await CreateAsync(await _api.SessionAsync("Admin"), "EXISTING-1");
        var by = await _api.SessionAsync(role);
        var version = Version(existing);
        var statuses = new[]
        {
            (await by.SendAsync(HttpMethod.Post, Url, Body("NEW-1"))).Status,
            (await by.SendAsync(HttpMethod.Post, $"{Url}/{Id(existing)}/revise", new { codeSystem = "Local", code = "EXISTING-1", description = "Office visit", category = "Diagnostic", scope = "WholeMouth", fee = 55m, reason = "Review", rowVersion = version })).Status,
        };
        Assert.All(statuses, s => Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden, s));
        if (!allowed) Assert.Equal(HttpStatusCode.Forbidden, (await by.SendAsync(HttpMethod.Post, $"{Url}/{Id(existing)}/inactivate", new { reason = "x", rowVersion = version })).Status);
    }

    // ---------- the contract ----------

    [Fact]
    public async Task Every_change_needs_a_csrf_token()
    {
        var admin = await _api.SessionAsync("Admin");
        var created = await CreateAsync(admin);
        var id = Id(created);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.SendAsync(HttpMethod.Post, Url, Body("NO-CSRF"), csrf: false)).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.SendAsync(HttpMethod.Post, $"{Url}/{id}/inactivate", new { reason = "x", rowVersion = Version(created) }, csrf: false)).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.SendAsync(HttpMethod.Post, $"{Url}/{id}/reactivate", new { reason = "x", rowVersion = Version(created) }, csrf: false)).Status);
        Assert.Single((await admin.SendAsync(HttpMethod.Get, Url)).Body.EnumerateArray());
    }

    [Fact]
    public async Task A_wrong_entry_is_refused_with_a_message_per_field_in_one_answer()
    {
        var admin = await _api.SessionAsync("Admin");
        var r = await admin.SendAsync(HttpMethod.Post, Url, new { codeSystem = "Local", code = "D1234", description = "", category = "Cosmetic", scope = "WholeMouth", fee = -5m });
        Assert.Equal((HttpStatusCode.BadRequest, "validation_failed"), (r.Status, Error(r.Body)));
        var fields = r.Body.GetProperty("fieldErrors");
        foreach (var f in new[] { "code", "description", "category", "fee" }) Assert.True(fields.TryGetProperty(f, out _), $"{f} should be named");
        Assert.Empty((await admin.SendAsync(HttpMethod.Get, Url)).Body.EnumerateArray());
    }

    [Fact]
    public async Task A_duplicate_code_is_a_409_and_an_identical_repeat_is_quiet()
    {
        var admin = await _api.SessionAsync("Admin");
        var first = await CreateAsync(admin);
        Assert.Equal(Id(first), Id(await CreateAsync(admin)));
        var clash = await admin.SendAsync(HttpMethod.Post, Url, Body(description: "A different one", fee: 99m));
        Assert.Equal((HttpStatusCode.Conflict, "procedure_exists"), (clash.Status, Error(clash.Body)));
    }

    [Fact]
    public async Task A_fee_change_is_a_new_version_and_a_stale_edit_is_a_409_that_changes_nothing()
    {
        var admin = await _api.SessionAsync("Billing");
        var created = await CreateAsync(admin);
        object Revise(decimal fee, string version) => new { codeSystem = "Local", code = "LOCAL-100", description = "Office visit", category = "Diagnostic", scope = "WholeMouth", fee, reason = "Review", rowVersion = version };

        var revised = await admin.SendAsync(HttpMethod.Post, $"{Url}/{Id(created)}/revise", Revise(60m, Version(created)));
        Assert.Equal(HttpStatusCode.OK, revised.Status);
        Assert.Equal(2, revised.Body.GetProperty("summary").GetProperty("currentVersionNumber").GetInt32());

        var stale = await admin.SendAsync(HttpMethod.Post, $"{Url}/{Id(created)}/revise", Revise(99m, Version(created)));
        Assert.Equal(HttpStatusCode.Conflict, stale.Status);
        Assert.Equal(60m, (await admin.SendAsync(HttpMethod.Get, $"{Url}/{Id(created)}")).Body.GetProperty("summary").GetProperty("version").GetProperty("fee").GetDecimal());

        var noReason = await admin.SendAsync(HttpMethod.Post, $"{Url}/{Id(created)}/revise", new { codeSystem = "Local", code = "LOCAL-100", description = "Office visit", category = "Diagnostic", scope = "WholeMouth", fee = 70m, rowVersion = Version(revised.Body) });
        Assert.Equal(HttpStatusCode.BadRequest, noReason.Status);
    }

    [Fact]
    public async Task Inactivating_and_reactivating_work_over_http_with_a_reason_and_leave_the_history_readable()
    {
        var admin = await _api.SessionAsync("OfficeManager");
        var created = await CreateAsync(admin);
        var id = Id(created);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.SendAsync(HttpMethod.Post, $"{Url}/{id}/inactivate", new { rowVersion = Version(created) })).Status);

        var off = await admin.SendAsync(HttpMethod.Post, $"{Url}/{id}/inactivate", new { reason = "Paused", rowVersion = Version(created) });
        Assert.Equal(HttpStatusCode.OK, off.Status);
        Assert.Empty((await admin.SendAsync(HttpMethod.Get, $"{Url}/active")).Body.EnumerateArray());

        var on = await admin.SendAsync(HttpMethod.Post, $"{Url}/{id}/reactivate", new { reason = "Back", rowVersion = Version(off.Body) });
        Assert.Equal(HttpStatusCode.OK, on.Status);
        var history = (await admin.SendAsync(HttpMethod.Get, $"{Url}/{id}/history")).Body;
        Assert.Equal(new[] { "Created", "Inactivated", "Reactivated" }, history.EnumerateArray().Select(h => h.GetProperty("changeType").GetString()!));
    }

    [Fact]
    public async Task Unknown_ids_are_404_and_bad_filters_are_400()
    {
        var admin = await _api.SessionAsync("Admin");
        Assert.Equal(HttpStatusCode.NotFound, (await admin.SendAsync(HttpMethod.Get, $"{Url}/{Guid.NewGuid()}")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.SendAsync(HttpMethod.Get, $"{Url}/versions/{Guid.NewGuid()}")).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.SendAsync(HttpMethod.Get, $"{Url}?status=bogus")).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.SendAsync(HttpMethod.Get, $"{Url}/active?toothKey=99")).Status);
    }
}
