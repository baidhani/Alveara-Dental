using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Configuration;
using Alveara.Api.Architecture.Forms;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Scheduling;
using Xunit;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N021 A2 (OD-17, OD-18) against real SQL Server: blocked time over booked appointments. A block over a visit under way is refused whatever the caller confirms; over future appointments it
/// needs a confirmation and a reason, then flags them; a flagged appointment cannot be checked in; the flag is cleared by reschedule, reassignment, cancellation or Keep (reason); a check-in
/// racing a block never leaves a saved block over an active visit.
/// </summary>
public class BlockedTimeOverlapTests : IAsyncLifetime
{
    private const string Why = "Dr. Rivera at a course";
    private readonly TestDatabaseFixture _fixture = new();
    private SchedulingTestSupport _s = null!;
    private Guid _ann, _bo;

    public Task InitializeAsync() => _fixture.InitializeOwnerAsync(async () =>
    {
        _s = new SchedulingTestSupport(_fixture);
        await _s.ArrangeAsync();
        _ann = await _s.PatientAsync();
        _bo = await _s.PatientAsync("Bo", "Kim");
    });
    public Task DisposeAsync() => _fixture.DisposeAsync();

    private async Task<AppointmentView> BookAsync(Guid patient, int hour, Guid? provider = null, Guid? operatory = null) =>
        (await _s.ScheduleAsync(_s.Request(patient, At(hour), provider, operatory))).Appointment;

    /// <summary>Blocks Dr. Rivera (by default) from <paramref name="from"/> to <paramref name="to"/> o'clock on the test day.</summary>
    private async Task<ProviderBlockedTime> BlockAsync(int from, int to, bool confirm = false, string? reason = Why, Guid? provider = null)
    {
        await using var db = _fixture.CreateContext();
        return await new BlockedTimeGuard(db, VisitDayClock).AddAsync(provider ?? _s.ProviderA, At(from), At(to), reason, confirm, _s.Actor, default, BlockedTimeLabels.Unavailable);
    }

    private static async Task<BlockedTimeRefusedException> RefusedBlockAsync(Func<Task> action) => await Assert.ThrowsAsync<BlockedTimeRefusedException>(action);

    private Task<int> BlocksAsync() => _s.CountAsync(db => db.ProviderBlockedTimes);
    private Task<int> BlockAuditsAsync() => _s.CountAsync(db => db.AuditLogEntries.Where(e => e.EventType == ConfigurationAuditEvents.ProviderBlockedTimeAdded));

    private Task<AppointmentView> KeepAsync(AppointmentView a, string? reason = "Dr. Rivera will see her between sessions", VisitActor? by = null) =>
        _s.FlowAsync(f => f.KeepDespiteBlockAsync(a.Id, reason, a.RowVersion, by ?? _s.Desk, default));

    // ---------- adding a block ----------

    [Fact]
    public async Task A_block_over_no_appointment_is_saved_as_before_without_a_confirmation_or_reason()
    {
        await BookAsync(_ann, 9);
        await BlockAsync(12, 14, reason: null);
        Assert.Equal((1, 1), (await BlocksAsync(), await BlockAuditsAsync()));
        Assert.False((await _s.ReloadAsync((await BookAsync(_bo, 15)).Id)).BlockFlagged);
    }

    [Fact]
    public async Task A_block_over_booked_appointments_without_confirmation_is_refused_with_the_list_and_nothing_is_written()
    {
        var nine = await BookAsync(_ann, 9);
        var confirmed = await _s.MoveAsync(await BookAsync(_bo, 11), VisitStates.Confirmed);
        await BookAsync(await _s.PatientAsync("Cy", "Poe"), 14);                     // outside the block
        await BookAsync(await _s.PatientAsync("Di", "Lo"), 10, _s.ProviderB, _s.Op2);  // another provider

        var ex = await RefusedBlockAsync(() => BlockAsync(8, 12));

        Assert.Equal((409, "block_overlaps_appointments"), (ex.StatusCode, ex.Code));
        Assert.Equal([nine.Id, confirmed.Id], ex.Overlaps.Select(o => o.AppointmentId));
        Assert.Equal(("2030-01-14T09:00", "2030-01-14T10:00", "Dr. Rivera", "Op 1", "Scheduled", "Ann Lee"),
            (ex.Overlaps[0].StartLocal, ex.Overlaps[0].EndLocal, ex.Overlaps[0].ProviderName, ex.Overlaps[0].OperatoryName, ex.Overlaps[0].FlowState, ex.Overlaps[0].PatientName));
        Assert.Equal((0, 0), (await BlocksAsync(), await BlockAuditsAsync()));
        Assert.False((await _s.ReloadAsync(nine.Id)).BlockFlagged);
    }

