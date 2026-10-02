using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Forms;
using Alveara.Api.Architecture.Scheduling;
using Xunit;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>ALV-011-C01 persistence, against real SQL Server: the database itself accepts the whole visit chain, refuses anything else, and keeps a visit's own provider and operatory apart from the booked ones.</summary>
public class VisitWorkflowSchemaTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private SchedulingTestSupport _s = null!;
    private Guid _ann;

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
        _s = new SchedulingTestSupport(_fixture);
        await _s.ArrangeAsync();
        _ann = await _s.PatientAsync();
    }
    public Task DisposeAsync() => _fixture.DisposeAsync();

    private async Task<Guid> BookAsync(int hour) => (await _s.ScheduleAsync(_s.Request(_ann, At(hour)))).Appointment.Id;

    [Fact]
    public async Task The_database_accepts_every_state_of_the_visit_chain_on_a_scheduled_appointment()
    {
        var id = await BookAsync(9);
        foreach (var state in VisitStates.InOrder)
        {
            await using var db = _fixture.CreateContext();
            var row = await db.Appointments.SingleAsync(a => a.Id == id);
            row.FlowState = state;
            await db.SaveChangesAsync();
        }
        Assert.Equal(VisitStates.Completed, (await _s.ReloadAsync(id)).FlowState);
    }

    [Theory]
    [InlineData("Arrived")]
    [InlineData("completed")]
    [InlineData("Cancelled")]
    [InlineData("NoShow")]
    [InlineData("")]
    public async Task The_database_refuses_any_state_outside_the_chain_including_cancelled_and_no_show(string state)
    {
        var id = await BookAsync(9);
        await using var db = _fixture.CreateContext();
        var row = await db.Appointments.SingleAsync(a => a.Id == id);
        row.FlowState = state;
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Equal(VisitStates.Scheduled, (await _s.ReloadAsync(id)).FlowState);
    }

    [Theory]
    [InlineData(VisitStates.Confirmed)]
    [InlineData(VisitStates.Ready)]
    [InlineData(VisitStates.Seated)]
    [InlineData(VisitStates.CheckedOut)]
    public async Task A_cancelled_appointment_cannot_hold_a_visit_state_beyond_Scheduled(string state)
    {
        var id = await BookAsync(9);
        var a = await _s.ReloadAsync(id);
        await _s.ManageAsync(m => m.CancelAsync(id, "Illness", a.RowVersion, _s.Actor, default));
        await using var db = _fixture.CreateContext();
        var row = await db.Appointments.SingleAsync(x => x.Id == id);
        row.FlowState = state;
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task A_new_appointment_has_no_visit_assignment_and_assigning_one_never_moves_the_booking()
    {
        var id = await BookAsync(9);
        await using (var db = _fixture.CreateContext())
        {
            var row = await db.Appointments.SingleAsync(a => a.Id == id);
            Assert.Null(row.VisitProviderProfileId);
            Assert.Null(row.VisitOperatoryId);
            row.VisitProviderProfileId = _s.ProviderB;
            row.VisitOperatoryId = _s.Op2;
            await db.SaveChangesAsync();
        }
        var after = await _s.ReloadAsync(id);
        Assert.Equal((_s.ProviderA, _s.Op1, "2030-01-14T09:00"), (after.ProviderId, after.OperatoryId, after.StartLocal)); // still booked as it was
        // and the freed booked operatory is still held by the booking, so the schedule's conflict rule is unchanged
        var clash = await Assert.ThrowsAsync<SchedulingException>(() => _s.ScheduleAsync(_s.Request(_ann, At(9, 30), _s.ProviderB, _s.Op1)));
        Assert.Equal("operatory_conflict", clash.Code);
    }

    [Fact]
    public async Task A_visit_assignment_must_name_a_real_provider_and_operatory()
    {
        var id = await BookAsync(9);
        await using var db = _fixture.CreateContext();
        var row = await db.Appointments.SingleAsync(a => a.Id == id);
        row.VisitOperatoryId = Guid.NewGuid();
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task A_form_template_is_not_required_at_check_in_until_the_practice_says_so()
    {
        await using var db = _fixture.CreateContext();
        db.FormTemplates.Add(new FormTemplate { Id = Guid.NewGuid(), Key = "privacy-notice", Category = FormCategories.Privacy, CreatedAtUtc = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        var saved = await db.FormTemplates.AsNoTracking().SingleAsync(t => t.Key == "privacy-notice");
        Assert.False(saved.RequiredAtCheckIn);
    }
}
