using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Alveara.Api.Controllers;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N003 API-level proof: authorization (401/403/CSRF), the stable error contract, concurrency
/// conflicts, the absence of any destructive-delete route, and audit of material changes - against
/// the real host and a real database.
/// </summary>
public class ConfigurationApiTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();
    public Task DisposeAsync() => _fixture.DisposeAsync();

    private WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Alveara", _fixture.ConnectionString);
            builder.UseSetting("AdminBootstrapSecret", IdentityTestHelpers.TestBootstrapSecret);
        });

    private static async Task<(HttpClient Client, string Csrf)> AdminAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        var username = $"admin-{Guid.NewGuid():N}";
        await client.PostAsJsonAsync("/api/auth/bootstrap-admin", new BootstrapAdminRequest(username, "admin-password", IdentityTestHelpers.TestBootstrapSecret));
        await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(username, "admin-password"));
        return (client, await CsrfAsync(client));
    }

    private static async Task<string> CsrfAsync(HttpClient client) =>
        (await (await client.GetAsync("/api/auth/csrf-token")).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;

    private static HttpRequestMessage Req(HttpMethod method, string url, string? csrf, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (csrf is not null) request.Headers.Add("X-CSRF-Token", csrf);
        if (body is not null) request.Content = JsonContent.Create(body);
        return request;
    }

    private static async Task<HttpClient> UserWithRoleAsync(WebApplicationFactory<Program> factory, HttpClient admin, string csrf, string role)
    {
        var username = $"user-{Guid.NewGuid():N}";
        var registered = await (await admin.PostAsJsonAsync("/api/auth/register", new RegisterRequest(username, "password"))).Content.ReadFromJsonAsync<JsonElement>();
        var id = registered.GetProperty("id").GetGuid();
        await admin.SendAsync(Req(HttpMethod.Put, $"/api/auth/{id}/role", csrf, new ChangeRoleRequest(role)));
        await admin.SendAsync(Req(HttpMethod.Put, $"/api/auth/{id}/enabled", csrf, new SetEnabledRequest(true)));
        var client = factory.CreateClient();
        await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(username, "password"));
        return client;
    }

    [Fact]
    public async Task Configuration_endpoints_require_authentication_and_the_ManagePracticeConfiguration_permission()
    {
        await using var factory = CreateFactory();
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/config/practice")).StatusCode);

        var (admin, csrf) = await AdminAsync(factory);
        var frontDesk = await UserWithRoleAsync(factory, admin, csrf, "FrontDesk");
        var dentist = await UserWithRoleAsync(factory, admin, csrf, "Dentist");
        var manager = await UserWithRoleAsync(factory, admin, csrf, "OfficeManager");

        foreach (var url in new[] { "/api/config/practice", "/api/config/staff", "/api/config/providers", "/api/config/operatories", "/api/config/appointment-types", "/api/config/locations", "/api/config/linkable-accounts" })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await frontDesk.GetAsync(url)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await dentist.GetAsync(url)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await manager.GetAsync(url)).StatusCode);
        }

        var denied = await frontDesk.SendAsync(Req(HttpMethod.Post, "/api/config/appointment-types", await CsrfAsync(frontDesk), new SaveAppointmentTypeRequest("Exam", 30, null)));
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal("ManagePracticeConfiguration", (await denied.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("required").GetString());
    }

    [Fact]
    public async Task The_scheduling_read_model_needs_only_ViewSchedule_so_front_desk_can_consume_but_not_change_configuration()
    {
        await using var factory = CreateFactory();
        var (admin, csrf) = await AdminAsync(factory);
        await admin.SendAsync(Req(HttpMethod.Post, "/api/config/appointment-types", csrf, new SaveAppointmentTypeRequest("Exam", 30, null)));
        var frontDesk = await UserWithRoleAsync(factory, admin, csrf, "FrontDesk");

        var snapshotResponse = await frontDesk.GetAsync("/api/config/scheduling");
        Assert.Equal(HttpStatusCode.OK, snapshotResponse.StatusCode);
        var snapshot = await snapshotResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("America/Chicago", snapshot.GetProperty("timeZoneId").GetString());
        Assert.Equal("Exam", snapshot.GetProperty("appointmentTypes")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task State_changing_configuration_calls_require_a_csrf_token()
    {
        await using var factory = CreateFactory();
        var (admin, _) = await AdminAsync(factory);
        var response = await admin.SendAsync(Req(HttpMethod.Post, "/api/config/appointment-types", null, new SaveAppointmentTypeRequest("Exam", 30, null)));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("csrf_token_invalid", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
    }

    [Fact]
    public async Task Validation_and_conflict_failures_return_stable_error_codes()
    {
        await using var factory = CreateFactory();
        var (admin, csrf) = await AdminAsync(factory);

        var badDuration = await admin.SendAsync(Req(HttpMethod.Post, "/api/config/appointment-types", csrf, new SaveAppointmentTypeRequest("Exam", 7, null)));
        Assert.Equal(HttpStatusCode.BadRequest, badDuration.StatusCode);
        Assert.Equal("invalid_duration", (await badDuration.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());

        await admin.SendAsync(Req(HttpMethod.Post, "/api/config/appointment-types", csrf, new SaveAppointmentTypeRequest("Exam", 30, null)));
        var duplicate = await admin.SendAsync(Req(HttpMethod.Post, "/api/config/appointment-types", csrf, new SaveAppointmentTypeRequest("exam", 45, null)));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("appointment_type_name_taken", (await duplicate.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());

        var noLocation = await admin.SendAsync(Req(HttpMethod.Post, "/api/config/operatories", csrf, new SaveOperatoryRequest("Op 1", null)));
        Assert.Equal("no_active_location", (await noLocation.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());

        var missing = await admin.GetAsync($"/api/config/providers/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task A_stale_edit_returns_the_shared_409_concurrency_conflict_and_changes_nothing()
    {
        await using var factory = CreateFactory();
        var (admin, csrf) = await AdminAsync(factory);
        var created = await (await admin.SendAsync(Req(HttpMethod.Post, "/api/config/appointment-types", csrf, new SaveAppointmentTypeRequest("Exam", 30, null)))).Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetGuid();
        var version = created.GetProperty("rowVersion").GetString();

        var first = await admin.SendAsync(Req(HttpMethod.Put, $"/api/config/appointment-types/{id}", csrf, new SaveAppointmentTypeRequest("Exam", 45, version)));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var stale = await admin.SendAsync(Req(HttpMethod.Put, $"/api/config/appointment-types/{id}", csrf, new SaveAppointmentTypeRequest("Exam", 60, version)));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var problem = await stale.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("concurrency_conflict", problem.GetProperty("error").GetString());
        Assert.Equal("AppointmentType", problem.GetProperty("entityType").GetString());

        var current = await (await admin.GetAsync($"/api/config/appointment-types/{id}")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(45, current.GetProperty("defaultDurationMinutes").GetInt32());

        var noVersion = await admin.SendAsync(Req(HttpMethod.Put, $"/api/config/appointment-types/{id}", csrf, new SaveAppointmentTypeRequest("Exam", 60, null)));
        Assert.Equal("row_version_required", (await noVersion.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
    }

    [Fact]
    public async Task There_is_no_destructive_delete_route_for_configuration_that_history_may_reference()
    {
        await using var factory = CreateFactory();
        var (admin, csrf) = await AdminAsync(factory);
        var id = Guid.NewGuid();
        foreach (var path in new[] { "staff", "providers", "operatories", "appointment-types", "locations" })
        {
            var response = await admin.SendAsync(Req(HttpMethod.Delete, $"/api/config/{path}/{id}", csrf));
            Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        }
    }

    [Fact]
    public async Task End_to_end_configuration_is_audited_and_consumed_by_the_scheduling_read_model()
    {
        await using var factory = CreateFactory();
        var (admin, csrf) = await AdminAsync(factory);

        async Task<JsonElement> Ok(HttpResponseMessage response)
        {
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            return await response.Content.ReadFromJsonAsync<JsonElement>();
        }

        await Ok(await admin.SendAsync(Req(HttpMethod.Put, "/api/config/practice", csrf, new SavePracticeRequest("Alveara Dental", "555-0100", null, null))));
        await Ok(await admin.SendAsync(Req(HttpMethod.Post, "/api/config/locations", csrf, new SaveLocationRequest("Main Office", null))));
        var operatory = await Ok(await admin.SendAsync(Req(HttpMethod.Post, "/api/config/operatories", csrf, new SaveOperatoryRequest("Op 1", null))));
        var type = await Ok(await admin.SendAsync(Req(HttpMethod.Post, "/api/config/appointment-types", csrf, new SaveAppointmentTypeRequest("Exam", 30, null))));
        var staff = await Ok(await admin.SendAsync(Req(HttpMethod.Post, "/api/config/staff", csrf, new SaveStaffRequest("Dr. Rivera", "Dentist", null, null))));
        var provider = await Ok(await admin.SendAsync(Req(HttpMethod.Post, "/api/config/providers", csrf, new SaveProviderRequest(staff.GetProperty("id").GetGuid(), "Dentistry", null))));
        var providerId = provider.GetProperty("id").GetGuid();
        await Ok(await admin.SendAsync(Req(HttpMethod.Put, $"/api/config/providers/{providerId}/availability", csrf,
            new ReplaceAvailabilityRequest([new Alveara.Api.Architecture.Configuration.AvailabilityWindow(DayOfWeek.Tuesday, "09:00", "17:00")]))));

        var snapshot = await Ok(await admin.GetAsync("/api/config/scheduling"));
        Assert.Equal(operatory.GetProperty("id").GetGuid(), snapshot.GetProperty("operatories")[0].GetProperty("id").GetGuid());
        Assert.Equal(type.GetProperty("id").GetGuid(), snapshot.GetProperty("appointmentTypes")[0].GetProperty("id").GetGuid());
        Assert.Equal(providerId, snapshot.GetProperty("providers")[0].GetProperty("providerId").GetGuid());

        var available = await Ok(await admin.GetAsync($"/api/config/scheduling/availability-check?providerId={providerId}&startUtc=2026-01-13T15:00:00Z&durationMinutes=30"));
        Assert.True(available.GetProperty("available").GetBoolean());

        var audit = await Ok(await admin.GetAsync("/api/auth/audit-log?take=500"));
        var entries = audit.EnumerateArray().ToList();
        foreach (var entityType in new[] { "PracticeSettings", "PracticeLocation", "Operatory", "AppointmentType", "StaffProfile", "ProviderProfile" })
            Assert.Contains(entries, e => e.GetProperty("entityType").GetString() == entityType && e.GetProperty("eventType").GetString() == "ConfigurationCreated");
        Assert.Contains(entries, e => e.GetProperty("eventType").GetString() == "ProviderAvailabilityReplaced");
    }
}
