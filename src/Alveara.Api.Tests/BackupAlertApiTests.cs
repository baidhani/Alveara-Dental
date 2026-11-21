using System.Net;
using Alveara.Api.Architecture.Backup;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N026 (PD-25, D-06) at the HTTP boundary: the administrator's System Status sections and the backup alerts are for administrators only (by role, whatever is granted), answer with counts and
/// states only (no paths, usernames or keys), and acknowledging needs a CSRF token. The public health endpoint stays open, as maintenance and deployment checks rely on it.
/// </summary>
public class BackupAlertApiTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private SchedulingTestSupport _s = null!;
    private SchedulingApiHarness _api = null!;

    public Task InitializeAsync() => _fixture.InitializeOwnerAsync(async () =>
    {
        _s = new SchedulingTestSupport(_fixture);
        _api = new SchedulingApiHarness(_fixture);
        await _s.ArrangeAsync();
    });
    public Task DisposeAsync() { _api.Dispose(); return _fixture.DisposeAsync(); }

    [Theory]
    [InlineData("Admin", HttpStatusCode.OK)]
    [InlineData("OfficeManager", HttpStatusCode.Forbidden)] [InlineData("Dentist", HttpStatusCode.Forbidden)] [InlineData("FrontDesk", HttpStatusCode.Forbidden)] [InlineData("Billing", HttpStatusCode.Forbidden)]
    public async Task The_administrator_sections_and_the_alerts_are_for_administrators_only(string role, HttpStatusCode expected)
    {
        var by = await _api.SessionAsync(role);
        Assert.Equal(expected, (await by.Client.GetAsync("/api/systemstatus/admin")).StatusCode);
        Assert.Equal(expected, (await by.Client.GetAsync("/api/backup/alerts/unacknowledged")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await by.Client.GetAsync("/api/systemstatus")).StatusCode);                            // the public health check stays open
    }

    [Fact]
    public async Task Anonymous_callers_get_401_from_the_administrator_routes()
    {
        var anon = _api.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/systemstatus/admin")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/backup/alerts/unacknowledged")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsync($"/api/backup/alerts/{Guid.NewGuid()}/acknowledge", null)).StatusCode);
    }

    [Fact]
    public async Task The_administrator_sections_hold_counts_and_states_only()
    {
        var admin = await _api.SessionAsync("Admin");
        var body = await (await admin.Client.GetAsync("/api/systemstatus/admin")).Content.ReadAsStringAsync();
        var json = System.Text.Json.JsonDocument.Parse(body).RootElement;
        foreach (var section in new[] { "backup", "storage", "accounts" }) Assert.NotEqual(System.Text.Json.JsonValueKind.Null, json.GetProperty(section).ValueKind);
        Assert.True(json.GetProperty("accounts").GetProperty("enabled").GetInt32() >= 1);
        Assert.True(json.GetProperty("storage").GetProperty("documentFolderReachable").ValueKind is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False);
        Assert.DoesNotContain(":\\\\", body);                                                                                 // no Windows path
        Assert.DoesNotContain("BEGIN PGP", body);
        Assert.DoesNotContain("admin-", body, StringComparison.OrdinalIgnoreCase);                                             // no username (the harness names accounts admin-... and user-...)
        Assert.DoesNotContain("user-", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Acknowledging_needs_a_csrf_token_and_answers_404_for_an_unknown_alert()
    {
        var admin = await _api.SessionAsync("Admin");
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.SendAsync(HttpMethod.Post, $"/api/backup/alerts/{Guid.NewGuid()}/acknowledge", csrf: false)).Status);
        var unknown = await admin.SendAsync(HttpMethod.Post, $"/api/backup/alerts/{Guid.NewGuid()}/acknowledge");
        Assert.Equal((HttpStatusCode.NotFound, "alert_not_found"), (unknown.Status, unknown.Body.GetProperty("error").GetString()));

        await using (var db = _fixture.CreateContext())
        {
            db.BackupNotifications.Add(new Alveara.Api.Architecture.Backup.BackupNotificationRecord { Id = Guid.NewGuid(), Kind = BackupAlertKinds.BackupFailed, Message = "Backup failed for a test.", CreatedAtUtc = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }
        var listed = await admin.SendAsync(HttpMethod.Get, "/api/backup/alerts/unacknowledged");
        var id = listed.Body.EnumerateArray().Single().GetProperty("id").GetString();
        Assert.Equal(HttpStatusCode.OK, (await admin.SendAsync(HttpMethod.Post, $"/api/backup/alerts/{id}/acknowledge")).Status);
        Assert.Empty((await admin.SendAsync(HttpMethod.Get, "/api/backup/alerts/unacknowledged")).Body.EnumerateArray());
    }
}
