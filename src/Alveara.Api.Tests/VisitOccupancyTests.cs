using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Scheduling;
using Xunit;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>ALV-011-C01 against real SQL Server: one operatory holds one patient at a time, including when two receptionists seat two patients into it at once.</summary>
public class VisitOccupancyTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private SchedulingTestSupport _s = null!;
    private Guid _ann, _bo;

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
        _s = new SchedulingTestSupport(_fixture);
        await _s.ArrangeAsync();
        _ann = await _s.PatientAsync();
        _bo = await _s.PatientAsync("Bo", "Kim");
    }
    public Task DisposeAsync() => _fixture.DisposeAsync();

    // two appointments in the SAME operatory at different hours (booking allows that), so only the visit-occupancy rule can stop them sharing the room
    private async Task<AppointmentView> BookAsync(Guid patient, int hour, Guid? provider = null, Guid? operatory = null) =>
        (await _s.ScheduleAsync(_s.Request(patient, At(hour), provider, operatory))).Appointment;

    private Task<AppointmentView> GoAsync(AppointmentView a, string target, string? version = null) =>
        _s.FlowAsync(f => f.TransitionAsync(a.Id, target, version ?? a.RowVersion, _s.Actor, default));

    private async Task<AppointmentView> ToAsync(AppointmentView a, string target)
    {
        if (a.FlowState == target) return a;
        foreach (var step in VisitStates.InOrder.SkipWhile(s => s != a.FlowState).Skip(1))
        {
            a = await GoAsync(a, step);
            if (step == target) break;
        }
        return a;
    }

    private static async Task<SchedulingException> RefusedAsync(Func<Task<AppointmentView>> action) => await Assert.ThrowsAsync<SchedulingException>(action);

    [Fact]
    public async Task A_second_patient_cannot_be_seated_in_an_operatory_that_holds_a_seated_patient_and_the_refusal_names_who_holds_it()
    {
        var first = await ToAsync(await BookAsync(_ann, 9), VisitStates.Seated);
        var second = await ToAsync(await BookAsync(_bo, 11), VisitStates.Ready);

        var ex = await RefusedAsync(() => GoAsync(second, VisitStates.Seated));

        Assert.Equal(("operatory_occupied", 409, first.Id), (ex.Code, ex.StatusCode, ex.ConflictingAppointmentId!.Value));
        Assert.Equal(VisitStates.Ready, (await _s.ReloadAsync(second.Id)).FlowState); // nothing changed
        Assert.Equal(0, await _s.CountAsync(db => db.AppointmentEvents.Where(e => e.AppointmentId == second.Id && e.EventType == AppointmentEventTypes.Seated))); // and nothing was logged
    }

    [Fact]
    public async Task The_room_is_free_again_once_the_patient_moves_on_and_a_different_operatory_is_never_blocked()
    {
        var first = await ToAsync(await BookAsync(_ann, 9), VisitStates.Seated);
        var inOtherRoom = await ToAsync(await BookAsync(_bo, 11, _s.ProviderB, _s.Op2), VisitStates.Seated);
        Assert.Equal(VisitStates.Seated, inOtherRoom.FlowState); // Op 2 was never occupied

        var second = await ToAsync(await BookAsync(_bo, 13), VisitStates.Ready);
        Assert.Equal("operatory_occupied", (await RefusedAsync(() => GoAsync(second, VisitStates.Seated))).Code);

        await ToAsync(first, VisitStates.CheckedOut); // the first patient has left the chair
        Assert.Equal(VisitStates.Seated, (await GoAsync(second, VisitStates.Seated)).FlowState);
    }

    [Fact]
    public async Task Moving_from_seated_to_in_treatment_does_not_count_against_the_room_the_patient_already_holds()
    {
        var a = await ToAsync(await BookAsync(_ann, 9), VisitStates.Seated);
        Assert.Equal(VisitStates.InTreatment, (await GoAsync(a, VisitStates.InTreatment)).FlowState);
    }

    [Fact]
    public async Task STORY_011s_check_in_straight_to_treatment_also_takes_the_room_and_is_refused_if_it_is_occupied()
    {
        await ToAsync(await BookAsync(_ann, 9), VisitStates.InTreatment);
        var second = await ToAsync(await BookAsync(_bo, 11), VisitStates.CheckedIn);

        var ex = await RefusedAsync(() => _s.FlowAsync(f => f.StartTreatmentAsync(second.Id, second.RowVersion, _s.Actor, default)));

        Assert.Equal("operatory_occupied", ex.Code);
        Assert.Equal(VisitStates.CheckedIn, (await _s.ReloadAsync(second.Id)).FlowState);
    }

    [Fact]
    public async Task Six_receptionists_seating_six_different_patients_into_one_room_at_once_seat_exactly_one()
    {
        var patients = new List<Guid>();
        for (var i = 0; i < 6; i++) patients.Add(await _s.PatientAsync($"P{i}", $"Racer{i}"));
        var ready = new List<AppointmentView>();
        for (var i = 0; i < 6; i++) ready.Add(await ToAsync(await BookAsync(patients[i], 8 + i), VisitStates.Ready)); // same provider, same operatory, six different hours

        // every contender pauses between its check and its write, so only the operatory lock can stop two of them both passing the check
        var results = await Task.WhenAll(ready.Select(async a =>
        {
            await using var db = _s.SlowSaveContext(TimeSpan.FromMilliseconds(200));
            try { return (await _s.Flow(db).TransitionAsync(a.Id, VisitStates.Seated, a.RowVersion, _s.Actor, default)).FlowState; }
            catch (SchedulingException ex) { return ex.Code; }
        }));

        Assert.Equal(1, results.Count(r => r == VisitStates.Seated));
        Assert.Equal(5, results.Count(r => r == "operatory_occupied"));
        Assert.Equal(1, await _s.CountAsync(db => db.Appointments.Where(a => a.FlowState == VisitStates.Seated)));
    }

    [Fact]
    public async Task A_patient_seated_in_an_assigned_operatory_holds_that_one_not_the_booked_one()
    {
        var a = await ToAsync(await BookAsync(_ann, 9), VisitStates.Ready);
        await _s.AssignAsync(v => v.AssignAsync(a.Id, new AssignVisitRequest(a.ProviderId, _s.Op2, a.RowVersion), _s.Actor, default));
        var current = await _s.ReloadAsync(a.Id);
        var seated = await GoAsync(current, VisitStates.Seated);
        Assert.Equal(("Op 2", "Op 1"), (seated.VisitOperatoryName, seated.OperatoryName));

        // Op 1 (booked) is free: someone else can be seated there; Op 2 (assigned) is held
        var inBooked = await ToAsync(await BookAsync(_bo, 11), VisitStates.Seated);
        Assert.Equal(VisitStates.Seated, inBooked.FlowState);
        var third = await ToAsync(await BookAsync(await _s.PatientAsync("Di", "Lo"), 13, _s.ProviderB, _s.Op2), VisitStates.Ready);
        var ex = await RefusedAsync(() => GoAsync(third, VisitStates.Seated));
        Assert.Equal(("operatory_occupied", seated.Id), (ex.Code, ex.ConflictingAppointmentId!.Value));
    }
}
