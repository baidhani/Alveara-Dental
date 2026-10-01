using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Patients;
using Alveara.Api.Controllers;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>STORY-003 at the HTTP boundary, against the real host and database: who may register, the CSRF and idempotency contract, and the stable error shapes the form depends on.</summary>
public class PatientsApiTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();
    public Task DisposeAsync() => _fixture.DisposeAsync();

    private WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Alveara", _fixture.ConnectionString);
            builder.UseSetting("AdminBootstrapSecret", IdentityTestHelpers.TestBootstrapSecret);
            builder.UseSetting("AuthAttemptRateLimit:PermitLimit", "500"); // test setup signs in many accounts from one address
        });

    private static async Task<string> CsrfAsync(HttpClient client) =>
        (await (await client.GetAsync("/api/auth/csrf-token")).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;

    private static async Task<(HttpClient Admin, string Csrf)> AdminAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        var username = $"admin-{Guid.NewGuid():N}";
        await client.PostAsJsonAsync("/api/auth/bootstrap-admin", new BootstrapAdminRequest(username, "admin-password", IdentityTestHelpers.TestBootstrapSecret));
        await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(username, "admin-password"));
        return (client, await CsrfAsync(client));
    }

    private static async Task<(HttpClient Client, Guid UserId)> UserWithRoleAsync(WebApplicationFactory<Program> factory, HttpClient admin, string csrf, string role)
    {
        var username = $"user-{Guid.NewGuid():N}";
        var registered = await (await admin.PostAsJsonAsync("/api/auth/register", new RegisterRequest(username, "password"))).Content.ReadFromJsonAsync<JsonElement>();
        var id = registered.GetProperty("id").GetGuid();
        await admin.SendAsync(Req(HttpMethod.Put, $"/api/auth/{id}/role", csrf, new ChangeRoleRequest(role)));
        await admin.SendAsync(Req(HttpMethod.Put, $"/api/auth/{id}/enabled", csrf, new SetEnabledRequest(true)));
        var client = factory.CreateClient();
        await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(username, "password"));
        return (client, id);
    }

    private static HttpRequestMessage Req(HttpMethod method, string url, string? csrf, object? body = null, string? idempotencyKey = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (csrf is not null) request.Headers.Add("X-CSRF-Token", csrf);
        if (idempotencyKey is not null) request.Headers.Add("Idempotency-Key", idempotencyKey);
        if (body is not null) request.Content = JsonContent.Create(body);
        return request;
    }

    private static RegisterPatientRequest Valid(string first = "Ann", string last = "Lee") =>
        new(first, null, last, "1985-03-09", "Female", "555-010-0100", "ann@example.test", "1 Main St", null, "Austin", "TX", "78701");

    [Fact]
    public async Task Front_desk_registers_a_patient_and_the_details_come_back_and_can_be_read_again()
    {
        await using var factory = CreateFactory();
        var (admin, adminCsrf) = await AdminAsync(factory);
        var (frontDesk, _) = await UserWithRoleAsync(factory, admin, adminCsrf, "FrontDesk");
        var csrf = await CsrfAsync(frontDesk);

        var response = await frontDesk.SendAsync(Req(HttpMethod.Post, "/api/patients", csrf, Valid(), "k-1"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Ann", created.GetProperty("firstName").GetString());
        Assert.Equal("1985-03-09", created.GetProperty("dateOfBirth").GetString());
        Assert.Equal("555-010-0100", created.GetProperty("phone").GetString());
        var id = created.GetProperty("id").GetGuid();
        Assert.Equal($"/api/patients/{id}", response.Headers.Location!.OriginalString);

        var fetched = await (await frontDesk.GetAsync($"/api/patients/{id}")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Lee", fetched.GetProperty("lastName").GetString());
    }

    [Fact]
    public async Task Incomplete_registration_returns_400_with_a_message_per_missing_field()
    {
        await using var factory = CreateFactory();
        var (admin, adminCsrf) = await AdminAsync(factory);
        var (frontDesk, _) = await UserWithRoleAsync(factory, admin, adminCsrf, "FrontDesk");

        var response = await frontDesk.SendAsync(Req(HttpMethod.Post, "/api/patients", await CsrfAsync(frontDesk),
            new RegisterPatientRequest("Ann", null, null, null, null, null, null, null, null, null, null, null), "k-2"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("validation_failed", body.GetProperty("error").GetString());
        var fields = body.GetProperty("fieldErrors");
        foreach (var required in new[] { "lastName", "dateOfBirth", "phone", "addressLine1", "city", "state", "postalCode" })
            Assert.Contains("required", fields.GetProperty(required).GetString());
        Assert.False(fields.TryGetProperty("firstName", out _)); // what was supplied is not nagged about
    }

    [Fact]
    public async Task A_retried_request_returns_200_with_the_same_patient_and_a_duplicate_person_gets_409()
    {
        await using var factory = CreateFactory();
        var (admin, adminCsrf) = await AdminAsync(factory);
        var (frontDesk, _) = await UserWithRoleAsync(factory, admin, adminCsrf, "FrontDesk");
        var csrf = await CsrfAsync(frontDesk);

        var first = await frontDesk.SendAsync(Req(HttpMethod.Post, "/api/patients", csrf, Valid(), "k-3"));
        var id = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var retry = await frontDesk.SendAsync(Req(HttpMethod.Post, "/api/patients", csrf, Valid(), "k-3"));
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal(id, (await retry.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());

        var duplicate = await frontDesk.SendAsync(Req(HttpMethod.Post, "/api/patients", csrf, Valid(), "k-4"));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        var body = await duplicate.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("duplicate_patient", body.GetProperty("error").GetString());
        Assert.Equal(id, body.GetProperty("existingPatientId").GetGuid());
    }

    [Fact]
    public async Task A_request_without_an_idempotency_key_is_refused()
    {
        await using var factory = CreateFactory();
        var (admin, adminCsrf) = await AdminAsync(factory);
        var (frontDesk, _) = await UserWithRoleAsync(factory, admin, adminCsrf, "FrontDesk");

        var response = await frontDesk.SendAsync(Req(HttpMethod.Post, "/api/patients", await CsrfAsync(frontDesk), Valid()));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("idempotency_key_required", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
    }

    [Fact]
    public async Task Registration_requires_authentication_the_RegisterPatients_permission_and_a_CSRF_token()
    {
        await using var factory = CreateFactory();
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().SendAsync(Req(HttpMethod.Post, "/api/patients", "x", Valid(), "k-5"))).StatusCode);

        var (admin, adminCsrf) = await AdminAsync(factory);
        foreach (var role in new[] { "Dentist", "Hygienist", "Assistant", "Billing" })
        {
            var (client, _) = await UserWithRoleAsync(factory, admin, adminCsrf, role);
            var denied = await client.SendAsync(Req(HttpMethod.Post, "/api/patients", await CsrfAsync(client), Valid(), $"k-{role}"));
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        }

        var (frontDesk, _) = await UserWithRoleAsync(factory, admin, adminCsrf, "FrontDesk");
        var noCsrf = await frontDesk.SendAsync(Req(HttpMethod.Post, "/api/patients", null, Valid(), "k-6"));
        Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode); // the project-wide CSRF contract: 400 csrf_token_invalid
        Assert.Equal("csrf_token_invalid", (await noCsrf.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());

        await using var db = _fixture.CreateContext();
        Assert.Equal(0, await db.Patients.CountAsync()); // every refused call stored nothing
    }

    [Fact]
    public async Task The_audit_log_attributes_the_registration_to_the_front_desk_user_with_a_timestamp()
    {
        await using var factory = CreateFactory();
        var (admin, adminCsrf) = await AdminAsync(factory);
        var (frontDesk, userId) = await UserWithRoleAsync(factory, admin, adminCsrf, "FrontDesk");

        var response = await frontDesk.SendAsync(Req(HttpMethod.Post, "/api/patients", await CsrfAsync(frontDesk), Valid(), "k-7"));
        var patientId = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        await using var db = _fixture.CreateContext();
        var entry = await db.AuditLogEntries.SingleAsync(a => a.EventType == PatientAuditEvents.Registered);
        Assert.Equal(userId, entry.PerformedByUserAccountId);
        Assert.Equal(patientId, entry.TargetUserAccountId);
        Assert.True(entry.TimestampUtc > DateTimeOffset.UtcNow.AddMinutes(-2));
    }

    [Fact]
    public async Task Reading_an_unknown_patient_returns_404()
    {
        await using var factory = CreateFactory();
        var (admin, adminCsrf) = await AdminAsync(factory);
        var (frontDesk, _) = await UserWithRoleAsync(factory, admin, adminCsrf, "FrontDesk");
        Assert.Equal(HttpStatusCode.NotFound, (await frontDesk.GetAsync($"/api/patients/{Guid.NewGuid()}")).StatusCode);
    }
}
