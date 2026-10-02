using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Forms;
using Alveara.Api.Architecture.Scheduling;
using Alveara.Api.Architecture.Time;
using Xunit;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>ALV-011-C01 against real SQL Server: the board shows exactly what is persisted for the day - states, assignments, next moves and the check-in form cue - and never invents anything.</summary>
public class VisitBoardTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private SchedulingTestSupport _s = null!;
    private FormTestSupport _forms = null!;
    private Guid _ann, _bo, _cy;

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
        _s = new SchedulingTestSupport(_fixture);
        _forms = new FormTestSupport(_fixture);
        await _s.ArrangeAsync();
        _ann = await _s.PatientAsync();
        _bo = await _s.PatientAsync("Bo", "Kim");
        _cy = await _s.PatientAsync("Cy", "Poe");
    }
    public Task DisposeAsync() => _fixture.DisposeAsync();

    private async Task<AppointmentView> BookAsync(Guid patient, DateTime start, Guid? provider = null, Guid? operatory = null) =>
        (await _s.ScheduleAsync(_s.Request(patient, start, provider, operatory))).Appointment;

    private Task<AppointmentView> GoAsync(AppointmentView a, string target) => _s.FlowAsync(f => f.TransitionAsync(a.Id, target, a.RowVersion, _s.Actor, default));

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

    private async Task<VisitBoard> BoardAsync(DateTime day, bool readiness = true, IPracticeClock? clock = null)
    {
        await using var db = _fixture.CreateContext();
        return await new VisitBoardService(db, clock ?? Clock, new CheckInReadinessService(db)).BoardAsync(DateOnly.FromDateTime(day), readiness, default);
    }

    private static readonly DateTime Monday = new(2030, 1, 14);

    [Fact]
    public async Task The_board_lists_only_that_days_appointments_in_time_order_with_the_state_chain_and_the_servers_clock()
    {
        var late = await BookAsync(_ann, At(14));
        var early = await BookAsync(_bo, At(9));
        await BookAsync(_cy, At(9, 0, dayOffset: 1)); // tomorrow
        var now = new DateTimeOffset(2030, 1, 14, 15, 0, 0, TimeSpan.Zero);

        var board = await BoardAsync(Monday, clock: new TestClock(now));

        Assert.Equal(("2030-01-14", now), (board.Date, board.ServerNowUtc));
        Assert.Equal(VisitStates.InOrder, board.States);
        Assert.Equal([early.Id, late.Id], board.Visits.Select(v => v.Appointment.Id));
        Assert.All(board.Visits, v => Assert.False(v.CarriedOver));
    }

    [Fact]
    public async Task Each_card_shows_the_persisted_state_the_next_moves_and_where_the_patient_actually_is()
    {
        var a = await ToAsync(await BookAsync(_ann, At(9)), VisitStates.Ready);
        await _s.AssignAsync(v => v.AssignAsync(a.Id, new AssignVisitRequest(_s.ProviderB, _s.Op2, a.RowVersion), _s.Actor, default));

        var card = (await BoardAsync(Monday)).Visits.Single();

        Assert.Equal((VisitStates.Ready, "Dr. Patel", "Op 2", "Dr. Rivera", "Op 1"),
            (card.Appointment.FlowState, card.Appointment.VisitProviderName, card.Appointment.VisitOperatoryName, card.Appointment.ProviderName, card.Appointment.OperatoryName));
        Assert.Equal([VisitStates.Seated], card.Appointment.NextFlowStates);
        Assert.NotNull(card.Appointment.FlowChangedAtUtc);
    }

    [Fact]
    public async Task The_board_follows_what_is_stored_a_change_made_elsewhere_shows_on_the_next_read()
    {
        var a = await BookAsync(_ann, At(9));
        Assert.Equal(VisitStates.Scheduled, (await BoardAsync(Monday)).Visits.Single().Appointment.FlowState);

        await GoAsync(a, VisitStates.CheckedIn);

        Assert.Equal(VisitStates.CheckedIn, (await BoardAsync(Monday)).Visits.Single().Appointment.FlowState);
    }

    [Fact]
    public async Task Cancelled_and_no_show_appointments_are_on_the_board_distinct_from_completed_and_offer_no_moves()
    {
        var done = await ToAsync(await BookAsync(_ann, At(9)), VisitStates.Completed);
        var b = await BookAsync(_bo, At(11));
        await _s.ManageAsync(m => m.CancelAsync(b.Id, "Illness", b.RowVersion, _s.Actor, default));
        var c = await BookAsync(_cy, At(13));
        await _s.ManageAsync(m => m.MarkNoShowAsync(c.Id, c.RowVersion, _s.Actor, default), new TestClock(c.StartUtc.AddHours(1)));

        var cards = (await BoardAsync(Monday)).Visits.ToDictionary(v => v.Appointment.Id);

        Assert.Equal((AppointmentStatuses.Scheduled, VisitStates.Completed), (cards[done.Id].Appointment.Status, cards[done.Id].Appointment.FlowState));
        Assert.Equal(AppointmentStatuses.Cancelled, cards[b.Id].Appointment.Status);
        Assert.Equal(AppointmentStatuses.NoShow, cards[c.Id].Appointment.Status);
        Assert.Empty(cards[b.Id].Appointment.NextFlowStates!);
        Assert.Empty(cards[c.Id].Appointment.NextFlowStates!);
        Assert.Empty(cards[done.Id].Appointment.NextFlowStates!);
    }

    // ---------- the check-in form cue ----------

    private async Task<TemplateDetail> RequiredTemplateAsync()
    {
        var t = await _forms.CreateTemplateAsync();
        await using var db = _fixture.CreateContext();
        return await _forms.Templates(db).SetRequiredAtCheckInAsync(t.Template.Id, true, t.Template.RowVersion, _forms.Actor, default);
    }

    [Fact]
    public async Task A_waiting_patients_card_carries_their_required_form_readiness_and_it_is_accurate()
    {
        var t = await RequiredTemplateAsync();
        var signed = await _forms.ReadyAsync(_ann, t.Template.Id);
        await _forms.SignAsync(signed);
        await _forms.StartAsync(_bo, t.Template.Id); // a draft only
        foreach (var p in new[] { _ann, _bo, _cy }) await BookAsync(p, At(9 + (p == _ann ? 0 : p == _bo ? 2 : 4)));

        var cards = (await BoardAsync(Monday)).Visits.ToDictionary(v => v.Appointment.PatientName);

        Assert.Equal((true, 1, 1, ReadinessStatuses.Complete), (cards["Ann Lee"].Readiness!.Ready, cards["Ann Lee"].Readiness!.RequiredCount, cards["Ann Lee"].Readiness!.CompleteCount, cards["Ann Lee"].Readiness!.Items[0].Status));
        Assert.Equal((false, ReadinessStatuses.InProgress), (cards["Bo Kim"].Readiness!.Ready, cards["Bo Kim"].Readiness!.Items[0].Status));
        Assert.Equal((false, ReadinessStatuses.Missing), (cards["Cy Poe"].Readiness!.Ready, cards["Cy Poe"].Readiness!.Items[0].Status));
    }

    [Fact]
    public async Task The_cue_is_only_for_patients_not_yet_seen_and_only_when_the_caller_may_see_form_status()
    {
        await RequiredTemplateAsync();
        var waiting = await ToAsync(await BookAsync(_ann, At(9)), VisitStates.CheckedIn);
        var inChair = await ToAsync(await BookAsync(_bo, At(11), _s.ProviderB, _s.Op2), VisitStates.Seated);
        var cancelled = await BookAsync(_cy, At(13));
        await _s.ManageAsync(m => m.CancelAsync(cancelled.Id, "Illness", cancelled.RowVersion, _s.Actor, default));

        var cards = (await BoardAsync(Monday)).Visits.ToDictionary(v => v.Appointment.Id);
        Assert.NotNull(cards[waiting.Id].Readiness);
        Assert.Null(cards[inChair.Id].Readiness);
        Assert.Null(cards[cancelled.Id].Readiness);

        Assert.All((await BoardAsync(Monday, readiness: false)).Visits, v => Assert.Null(v.Readiness)); // a caller who may not see form status gets none
    }

    [Fact]
    public async Task When_the_practice_requires_no_forms_the_cue_says_nothing_is_required_instead_of_claiming_completion()
    {
        await _forms.CreateTemplateAsync(); // exists, not required
        await BookAsync(_ann, At(9));

        var cue = (await BoardAsync(Monday)).Visits.Single().Readiness!;

        Assert.Equal((true, 0, 0), (cue.Ready, cue.RequiredCount, cue.CompleteCount));
        Assert.Empty(cue.Items);
    }

    // ---------- a visit left open ----------

    [Fact]
    public async Task A_visit_left_open_from_an_earlier_day_appears_on_todays_board_marked_carried_over_because_it_still_holds_its_room()
    {
        var friday = await ToAsync(await BookAsync(_ann, At(9, 0, dayOffset: -3)), VisitStates.Seated); // Friday 2030-01-11, left seated
        var finished = await ToAsync(await BookAsync(_bo, At(11, 0, dayOffset: -3), _s.ProviderB, _s.Op2), VisitStates.Completed);
        var today = new TestClock(new DateTimeOffset(2030, 1, 14, 14, 0, 0, TimeSpan.Zero)); // "now" is Monday the 14th, practice time

        var board = await BoardAsync(Monday, clock: today);

        var carried = Assert.Single(board.Visits, v => v.CarriedOver);
        Assert.Equal((friday.Id, VisitStates.Seated), (carried.Appointment.Id, carried.Appointment.FlowState));
        Assert.DoesNotContain(board.Visits, v => v.Appointment.Id == finished.Id); // completed visits from earlier days stay off
        // and the room really is blocked, so showing it is not decoration
        var other = await ToAsync(await BookAsync(_cy, At(9)), VisitStates.Ready);
        Assert.Equal("operatory_occupied", (await Assert.ThrowsAsync<SchedulingException>(() => GoAsync(other, VisitStates.Seated))).Code);
    }

    [Fact]
    public async Task Carried_over_visits_are_not_added_to_another_days_board()
    {
        await ToAsync(await BookAsync(_ann, At(9, 0, dayOffset: -3)), VisitStates.Seated);
        var today = new TestClock(new DateTimeOffset(2030, 1, 14, 14, 0, 0, TimeSpan.Zero));

        Assert.Empty((await BoardAsync(Monday.AddDays(1), clock: today)).Visits); // Tuesday's board
        Assert.Single((await BoardAsync(Monday.AddDays(-3), clock: today)).Visits); // Friday's own board still shows it on its own day, not as carried over
        Assert.All((await BoardAsync(Monday.AddDays(-3), clock: today)).Visits, v => Assert.False(v.CarriedOver));
    }

    [Fact]
    public async Task An_empty_day_gives_an_empty_board_and_the_board_never_writes_anything()
    {
        await BookAsync(_ann, At(9));
        var before = await _s.CountAsync(db => db.AuditLogEntries) + await _s.CountAsync(db => db.AppointmentEvents) + await _s.CountAsync(db => db.Appointments);

        Assert.Empty((await BoardAsync(Monday.AddDays(5))).Visits);
        await BoardAsync(Monday);

        Assert.Equal(before, await _s.CountAsync(db => db.AuditLogEntries) + await _s.CountAsync(db => db.AppointmentEvents) + await _s.CountAsync(db => db.Appointments));
    }
}
