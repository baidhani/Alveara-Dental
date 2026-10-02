using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Patients;
using Alveara.Api.Controllers;
using Xunit;
using static Alveara.Api.Tests.PatientTestSupport;

namespace Alveara.Api.Tests;

/// <summary>ALV-003-C01: "validation appropriate to configurable practice requirements" - a practice may require email and/or sex; the server enforces it everywhere a patient is saved.</summary>
public class PatientRequirementsTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private PatientTestSupport _s = null!;

    public async Task InitializeAsync() { await _fixture.InitializeAsync(); _s = new PatientTestSupport(_fixture); }
    public Task DisposeAsync() => _fixture.DisposeAsync();

    private PatientRegistrationSettingsService Settings(Alveara.Api.Data.AlveraDbContext db) => new(db, PatientTestSupport.Clock);

    private async Task<PatientRegistrationSettingsView> SaveAsync(bool email, bool sex, string? version)
    {
        await using var db = _fixture.CreateContext();
        return await Settings(db).UpdateAsync(email, sex, version, _s.Actor, default);
    }

    private async Task<PatientException> RefusedRegistrationAsync(RegisterPatientRequest request)
    {
        await using var db = _fixture.CreateContext();
        return await Assert.ThrowsAsync<PatientRegistrationException>(() => _s.Registration(db).RegisterAsync(request, Guid.NewGuid().ToString("N"), _s.Actor, default));
    }

    private static RegisterPatientRequest Person(string? email = null, string? sex = null) =>
        new("Ann", null, "Lee", "1985-03-09", sex, "555-010-0100", email, "1 Main St", null, "Austin", "TX", "78701");

    [Fact]
    public async Task By_default_nothing_extra_is_required_which_is_STORY_003s_behavior()
    {
        await using var db = _fixture.CreateContext();
        Assert.Equal(PatientRequirements.None, await PatientRegistrationSettingsService.LoadRequirementsAsync(db, default));
        var view = await Settings(db).GetAsync(default);
        Assert.Equal((false, false, ""), (view.RequireEmail, view.RequireSex, view.RowVersion));

        Assert.True((await _s.Registration(db).RegisterAsync(Person(), "k-default", _s.Actor, default)).Created);
    }

    [Fact]
    public async Task When_the_practice_requires_email_registration_prompts_for_it_and_stores_nothing_without_it()
    {
        await SaveAsync(email: true, sex: false, version: "");

        foreach (var blank in new string?[] { null, "", "   " })
        {
            var refused = await RefusedRegistrationAsync(Person(email: blank));
            Assert.Equal("validation_failed", refused.Code);
            Assert.Equal(new[] { "email" }, refused.FieldErrors.Keys);
            Assert.Equal("Email is required.", refused.FieldErrors["email"]);
        }
        await using (var db = _fixture.CreateContext()) Assert.Equal(0, await db.Patients.CountAsync());

        await using var ok = _fixture.CreateContext();
        Assert.True((await _s.Registration(ok).RegisterAsync(Person(email: "ann@example.test"), "k-ok", _s.Actor, default)).Created);
    }

    [Fact]
    public async Task A_required_email_is_still_format_checked_with_one_message()
    {
        await SaveAsync(true, false, "");
        var refused = await RefusedRegistrationAsync(Person(email: "nope"));
        Assert.Equal("Email must look like name@example.com.", refused.FieldErrors["email"]);
    }

    [Fact]
    public async Task When_the_practice_requires_sex_registration_prompts_for_it()
    {
        await SaveAsync(false, true, "");

        var refused = await RefusedRegistrationAsync(Person(sex: null));
        Assert.Equal(new[] { "sex" }, refused.FieldErrors.Keys);
        Assert.Equal("Sex is required.", refused.FieldErrors["sex"]);

        await using var db = _fixture.CreateContext();
        Assert.True((await _s.Registration(db).RegisterAsync(Person(sex: "Female"), "k-ok", _s.Actor, default)).Created);
    }

    [Fact]
    public async Task Both_requirements_are_reported_together_with_the_always_required_fields()
    {
        await SaveAsync(true, true, "");
        var refused = await RefusedRegistrationAsync(new RegisterPatientRequest("Ann", null, null, "1985-03-09", null, "555-010-0100", null, "1 Main St", null, "Austin", "TX", "78701"));
        Assert.Equal(new[] { "email", "lastName", "sex" }, refused.FieldErrors.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task The_early_duplicate_check_applies_the_same_requirements()
    {
        await SaveAsync(true, false, "");
        await using var db = _fixture.CreateContext();
        var ex = await Assert.ThrowsAsync<PatientRegistrationException>(() => _s.Registration(db).CheckDuplicatesAsync(Person().Fields, default));
        Assert.Contains("email", ex.FieldErrors.Keys);
    }

    [Fact]
    public async Task Editing_a_patient_who_lacks_a_newly_required_field_prompts_for_it_but_inactivating_and_relationships_do_not()
    {
        var patient = await _s.RegisterAsync("Ann", "Lee", "1985-03-09", "555-010-0100"); // registered before the requirement existed
        await SaveAsync(true, false, "");

        await using (var db = _fixture.CreateContext())
        {
            var ex = await Assert.ThrowsAsync<PatientException>(() => _s.Edits(db).UpdateAsync(patient.Id, Fields(patient) with { City = "Dallas" }, Version(patient), _s.Actor, default));
            Assert.Equal(new[] { "email" }, ex.FieldErrors.Keys);
        }
        Assert.Equal("Austin", (await _s.ReloadAsync(patient.Id)).City); // refused, nothing changed

        await using (var db = _fixture.CreateContext())
        {
            var fixedUp = await _s.Edits(db).UpdateAsync(patient.Id, Fields(patient) with { City = "Dallas", Email = "ann@example.test" }, Version(patient), _s.Actor, default);
            Assert.Equal("ann@example.test", fixedUp.Email);
        }

        var current = await _s.ReloadAsync(patient.Id);
        await using var db2 = _fixture.CreateContext();
        Assert.False((await _s.Edits(db2).SetActiveAsync(patient.Id, false, Version(current), _s.Actor, default)).IsActive); // status changes never need the field
    }

    [Fact]
    public async Task Relaxing_a_requirement_applies_immediately()
    {
        var saved = await SaveAsync(true, true, "");
        await SaveAsync(false, false, saved.RowVersion);

        await using var db = _fixture.CreateContext();
        Assert.True((await _s.Registration(db).RegisterAsync(Person(), "k-relaxed", _s.Actor, default)).Created);
    }

    // ---------- the settings record itself ----------

    [Fact]
    public async Task Settings_round_trip_with_a_row_version_and_are_audited_with_the_user_and_time_and_no_patient_data()
    {
        var first = await SaveAsync(true, false, "");
        Assert.True(first.RequireEmail);
        Assert.False(string.IsNullOrEmpty(first.RowVersion));

        var second = await SaveAsync(true, true, first.RowVersion);
        Assert.True(second.RequireSex);
        Assert.NotEqual(first.RowVersion, second.RowVersion);

        await using var db = _fixture.CreateContext();
        var audits = await db.AuditLogEntries.Where(a => a.EventType == PatientAuditEventsV2.RequirementsChanged).OrderBy(a => a.TimestampUtc).ToListAsync();
        Assert.Equal(2, audits.Count);
        Assert.All(audits, a => { Assert.Equal(_s.Actor, a.PerformedByUserAccountId); Assert.Equal(nameof(PatientRegistrationSettings), a.EntityType); });
        Assert.Contains("email required", audits[1].Details);
        Assert.Contains("sex required", audits[1].Details);
        Assert.Equal(1, await db.PatientRegistrationSettings.CountAsync());
    }

    [Fact]
    public async Task Saving_unchanged_settings_is_a_no_op_and_a_stale_save_is_a_conflict()
    {
        var first = await SaveAsync(true, false, "");
        var same = await SaveAsync(true, false, first.RowVersion);
        Assert.Equal(first.RowVersion, same.RowVersion);
        await using (var db = _fixture.CreateContext()) Assert.Equal(1, await db.AuditLogEntries.CountAsync(a => a.EventType == PatientAuditEventsV2.RequirementsChanged));

        var current = await SaveAsync(false, true, first.RowVersion); // another admin saves
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => SaveAsync(true, true, first.RowVersion)); // the first admin's page is stale
        await using var verify = _fixture.CreateContext();
        var stored = await Settings(verify).GetAsync(default);
        Assert.Equal((false, true, current.RowVersion), (stored.RequireEmail, stored.RequireSex, stored.RowVersion));
    }

    [Fact]
    public async Task A_save_without_a_version_is_refused_and_two_simultaneous_first_saves_create_exactly_one_row()
    {
        await using (var db = _fixture.CreateContext())
        {
            var ex = await Assert.ThrowsAsync<PatientException>(() => Settings(db).UpdateAsync(true, false, null, _s.Actor, default));
            Assert.Equal("row_version_required", ex.Code);
        }

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 6).Select(async i =>
        {
            try { await SaveAsync(i % 2 == 0, i % 2 == 1, ""); return "saved"; }
            catch (ConcurrencyConflictException) { return "conflict"; }
        }));

        Assert.Equal(1, outcomes.Count(o => o == "saved"));
        await using var verify = _fixture.CreateContext();
        Assert.Equal(1, await verify.PatientRegistrationSettings.CountAsync()); // the database guarantees a single row
    }

    // ---------- over HTTP ----------

    private WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Alveara", _fixture.ConnectionString);
            builder.UseSetting("AdminBootstrapSecret", IdentityTestHelpers.TestBootstrapSecret);
            builder.UseSetting("AuthAttemptRateLimit:PermitLimit", "500");
        });

    private static async Task<string> CsrfAsync(HttpClient c) => (await (await c.GetAsync("/api/auth/csrf-token")).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;

    private static HttpRequestMessage Put(string url, string? csrf, object body)
    {
        var r = new HttpRequestMessage(HttpMethod.Put, url) { Content = JsonContent.Create(body) };
        if (csrf is not null) r.Headers.Add("X-CSRF-Token", csrf);
        return r;
    }

    private static async Task<HttpClient> UserAsync(WebApplicationFactory<Program> factory, HttpClient admin, string csrf, string role)
    {
        var username = $"user-{Guid.NewGuid():N}";
        var id = (await (await admin.PostAsJsonAsync("/api/auth/register", new RegisterRequest(username, "password"))).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await admin.SendAsync(Put($"/api/auth/{id}/role", csrf, new ChangeRoleRequest(role)));
        await admin.SendAsync(Put($"/api/auth/{id}/enabled", csrf, new SetEnabledRequest(true)));
        var client = factory.CreateClient();
        await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(username, "password"));
        return client;
    }

    [Fact]
    public async Task Everyone_who_can_see_patients_can_read_the_settings_but_only_practice_managers_can_change_them()
    {
        await using var factory = CreateFactory();
        var admin = factory.CreateClient();
        var adminName = $"admin-{Guid.NewGuid():N}";
        await admin.PostAsJsonAsync("/api/auth/bootstrap-admin", new BootstrapAdminRequest(adminName, "admin-password", IdentityTestHelpers.TestBootstrapSecret));
        await admin.PostAsJsonAsync("/api/auth/login", new LoginRequest(adminName, "admin-password"));
        var adminCsrf = await CsrfAsync(admin);

        var manager = await UserAsync(factory, admin, adminCsrf, "OfficeManager");
        var desk = await UserAsync(factory, admin, adminCsrf, "FrontDesk");
        var dentist = await UserAsync(factory, admin, adminCsrf, "Dentist");

        foreach (var reader in new[] { manager, desk, dentist })
            Assert.Equal(HttpStatusCode.OK, (await reader.GetAsync("/api/patients/registration-settings")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/patients/registration-settings")).StatusCode);

        foreach (var writer in new[] { desk, dentist })
            Assert.Equal(HttpStatusCode.Forbidden, (await writer.SendAsync(Put("/api/patients/registration-settings", await CsrfAsync(writer), new SaveRegistrationSettingsRequest(true, false, "")))).StatusCode);

        var noCsrf = await manager.SendAsync(Put("/api/patients/registration-settings", null, new SaveRegistrationSettingsRequest(true, false, "")));
        Assert.Equal(HttpStatusCode.BadRequest, noCsrf.StatusCode);

        var saved = await manager.SendAsync(Put("/api/patients/registration-settings", await CsrfAsync(manager), new SaveRegistrationSettingsRequest(true, false, "")));
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var view = await saved.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(view.GetProperty("requireEmail").GetBoolean());

        // the front desk now sees the requirement and is held to it by the server
        var seen = await (await desk.GetAsync("/api/patients/registration-settings")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(seen.GetProperty("requireEmail").GetBoolean());
        var deskCsrf = await CsrfAsync(desk);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/patients") { Content = JsonContent.Create(Person()) };
        request.Headers.Add("X-CSRF-Token", deskCsrf);
        request.Headers.Add("Idempotency-Key", "k-1");
        var refused = await desk.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal("Email is required.", (await refused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("fieldErrors").GetProperty("email").GetString());

        var stale = await manager.SendAsync(Put("/api/patients/registration-settings", await CsrfAsync(manager), new SaveRegistrationSettingsRequest(false, false, "")));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode); // "" is no longer the current version
    }
}
