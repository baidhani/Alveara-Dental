using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Alveara.Api.Architecture.Scheduling;
using Alveara.Api.Controllers;
using Xunit;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>
/// STORY-011 at the HTTP boundary: who may move a patient through check-in, treatment and completion, the CSRF and row-version contract, and the stable
/// refusal shapes. The harness (real API, real SQL Server, real sessions per role) is the same as ALV-004-C01's AppointmentLifecycleApiTests.
/// </summary>
public class PatientFlowApiTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private SchedulingTestSupport _s = null!;
    private Guid _ann;
    private Session? _admin;

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
        _s = new SchedulingTestSupport(_fixture);
        await _s.ArrangeAsync();
        _ann = await _s.PatientAsync();
    }
    public Task DisposeAsync() => _fixture.DisposeAsync();

    private WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Alveara", _fixture.ConnectionString);
            builder.UseSetting("AdminBootstrapSecret", IdentityTestHelpers.TestBootstrapSecret);
            builder.UseSetting("AuthAttemptRateLimit:PermitLimit", "500");
        });

    private static async Task<string> CsrfAsync(HttpClient client) =>
        (await (await client.GetAsync("/api/auth/csrf-token")).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;

    private sealed record Session(HttpClient Client, string Csrf)
    {
        public async Task<(HttpStatusCode Status, JsonElement Body)> SendAsync(HttpMethod method, string url, object? body = null, string? key = null, bool csrf = true)
        {
            var request = new HttpRequestMessage(method, url);
            if (csrf) request.Headers.Add("X-CSRF-Token", Csrf);
            if (key is not null) request.Headers.Add("Idempotency-Key", key);
            if (body is not null) request.Content = JsonContent.Create(body);
            var response = await Client.SendAsync(request);
            var text = await response.Content.ReadAsStringAsync();
            return (response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone());
        }
    }

    private async Task<Session> SessionAsync(WebApplicationFactory<Program> factory, string role)
    {
        if (_admin is null)
        {
            var first = factory.CreateClient();
            var adminName = $"admin-{Guid.NewGuid():N}";
            await first.PostAsJsonAsync("/api/auth/bootstrap-admin", new BootstrapAdminRequest(adminName, "admin-password", IdentityTestHelpers.TestBootstrapSecret));
            await first.PostAsJsonAsync("/api/auth/login", new LoginRequest(adminName, "admin-password"));
            _admin = new Session(first, await CsrfAsync(first));
        }
        if (role == "Admin") return _admin;

        var username = $"user-{Guid.NewGuid():N}";
        var id = (await (await _admin.Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(username, "password"))).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await _admin.SendAsync(HttpMethod.Put, $"/api/auth/{id}/role", new ChangeRoleRequest(role));
        await _admin.SendAsync(HttpMethod.Put, $"/api/auth/{id}/enabled", new SetEnabledRequest(true));
        var client = factory.CreateClient();
        await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(username, "password"));
        return new Session(client, await CsrfAsync(client));
    }

    private async Task<JsonElement> BookAsync(Session session, DateTime start)
    {
        var body = new
        {
            patientId = _ann, providerId = _s.ProviderA, operatoryId = _s.Op1, appointmentTypeId = _s.Long60,
            startLocal = start.ToString("yyyy-MM-ddTHH:mm"), durationMinutes = (int?)null,
        };
        var r = await session.SendAsync(HttpMethod.Post, "/api/appointments", body, $"book-{Guid.NewGuid():N}");
        Assert.Equal(HttpStatusCode.Created, r.Status);
        return r.Body;
    }

    private static string Url(JsonElement a, string tail) => $"/api/appointments/{a.GetProperty("id").GetString()}{tail}";
    private static object Version(JsonElement a) => new { rowVersion = a.GetProperty("rowVersion").GetString() };

    // ---------- authorization ----------

    [Fact]
    public async Task Anonymous_callers_get_401_from_every_flow_endpoint()
    {
        await using var factory = CreateFactory();
        var anon = factory.CreateClient();
        var id = Guid.NewGuid();
        foreach (var tail in new[] { "check-in", "start-treatment", "complete" })
            Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsJsonAsync($"/api/appointments/{id}/{tail}", new { })).StatusCode);
    }

    [Theory]
    [InlineData("FrontDesk", HttpStatusCode.OK)]
    [InlineData("OfficeManager", HttpStatusCode.OK)]
    [InlineData("Admin", HttpStatusCode.OK)]
    [InlineData("Dentist", HttpStatusCode.Forbidden)]
    [InlineData("Hygienist", HttpStatusCode.Forbidden)]
    [InlineData("Assistant", HttpStatusCode.Forbidden)]
    [InlineData("Billing", HttpStatusCode.Forbidden)]
    [InlineData("Unassigned", HttpStatusCode.Forbidden)]
    public async Task Only_roles_that_manage_appointments_can_move_a_patient_through_the_visit(string role, HttpStatusCode expected)
    {
        await using var factory = CreateFactory();
        var desk = await SessionAsync(factory, "FrontDesk");
        var a = await BookAsync(desk, At(9));
        var session = await SessionAsync(factory, role);

        var checkIn = await session.SendAsync(HttpMethod.Post, Url(a, "/check-in"), Version(a));
        var version = expected == HttpStatusCode.OK ? new { rowVersion = checkIn.Body.GetProperty("rowVersion").GetString() } : Version(a);
        var complete = await session.SendAsync(HttpMethod.Post, Url(a, "/complete"), version);

        Assert.Equal((expected, expected), (checkIn.Status, complete.Status));
        if (expected == HttpStatusCode.Forbidden)
        {
            Assert.Equal("ManageAppointments", checkIn.Body.GetProperty("required").GetString());
            Assert.Equal("Scheduled", (await desk.SendAsync(HttpMethod.Get, Url(a, ""))).Body.GetProperty("flowState").GetString()); // nothing moved
        }
    }

    [Fact]
    public async Task Every_flow_change_needs_a_csrf_token_and_without_one_nothing_moves()
    {
        await using var factory = CreateFactory();
        var desk = await SessionAsync(factory, "FrontDesk");
        var a = await BookAsync(desk, At(9));

        foreach (var tail in new[] { "/check-in", "/start-treatment", "/complete" })
            Assert.Equal(HttpStatusCode.BadRequest, (await desk.SendAsync(HttpMethod.Post, Url(a, tail), Version(a), csrf: false)).Status);
        Assert.Equal("Scheduled", (await desk.SendAsync(HttpMethod.Get, Url(a, ""))).Body.GetProperty("flowState").GetString());
    }

    // ---------- the acceptance path over HTTP ----------

    [Fact]
    public async Task A_scheduled_patient_checks_in_and_then_completes_and_the_responses_and_history_show_it()
    {
        await using var factory = CreateFactory();
        var desk = await SessionAsync(factory, "FrontDesk");
        var a = await BookAsync(desk, At(9));
        Assert.Equal("Scheduled", a.GetProperty("flowState").GetString());

        var checkedIn = await desk.SendAsync(HttpMethod.Post, Url(a, "/check-in"), Version(a));
        Assert.Equal((HttpStatusCode.OK, "CheckedIn"), (checkedIn.Status, checkedIn.Body.GetProperty("flowState").GetString()));
        Assert.NotEqual(a.GetProperty("rowVersion").GetString(), checkedIn.Body.GetProperty("rowVersion").GetString());

        var completed = await desk.SendAsync(HttpMethod.Post, Url(a, "/complete"), Version(checkedIn.Body));
        Assert.Equal((HttpStatusCode.OK, "Completed"), (completed.Status, completed.Body.GetProperty("flowState").GetString()));

        var detail = await desk.SendAsync(HttpMethod.Get, Url(a, ""));
        Assert.Equal("Completed", detail.Body.GetProperty("flowState").GetString());
        var listed = await desk.SendAsync(HttpMethod.Get, "/api/appointments?from=2030-01-14&to=2030-01-14");
        Assert.Equal("Completed", listed.Body.EnumerateArray().Single().GetProperty("flowState").GetString());

        var history = (await desk.SendAsync(HttpMethod.Get, Url(a, "/history"))).Body.EnumerateArray().Select(e => e.GetProperty("eventType").GetString()).ToList();
        Assert.Equal(["Scheduled", "CheckedIn", "Completed"], history);
    }

    // ---------- refusals ----------

    [Fact]
    public async Task A_stale_or_missing_version_and_a_refused_move_have_stable_error_codes()
    {
        await using var factory = CreateFactory();
        var desk = await SessionAsync(factory, "FrontDesk");
        var a = await BookAsync(desk, At(9));

        var skip = await desk.SendAsync(HttpMethod.Post, Url(a, "/start-treatment"), Version(a));
        Assert.Equal((HttpStatusCode.Conflict, "invalid_flow_transition"), (skip.Status, skip.Body.GetProperty("error").GetString()));

        var missing = await desk.SendAsync(HttpMethod.Post, Url(a, "/check-in"), new { });
        Assert.Equal((HttpStatusCode.BadRequest, "row_version_required"), (missing.Status, missing.Body.GetProperty("error").GetString()));

        var first = await desk.SendAsync(HttpMethod.Post, Url(a, "/check-in"), Version(a));
        Assert.Equal(HttpStatusCode.OK, first.Status);
        // someone else changes the appointment, then a caller holding the version from before the check-in tries the next step
        var started = await desk.SendAsync(HttpMethod.Post, Url(a, "/start-treatment"), Version(first.Body));
        Assert.Equal(HttpStatusCode.OK, started.Status);
        var stale = await desk.SendAsync(HttpMethod.Post, Url(a, "/complete"), Version(first.Body));
        Assert.Equal((HttpStatusCode.Conflict, "concurrency_conflict"), (stale.Status, stale.Body.GetProperty("error").GetString()));

        var unknown = await desk.SendAsync(HttpMethod.Post, $"/api/appointments/{Guid.NewGuid()}/check-in", new { rowVersion = "AAAA" });
        Assert.Equal((HttpStatusCode.NotFound, "appointment_not_found"), (unknown.Status, unknown.Body.GetProperty("error").GetString()));
    }

    [Fact]
    public async Task Once_checked_in_cancel_is_a_409_appointment_in_progress_and_a_cancelled_appointment_cannot_check_in()
    {
        await using var factory = CreateFactory();
        var desk = await SessionAsync(factory, "FrontDesk");
        var a = await BookAsync(desk, At(9));
        var ci = await desk.SendAsync(HttpMethod.Post, Url(a, "/check-in"), Version(a));

        var cancel = await desk.SendAsync(HttpMethod.Post, Url(a, "/cancel"), new { reason = "Illness", rowVersion = ci.Body.GetProperty("rowVersion").GetString() });
        Assert.Equal((HttpStatusCode.Conflict, "appointment_in_progress"), (cancel.Status, cancel.Body.GetProperty("error").GetString()));

        var b = await BookAsync(desk, At(13));
        var cancelled = await desk.SendAsync(HttpMethod.Post, Url(b, "/cancel"), new { reason = "Illness", rowVersion = b.GetProperty("rowVersion").GetString() });
        var refused = await desk.SendAsync(HttpMethod.Post, Url(b, "/check-in"), Version(cancelled.Body));
        Assert.Equal((HttpStatusCode.Conflict, "appointment_not_scheduled"), (refused.Status, refused.Body.GetProperty("error").GetString()));
    }
}