    [Fact]
    public async Task A_confirmed_block_needs_a_reason_and_then_is_saved_audited_without_patient_details_and_flags_only_the_covered_appointments()
    {
        var nine = await BookAsync(_ann, 9);
        var later = await BookAsync(_bo, 14);
        var other = await BookAsync(await _s.PatientAsync("Cy", "Poe"), 9, _s.ProviderB, _s.Op2);

        foreach (var blank in new string?[] { null, "  " })
            Assert.Equal((400, "reason_required"), Code(await RefusedBlockAsync(() => BlockAsync(8, 12, confirm: true, reason: blank))));
        Assert.Equal(0, await BlocksAsync());

        await BlockAsync(8, 12, confirm: true);

        Assert.Equal((true, false, false), ((await _s.ReloadAsync(nine.Id)).BlockFlagged, (await _s.ReloadAsync(later.Id)).BlockFlagged, (await _s.ReloadAsync(other.Id)).BlockFlagged));
        await using var db = _fixture.CreateContext();
        var audit = await db.AuditLogEntries.SingleAsync(e => e.EventType == ConfigurationAuditEvents.ProviderBlockedTimeAdded);
        Assert.Contains("1 booked appointment", audit.Details);
        Assert.DoesNotContain("Ann", audit.Details);
        Assert.DoesNotContain("course", audit.Details);
        // the flag shows on the board card as well (Calendar and the provider's day use the same view)
        var card = (await new VisitBoardService(db, VisitDayClock, new CheckInReadinessService(db)).BoardAsync(DateOnly.FromDateTime(Monday), false, default)).Visits.Single(v => v.Appointment.Id == nine.Id);
        Assert.True(card.Appointment.BlockFlagged);
    }

    private static (int, string) Code(BlockedTimeRefusedException ex) => (ex.StatusCode, ex.Code);

    [Theory]
    [InlineData(VisitStates.CheckedIn)]
    [InlineData(VisitStates.Ready)]
    [InlineData(VisitStates.Seated)]
    [InlineData(VisitStates.InTreatment)]
    [InlineData(VisitStates.CheckedOut)]
    public async Task A_block_over_a_visit_under_way_is_refused_even_with_confirmation_and_nothing_is_written(string state)
    {
        var active = await _s.WalkAsync(await BookAsync(_ann, 9), state);
        var future = await BookAsync(_bo, 11);

        foreach (var confirm in new[] { false, true })
        {
            var ex = await RefusedBlockAsync(() => BlockAsync(8, 12, confirm));
            Assert.Equal((409, "block_overlaps_active_visit"), Code(ex));
            Assert.Equal([active.Id], ex.Overlaps.Select(o => o.AppointmentId));   // only the visit under way is listed
        }
        Assert.Equal((0, 0), (await BlocksAsync(), await BlockAuditsAsync()));
        Assert.False((await _s.ReloadAsync(future.Id)).BlockFlagged);
    }

    [Fact]
    public async Task Completed_cancelled_and_no_show_appointments_are_never_listed_or_flagged()
    {
        var done = await _s.WalkAsync(await BookAsync(_ann, 8), VisitStates.Completed);
        var b = await BookAsync(_bo, 10);
        await _s.ManageAsync(m => m.CancelAsync(b.Id, "patient", null, b.RowVersion, _s.Actor, default));

        await BlockAsync(8, 12, reason: null);   // nothing booked is covered: no confirmation needed

        Assert.Equal(1, await BlocksAsync());
        Assert.False((await _s.ReloadAsync(done.Id)).BlockFlagged);
        Assert.False((await _s.ReloadAsync(b.Id)).BlockFlagged);
    }

    // ---------- a flagged appointment ----------

    [Fact]
    public async Task A_flagged_appointment_cannot_be_checked_in_and_nothing_moves()
    {
        var a = await _s.MoveAsync(await BookAsync(_ann, 9), VisitStates.Confirmed);
        await BlockAsync(8, 12, confirm: true);

        var ex = await Assert.ThrowsAsync<SchedulingException>(() => _s.MoveAsync(a, VisitStates.CheckedIn));

        Assert.Equal((409, "block_flag_unresolved"), (ex.StatusCode, ex.Code));
        Assert.Equal(VisitStates.Confirmed, (await _s.ReloadAsync(a.Id)).FlowState);
    }

    [Fact]
    public async Task Rescheduling_clears_the_flag()
    {
        var a = await BookAsync(_ann, 9);
        await BlockAsync(8, 12, confirm: true);
        var moved = await _s.ManageAsync(m => m.RescheduleAsync(a.Id, new RescheduleRequest(a.ProviderId, a.OperatoryId, At(14), null, a.RowVersion), _s.Actor, default));
        Assert.False(moved.BlockFlagged);
    }

    [Fact]
    public async Task Reassigning_to_another_provider_clears_the_flag_and_a_room_only_change_does_not()
    {
        var a = await BookAsync(_ann, 9);
        await BlockAsync(8, 12, confirm: true);

        var roomOnly = await _s.AssignAsync(v => v.AssignAsync(a.Id, new AssignVisitRequest(a.ProviderId, _s.Op2, a.RowVersion), _s.OfficeManager, default));
        Assert.True(roomOnly.BlockFlagged);
        var reassigned = await _s.AssignAsync(v => v.AssignAsync(a.Id, new AssignVisitRequest(_s.ProviderB, _s.Op2, roomOnly.RowVersion, "Dr. Rivera at a course"), _s.OfficeManager, default));
        Assert.False(reassigned.BlockFlagged);
    }

