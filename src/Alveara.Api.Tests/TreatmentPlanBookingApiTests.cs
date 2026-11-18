using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Alveara.Api.Architecture.Clinical;
using Session = Alveara.Api.Tests.SchedulingApiHarness.Session;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N023 (owner decision OQ-1; briefing revision 2, S14 and the OQ-1 row of section 6), over HTTP against a real SQL Server: the front desk books an appointment naming the patient's accepted
/// procedures, and the appointment and its links are written together - a retry with the same key makes neither twice; another patient's or an unaccepted procedure refuses the whole booking and
/// books nothing; the bookable list and the appointment's linked procedures carry no plan title or diagnosis; the front desk may link and unlink (with a reason) but may not answer, rename, set a
/// target date or read the plan.
/// </summary>
public class TreatmentPlanBookingApiTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private SchedulingTestSupport _s = null!;
    private SchedulingApiHarness _api = null!;
    private Guid _ann, _bo, _annEncounter, _boEncounter;

    public Task InitializeAsync() => _fixture.InitializeOwnerAsync(async () =>
    {
        _s = new SchedulingTestSupport(_fixture);
        _api = new SchedulingApiHarness(_fixture);
        await _s.ArrangeAsync();
        _ann = await _s.PatientAsync();
        _bo = await _s.PatientAsync("Bo", "Kim");
        _annEncounter = await EncounterAsync(_ann);
        _boEncounter = await EncounterAsync(_bo);
    });
    public Task DisposeAsync() { _api.Dispose(); return _fixture.DisposeAsync(); }

    private async Task<Guid> EncounterAsync(Guid patient)
    {
        await using var db = _fixture.CreateContext();
        var e = new Encounter { Id = Guid.NewGuid(), PatientId = patient, EncounterAtUtc = DateTimeOffset.UtcNow, Status = EncounterStatuses.Draft, CreatedAtUtc = DateTimeOffset.UtcNow };
        db.Encounters.Add(e);
        await db.SaveChangesAsync();
        return e.Id;
    }

    private static string Id(JsonElement e) => e.GetProperty("id").GetString()!;

    /// <summary>A plan with one accepted whole-mouth procedure, made by a dentist; returns the plan and its item id.</summary>
    private async Task<(JsonElement Plan, string Item)> AcceptedPlanAsync(Session dentist, Session admin, Guid patient, string code, string title = "Implant for the upper left molar")
    {
        var proc = await admin.SendAsync(HttpMethod.Post, "/api/procedures", new { codeSystem = "Local", code, description = "Periodontal maintenance", category = "Periodontic", scope = "WholeMouth", fee = 50m });
        Assert.Equal(HttpStatusCode.OK, proc.Status);
        var dx = await dentist.SendAsync(HttpMethod.Post, $"/api/patients/{patient}/diagnoses",
            new { idempotencyKey = Guid.NewGuid().ToString("N"), encounterId = patient == _bo ? _boEncounter : _annEncounter, label = "Chronic periodontitis", toothKey = (string?)null, notes = (string?)null, treatmentPlanReference = (string?)null });
        var plan = await dentist.SendAsync(HttpMethod.Post, $"/api/patients/{patient}/treatment-plans",
            new { idempotencyKey = Guid.NewGuid().ToString("N"), title, items = new[] { new { diagnosisId = Id(dx.Body), procedureId = proc.Body.GetProperty("summary").GetProperty("id").GetString(), toothKey = (string?)null, surface = (string?)null } } });
        Assert.Equal(HttpStatusCode.OK, plan.Status);                                                                        // this API answers 200 for a new plan
        var item = plan.Body.GetProperty("items")[0].GetProperty("id").GetString()!;
        var accepted = await dentist.SendAsync(HttpMethod.Post, $"/api/treatment-plans/{Id(plan.Body)}/items/{item}/decision", new { decision = "Accepted", rowVersion = plan.Body.GetProperty("rowVersion").GetString() });
        Assert.Equal(HttpStatusCode.OK, accepted.Status);
        return (accepted.Body, item);
    }

    private object Booking(DateTime start, Guid patient, params string[] items) => new
    {
        patientId = patient, providerId = _s.ProviderA, operatoryId = _s.Op1, appointmentTypeId = _s.Long60, startLocal = start.ToString("yyyy-MM-ddTHH:mm"), durationMinutes = (int?)null,
        planItemIds = items,
    };

    [Fact]
    public async Task The_front_desk_books_with_treatment_in_one_save_and_a_retry_makes_neither_twice()
    {
        var (dentist, admin, desk) = (await _api.SessionAsync("Dentist"), await _api.SessionAsync("Admin"), await _api.SessionAsync("FrontDesk"));
        var (plan, item) = await AcceptedPlanAsync(dentist, admin, _ann, "LOCAL-BOOK1");

        var bookable = await desk.SendAsync(HttpMethod.Get, $"/api/patients/{_ann}/bookable-treatment");
        Assert.Equal(HttpStatusCode.OK, bookable.Status);
        var offered = Assert.Single(bookable.Body.GetProperty("items").EnumerateArray());
        Assert.Equal(("LOCAL-BOOK1", item), (offered.GetProperty("procedureCode").GetString(), offered.GetProperty("itemId").GetString()));
        Assert.DoesNotContain("Implant for the upper left molar", bookable.Body.GetRawText());                         // no plan title, no diagnosis
        Assert.DoesNotContain("periodontitis", bookable.Body.GetRawText());

        var booked = await desk.SendAsync(HttpMethod.Post, "/api/appointments", Booking(At(9), _ann, item), "book-treat-0001");
        Assert.Equal(HttpStatusCode.Created, booked.Status);
        var again = await desk.SendAsync(HttpMethod.Post, "/api/appointments", Booking(At(9), _ann, item), "book-treat-0001");
        Assert.Equal((HttpStatusCode.OK, Id(booked.Body)), (again.Status, Id(again.Body)));
        Assert.Equal(1, await _s.CountAsync(db => db.TreatmentPlanItemAppointmentLinks));
        Assert.Equal(1, await _s.CountAsync());

        var view = await dentist.SendAsync(HttpMethod.Get, $"/api/treatment-plans/{Id(plan)}");
        var appointment = view.Body.GetProperty("items")[0].GetProperty("appointment");
        Assert.Equal((Id(booked.Body), true), (appointment.GetProperty("appointmentId").GetString(), appointment.GetProperty("live").GetBoolean()));
        var onAppointment = await desk.SendAsync(HttpMethod.Get, $"/api/appointments/{Id(booked.Body)}/treatment-items");
        Assert.Equal(item, Assert.Single(onAppointment.Body.GetProperty("items").EnumerateArray()).GetProperty("itemId").GetString());
        Assert.Empty((await desk.SendAsync(HttpMethod.Get, $"/api/patients/{_ann}/bookable-treatment")).Body.GetProperty("items").EnumerateArray());   // booked: not offered again
    }

    [Fact]
    public async Task Another_patients_or_an_unaccepted_procedure_refuses_the_whole_booking_and_books_nothing()
    {
        var (dentist, admin, desk) = (await _api.SessionAsync("Dentist"), await _api.SessionAsync("Admin"), await _api.SessionAsync("FrontDesk"));
        var (_, annItem) = await AcceptedPlanAsync(dentist, admin, _ann, "LOCAL-BOOK2");
        var (_, boItem) = await AcceptedPlanAsync(dentist, admin, _bo, "LOCAL-BOOK3");

        var foreign = await desk.SendAsync(HttpMethod.Post, "/api/appointments", Booking(At(10), _ann, annItem, boItem), "book-treat-0002");
        Assert.Equal((HttpStatusCode.BadRequest, "plan_item_invalid"), (foreign.Status, foreign.Body.GetProperty("error").GetString()));
        var unknown = await desk.SendAsync(HttpMethod.Post, "/api/appointments", Booking(At(10), _ann, Guid.NewGuid().ToString()), "book-treat-0003");
        Assert.Equal(HttpStatusCode.BadRequest, unknown.Status);
        Assert.Equal((0, 0), (await _s.CountAsync(), await _s.CountAsync(db => db.TreatmentPlanItemAppointmentLinks)));
    }

    [Fact]
    public async Task The_front_desk_may_link_and_unlink_but_not_answer_rename_set_a_target_date_or_read_the_plan()
    {
        var (dentist, admin, desk) = (await _api.SessionAsync("Dentist"), await _api.SessionAsync("Admin"), await _api.SessionAsync("FrontDesk"));
        var (plan, item) = await AcceptedPlanAsync(dentist, admin, _ann, "LOCAL-BOOK4");
        var appointment = await desk.SendAsync(HttpMethod.Post, "/api/appointments", Booking(At(11), _ann), "book-treat-0004");
        Assert.Equal(HttpStatusCode.Created, appointment.Status);
        var p = $"/api/treatment-plans/{Id(plan)}";

        var linked = await desk.SendAsync(HttpMethod.Post, $"{p}/items/{item}/appointment-link", new { appointmentId = Id(appointment.Body) });
        Assert.Equal(HttpStatusCode.OK, linked.Status);
        Assert.False(linked.Body.TryGetProperty("title", out _));                                                        // the answer carries no plan title
        var unlinked = await desk.SendAsync(HttpMethod.Post, $"{p}/items/{item}/appointment-unlink", new { reason = "Patient will call back" });
        Assert.Equal(HttpStatusCode.OK, unlinked.Status);
        Assert.Single((await desk.SendAsync(HttpMethod.Get, $"/api/patients/{_ann}/bookable-treatment")).Body.GetProperty("items").EnumerateArray());

        var version = plan.GetProperty("rowVersion").GetString();
        Assert.Equal(HttpStatusCode.Forbidden, (await desk.SendAsync(HttpMethod.Post, $"{p}/items/{item}/decision", new { decision = "Declined", reason = "x", rowVersion = version })).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await desk.SendAsync(HttpMethod.Post, $"{p}/rename", new { title = "x", reason = "y", rowVersion = version })).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await desk.SendAsync(HttpMethod.Post, $"{p}/items/{item}/schedule", new { scheduled = true, plannedDate = "2030-05-20" })).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await desk.SendAsync(HttpMethod.Get, p)).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await desk.SendAsync(HttpMethod.Get, $"/api/patients/{_ann}/treatment-plans")).Status);
    }

    [Theory]
    [InlineData("Billing")]
    [InlineData("Assistant")]
    public async Task A_role_without_appointment_or_plan_authority_can_neither_read_bookable_treatment_nor_link(string role)
    {
        var (dentist, admin, other) = (await _api.SessionAsync("Dentist"), await _api.SessionAsync("Admin"), await _api.SessionAsync(role));
        var (plan, item) = await AcceptedPlanAsync(dentist, admin, _ann, $"LOCAL-{role[..4].ToUpperInvariant()}5");
        Assert.Equal(HttpStatusCode.Forbidden, (await other.SendAsync(HttpMethod.Get, $"/api/patients/{_ann}/bookable-treatment")).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.SendAsync(HttpMethod.Post, $"/api/treatment-plans/{Id(plan)}/items/{item}/appointment-link", new { appointmentId = Guid.NewGuid() })).Status);
    }
}
