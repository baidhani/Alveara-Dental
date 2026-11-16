using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Configuration;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Scheduling;
using Xunit;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N021 A2 (briefing 5.5) against real SQL Server: GET appointments/{id}/actions, the list the Calendar and Board render.
/// The expected lists are written out from the briefing's 5.4 table (an independent source, not the code's own rules); the walk test then proves an enabled action is accepted
/// by the server, and the blocked cases carry the same error code the server returns.
/// </summary>
public class VisitActionsTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private SchedulingTestSupport _s = null!;
    private Guid _ann;

    public Task InitializeAsync() => _fixture.InitializeOwnerAsync(async () =>
    {
        _s = new SchedulingTestSupport(_fixture);
        await _s.ArrangeAsync();
        _ann = await _s.PatientAsync();
    });
    public Task DisposeAsync() => _fixture.DisposeAsync();

    private async Task<AppointmentView> BookAsync(int hour = 9, int dayOffset = 0) => (await _s.ScheduleAsync(_s.Request(_ann, At(hour, dayOffset: dayOffset)))).Appointment;

    private async Task<AppointmentActions> ActionsAsync(Guid id, VisitActor actor, Architecture.Time.IPracticeClock? clock = null)
    {
        await using var db = _fixture.CreateContext();
        return await new VisitActionsService(db, clock ?? VisitDayClock, PolicyTestSupport.Defaults()).ForAsync(id, actor, default);
    }

    /// <summary>The list as a sorted, space-separated string; a disabled action ends in "-".</summary>
    private async Task<string> CodesAsync(Guid id, VisitActor actor) =>
        string.Join(' ', (await ActionsAsync(id, actor)).Actions.Select(x => x.Code + (x.Enabled ? "" : "-")).Order(StringComparer.Ordinal));

    private static string Sorted(string codes) => string.Join(' ', codes.Split(' ', StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal));

    private VisitActor As(Role role) => new(_s.Actor, role);   // an account NOT linked to any provider record

    // ---------- the 5.4 table, per state and role (default settings: confirmation and mark-ready required; the 09:00 visit is later today, so no-show is not yet possible) ----------

    public static TheoryData<string, string, string, string, string, string, string, string, string> Table => new()
    {
        // state,                 front desk,                                    booked dentist,          other dentist,  hygienist (unlinked),   assistant,          office manager,                                                         admin (unlinked),                                                              billing
        { VisitStates.Scheduled,  "confirm reschedule cancel no_show-",          "change_room",            "change_room",  "change_room",          "change_room",      "confirm reschedule cancel no_show- reassign_provider change_room",     "confirm reschedule cancel no_show- reassign_provider change_room",           "" },
        { VisitStates.Confirmed,  "check_in reschedule cancel no_show-",         "change_room",            "change_room",  "change_room",          "change_room",      "check_in reschedule cancel no_show- reassign_provider change_room",    "check_in reschedule cancel no_show- reassign_provider change_room",          "" },
        { VisitStates.CheckedIn,  "undo_check_in",                               "mark_ready change_room", "mark_ready change_room", "mark_ready change_room", "mark_ready change_room", "undo_check_in reassign_provider change_room",          "mark_ready undo_check_in reassign_provider change_room",                     "" },
        { VisitStates.Ready,      "undo_check_in",                               "seat change_room",       "seat change_room", "seat change_room",  "seat change_room", "undo_check_in reassign_provider change_room",                          "seat undo_check_in reassign_provider change_room",                           "" },
        { VisitStates.Seated,     "",                                            "start_treatment change_room", "change_room", "change_room",       "change_room",      "reassign_provider change_room",                                        "reassign_provider change_room",                                              "" },
        { VisitStates.InTreatment, "",                                           "end_treatment",          "",             "",                     "",                 "",                                                                     "",                                                                           "" },
        { VisitStates.CheckedOut, "complete-",                                   "complete-",              "",             "",                     "",                 "complete-",                                                            "complete-",                                                                  "" },
        { VisitStates.Completed,  "",                                            "",                       "",             "",                     "",                 "",                                                                     "",                                                                           "" },
    };

    [Theory]
    [MemberData(nameof(Table))]
    public async Task Each_role_is_offered_exactly_the_actions_of_the_briefing_table_in_each_state(string state, string desk, string booked, string other, string hygienist, string assistant, string manager, string admin, string billing)
    {
        var a = await _s.WalkAsync(await BookAsync(), state);   // the booked dentist (Dr. Rivera's account) started treatment, so "complete" is theirs too

        Assert.Equal(Sorted(desk), await CodesAsync(a.Id, _s.Desk));
        Assert.Equal(Sorted(booked), await CodesAsync(a.Id, _s.ProviderOf(_s.ProviderA)));
        Assert.Equal(Sorted(other), await CodesAsync(a.Id, _s.ProviderOf(_s.ProviderB)));
        Assert.Equal(Sorted(hygienist), await CodesAsync(a.Id, As(Role.Hygienist)));
        Assert.Equal(Sorted(assistant), await CodesAsync(a.Id, As(Role.Assistant)));
        Assert.Equal(Sorted(manager), await CodesAsync(a.Id, _s.OfficeManager));
        Assert.Equal(Sorted(admin), await CodesAsync(a.Id, As(Role.Admin)));
        Assert.Equal(Sorted(billing), await CodesAsync(a.Id, As(Role.Billing)));
    }

    // ---------- an enabled action is one the server accepts; a blocked one carries the server's own code ----------

    [Fact]
    public async Task Walking_the_visit_by_taking_only_enabled_listed_moves_reaches_completed_and_every_move_is_accepted()
    {
        var a = await BookAsync();
        VisitActor[] people = [_s.Desk, As(Role.Assistant), _s.ProviderOf(_s.ProviderA)];
        while (a.FlowState != VisitStates.Completed)
        {
            if (a.FlowState == VisitStates.CheckedOut) await _s.ResolveCheckoutAsync(a.Id);
            (VisitActor Who, VisitAction Move)? next = null;
            foreach (var who in people)
            {
                var move = (await ActionsAsync(a.Id, who)).Actions.FirstOrDefault(x => x.TargetState is not null && x.Enabled);
                if (move is not null) { next = (who, move); break; }
            }
            Assert.NotNull(next);   // someone is always offered the next step
            var before = a.FlowState;
            a = await _s.FlowAsync(f => f.TransitionAsync(a.Id, next.Value.Move.TargetState!, a.RowVersion, next.Value.Who, default));
            Assert.NotEqual(before, a.FlowState);
        }
        Assert.Empty((await ActionsAsync(a.Id, _s.Desk)).Actions);
    }

    [Fact]
    public async Task A_move_not_offered_to_a_role_is_refused_by_the_server()
    {
        var a = await _s.WalkAsync(await BookAsync(), VisitStates.Ready);
        Assert.DoesNotContain(await ActionsAsync(a.Id, _s.Desk) is var d ? d.Actions : [], x => x.Code == VisitActionCodes.Seat);
        var ex = await Assert.ThrowsAsync<SchedulingException>(() => _s.FlowAsync(f => f.TransitionAsync(a.Id, VisitStates.Seated, a.RowVersion, _s.Desk, default)));
        Assert.Equal((403, "chairside_only"), (ex.StatusCode, ex.Reason));
    }

    [Fact]
    public async Task Check_in_on_another_day_is_listed_disabled_with_the_day_rule_code_the_server_returns()
    {
        var tomorrow = await _s.MoveAsync(await BookAsync(dayOffset: 1), VisitStates.Confirmed);

        var checkIn = (await ActionsAsync(tomorrow.Id, _s.Desk)).Actions.Single(x => x.Code == VisitActionCodes.CheckIn);
        Assert.Equal((false, "check_in_wrong_day"), (checkIn.Enabled, checkIn.BlockedBy));
        var ex = await Assert.ThrowsAsync<SchedulingException>(() => _s.FlowAsync(f => f.TransitionAsync(tomorrow.Id, VisitStates.CheckedIn, tomorrow.RowVersion, _s.Desk, default)));
        Assert.Equal(checkIn.BlockedBy, ex.Code);
    }

    [Fact]
    public async Task A_flagged_appointment_offers_keep_shows_check_in_blocked_and_says_who_must_act()
    {
        var a = await _s.MoveAsync(await BookAsync(), VisitStates.Confirmed);
        await using (var db = _fixture.CreateContext())
            await new BlockedTimeGuard(db, VisitDayClock).AddAsync(_s.ProviderA, At(8), At(12), "Course", true, _s.Actor, default, BlockedTimeLabels.Training);

        var list = await ActionsAsync(a.Id, _s.Desk);
        Assert.True(list.BlockFlagged);
        Assert.Equal((false, "block_flag_unresolved"), list.Actions.Single(x => x.Code == VisitActionCodes.CheckIn) is var c ? (c.Enabled, c.BlockedBy) : default);
        Assert.True(list.Actions.Single(x => x.Code == VisitActionCodes.KeepDespiteBlock) is { Enabled: true, NeedsReason: true });
        Assert.StartsWith("Blocked time covers this appointment", list.NextActorText);
        Assert.DoesNotContain((await ActionsAsync(a.Id, As(Role.Assistant))).Actions, x => x.Code == VisitActionCodes.KeepDespiteBlock);

        var ex = await Assert.ThrowsAsync<SchedulingException>(() => _s.FlowAsync(f => f.TransitionAsync(a.Id, VisitStates.CheckedIn, a.RowVersion, _s.Desk, default)));
        Assert.Equal("block_flag_unresolved", ex.Code);
    }

    [Fact]
    public async Task Complete_is_blocked_by_the_checkout_gate_with_its_sentence_then_enabled_and_accepted_once_resolved()
    {
        var a = await _s.WalkAsync(await BookAsync(), VisitStates.CheckedOut);

        var open = await ActionsAsync(a.Id, _s.Desk);
        var complete = open.Actions.Single(x => x.Code == VisitActionCodes.Complete);
        Assert.Equal((false, "checkout_incomplete"), (complete.Enabled, complete.BlockedBy));
        Assert.StartsWith("Ready for checkout", complete.BlockedText);
        Assert.Equal(complete.BlockedText, open.NextActorText);
        Assert.Equal("checkout_incomplete", (await Assert.ThrowsAsync<SchedulingException>(() => _s.FlowAsync(f => f.TransitionAsync(a.Id, VisitStates.Completed, a.RowVersion, _s.Desk, default)))).Code);

        await _s.ResolveCheckoutAsync(a.Id);
        Assert.True((await ActionsAsync(a.Id, _s.Desk)).Actions.Single(x => x.Code == VisitActionCodes.Complete).Enabled);
        Assert.Equal(VisitStates.Completed, (await _s.FlowAsync(f => f.TransitionAsync(a.Id, VisitStates.Completed, a.RowVersion, _s.Desk, default))).FlowState);
    }

    [Fact]
    public async Task No_show_becomes_enabled_once_the_start_time_has_passed()
    {
        var a = await BookAsync(9);
        Assert.Equal("no_show_too_early", (await ActionsAsync(a.Id, _s.Desk)).Actions.Single(x => x.Code == VisitActionCodes.NoShow).BlockedBy);   // 07:30
        var afterStart = new TestClock(a.StartUtc.AddMinutes(30));                                                                                // 09:30
        Assert.True((await ActionsAsync(a.Id, _s.Desk, afterStart)).Actions.Single(x => x.Code == VisitActionCodes.NoShow).Enabled);
        Assert.Equal(AppointmentStatuses.NoShow, (await _s.ManageAsync(m => m.MarkNoShowAsync(a.Id, a.RowVersion, _s.Actor, default), afterStart)).Status);   // and the server agrees
    }

    // ---------- settings, next-actor text, terminal states ----------

    [Fact]
    public async Task With_both_settings_off_check_in_is_offered_from_scheduled_and_seat_from_checked_in()
    {
        await using (var db = _fixture.CreateContext())
        {
            await db.Database.ExecuteSqlRawAsync("DELETE FROM [PracticeSettings]");
            await db.Database.ExecuteSqlRawAsync("INSERT INTO [PracticeSettings] ([Id], [Name], [UpdatedAtUtc], [ConfirmationRequired], [MarkReadyRequired]) VALUES ({0}, N'Practice', SYSUTCDATETIME(), 0, 0)",
                Architecture.Configuration.PracticeSettings.SingletonId);
        }
        var a = await BookAsync();
        var desk = await ActionsAsync(a.Id, _s.Desk);
        Assert.Contains(desk.Actions, x => x.Code == VisitActionCodes.CheckIn && x.Enabled);
        Assert.Equal("Waiting for the front desk to check the patient in.", desk.NextActorText);

        a = await _s.FlowAsync(f => f.TransitionAsync(a.Id, VisitStates.CheckedIn, a.RowVersion, _s.Desk, default));
        var chair = await ActionsAsync(a.Id, As(Role.Assistant));
        Assert.Equal(Sorted("mark_ready seat change_room"), await CodesAsync(a.Id, As(Role.Assistant)));
        Assert.Equal("Waiting for chairside staff to seat the patient.", chair.NextActorText);
    }

    [Theory]
    [InlineData(VisitStates.Scheduled, "Waiting for the front desk to confirm.")]
    [InlineData(VisitStates.Confirmed, "Waiting for the front desk to check the patient in.")]
    [InlineData(VisitStates.CheckedIn, "Waiting for chairside staff to mark the patient ready.")]
    [InlineData(VisitStates.Ready, "Waiting for chairside staff to seat the patient.")]
    [InlineData(VisitStates.Seated, "Waiting for the booked provider to start treatment.")]
    [InlineData(VisitStates.InTreatment, "In treatment; the booked provider ends treatment.")]
    public async Task The_next_actor_text_names_who_the_visit_waits_for_and_is_the_same_for_every_viewer(string state, string text)
    {
        var a = await _s.WalkAsync(await BookAsync(), state);
        Assert.Equal(text, (await ActionsAsync(a.Id, _s.Desk)).NextActorText);
        Assert.Equal(text, (await ActionsAsync(a.Id, As(Role.Billing))).NextActorText);
        Assert.DoesNotContain("Ann", text);
    }

    /// <summary>A cancelled (patient's reason) or a no-show (marked once the 09:00 start has passed) appointment.</summary>
    private async Task<AppointmentView> EndedAsync(string status)
    {
        var a = await BookAsync(9);
        return status == AppointmentStatuses.Cancelled
            ? await _s.ManageAsync(m => m.CancelAsync(a.Id, AppointmentCancelCategories.Patient, null, a.RowVersion, _s.Actor, default), VisitDayClock)
            : await _s.ManageAsync(m => m.MarkNoShowAsync(a.Id, a.RowVersion, _s.Actor, default), new TestClock(a.StartUtc.AddMinutes(30)));
    }

    // The 5.4 row "Rebook / Open patient | Cancelled, NoShow": FD Y, OM Y, AD Y, everyone else not offered (ALV-N021 A2, F-045, F-051, F-073).
    [Theory]
    [InlineData("Cancelled")]
    [InlineData("NoShow")]
    public async Task A_cancelled_or_no_show_appointment_offers_rebook_and_open_patient_to_the_booking_roles_only(string status)
    {
        var a = await EndedAsync(status);
        Assert.Equal(status, a.Status);
        Assert.Equal("open_patient rebook", await CodesAsync(a.Id, _s.Desk));
        Assert.Equal("open_patient rebook", await CodesAsync(a.Id, _s.OfficeManager));
        Assert.Equal("open_patient rebook", await CodesAsync(a.Id, As(Role.Admin)));
        foreach (var other in new[] { _s.ProviderOf(_s.ProviderA), As(Role.Hygienist), As(Role.Assistant), As(Role.Billing), As(Role.Unassigned) })
            Assert.Equal("", await CodesAsync(a.Id, other));
        var view = await ActionsAsync(a.Id, _s.Desk);
        Assert.Equal((false, (string?)null), (view.BlockFlagged, view.NextActorText));             // nothing is waiting on anyone
    }

    [Fact]
    public async Task Rebooking_is_a_new_appointment_through_the_normal_booking_rules_and_the_ended_one_is_left_as_it_was()
    {
        var gone = await EndedAsync(AppointmentStatuses.NoShow);
        var again = await _s.ScheduleAsync(_s.Request(_ann, At(11, dayOffset: 1)));                   // what the Rebook button opens: the booking form for the same patient
        Assert.True(again.Created);
        Assert.NotEqual(gone.Id, again.Appointment.Id);
        Assert.Equal(AppointmentStatuses.NoShow, (await _s.ReloadAsync(gone.Id)).Status);
        Assert.Equal("", await CodesAsync(gone.Id, As(Role.Hygienist)));
    }

    [Fact]
    public async Task A_completed_visit_offers_nothing_and_an_unknown_id_is_404()
    {
        var done = await _s.WalkAsync(await BookAsync(), VisitStates.Completed);
        Assert.Empty((await ActionsAsync(done.Id, As(Role.Admin))).Actions);
        Assert.Equal((404, "appointment_not_found"), await Assert.ThrowsAsync<SchedulingException>(() => ActionsAsync(Guid.NewGuid(), _s.Desk)) is var ex ? (ex.StatusCode, ex.Code) : default);
    }

    [Fact]
    public async Task Reading_the_actions_writes_nothing()
    {
        var a = await _s.WalkAsync(await BookAsync(), VisitStates.CheckedOut);
        var counts = async () => (await _s.CountAsync(db => db.AppointmentEvents), await _s.CountAsync(db => db.AuditLogEntries), await _s.CountAsync(db => db.VisitCheckoutOutcomes));
        var before = await counts();
        foreach (var who in new[] { _s.Desk, _s.ProviderOf(_s.ProviderA), As(Role.Admin) }) await ActionsAsync(a.Id, who);
        Assert.Equal(before, await counts());
        Assert.Equal(a.RowVersion, (await _s.ReloadAsync(a.Id)).RowVersion);
    }
}