    [Fact]
    public async Task Cancelling_clears_the_flag()
    {
        var a = await BookAsync(_ann, 9);
        await BlockAsync(8, 12, confirm: true);
        var cancelled = await _s.ManageAsync(m => m.CancelAsync(a.Id, AppointmentCancelCategories.Provider, null, a.RowVersion, _s.Actor, default));
        Assert.False(cancelled.BlockFlagged);
    }

    [Fact]
    public async Task Keep_with_a_reason_clears_the_flag_records_who_and_why_and_lets_the_patient_check_in_and_a_later_block_flags_it_again()
    {
        var a = await _s.MoveAsync(await BookAsync(_ann, 9), VisitStates.Confirmed);
        await BlockAsync(8, 10, confirm: true);

        var kept = await KeepAsync(a);

        Assert.False(kept.BlockFlagged);
        var keep = (await _s.HistoryAsync(a.Id)).Single(e => e.EventType == AppointmentEventTypes.KeptDespiteBlock);
        Assert.Equal((_s.Actor, "Dr. Rivera will see her between sessions"), (keep.ActorUserId, keep.Reason));
        await using (var db = _fixture.CreateContext())
        {
            var audit = await db.AuditLogEntries.SingleAsync(e => e.EventType == SchedulingAuditEvents.KeptDespiteBlock);
            Assert.DoesNotContain("between", audit.Details);
        }

        await BlockAsync(9, 11, confirm: true);   // a new block after the keep
        Assert.True((await _s.ReloadAsync(a.Id)).BlockFlagged);
        var keptAgain = await KeepAsync(await _s.ReloadAsync(a.Id), "Still fine, short visit");
        Assert.Equal(VisitStates.CheckedIn, (await _s.MoveAsync(keptAgain, VisitStates.CheckedIn)).FlowState);
    }

    [Fact]
    public async Task Keep_needs_a_reason_the_front_office_and_a_flag_and_a_retry_changes_nothing()
    {
        var a = await BookAsync(_ann, 9);
        Assert.Equal("not_flagged", (await Assert.ThrowsAsync<SchedulingException>(() => KeepAsync(a))).Code);
        await BlockAsync(8, 12, confirm: true);

        Assert.Equal("reason_required", (await Assert.ThrowsAsync<SchedulingException>(() => KeepAsync(a, "  "))).Code);
        foreach (var role in new[] { Role.Dentist, Role.Hygienist, Role.Assistant, Role.Billing })
            Assert.Equal("front_office_only", (await Assert.ThrowsAsync<SchedulingException>(() => KeepAsync(a, by: new VisitActor(_s.Actor, role)))).Reason);
        Assert.True((await _s.ReloadAsync(a.Id)).BlockFlagged);

        await KeepAsync(a);
        await KeepAsync(a);   // a retry
        Assert.Equal(1, await _s.CountAsync(db => db.AppointmentEvents.Where(e => e.AppointmentId == a.Id && e.EventType == AppointmentEventTypes.KeptDespiteBlock)));
        Assert.Equal("not_flagged", (await Assert.ThrowsAsync<SchedulingException>(() => KeepAsync(a, "Another reason"))).Code);
    }

    // ---------- the race ----------

    [Fact]
    public async Task A_check_in_racing_a_confirmed_block_never_leaves_a_saved_block_over_an_active_visit()
    {
        for (var round = 0; round < 3; round++)
        {
            var a = await _s.MoveAsync(await BookAsync(await _s.PatientAsync($"R{round}", "Ace"), 9 + 2 * round), VisitStates.Confirmed);
            var block = Task.Run(async () =>
            {
                await using var db = _s.SlowSaveContext(TimeSpan.FromMilliseconds(200));
                try { await new BlockedTimeGuard(db, VisitDayClock).AddAsync(_s.ProviderA, At(9 + 2 * round), At(10 + 2 * round), Why, true, _s.Actor, default, BlockedTimeLabels.Unavailable); return "block"; }
                catch (BlockedTimeRefusedException ex) { return ex.Code; }
            });
            var checkIn = Task.Run(async () =>
            {
                await using var db = _s.SlowSaveContext(TimeSpan.FromMilliseconds(200));
                try { await _s.Flow(db).TransitionAsync(a.Id, VisitStates.CheckedIn, a.RowVersion, _s.Desk, default); return "checked_in"; }
                catch (SchedulingException ex) { return ex.Code; }
            });
            var outcome = (await block, await checkIn);

            Assert.True(outcome is ("block", "block_flag_unresolved") or ("block_overlaps_active_visit", "checked_in"), $"round {round}: {outcome}");
            var state = (await _s.ReloadAsync(a.Id)).FlowState;
            var blocked = await _s.CountAsync(db => db.ProviderBlockedTimes.Where(b => b.StartUtc < (DateTimeOffset)a.EndUtc && (DateTimeOffset)a.StartUtc < b.EndUtc));
            Assert.False(state == VisitStates.CheckedIn && blocked > 0, $"round {round}: a block was saved over a checked-in visit");
        }
    }
}
