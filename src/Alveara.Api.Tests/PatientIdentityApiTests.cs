using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Patients;
using Alveara.Api.Controllers;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>ALV-003-C01 at the HTTP boundary: who may read/edit, the CSRF + row-version contract, and the stable error shapes the workspace forms depend on.</summary>
public class PatientIdentityApiTests : IAsyncLifetime
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

    private static HttpRequestMessage Req(HttpMethod method, string url, string? csrf, object? body = null, string? key = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (csrf is not null) request.Headers.Add("X-CSRF-Token", csrf);
        if (key is not null) request.Headers.Add("Idempotency-Key", key);
        if (body is not null) request.Content = JsonContent.Create(body);
        return request;
    }

    private static async Task<(HttpClient Admin, string Csrf)> AdminAsync(WebApplicationFactory<Program> factory)
    {
        var admin = factory.CreateClient();
        var adminName = $"admin-{Guid.NewGuid():N}";
        await admin.PostAsJsonAsync("/api/auth/bootstrap-admin", new BootstrapAdminRequest(adminName, "admin-password", IdentityTestHelpers.TestBootstrapSecret));
        await admin.PostAsJsonAsync("/api/auth/login", new LoginRequest(adminName, "admin-password"));
        return (admin, await CsrfAsync(admin));
    }

    private static async Task<HttpClient> AsRoleAsync(WebApplicationFactory<Program> factory, string role)
    {
        var (admin, adminCsrf) = await AdminAsync(factory);
        return await UserAsync(factory, admin, adminCsrf, role);
    }

    private static async Task<HttpClient> UserAsync(WebApplicationFactory<Program> factory, HttpClient admin, string csrf, string role)
    {
        var username = $"user-{Guid.NewGuid():N}";
        var id = (await (await admin.PostAsJsonAsync("/api/auth/register", new RegisterRequest(username, "password"))).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await admin.SendAsync(Req(HttpMethod.Put, $"/api/auth/{id}/role", csrf, new ChangeRoleRequest(role)));
        await admin.SendAsync(Req(HttpMethod.Put, $"/api/auth/{id}/enabled", csrf, new SetEnabledRequest(true)));
        var client = factory.CreateClient();
        await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(username, "password"));
        return client;
    }

    private static RegisterPatientRequest Person(string first = "Ann", string last = "Lee", string dob = "1985-03-09", string phone = "555-010-0100") =>
        new(first, null, last, dob, "Female", phone, null, "1 Main St", null, "Austin", "TX", "78701");

    private static async Task<JsonElement> RegisterAsync(HttpClient client, string csrf, RegisterPatientRequest person, string key)
    {
        var response = await client.SendAsync(Req(HttpMethod.Post, "/api/patients", csrf, person, key));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static UpdatePatientRequest Edit(JsonElement p, string? city = null, string? rowVersion = null) =>
        new(p.GetProperty("firstName").GetString(), null, p.GetProperty("lastName").GetString(), p.GetProperty("dateOfBirth").GetString(), p.GetProperty("sex").GetString(),
            p.GetProperty("phone").GetString(), null, p.GetProperty("addressLine1").GetString(), null, city ?? p.GetProperty("city").GetString(), p.GetProperty("state").GetString(), p.GetProperty("postalCode").GetString(),
            rowVersion ?? p.GetProperty("rowVersion").GetString());

    [Fact]
    public async Task Front_desk_registers_searches_opens_edits_links_and_reads_history_end_to_end()
    {
        await using var factory = CreateFactory();
        var desk = await AsRoleAsync(factory, "FrontDesk");
        var csrf = await CsrfAsync(desk);

        var mom = await RegisterAsync(desk, csrf, Person("Mia", "Lee", "1980-01-01", "555-010-0001"), "k-mom");
        var kid = await RegisterAsync(desk, csrf, Person("Cal", "Lee", "2015-01-01", "555-010-0002"), "k-kid");

        var found = await (await desk.GetAsync("/api/patients?q=cal")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(kid.GetProperty("id").GetGuid(), Assert.Single(found.EnumerateArray()).GetProperty("id").GetGuid());

        var edited = await desk.SendAsync(Req(HttpMethod.Put, $"/api/patients/{kid.GetProperty("id").GetGuid()}", csrf, Edit(kid, city: "Dallas")));
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        var afterEdit = await edited.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Dallas", afterEdit.GetProperty("city").GetString());
        Assert.NotEqual(kid.GetProperty("rowVersion").GetString(), afterEdit.GetProperty("rowVersion").GetString());

        var kidId = kid.GetProperty("id").GetGuid();
        var momId = mom.GetProperty("id").GetGuid();
        var withGuarantor = await (await desk.SendAsync(Req(HttpMethod.Put, $"/api/patients/{kidId}/guarantor", csrf, new SetGuarantorRequest(momId, afterEdit.GetProperty("rowVersion").GetString())))).Content.ReadFromJsonAsync<JsonElement>();
        var withHousehold = await desk.SendAsync(Req(HttpMethod.Put, $"/api/patients/{kidId}/household", csrf, new SetHouseholdRequest(momId, "Child", withGuarantor.GetProperty("rowVersion").GetString())));
        Assert.Equal(HttpStatusCode.OK, withHousehold.StatusCode);

        var detail = await (await desk.GetAsync($"/api/patients/{kidId}")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(momId, detail.GetProperty("guarantor").GetProperty("id").GetGuid());
        Assert.Equal("Child", detail.GetProperty("household").GetProperty("relationship").GetString());
        Assert.Equal(2, detail.GetProperty("household").GetProperty("members").GetArrayLength());
        Assert.True(detail.GetProperty("age").GetInt32() >= 10);
        Assert.True(detail.GetProperty("isActive").GetBoolean());

        var momDetail = await (await desk.GetAsync($"/api/patients/{momId}")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(kidId, momDetail.GetProperty("guaranteeFor")[0].GetProperty("id").GetGuid());

        var history = await (await desk.GetAsync($"/api/patients/{kidId}/history")).Content.ReadFromJsonAsync<JsonElement>();
        var changes = history.EnumerateArray().Select(h => h.GetProperty("changeType").GetString() + ":" + h.GetProperty("fieldName").GetString()).ToHashSet();
        Assert.Contains("Updated:city", changes);
        Assert.Contains("GuarantorChanged:guarantorPatientId", changes);
        Assert.Contains("HouseholdChanged:householdId", changes);

        var momVersion = momDetail.GetProperty("rowVersion").GetString();
        var inactive = await desk.SendAsync(Req(HttpMethod.Put, $"/api/patients/{momId}/active", csrf, new SetPatientActiveRequest(false, momVersion)));
        Assert.Equal(HttpStatusCode.Conflict, inactive.StatusCode); // mom guarantees the child, so she cannot be inactivated yet
        Assert.Equal("guarantor_in_use", (await inactive.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
    }

    [Fact]
    public async Task Clinical_and_billing_roles_may_view_patients_but_not_register_or_change_them()
    {
        await using var factory = CreateFactory();
        var (admin, adminCsrf) = await AdminAsync(factory);
        var desk = await UserAsync(factory, admin, adminCsrf, "FrontDesk");
        var patient = await RegisterAsync(desk, await CsrfAsync(desk), Person(), "k-1");
        var id = patient.GetProperty("id").GetGuid();

        foreach (var role in new[] { "Dentist", "Hygienist", "Assistant", "Billing" })
        {
            var client = await UserAsync(factory, admin, adminCsrf, role);
            var csrf = await CsrfAsync(client);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/patients?q=ann")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/patients/{id}")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/patients/{id}/history")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(Req(HttpMethod.Put, $"/api/patients/{id}", csrf, Edit(patient, "X")))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(Req(HttpMethod.Put, $"/api/patients/{id}/active", csrf, new SetPatientActiveRequest(false, "AAAA")))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(Req(HttpMethod.Put, $"/api/patients/{id}/guarantor", csrf, new SetGuarantorRequest(null, "AAAA")))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(Req(HttpMethod.Put, $"/api/patients/{id}/household", csrf, new SetHouseholdRequest(null, null, "AAAA")))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(Req(HttpMethod.Post, "/api/patients/duplicate-check", csrf, Person()))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(Req(HttpMethod.Post, "/api/patients", csrf, Person("Zed"), "k-" + role))).StatusCode);
        }

        var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/patients?q=ann")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/patients/{id}")).StatusCode);

        await using var db = _fixture.CreateContext();
        Assert.Equal(1, await db.Patients.CountAsync());
        Assert.Equal(0, await db.PatientHistory.CountAsync()); // every refused change left nothing behind
    }

    [Fact]
    public async Task Every_change_needs_a_csrf_token_and_a_row_version()
    {
        await using var factory = CreateFactory();
        var desk = await AsRoleAsync(factory, "FrontDesk");
        var csrf = await CsrfAsync(desk);
        var patient = await RegisterAsync(desk, csrf, Person(), "k-1");
        var id = patient.GetProperty("id").GetGuid();

        foreach (var (url, body) in new (string, object)[]
        {
            ($"/api/patients/{id}", Edit(patient, "X")),
            ($"/api/patients/{id}/active", new SetPatientActiveRequest(false, null)),
            ($"/api/patients/{id}/guarantor", new SetGuarantorRequest(null, null)),
            ($"/api/patients/{id}/household", new SetHouseholdRequest(null, null, null)),
        })
        {
            var noCsrf = await desk.SendAsync(Req(HttpMethod.Put, url, null, body));
            Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode);
            Assert.Equal("csrf_token_invalid", (await noCsrf.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
        }

        var noVersion = await desk.SendAsync(Req(HttpMethod.Put, $"/api/patients/{id}", csrf, Edit(patient, "X", rowVersion: "")));
        Assert.Equal(HttpStatusCode.BadRequest, noVersion.StatusCode);
        Assert.Equal("row_version_required", (await noVersion.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());
    }

    [Fact]
    public async Task A_stale_edit_returns_the_shared_409_concurrency_shape_and_keeps_the_other_users_change()
    {
        await using var factory = CreateFactory();
        var userA = await AsRoleAsync(factory, "FrontDesk");
        var csrfA = await CsrfAsync(userA);
        var patient = await RegisterAsync(userA, csrfA, Person(), "k-1");
        var id = patient.GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.OK, (await userA.SendAsync(Req(HttpMethod.Put, $"/api/patients/{id}", csrfA, Edit(patient, "Dallas")))).StatusCode);
        var stale = await userA.SendAsync(Req(HttpMethod.Put, $"/api/patients/{id}", csrfA, Edit(patient, "Waco"))); // the same old version again = user B

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var body = await stale.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("concurrency_conflict", body.GetProperty("error").GetString());
        Assert.Equal("Patient", body.GetProperty("entityType").GetString());
        Assert.Equal("Dallas", (await (await userA.GetAsync($"/api/patients/{id}")).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("city").GetString());
    }

    [Fact]
    public async Task An_edit_with_bad_values_returns_400_with_field_messages()
    {
        await using var factory = CreateFactory();
        var desk = await AsRoleAsync(factory, "FrontDesk");
        var csrf = await CsrfAsync(desk);
        var patient = await RegisterAsync(desk, csrf, Person(), "k-1");

        var response = await desk.SendAsync(Req(HttpMethod.Put, $"/api/patients/{patient.GetProperty("id").GetGuid()}", csrf,
            Edit(patient) with { LastName = "", Email = "nope" }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var fields = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("fieldErrors");
        Assert.Contains("required", fields.GetProperty("lastName").GetString());
        Assert.True(fields.TryGetProperty("email", out _));
    }

    [Fact]
    public async Task A_likely_duplicate_returns_409_with_candidates_and_registers_only_once_acknowledged()
    {
        await using var factory = CreateFactory();
        var desk = await AsRoleAsync(factory, "FrontDesk");
        var csrf = await CsrfAsync(desk);
        var ann = await RegisterAsync(desk, csrf, Person("Ann", "Lee", "1985-03-09", "555-010-0100"), "k-1");

        var twin = Person("Anna", "Lee", "1985-03-09", "555-777-0000");
        var check = await (await desk.SendAsync(Req(HttpMethod.Post, "/api/patients/duplicate-check", csrf, twin))).Content.ReadFromJsonAsync<JsonElement>();
        var candidate = Assert.Single(check.GetProperty("candidates").EnumerateArray());
        Assert.Equal(ann.GetProperty("id").GetGuid(), candidate.GetProperty("id").GetGuid());
        Assert.Contains("Same last name and date of birth", candidate.GetProperty("reasons").EnumerateArray().Select(r => r.GetString()));

        var warned = await desk.SendAsync(Req(HttpMethod.Post, "/api/patients", csrf, twin, "k-2"));
        Assert.Equal(HttpStatusCode.Conflict, warned.StatusCode);
        var body = await warned.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("possible_duplicate", body.GetProperty("error").GetString());
        Assert.Equal("Ann", body.GetProperty("candidates")[0].GetProperty("firstName").GetString());

        var acknowledged = await desk.SendAsync(Req(HttpMethod.Post, "/api/patients", csrf, twin with { AcknowledgedDuplicateIds = [ann.GetProperty("id").GetGuid()] }, "k-2"));
        Assert.Equal(HttpStatusCode.Created, acknowledged.StatusCode);

        var exact = await desk.SendAsync(Req(HttpMethod.Post, "/api/patients", csrf, Person("ann", "LEE"), "k-3"));
        Assert.Equal(HttpStatusCode.Conflict, exact.StatusCode);
        var exactBody = await exact.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("duplicate_patient", exactBody.GetProperty("error").GetString());
        Assert.True(exactBody.GetProperty("candidates")[0].GetProperty("exact").GetBoolean());
    }

    [Fact]
    public async Task Invalid_relationships_come_back_as_400_invalid_relationship_and_unknown_patients_as_404()
    {
        await using var factory = CreateFactory();
        var desk = await AsRoleAsync(factory, "FrontDesk");
        var csrf = await CsrfAsync(desk);
        var p = await RegisterAsync(desk, csrf, Person(), "k-1");
        var id = p.GetProperty("id").GetGuid();
        var version = p.GetProperty("rowVersion").GetString();

        var badGuarantor = await desk.SendAsync(Req(HttpMethod.Put, $"/api/patients/{id}/guarantor", csrf, new SetGuarantorRequest(Guid.NewGuid(), version)));
        Assert.Equal(HttpStatusCode.BadRequest, badGuarantor.StatusCode);
        Assert.Equal("invalid_relationship", (await badGuarantor.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());

        var selfHousehold = await desk.SendAsync(Req(HttpMethod.Put, $"/api/patients/{id}/household", csrf, new SetHouseholdRequest(id, "Child", version)));
        Assert.Equal("invalid_relationship", (await selfHousehold.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString());

        Assert.Equal(HttpStatusCode.NotFound, (await desk.GetAsync($"/api/patients/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await desk.GetAsync($"/api/patients/{Guid.NewGuid()}/history")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await desk.SendAsync(Req(HttpMethod.Put, $"/api/patients/{Guid.NewGuid()}/active", csrf, new SetPatientActiveRequest(false, "AAAA")))).StatusCode);
    }

    [Theory]
    [InlineData(Role.FrontDesk, true, true, true)]
    [InlineData(Role.OfficeManager, true, true, true)]
    [InlineData(Role.Admin, true, true, true)]
    [InlineData(Role.Dentist, false, false, true)]
    [InlineData(Role.Hygienist, false, false, true)]
    [InlineData(Role.Assistant, false, false, true)]
    [InlineData(Role.Billing, false, false, true)]
    [InlineData(Role.Unassigned, false, false, false)]
    public void The_permission_matrix_grants_register_edit_and_view_as_designed(Role role, bool register, bool edit, bool view)
    {
        Assert.Equal(register, PermissionMatrix.RoleHas(role, Permission.RegisterPatients));
        Assert.Equal(edit, PermissionMatrix.RoleHas(role, Permission.EditPatients));
        Assert.Equal(view, PermissionMatrix.RoleHas(role, Permission.ViewPatientRecords));
    }
}
