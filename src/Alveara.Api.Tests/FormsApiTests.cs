using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Alveara.Api.Architecture.Patients;
using Alveara.Api.Controllers;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>ALV-N010 at the HTTP boundary: who may administer, complete, sign, void and view; the CSRF, row-version and Idempotency-Key contract; and the stable error shapes the forms UI depends on.</summary>
public class FormsApiTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();
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

    private static HttpRequestMessage Req(HttpMethod method, string url, string? csrf, object? body = null, string? key = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (csrf is not null) request.Headers.Add("X-CSRF-Token", csrf);
        if (key is not null) request.Headers.Add("Idempotency-Key", key);
        if (body is not null) request.Content = JsonContent.Create(body);
        return request;
    }

    private sealed record Session(HttpClient Client, string Csrf)
    {
        public async Task<(HttpStatusCode Status, JsonElement Body)> SendAsync(HttpMethod method, string url, object? body = null, string? key = null, bool csrf = true)
        {
            var response = await Client.SendAsync(Req(method, url, csrf ? Csrf : null, body, key));
            var text = await response.Content.ReadAsStringAsync();
            return (response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone());
        }
    }

    private Session? _admin; // one bootstrap administrator per test (the bootstrap can happen only once)

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
        var (admin, adminCsrf) = (_admin.Client, _admin.Csrf);
        if (role == "Admin") return _admin;

        var username = $"user-{Guid.NewGuid():N}";
        var id = (await (await admin.PostAsJsonAsync("/api/auth/register", new RegisterRequest(username, "password"))).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await admin.SendAsync(Req(HttpMethod.Put, $"/api/auth/{id}/role", adminCsrf, new ChangeRoleRequest(role)));
        await admin.SendAsync(Req(HttpMethod.Put, $"/api/auth/{id}/enabled", adminCsrf, new SetEnabledRequest(true)));
        var client = factory.CreateClient();
        await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(username, "password"));
        return new Session(client, await CsrfAsync(client));
    }

    private static readonly object[] Fields =
    [
        new { id = "acknowledged", label = "I have read the privacy notice", kind = "checkbox", required = true },
        new { id = "nickname", label = "Preferred name", kind = "text", required = false },
    ];

    private static object TemplateBody(string key = "privacy-notice", string body = "We protect your information.", string? rowVersion = null) =>
        new { key, category = "Privacy", title = "Privacy notice", body, fields = Fields, changeNote = "test", rowVersion };

    private async Task<(Session Desk, Session Manager, JsonElement Template, string PatientId)> ArrangeAsync(WebApplicationFactory<Program> factory)
    {
        var manager = await SessionAsync(factory, "OfficeManager");
        var desk = await SessionAsync(factory, "FrontDesk");
        var template = await manager.SendAsync(HttpMethod.Post, "/api/forms/templates", TemplateBody());
        Assert.Equal(HttpStatusCode.Created, template.Status);
        var patient = await desk.SendAsync(HttpMethod.Post, "/api/patients", new RegisterPatientRequest("Ann", null, "Lee", "1985-03-09", "Female", "555-010-0100", null, "1 Main St", null, "Austin", "TX", "78701"), "register-ann-1");
        Assert.Equal(HttpStatusCode.Created, patient.Status);
        return (desk, manager, template.Body, patient.Body.GetProperty("id").GetString()!);
    }

    private static async Task<JsonElement> ReadyFormAsync(Session desk, string patientId, string templateId)
    {
        var started = await desk.SendAsync(HttpMethod.Post, $"/api/patients/{patientId}/forms", new { templateId });
        Assert.Equal(HttpStatusCode.Created, started.Status);
        var formId = started.Body.GetProperty("summary").GetProperty("id").GetString();
        var saved = await desk.SendAsync(HttpMethod.Put, $"/api/forms/{formId}/responses",
            new { responses = new Dictionary<string, string> { ["acknowledged"] = "true", ["nickname"] = "Annie" }, rowVersion = started.Body.GetProperty("rowVersion").GetString() });
        Assert.Equal(HttpStatusCode.OK, saved.Status);
        return saved.Body;
    }

    private static object SignBody(JsonElement form, string signer = "Ann Lee", string relationship = "Self", bool attested = true) =>
        new
        {
            signerName = signer, relationship, relationshipNote = (string?)null, signatureText = signer, attested,
            templateVersionId = form.GetProperty("version").GetProperty("id").GetString(), rowVersion = form.GetProperty("rowVersion").GetString(),
        };

    // ---------- authorization ----------

    [Fact]
    public async Task Anonymous_callers_get_401_from_every_forms_endpoint()
    {
        await using var factory = CreateFactory();
        var anon = factory.CreateClient();
        foreach (var url in new[] { "/api/forms/templates", "/api/forms/templates/available", $"/api/forms/{Guid.NewGuid()}", $"/api/patients/{Guid.NewGuid()}/forms", $"/api/patients/{Guid.NewGuid()}/signed-documents" })
            Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync(url)).StatusCode);
    }

    [Theory]
    [InlineData("FrontDesk", HttpStatusCode.Forbidden)]
    [InlineData("Dentist", HttpStatusCode.Forbidden)]
    [InlineData("Billing", HttpStatusCode.Forbidden)]
    [InlineData("Unassigned", HttpStatusCode.Forbidden)]
    [InlineData("OfficeManager", HttpStatusCode.Created)]
    [InlineData("Admin", HttpStatusCode.Created)]
    public async Task Only_template_administrators_can_create_templates(string role, HttpStatusCode expected)
    {
        await using var factory = CreateFactory();
        var session = await SessionAsync(factory, role);
        var result = await session.SendAsync(HttpMethod.Post, "/api/forms/templates", TemplateBody());
        Assert.Equal(expected, result.Status);
        if (expected == HttpStatusCode.Forbidden) Assert.Equal("ManageFormTemplates", result.Body.GetProperty("required").GetString());
    }

    [Theory]
    [InlineData("FrontDesk", HttpStatusCode.Created)]
    [InlineData("Dentist", HttpStatusCode.Created)]
    [InlineData("Hygienist", HttpStatusCode.Created)]
    [InlineData("Assistant", HttpStatusCode.Created)]
    [InlineData("OfficeManager", HttpStatusCode.Created)]
    [InlineData("Billing", HttpStatusCode.Forbidden)]
    [InlineData("Unassigned", HttpStatusCode.Forbidden)]
    public async Task Only_staff_who_complete_forms_can_start_one(string role, HttpStatusCode expected)
    {
        await using var factory = CreateFactory();
        var (_, _, template, patientId) = await ArrangeAsync(factory);
        var session = await SessionAsync(factory, role);

        var result = await session.SendAsync(HttpMethod.Post, $"/api/patients/{patientId}/forms", new { templateId = template.GetProperty("template").GetProperty("id").GetString() });

        Assert.Equal(expected, result.Status);
    }

    [Theory]
    [InlineData("FrontDesk", HttpStatusCode.OK)]
    [InlineData("Dentist", HttpStatusCode.OK)]
    [InlineData("Billing", HttpStatusCode.OK)]
    [InlineData("OfficeManager", HttpStatusCode.OK)]
    [InlineData("Unassigned", HttpStatusCode.Forbidden)]
    public async Task Signed_forms_are_readable_only_with_the_view_permission(string role, HttpStatusCode expected)
    {
        await using var factory = CreateFactory();
        var (desk, _, template, patientId) = await ArrangeAsync(factory);
        var ready = await ReadyFormAsync(desk, patientId, template.GetProperty("template").GetProperty("id").GetString()!);
        var signed = await desk.SendAsync(HttpMethod.Post, $"/api/forms/{ready.GetProperty("summary").GetProperty("id").GetString()}/sign", SignBody(ready), "sign-key-000001");
        Assert.Equal(HttpStatusCode.Created, signed.Status);
        var formId = signed.Body.GetProperty("summary").GetProperty("id").GetString();
        var reader = await SessionAsync(factory, role);

        Assert.Equal(expected, (await reader.SendAsync(HttpMethod.Get, $"/api/forms/{formId}")).Status);
        Assert.Equal(expected, (await reader.SendAsync(HttpMethod.Get, $"/api/patients/{patientId}/forms")).Status);
        Assert.Equal(expected, (await reader.SendAsync(HttpMethod.Get, $"/api/patients/{patientId}/signed-documents")).Status);
    }

    [Fact]
    public async Task Billing_can_read_but_not_complete_sign_or_void_and_the_front_desk_cannot_void_a_signed_form()
    {
        await using var factory = CreateFactory();
        var (desk, manager, template, patientId) = await ArrangeAsync(factory);
        var ready = await ReadyFormAsync(desk, patientId, template.GetProperty("template").GetProperty("id").GetString()!);
        var formId = ready.GetProperty("summary").GetProperty("id").GetString();
        var billing = await SessionAsync(factory, "Billing");

        Assert.Equal(HttpStatusCode.Forbidden, (await billing.SendAsync(HttpMethod.Post, $"/api/forms/{formId}/sign", SignBody(ready), "billing-key-0001")).Status);
        var signed = await desk.SendAsync(HttpMethod.Post, $"/api/forms/{formId}/sign", SignBody(ready), "sign-key-000002");
        Assert.Equal(HttpStatusCode.Created, signed.Status);
        var rv = signed.Body.GetProperty("rowVersion").GetString();

        Assert.Equal(HttpStatusCode.Forbidden, (await billing.SendAsync(HttpMethod.Post, $"/api/forms/{formId}/void", new { reason = "x", rowVersion = rv })).Status);
        var deskVoid = await desk.SendAsync(HttpMethod.Post, $"/api/forms/{formId}/void", new { reason = "Wrong chart", rowVersion = rv });
        Assert.Equal(HttpStatusCode.Forbidden, deskVoid.Status);
        Assert.Equal("void_not_permitted", deskVoid.Body.GetProperty("error").GetString());
        var managerVoid = await manager.SendAsync(HttpMethod.Post, $"/api/forms/{formId}/void", new { reason = "Wrong chart", rowVersion = rv });
        Assert.Equal(HttpStatusCode.OK, managerVoid.Status);
        Assert.Equal("Void", managerVoid.Body.GetProperty("summary").GetProperty("status").GetString());
        Assert.True(managerVoid.Body.GetProperty("snapshot").GetProperty("integrityVerified").GetBoolean());
    }

    [Fact]
    public async Task Every_state_changing_call_needs_a_csrf_token()
    {
        await using var factory = CreateFactory();
        var (desk, manager, template, patientId) = await ArrangeAsync(factory);
        var templateId = template.GetProperty("template").GetProperty("id").GetString();

        Assert.Equal(HttpStatusCode.BadRequest, (await manager.SendAsync(HttpMethod.Post, "/api/forms/templates", TemplateBody("another"), csrf: false)).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await desk.SendAsync(HttpMethod.Post, $"/api/patients/{patientId}/forms", new { templateId }, csrf: false)).Status);
        var ready = await ReadyFormAsync(desk, patientId, templateId!);
        var formId = ready.GetProperty("summary").GetProperty("id").GetString();
        Assert.Equal(HttpStatusCode.BadRequest, (await desk.SendAsync(HttpMethod.Post, $"/api/forms/{formId}/sign", SignBody(ready), "csrf-key-00001", csrf: false)).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await desk.SendAsync(HttpMethod.Post, $"/api/forms/{formId}/void", new { reason = "x", rowVersion = ready.GetProperty("rowVersion").GetString() }, csrf: false)).Status);
    }

    // ---------- the signing contract ----------

    [Fact]
    public async Task A_form_is_completed_signed_replayed_and_viewed_over_http()
    {
        await using var factory = CreateFactory();
        var (desk, _, template, patientId) = await ArrangeAsync(factory);
        var templateId = template.GetProperty("template").GetProperty("id").GetString()!;
        var ready = await ReadyFormAsync(desk, patientId, templateId);
        var formId = ready.GetProperty("summary").GetProperty("id").GetString();

        var first = await desk.SendAsync(HttpMethod.Post, $"/api/forms/{formId}/sign", SignBody(ready), "http-sign-key-01");
        var replay = await desk.SendAsync(HttpMethod.Post, $"/api/forms/{formId}/sign", SignBody(ready), "http-sign-key-01");

        Assert.Equal(HttpStatusCode.Created, first.Status);
        Assert.Equal(HttpStatusCode.OK, replay.Status); // the same submit again: same signed form, nothing new
        Assert.Equal(first.Body.GetProperty("snapshot").GetProperty("id").GetString(), replay.Body.GetProperty("snapshot").GetProperty("id").GetString());
        Assert.Equal("Signed", first.Body.GetProperty("summary").GetProperty("status").GetString());
        Assert.Equal("Annie", first.Body.GetProperty("snapshot").GetProperty("responses").GetProperty("nickname").GetString());

        var other = await desk.SendAsync(HttpMethod.Post, $"/api/forms/{formId}/sign", SignBody(ready), "http-sign-key-02");
        Assert.Equal(HttpStatusCode.Conflict, other.Status);
        Assert.Equal("already_signed", other.Body.GetProperty("error").GetString());
        Assert.Equal(first.Body.GetProperty("snapshot").GetProperty("id").GetString(), other.Body.GetProperty("existingId").GetString());

        var list = await desk.SendAsync(HttpMethod.Get, $"/api/patients/{patientId}/forms");
        Assert.Equal(1, list.Body.GetArrayLength());
        var docs = await desk.SendAsync(HttpMethod.Get, $"/api/patients/{patientId}/signed-documents");
        Assert.Equal(1, docs.Body.GetArrayLength());
        Assert.False(docs.Body[0].TryGetProperty("responses", out _));
    }

    [Fact]
    public async Task Signing_without_an_idempotency_key_or_with_missing_signer_details_is_a_400_with_codes_and_field_messages()
    {
        await using var factory = CreateFactory();
        var (desk, _, template, patientId) = await ArrangeAsync(factory);
        var ready = await ReadyFormAsync(desk, patientId, template.GetProperty("template").GetProperty("id").GetString()!);
        var formId = ready.GetProperty("summary").GetProperty("id").GetString();

        var noKey = await desk.SendAsync(HttpMethod.Post, $"/api/forms/{formId}/sign", SignBody(ready));
        Assert.Equal(HttpStatusCode.BadRequest, noKey.Status);
        Assert.Equal("idempotency_key_required", noKey.Body.GetProperty("error").GetString());

        var missing = await desk.SendAsync(HttpMethod.Post, $"/api/forms/{formId}/sign", SignBody(ready, signer: "", relationship: "", attested: false), "missing-details-01");
        Assert.Equal(HttpStatusCode.BadRequest, missing.Status);
        Assert.Equal("validation_failed", missing.Body.GetProperty("error").GetString());
        var fe = missing.Body.GetProperty("fieldErrors");
        Assert.True(fe.TryGetProperty("signerName", out _));
        Assert.True(fe.TryGetProperty("relationship", out _));
        Assert.True(fe.TryGetProperty("attested", out _));

        Assert.Equal("Draft", (await desk.SendAsync(HttpMethod.Get, $"/api/forms/{formId}")).Body.GetProperty("summary").GetProperty("status").GetString());
    }

    [Fact]
    public async Task A_stale_draft_version_is_a_409_concurrency_conflict_and_a_missing_one_is_a_400()
    {
        await using var factory = CreateFactory();
        var (desk, _, template, patientId) = await ArrangeAsync(factory);
        var ready = await ReadyFormAsync(desk, patientId, template.GetProperty("template").GetProperty("id").GetString()!);
        var formId = ready.GetProperty("summary").GetProperty("id").GetString();
        await desk.SendAsync(HttpMethod.Put, $"/api/forms/{formId}/responses", new { responses = new Dictionary<string, string> { ["acknowledged"] = "true", ["nickname"] = "Changed" }, rowVersion = ready.GetProperty("rowVersion").GetString() });

        var stale = await desk.SendAsync(HttpMethod.Post, $"/api/forms/{formId}/sign", SignBody(ready), "stale-key-000001");
        Assert.Equal(HttpStatusCode.Conflict, stale.Status);
        Assert.Equal("concurrency_conflict", stale.Body.GetProperty("error").GetString());

        var missing = await desk.SendAsync(HttpMethod.Put, $"/api/forms/{formId}/responses", new { responses = new Dictionary<string, string>() });
        Assert.Equal(HttpStatusCode.BadRequest, missing.Status);
        Assert.Equal("row_version_required", missing.Body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Template_versions_are_published_over_http_and_a_stale_administrator_gets_a_409()
    {
        await using var factory = CreateFactory();
        var manager = await SessionAsync(factory, "OfficeManager");
        var created = await manager.SendAsync(HttpMethod.Post, "/api/forms/templates", TemplateBody());
        var id = created.Body.GetProperty("template").GetProperty("id").GetString();
        var rv = created.Body.GetProperty("template").GetProperty("rowVersion").GetString();

        var v2 = await manager.SendAsync(HttpMethod.Put, $"/api/forms/templates/{id}", TemplateBody(body: "Second wording.", rowVersion: rv));
        Assert.Equal(HttpStatusCode.OK, v2.Status);
        Assert.Equal(2, v2.Body.GetProperty("versions").GetArrayLength());
        var stale = await manager.SendAsync(HttpMethod.Put, $"/api/forms/templates/{id}", TemplateBody(body: "Third wording.", rowVersion: rv));
        Assert.Equal(HttpStatusCode.Conflict, stale.Status);
        Assert.Equal("concurrency_conflict", stale.Body.GetProperty("error").GetString());
        var dup = await manager.SendAsync(HttpMethod.Post, "/api/forms/templates", TemplateBody());
        Assert.Equal(HttpStatusCode.Conflict, dup.Status);
        Assert.Equal("template_key_taken", dup.Body.GetProperty("error").GetString());
        var bad = await manager.SendAsync(HttpMethod.Post, "/api/forms/templates", new { key = "BAD KEY", category = "Privacy", title = "", body = "", fields = Array.Empty<object>() });
        Assert.Equal("validation_failed", bad.Body.GetProperty("error").GetString());
        Assert.True(bad.Body.GetProperty("fieldErrors").TryGetProperty("key", out _));
    }

    [Fact]
    public async Task The_available_templates_list_shows_only_active_templates_to_staff_who_complete_forms()
    {
        await using var factory = CreateFactory();
        var (desk, manager, template, _) = await ArrangeAsync(factory);
        var second = await manager.SendAsync(HttpMethod.Post, "/api/forms/templates", TemplateBody("second-form"));
        var id = template.GetProperty("template").GetProperty("id").GetString();
        await manager.SendAsync(HttpMethod.Put, $"/api/forms/templates/{id}/active", new { isActive = false, rowVersion = template.GetProperty("template").GetProperty("rowVersion").GetString() });

        var available = await desk.SendAsync(HttpMethod.Get, "/api/forms/templates/available");
        Assert.Equal(HttpStatusCode.OK, available.Status);
        Assert.Equal(["second-form"], available.Body.EnumerateArray().Select(t => t.GetProperty("key").GetString()));
        Assert.Equal(HttpStatusCode.Forbidden, (await desk.SendAsync(HttpMethod.Get, "/api/forms/templates")).Status); // the administration list is for administrators
        Assert.Equal(2, (await manager.SendAsync(HttpMethod.Get, "/api/forms/templates")).Body.GetArrayLength());
        Assert.Equal(HttpStatusCode.Created, second.Status);
    }
}
