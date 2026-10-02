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

/// <summary>ALV-004-C01 at the HTTP boundary: who may reschedule, cancel, mark a no-show or add a note, the CSRF and row-version contract, and the stable refusal shapes the calendar depends on.</summary>
public class AppointmentLifecycleApiTests : IAsyncLifetime
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

    private async Task<JsonElement> BookAsync(Session session, DateTime start, Guid? provider = null, Guid? operatory = null, Guid? patient = null, string? key = null)
    {
        var r = await session.SendAsync(HttpMethod.Post, "/api/appointments", Body(start, provider, operatory, patient: patient), key ?? $"book-{Guid.NewGuid():N}");
        Assert.Equal(HttpStatusCode.Created, r.Status);
        return r.Body;
    }

    private static object Move(JsonElement a, DateTime start, Guid? provider = null, Guid? operatory = null, int? duration = null, string? rowVersion = null) => new
    {
        providerId = provider ?? a.GetProperty("providerId").GetGuid(), operatoryId = operatory ?? a.GetProperty("operatoryId").GetGuid(),
        startLocal = start.ToString("yyyy-MM-ddTHH:mm"), durationMinutes = duration, rowVersion = rowVersion ?? a.GetProperty("rowVersion").GetString(),
    };

    private static string Url(JsonElement a, string tail = "") => $"/api/appointments/{a.GetProperty("id").GetString()}{tail}";

    // ---------- authorization ----------

    [Fact]
    public async Task Anonymous_callers_get_401_from_every_lifecycle_endpoint()
    {
        await using var factory = CreateFactory();
        var anon = factory.CreateClient();
        var id = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync($"/api/appointments/{id}/history")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PutAsJsonAsync($"/api/appointments/{id}/reschedule", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsJsonAsync($"/api/appointments/{id}/cancel", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsJsonAsync($"/api/appointments/{id}/no-show", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PutAsJsonAsync($"/api/appointments/{id}/notes", new { })).StatusCode);
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
    public async Task Only_roles_that_manage_appointments_can_reschedule_cancel_or_annotate_one(string role, HttpStatusCode expected)
    {
        await using var factory = CreateFactory();
        var desk = await SessionAsync(factory, "FrontDesk");
        var a = await BookAsync(desk, At(9));
        var session = await SessionAsync(factory, role);
        var ok = expected == HttpStatusCode.OK;

        var moved = await session.SendAsync(HttpMethod.Put, Url(a, "/reschedule"), Move(a, At(14)));
        var afterMove = ok ? moved.Body.GetProperty("rowVersion").GetString() : a.GetProperty("rowVersion").GetString();
        var noted = await session.SendAsync(HttpMethod.Put, Url(a, "/notes"), new { notes = "Hello", rowVersion = afterMove });
        var afterNote = ok ? noted.Body.GetProperty("rowVersion").GetString() : afterMove;
        var cancelled = await session.SendAsync(HttpMethod.Post, Url(a, "/cancel"), new { reason = "Illness", rowVersion = afterNote });

        Assert.Equal((expected, expected, expected), (moved.Status, noted.Status, cancelled.Status));
        if (!ok) Assert.Equal("ManageAppointments", moved.Body.GetProperty("required").GetString());
    }

    [Theory]
    [InlineData("FrontDesk", HttpStatusCode.OK)]
    [InlineData("Dentist", HttpStatusCode.OK)]
    [InlineData("Hygienist", HttpStatusCode.OK)]
    [InlineData("Billing", HttpStatusCode.Forbidden)]
    [InlineData("Unassigned", HttpStatusCode.Forbidden)]
    public async Task Only_roles_that_view_the_schedule_can_read_an_appointments_history(string role, HttpStatusCode expected)
    {
        await using var factory = CreateFactory();
        var desk = await SessionAsync(factory, "FrontDesk");
        var a = await BookAsync(desk, At(9));
        var reader = await SessionAsync(factory, role);

        Assert.Equal(expected, (await reader.SendAsync(HttpMethod.Get, Url(a, "/history"))).Status);
    }

    [Fact]
    public async Task Every_lifecycle_change_needs_a_csrf_token()
    {
        await using var factory = CreateFactory();
        var desk = await SessionAsync(factory, "FrontDesk");
        var a = await BookAsync(desk, At(9));
        var rv = a.GetProperty("rowVersion").GetString();

        Assert.Equal(HttpStatusCode.BadRequest, (await desk.SendAsync(HttpMethod.Put, Url(a, "/reschedule"), Move(a, At(14)), csrf: false)).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await desk.SendAsync(HttpMethod.Post, Url(a, "/cancel"), new { reason = "x", rowVersion = rv }, csrf: false)).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await desk.SendAsync(HttpMethod.Post, Url(a, "/no-show"), new { rowVersion = rv }, csrf: false)).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await desk.SendAsync(HttpMethod.Put, Url(a, "/notes"), new { notes = "x", rowVersion = rv }, csrf: false)).Status);
        Assert.Equal("2030-01-14T09:00", (await desk.SendAsync(HttpMethod.Get, Url(a))).Body.GetProperty("startLocal").GetString());
    }

    // ---------- reschedule ----------

    [Fact]
    public async Task A_reschedule_returns_the_moved_appointment_with_a_new_version_and_the_old_version_is_then_a_409()
    {
        await using var factory = CreateFactory();
        var desk = await SessionAsync(factory, "FrontDesk");
        var a = await BookAsync(desk, At(9));

        var moved = await desk.SendAsync(HttpMethod.Put, Url(a, "/reschedule"), Move(a, At(14), _s.ProviderB, _s.Op2));
        Assert.Equal(HttpStatusCode.OK, moved.Status);
        Assert.Equal(("2030-01-14T14:00", "Dr. Patel", "Op 2"), (moved.Body.GetProperty("startLocal").GetString(), moved.Body.GetProperty("providerName").GetString(), moved.Body.GetProperty("operatoryName").GetString()));
        Assert.NotEqual(a.GetProperty("rowVersion").GetString(), moved.Body.GetProperty("rowVersion").GetString());

        var stale = await desk.SendAsync(HttpMethod.Put, Url(a, "/reschedule"), Move(a, At(15))); // still carrying the old version
        Assert.Equal(HttpStatusCode.Conflict, stale.Status);
        Assert.Equal("concurrency_conflict", stale.Body.GetProperty("error").GetString());
        var missing = await desk.SendAsync(HttpMethod.Put, Url(a, "/reschedule"), new { providerId = _s.ProviderA, operatoryId = _s.Op1, startLocal = "2030-01-14T15:00" });
        Assert.Equal((HttpStatusCode.BadRequest, "row_version_required"), (missing.Status, missing.Body.GetProperty("error").GetString()));
    }

    [Fact]
    public async Task A_reschedule_into_a_conflict_is_a_409_with_the_code_and_the_appointment_in_the_way_and_nothing_moves()
    {
        await using var factory = CreateFactory();
        var desk = await SessionAsync(factory, "FrontDesk");
        var bo = await _s.PatientAsync("Bo", "Kim");
        var mine = await BookAsync(desk, At(9));
        var theirs = await BookAsync(desk, At(11), _s.ProviderB, _s.Op2, bo);
        var holder = theirs.GetProperty("id").GetString();

        var provider = await desk.SendAsync(HttpMethod.Put, Url(mine, "/reschedule"), Move(mine, At(11, 30), _s.ProviderB, _s.Op1));
        Assert.Equal((HttpStatusCode.Conflict, "provider_double_booked", holder), (provider.Status, provider.Body.GetProperty("error").GetString(), provider.Body.GetProperty("conflictingAppointmentId").GetString()));
        var operatory = await desk.SendAsync(HttpMethod.Put, Url(mine, "/reschedule"), Move(mine, At(11, 30), _s.ProviderA, _s.Op2));
        Assert.Equal("operatory_conflict", operatory.Body.GetProperty("error").GetString());
        var unavailable = await desk.SendAsync(HttpMethod.Put, Url(mine, "/reschedule"), Move(mine, At(6)));
        Assert.Equal(("provider_unavailable", "outside_working_hours"), (unavailable.Body.GetProperty("error").GetString(), unavailable.Body.GetProperty("reason").GetString()));
        var duration = await desk.SendAsync(HttpMethod.Put, Url(mine, "/reschedule"), Move(mine, At(14), duration: 7));
        Assert.Equal((HttpStatusCode.BadRequest, "invalid_duration"), (duration.Status, duration.Body.GetProperty("error").GetString()));
        var badStart = await desk.SendAsync(HttpMethod.Put, Url(mine, "/reschedule"), new { providerId = _s.ProviderA, operatoryId = _s.Op1, startLocal = "soon", rowVersion = mine.GetProperty("rowVersion").GetString() });
        Assert.Equal("invalid_local_time", badStart.Body.GetProperty("error").GetString());
        var unknown = await desk.SendAsync(HttpMethod.Put, $"/api/appointments/{Guid.NewGuid()}/reschedule", Move(mine, At(14)));
        Assert.Equal((HttpStatusCode.NotFound, "appointment_not_found"), (unknown.Status, unknown.Body.GetProperty("error").GetString()));

        Assert.Equal("2030-01-14T09:00", (await desk.SendAsync(HttpMethod.Get, Url(mine))).Body.GetProperty("startLocal").GetString());
    }

    [Fact]
    public async Task A_patient_booked_elsewhere_at_the_same_time_is_a_patient_double_booked_409_for_a_new_booking_too()
    {
        await using var factory = CreateFactory();
        var desk = await SessionAsync(factory, "FrontDesk");
        var first = await BookAsync(desk, At(9));

        var refused = await desk.SendAsync(HttpMethod.Post, "/api/appointments", Body(At(9, 30), _s.ProviderB, _s.Op2), "patient-clash-001");

        Assert.Equal((HttpStatusCode.Conflict, "patient_double_booked", first.GetProperty("id").GetString()),
            (refused.Status, refused.Body.GetProperty("error").GetString(), refused.Body.GetProperty("conflictingAppointmentId").GetString()));
    }

    // ---------- cancel, no-show, notes, history ----------

    [Fact]
    public async Task Cancelling_needs_a_reason_and_the_cancelled_appointment_leaves_the_booked_list_but_stays_in_the_calendar_view()
    {
        await using var factory = CreateFactory();
        var desk = await SessionAsync(factory, "FrontDesk");
        var a = await BookAsync(desk, At(9));
        var rv = a.GetProperty("rowVersion").GetString();

        var noReason = await desk.SendAsync(HttpMethod.Post, Url(a, "/cancel"), new { reason = " ", rowVersion = rv });
        Assert.Equal((HttpStatusCode.BadRequest, "reason_required"), (noReason.Status, noReason.Body.GetProperty("error").GetString()));

        var cancelled = await desk.SendAsync(HttpMethod.Post, Url(a, "/cancel"), new { reason = "Patient called", rowVersion = rv });
        Assert.Equal(HttpStatusCode.OK, cancelled.Status);
        Assert.Equal(("Cancelled", "Patient called"), (cancelled.Body.GetProperty("status").GetString(), cancelled.Body.GetProperty("cancelReason").GetString()));
        var again = await desk.SendAsync(HttpMethod.Post, Url(a, "/cancel"), new { reason = "Patient called", rowVersion = rv }); // a retry of the same request
        Assert.Equal(HttpStatusCode.OK, again.Status);

        Assert.Equal(0, (await desk.SendAsync(HttpMethod.Get, "/api/appointments?from=2030-01-14")).Body.GetArrayLength());
        var calendar = await desk.SendAsync(HttpMethod.Get, "/api/appointments?from=2030-01-14&includeAll=true");
        Assert.Equal(1, calendar.Body.GetArrayLength());
        Assert.Equal("Cancelled", calendar.Body[0].GetProperty("status").GetString());
    }

    [Fact]
    public async Task A_no_show_cannot_be_recorded_before_the_start_time_and_the_refusal_says_so()
    {
        await using var factory = CreateFactory();
        var desk = await SessionAsync(factory, "FrontDesk");
        var a = await BookAsync(desk, At(9));

        var early = await desk.SendAsync(HttpMethod.Post, Url(a, "/no-show"), new { rowVersion = a.GetProperty("rowVersion").GetString() });

        Assert.Equal((HttpStatusCode.Conflict, "no_show_too_early"), (early.Status, early.Body.GetProperty("error").GetString()));
        Assert.Equal("Scheduled", (await desk.SendAsync(HttpMethod.Get, Url(a))).Body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Notes_are_set_at_booking_changed_over_http_and_a_note_too_long_is_a_400()
    {
        await using var factory = CreateFactory();
        var desk = await SessionAsync(factory, "FrontDesk");
        var booked = await desk.SendAsync(HttpMethod.Post, "/api/appointments", new { patientId = _ann, providerId = _s.ProviderA, operatoryId = _s.Op1, appointmentTypeId = _s.Long60, startLocal = "2030-01-14T09:00", notes = "Needs an accessible room" }, "note-book-0001");
        Assert.Equal("Needs an accessible room", booked.Body.GetProperty("notes").GetString());

        var changed = await desk.SendAsync(HttpMethod.Put, Url(booked.Body, "/notes"), new { notes = "Prefers mornings", rowVersion = booked.Body.GetProperty("rowVersion").GetString() });
        Assert.Equal("Prefers mornings", changed.Body.GetProperty("notes").GetString());
        var tooLong = await desk.SendAsync(HttpMethod.Put, Url(booked.Body, "/notes"), new { notes = new string('x', 1001), rowVersion = changed.Body.GetProperty("rowVersion").GetString() });
        Assert.Equal((HttpStatusCode.BadRequest, "invalid_notes"), (tooLong.Status, tooLong.Body.GetProperty("error").GetString()));
    }

    [Fact]
    public async Task The_history_lists_what_happened_oldest_first_with_where_a_rescheduled_appointment_was()
    {
        await using var factory = CreateFactory();
        var desk = await SessionAsync(factory, "FrontDesk");
        var a = await BookAsync(desk, At(9));
        var moved = await desk.SendAsync(HttpMethod.Put, Url(a, "/reschedule"), Move(a, At(14), _s.ProviderB, _s.Op2));
        await desk.SendAsync(HttpMethod.Post, Url(a, "/cancel"), new { reason = "Illness", rowVersion = moved.Body.GetProperty("rowVersion").GetString() });

        var history = await desk.SendAsync(HttpMethod.Get, Url(a, "/history"));

        Assert.Equal(["Scheduled", "Rescheduled", "Cancelled"], history.Body.EnumerateArray().Select(e => e.GetProperty("eventType").GetString()));
        var rescheduled = history.Body[1];
        Assert.Equal(("2030-01-14T09:00", "Dr. Rivera", "Op 1"), (rescheduled.GetProperty("previousStartLocal").GetString(), rescheduled.GetProperty("previousProviderName").GetString(), rescheduled.GetProperty("previousOperatoryName").GetString()));
        Assert.Equal(HttpStatusCode.NotFound, (await desk.SendAsync(HttpMethod.Get, $"/api/appointments/{Guid.NewGuid()}/history")).Status);
    }

    [Fact]
    public async Task Lifecycle_changes_over_http_are_audited_with_the_signed_in_user()
    {
        await using var factory = CreateFactory();
        var desk = await SessionAsync(factory, "FrontDesk");
        var a = await BookAsync(desk, At(9));
        var moved = await desk.SendAsync(HttpMethod.Put, Url(a, "/reschedule"), Move(a, At(14)));
        await desk.SendAsync(HttpMethod.Post, Url(a, "/cancel"), new { reason = "Illness", rowVersion = moved.Body.GetProperty("rowVersion").GetString() });

        await using var db = _fixture.CreateContext();
        var entries = await db.AuditLogEntries.Where(e => e.EventType == SchedulingAuditEvents.Rescheduled || e.EventType == SchedulingAuditEvents.Cancelled).OrderBy(e => e.TimestampUtc).ToListAsync();
        Assert.Equal([SchedulingAuditEvents.Rescheduled, SchedulingAuditEvents.Cancelled], entries.Select(e => e.EventType));
        Assert.Single(entries.Select(e => e.PerformedByUserAccountId).Distinct());
        Assert.NotNull(entries[0].PerformedByUserAccountId);
        Assert.All(entries, e => Assert.DoesNotContain("Illness", e.Details));
    }
}
