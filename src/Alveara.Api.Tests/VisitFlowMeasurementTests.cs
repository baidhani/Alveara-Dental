using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Measurement;
using Alveara.Api.Architecture.Scheduling;
using Xunit;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N021 A2 against real SQL Server, through the REAL validating measurement sink: every successful visit move (and undo check-in) records its "appointment.flow" event.
/// Before this fix the PascalCase state ("CheckedIn") was sent as the category, which the validator (lower-case snake_case only) rejected, so these events were dropped.
/// </summary>
public class VisitFlowMeasurementTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private SchedulingTestSupport _s = null!;

    public Task InitializeAsync() => _fixture.InitializeOwnerAsync(async () =>
    {
        _s = new SchedulingTestSupport(_fixture);
        await _s.ArrangeAsync();
    });
    public Task DisposeAsync() => _fixture.DisposeAsync();

    private async Task<AppointmentView> MeasuredAsync(Func<AppointmentFlowService, Task<AppointmentView>> action)
    {
        await using var db = _fixture.CreateContext();
        return await action(_s.Flow(db, new MeasurementEventSink(db), VisitDayClock));
    }

    [Fact]
    public async Task Every_successful_move_and_an_undo_record_a_flow_measurement_with_a_valid_category()
    {
        var a = (await _s.ScheduleAsync(_s.Request(await _s.PatientAsync(), At(9)))).Appointment;
        foreach (var step in VisitStates.InOrder.Skip(1))
        {
            if (step == VisitStates.Completed) await _s.ResolveCheckoutAsync(a.Id);
            var current = a;
            a = await MeasuredAsync(f => f.TransitionAsync(current.Id, step, current.RowVersion, _s.MoverFor(step, current), default));
        }
        var b = (await _s.ScheduleAsync(_s.Request(await _s.PatientAsync("Bo", "Kim"), At(11)))).Appointment;
        b = await _s.WalkAsync(b, VisitStates.CheckedIn);
        await MeasuredAsync(f => f.UndoCheckInAsync(b.Id, "Wrong patient", b.RowVersion, _s.Desk, default));

        await using var read = _fixture.CreateContext();
        var events = await read.MeasurementEvents.Where(e => e.EventName == "appointment.flow").OrderBy(e => e.OccurredAtUtc).ThenBy(e => e.Id).ToListAsync();
        Assert.Equal(
            ["confirmed", "checked_in", "ready", "seated", "in_treatment", "checked_out", "completed", "undo_check_in"],
            events.Select(e => System.Text.Json.JsonDocument.Parse(e.PropertiesJson).RootElement.GetProperty("category").GetString()));
        Assert.All(events, e => Assert.Contains("\"outcome\":\"success\"", e.PropertiesJson));
    }

    [Theory]
    [InlineData("CheckedIn", "checked_in")]
    [InlineData("InTreatment", "in_treatment")]
    [InlineData("Completed", "completed")]
    [InlineData("UndoCheckIn", "undo_check_in")]
    public void A_state_becomes_a_lower_case_snake_case_category(string state, string expected) =>
        Assert.Equal(expected, AppointmentFlowService.MeasureCategory(state));
}
