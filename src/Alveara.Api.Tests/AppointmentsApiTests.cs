using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Scheduling;
using Alveara.Api.Controllers;
using Xunit;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>STORY-004 at the HTTP boundary: who may book and who may read, the CSRF + Idempotency-Key contract, and the stable refusal shapes the scheduling form depends on.</summary>
public class AppointmentsApiTests : IAsyncLifetime
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

    private object Body(DateTime start, Guid? provider = null, Guid? operatory = null, int? duration = null, Guid? patient = null) => new
    {
        patientId = patient ?? _ann, providerId = provider ?? _s.ProviderA, operatoryId = operatory ?? _s.Op1, appointmentTypeId = _s.Long60,
        startLocal = start.ToString("yyyy-MM-ddTHH:mm"), durationMinutes = duration,
    };

    // ---------- authorization ----------

    [Fact]
    public async Task Anonymous_callers_get_401_everywhere()
    {
        await using var factory = CreateFactory();
        var anon = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/appointments?from=2030-01-14")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync($"/api/appointments/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsJsonAsync("/api/appointments", Body(At(9)))).StatusCode);
    }

    [Theory]
    [InlineData("FrontDesk", HttpStatusCode.Created)]
    [InlineData("OfficeManager", HttpStatusCode.Created)]
    [InlineData("Admin", HttpStatusCode.Created)]
    [InlineData("Dentist", HttpStatusCode.Forbidden)]
    [InlineData("Hygienist", HttpStatusCode.Forbidden)]
    [InlineData("Assistant", HttpStatusCode.Forbidden)]
    [InlineData("Billing", HttpStatusCode.Forbidden)]
    [InlineData("Unassigned", HttpStatusCode.Forbidden)]
    public async Task Only_roles_that_manage_appointments_can_book(string role, HttpStatusCode expected)
    {
        await using var factory = CreateFactory();
        var session = await SessionAsync(factory, role);

        var result = await session.SendAsync(HttpMethod.Post, "/api/appointments", Body(At(9)), "book-key-00001");

        Assert.Equal(expected, result.Status);
        if (expected == HttpStatusCode.Forbidden) Assert.Equal("ManageAppointments", result.Body.GetProperty("required").GetString());
        Assert.Equal(expected == HttpStatusCode.Created ? 1 : 0, await _s.CountAsync());
    }

    [Theory]
    [InlineData("FrontDesk", HttpStatusCode.OK)]
    [InlineData("OfficeManager", HttpStatusCode.OK)]
    [InlineData("Dentist", HttpStatusCode.OK)]
    [InlineData("Hygienist", HttpStatusCode.OK)]
    [InlineData("Assistant", HttpStatusCode.OK)]
    [InlineData("Billing", HttpStatusCode.Forbidden)]
    [InlineData("Unassigned", HttpStatusCode.Forbidden)]
    public async Task Only_roles_that_view_the_schedule_can_read_it(string role, HttpStatusCode expected)
    {
        await using var factory = CreateFactory();
        var created = (await _s.ScheduleAsync(_s.Request(_ann, At(9)))).Appointment;
        var reader = await SessionAsync(factory, role);

        Assert.Equal(expected, (await reader.SendAsync(HttpMethod.Get, "/api/appointments?from=2030-01-14")).Status);
        Assert.Equal(expected, (await reader.SendAsync(HttpMethod.Get, $"/api/appointments/{created.Id}")).Status);
    }

    [Fact]
    public async Task Booking_needs_a_csrf_token_and_an_idempotency_key()
    {
        await using var factory = CreateFactory();
        var desk = await SessionAsync(factory, "FrontDesk");

        Assert.Equal(HttpStatusCode.BadRequest, (await desk.SendAsync(HttpMethod.Post, "/api/appointments", Body(At(9)), "csrf-key-0001", csrf: false)).Status);
        var noKey = await desk.SendAsync(HttpMethod.Post, "/api/appointments", Body(At(9)));
        Assert.Equal(HttpStatusCode.BadRequest, noKey.Status);
        Assert.Equal("idempotency_key_required", noKey.Body.GetProperty("error").GetString());
        Assert.Equal(0, await _s.CountAsync());
    }

    // ---------- booking over HTTP ----------

    [Fact]
    public async Task A_front_desk_books_an_available_provider_and_a_retry_returns_the_same_appointment()
    {
        await using var factory = CreateFactory();
        var desk = await SessionAsync(factory, "FrontDesk");

        var first = await desk.SendAsync(HttpMethod.Post, "/api/appointments", Body(At(9)), "http-book-key-1");
        var retry = await desk.SendAsync(HttpMethod.Post, "/api/appointments", Body(At(9)), "http-book-key-1");

        Assert.Equal(HttpStatusCode.Created, first.Status);
        Assert.Equal(HttpStatusCode.OK, retry.Status); // the same submit again: the first result, nothing new
        Assert.Equal(first.Body.GetProperty("id").GetString(), retry.Body.GetProperty("id").GetString());
        Assert.Equal("Scheduled", first.Body.GetProperty("status").GetString());
        Assert.Equal("2030-01-14T09:00", first.Body.GetProperty("startLocal").GetString());
        Assert.Equal("2030-01-14T10:00", first.Body.GetProperty("endLocal").GetString());
        Assert.Equal("Dr. Rivera", first.Body.GetProperty("providerName").GetString());
        Assert.Equal(1, await _s.CountAsync());
    }

    [Fact]
    public async Task A_double_booked_provider_is_a_409_naming_the_appointment_already_holding_the_time()
    {
        await using var factory = CreateFactory();
        var desk = await SessionAsync(factory, "FrontDesk");
        var bo = await _s.PatientAsync("Bo", "Kim");
        var held = await desk.SendAsync(HttpMethod.Post, "/api/appointments", Body(At(9)), "held-key-00001");

        var refused = await desk.SendAsync(HttpMethod.Post, "/api/appointments", Body(At(9, 30), operatory: _s.Op2, patient: bo), "second-key-0001");

        Assert.Equal(HttpStatusCode.Conflict, refused.Status);
        Assert.Equal("provider_double_booked", refused.Body.GetProperty("error").GetString());
        Assert.Equal(held.Body.GetProperty("id").GetString(), refused.Body.GetProperty("conflictingAppointmentId").GetString());
        Assert.Equal(1, await _s.CountAsync());
    }

    [Fact]
    public async Task An_operatory_conflict_and_an_unavailable_provider_are_409s_with_their_codes_and_reasons()
    {
        await using var factory = CreateFactory();
        var desk = await SessionAsync(factory, "FrontDesk");
        var bo = await _s.PatientAsync("Bo", "Kim");
        await desk.SendAsync(HttpMethod.Post, "/api/appointments", Body(At(9)), "held-key-00002");

        var operatory = await desk.SendAsync(HttpMethod.Post, "/api/appointments", Body(At(9), provider: _s.ProviderB, patient: bo), "op-key-0000001");
        Assert.Equal((HttpStatusCode.Conflict, "operatory_conflict"), (operatory.Status, operatory.Body.GetProperty("error").GetString()));

        var unavailable = await desk.SendAsync(HttpMethod.Post, "/api/appointments", Body(At(6), provider: _s.ProviderB, operatory: _s.Op2, patient: bo), "off-key-0000001");
        Assert.Equal((HttpStatusCode.Conflict, "provider_unavailable"), (unavailable.Status, unavailable.Body.GetProperty("error").GetString()));
        Assert.Equal("outside_working_hours", unavailable.Body.GetProperty("reason").GetString());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(600)]
    public async Task An_incorrect_duration_is_a_400_with_a_stable_code(int minutes)
    {
        await using var factory = CreateFactory();
        var desk = await SessionAsync(factory, "FrontDesk");

        var result = await desk.SendAsync(HttpMethod.Post, "/api/appointments", Body(At(9), duration: minutes), "dur-key-0000001");

        Assert.Equal((HttpStatusCode.BadRequest, "invalid_duration"), (result.Status, result.Body.GetProperty("error").GetString()));
    }

    [Fact]
    public async Task Missing_ids_and_an_unreadable_start_time_are_400s_and_unknown_ids_are_404s()
    {
        await using var factory = CreateFactory();
        var desk = await SessionAsync(factory, "FrontDesk");

        var missing = await desk.SendAsync(HttpMethod.Post, "/api/appointments", new { startLocal = "2030-01-14T09:00" }, "miss-key-000001");
        Assert.Equal("validation_failed", missing.Body.GetProperty("error").GetString());
        var badStart = await desk.SendAsync(HttpMethod.Post, "/api/appointments", new { patientId = _ann, providerId = _s.ProviderA, operatoryId = _s.Op1, appointmentTypeId = _s.Long60, startLocal = "next tuesday" }, "bad-key-0000001");
        Assert.Equal((HttpStatusCode.BadRequest, "invalid_local_time"), (badStart.Status, badStart.Body.GetProperty("error").GetString()));
        var unknown = await desk.SendAsync(HttpMethod.Post, "/api/appointments", Body(At(9), provider: Guid.NewGuid()), "unk-key-0000001");
        Assert.Equal((HttpStatusCode.NotFound, "provider_not_found"), (unknown.Status, unknown.Body.GetProperty("error").GetString()));
        Assert.Equal(0, await _s.CountAsync());
    }

    [Fact]
    public async Task Booking_over_http_is_audited_with_the_signed_in_user()
    {
        await using var factory = CreateFactory();
        var desk = await SessionAsync(factory, "FrontDesk");
        var created = await desk.SendAsync(HttpMethod.Post, "/api/appointments", Body(At(9)), "audit-key-00001");
        await desk.SendAsync(HttpMethod.Post, "/api/appointments", Body(At(9), operatory: _s.Op2), "audit-key-00002"); // refused: provider double-booked

        await using var db = _fixture.CreateContext();
        var entries = await db.AuditLogEntries.Where(a => a.EventType == SchedulingAuditEvents.Scheduled || a.EventType == SchedulingAuditEvents.Rejected).OrderBy(a => a.TimestampUtc).ToListAsync();
        Assert.Equal([SchedulingAuditEvents.Scheduled, SchedulingAuditEvents.Rejected], entries.Select(e => e.EventType));
        Assert.Single(entries.Select(e => e.PerformedByUserAccountId).Distinct());
        Assert.NotNull(entries[0].PerformedByUserAccountId);
        Assert.Equal(created.Body.GetProperty("id").GetGuid(), entries[0].TargetUserAccountId);
    }

    // ---------- reading ----------

    [Fact]
    public async Task The_schedule_can_be_listed_by_day_range_provider_and_operatory_and_read_by_id()
    {
        await using var factory = CreateFactory();
        var desk = await SessionAsync(factory, "FrontDesk");
        var bo = await _s.PatientAsync("Bo", "Kim");
        var a1 = (await _s.ScheduleAsync(_s.Request(_ann, At(11)))).Appointment;
        await _s.ScheduleAsync(_s.Request(bo, At(9), provider: _s.ProviderB, operatory: _s.Op2));
        await _s.ScheduleAsync(_s.Request(bo, At(9, 0, dayOffset: 1)));

        Assert.Equal(2, (await desk.SendAsync(HttpMethod.Get, "/api/appointments?from=2030-01-14")).Body.GetArrayLength());
        Assert.Equal(3, (await desk.SendAsync(HttpMethod.Get, "/api/appointments?from=2030-01-14&to=2030-01-15")).Body.GetArrayLength());
        var rivera = await desk.SendAsync(HttpMethod.Get, $"/api/appointments?from=2030-01-14&to=2030-01-15&providerId={_s.ProviderA}");
        Assert.Equal(2, rivera.Body.GetArrayLength());
        Assert.Equal(1, (await desk.SendAsync(HttpMethod.Get, $"/api/appointments?from=2030-01-14&operatoryId={_s.Op2}")).Body.GetArrayLength());

        var byId = await desk.SendAsync(HttpMethod.Get, $"/api/appointments/{a1.Id}");
        Assert.Equal("Ann Lee", byId.Body.GetProperty("patientName").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await desk.SendAsync(HttpMethod.Get, $"/api/appointments/{Guid.NewGuid()}")).Status);
    }

    [Theory]
    [InlineData("")]
    [InlineData("?from=tomorrow")]
    [InlineData("?from=2030-01-15&to=2030-01-14")]
    [InlineData("?from=2030-01-01&to=2030-03-01")]
    public async Task A_bad_or_oversized_date_range_is_a_400(string query)
    {
        await using var factory = CreateFactory();
        var desk = await SessionAsync(factory, "FrontDesk");
        var result = await desk.SendAsync(HttpMethod.Get, $"/api/appointments{query}");
        Assert.Equal((HttpStatusCode.BadRequest, "invalid_range"), (result.Status, result.Body.GetProperty("error").GetString()));
    }
}
