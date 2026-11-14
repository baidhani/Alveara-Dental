using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Permissions;
using Alveara.Api.Architecture.Scheduling;
using Alveara.Api.Architecture.Time;
using Xunit;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N021 A2 step 3a against real SQL Server: WHO may make each visit move (briefing 5.4, OD-07 to OD-09), the check-in day rule (5.5), the two settings, terminal states, and the checkout gate on
/// every completion route (R03 blocking 1: no route completes a visit with an open checkout item). Every refusal writes nothing.
/// </summary>
public class VisitMovePolicyTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private SchedulingTestSupport _s = null!;
    private Guid _ann;

    /// <summary>The default matrix plus deliberate grants, standing in for a stored policy (a grant here never bypasses the role rules the policy itself enforces).</summary>
    private sealed class Granting(params (Role Role, Permission Permission)[] grants) : IEffectivePermissions
    {
        public Task<bool> HasAsync(Role role, Permission permission, CancellationToken ct = default) =>
            Task.FromResult(PermissionMatrix.RoleHas(role, permission) || grants.Contains((role, permission)));
        public Task<PermissionOutcome> CheckAsync(Role role, Permission permission, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PermissionOutcome> CheckAnyAsync(Role role, IReadOnlyCollection<Permission> permissions, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlySet<Permission>> PermissionsForAsync(Role role, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<Role, IReadOnlySet<Permission>>> MatrixAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PolicyRead> SnapshotAsync(CancellationToken ct = default) => throw new NotSupportedException();
    }

    public Task InitializeAsync() => _fixture.InitializeOwnerAsync(async () =>
    {
        _s = new SchedulingTestSupport(_fixture);
        await _s.ArrangeAsync();
        _ann = await _s.PatientAsync();
    });
    public Task DisposeAsync() => _fixture.DisposeAsync();

    private async Task<AppointmentView> BookAsync(int hour = 9, int dayOffset = 0) => (await _s.ScheduleAsync(_s.Request(_ann, At(hour, 0, dayOffset)))).Appointment;

    private async Task<AppointmentView> MoveAsync(AppointmentView a, string target, VisitActor actor, IEffectivePermissions? permissions = null, IPracticeClock? clock = null)
    {
        await using var db = _fixture.CreateContext();
        return await new AppointmentFlowService(db, clock ?? VisitDayClock, permissions ?? PolicyTestSupport.Defaults()).TransitionAsync(a.Id, target, a.RowVersion, actor, default);
    }

    private static async Task<SchedulingException> Refused(Func<Task> action) => await Assert.ThrowsAsync<SchedulingException>(action);

    private async Task<(string Flow, int Events)> StateAsync(Guid id)
    {
        await using var db = _fixture.CreateContext();
        return ((await db.Appointments.SingleAsync(a => a.Id == id)).FlowState, await db.AppointmentEvents.CountAsync(e => e.AppointmentId == id));
    }

    private async Task<Guid> StaffAccountAsync(Role role, Guid? linkToProvider = null)
    {
        var id = Guid.NewGuid();
        await using var db = _fixture.CreateContext();
        db.UserAccounts.Add(new UserAccount { Id = id, Username = $"u-{Guid.NewGuid():N}", Role = role, CreatedAtUtc = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        if (linkToProvider is { } p)
            await db.Database.ExecuteSqlRawAsync("UPDATE s SET [UserAccountId] = {0} FROM [StaffProfiles] s JOIN [ProviderProfiles] p ON p.[StaffProfileId] = s.[Id] WHERE p.[Id] = {1}", id, p);
        return id;
    }

    // ---------- who ----------

    [Theory]
    [InlineData("Confirmed", Role.Assistant)]
    [InlineData("Confirmed", Role.Dentist)]
    [InlineData("Confirmed", Role.Billing)]
    [InlineData("Confirmed", Role.Unassigned)]
    public async Task Front_office_moves_are_refused_to_other_roles(string target, Role role)
    {
        var a = await BookAsync();
        var before = await StateAsync(a.Id);
        var e = await Refused(() => MoveAsync(a, target, new VisitActor(_s.Actor, role)));
        Assert.Equal((403, "visit_move_forbidden", "front_office_only"), (e.StatusCode, e.Code, e.Reason));
        Assert.Equal(before, await StateAsync(a.Id));
    }

    [Theory]
    [InlineData(Role.FrontDesk)]
    [InlineData(Role.OfficeManager)]   // OD-07: no chairside default for the office manager
    [InlineData(Role.Billing)]
    public async Task Chairside_moves_are_refused_to_other_roles(Role role)
    {
        var a = await _s.WalkAsync(await BookAsync(), VisitStates.CheckedIn);
        Assert.Equal("chairside_only", (await Refused(() => MoveAsync(a, VisitStates.Ready, new VisitActor(_s.Actor, role)))).Reason);
    }

    [Fact]
    public async Task Only_the_booked_provider_starts_and_ends_treatment_and_an_unlinked_administrator_is_bound_too()
    {
        var a = await _s.WalkAsync(await BookAsync(), VisitStates.Seated);
        Assert.Equal("booked_provider_only", (await Refused(() => MoveAsync(a, VisitStates.InTreatment, new VisitActor(_s.ProviderBUser, Role.Dentist)))).Reason);   // another dentist
        Assert.Equal("booked_provider_only", (await Refused(() => MoveAsync(a, VisitStates.InTreatment, new VisitActor(_s.ProviderAUser, Role.Assistant)))).Reason); // assistant role
        Assert.Equal("booked_provider_only", (await Refused(() => MoveAsync(a, VisitStates.InTreatment, new VisitActor(_s.Actor, Role.Admin)))).Reason);            // OD-08
        a = await MoveAsync(a, VisitStates.InTreatment, _s.ProviderOf(_s.ProviderA));
        Assert.Equal("booked_provider_only", (await Refused(() => MoveAsync(a, VisitStates.CheckedOut, new VisitActor(_s.ProviderBUser, Role.Hygienist)))).Reason);
        Assert.Equal(VisitStates.CheckedOut, (await MoveAsync(a, VisitStates.CheckedOut, new VisitActor(_s.ProviderAUser, Role.Hygienist))).FlowState);             // a hygienist who is the booked provider
    }

    // ---------- completion authority (R03 blocking 2, owner addendum 4) ----------

    private async Task<AppointmentView> ReadyToCompleteAsync()
    {
        var a = await _s.WalkAsync(await BookAsync(), VisitStates.CheckedOut);
        await _s.ResolveCheckoutAsync(a.Id);
        return await _s.ReloadAsync(a.Id);
    }

    [Fact]
    public async Task The_dentist_who_started_treatment_may_complete()
    {
        var a = await ReadyToCompleteAsync();                                                                       // ProviderAUser started it
        Assert.Equal(VisitStates.Completed, (await MoveAsync(a, VisitStates.Completed, new VisitActor(_s.ProviderAUser, Role.Dentist))).FlowState);
    }

    [Fact]
    public async Task A_hygienist_who_started_treatment_may_complete_only_when_granted()
    {
        var a = await ReadyToCompleteAsync();
        var hygienist = new VisitActor(_s.ProviderAUser, Role.Hygienist);
        Assert.Equal("complete_not_permitted", (await Refused(() => MoveAsync(a, VisitStates.Completed, hygienist))).Reason);
        var granted = new Granting((Role.Hygienist, Permission.CompleteOwnStartedVisit));
        Assert.Equal(VisitStates.Completed, (await MoveAsync(a, VisitStates.Completed, hygienist, granted)).FlowState);
    }

    [Fact]
    public async Task A_dentist_who_did_not_start_the_treatment_or_is_not_linked_to_a_provider_may_not_complete()
    {
        var a = await ReadyToCompleteAsync();
        Assert.Equal("not_the_one_who_started_treatment", (await Refused(() => MoveAsync(a, VisitStates.Completed, new VisitActor(_s.ProviderBUser, Role.Dentist)))).Reason);
        var unlinked = await StaffAccountAsync(Role.Dentist);
        Assert.Equal("not_linked_to_a_provider", (await Refused(() => MoveAsync(a, VisitStates.Completed, new VisitActor(unlinked, Role.Dentist)))).Reason);
    }

    [Fact]
    public async Task An_assistant_is_denied_even_with_the_permission_forced_into_the_policy()
    {
        var a = await ReadyToCompleteAsync();
        var forced = new Granting((Role.Assistant, Permission.CompleteOwnStartedVisit));
        Assert.Equal("complete_not_permitted", (await Refused(() => MoveAsync(a, VisitStates.Completed, new VisitActor(_s.ProviderAUser, Role.Assistant), forced))).Reason);
        Assert.Equal(VisitStates.CheckedOut, (await StateAsync(a.Id)).Flow);
    }

    // ---------- the checkout gate on every completion route ----------

    [Fact]
    public async Task No_completion_route_completes_a_visit_with_an_open_checkout_item_and_nothing_is_written()
    {
        var a = await _s.WalkAsync(await BookAsync(), VisitStates.CheckedOut);
        var before = await StateAsync(a.Id);
        await using (var db = _fixture.CreateContext())
        {
            var flow = _s.Flow(db);
            var viaState = await Refused(() => flow.TransitionAsync(a.Id, VisitStates.Completed, a.RowVersion, _s.Desk, default));
            var viaLegacy = await Refused(() => flow.CompleteAsync(a.Id, a.RowVersion, _s.Desk, default));
            var viaClinician = await Refused(() => flow.CompleteAsync(a.Id, a.RowVersion, new VisitActor(_s.ProviderAUser, Role.Dentist), default));
            foreach (var e in new[] { viaState, viaLegacy, viaClinician })
                Assert.Equal((409, "checkout_incomplete", "departure,follow_up"), (e.StatusCode, e.Code, e.Reason));
        }
        Assert.Equal(before, await StateAsync(a.Id));
        await using var check = _fixture.CreateContext();
        Assert.Equal(0, await check.AuditLogEntries.CountAsync(x => x.TargetUserAccountId == a.Id && x.EventType == SchedulingAuditEvents.TreatmentCompleted));
    }

    [Fact]
    public async Task Completing_twice_at_once_ends_in_one_completion_event()
    {
        var a = await ReadyToCompleteAsync();
        await Task.WhenAll(MoveAsync(a, VisitStates.Completed, _s.Desk), MoveAsync(a, VisitStates.Completed, new VisitActor(_s.ProviderAUser, Role.Dentist)))
            .ContinueWith(t => t.Exception?.Flatten().InnerExceptions.All(x => x is Architecture.Concurrency.ConcurrencyConflictException) ?? true ? 0 : throw t.Exception!);
        await using var db = _fixture.CreateContext();
        Assert.Equal(1, await db.AppointmentEvents.CountAsync(e => e.AppointmentId == a.Id && e.EventType == AppointmentEventTypes.Completed));
        Assert.Equal(VisitStates.Completed, (await StateAsync(a.Id)).Flow);
    }

    // ---------- the day rule ----------

    private static IPracticeClock LocalClock(int year, int month, int day, int hour, int minute) =>
        new TestClock(new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.FromHours(-6)));

    [Fact]
    public async Task Check_in_is_allowed_only_on_the_appointments_practice_day()
    {
        var a = await _s.MoveAsync(await BookAsync(), VisitStates.Confirmed);                                      // Monday 09:00 (CST)
        Assert.Equal("check_in_wrong_day", (await Refused(() => MoveAsync(a, VisitStates.CheckedIn, _s.Desk, clock: LocalClock(2030, 1, 13, 23, 59)))).Code);   // one minute before the day
        Assert.Equal("check_in_wrong_day", (await Refused(() => MoveAsync(a, VisitStates.CheckedIn, _s.Desk, clock: LocalClock(2030, 1, 15, 0, 0)))).Code);     // the next day has begun
        Assert.Equal(VisitStates.CheckedIn, (await MoveAsync(a, VisitStates.CheckedIn, _s.Desk, clock: LocalClock(2030, 1, 14, 0, 0))).FlowState);             // the day's first minute
    }

    [Fact]
    public async Task An_evening_check_in_is_the_practice_day_although_utc_is_already_tomorrow_and_lateness_is_recorded()
    {
        var a = await _s.MoveAsync(await BookAsync(16), VisitStates.Confirmed);                                  // Monday 16:00 CST
        var evening = LocalClock(2030, 1, 14, 19, 30);                                                            // 01:30 UTC on Tuesday
        Assert.Equal(VisitStates.CheckedIn, (await MoveAsync(a, VisitStates.CheckedIn, _s.Desk, clock: evening)).FlowState);
        await using var db = _fixture.CreateContext();
        var e = await db.AppointmentEvents.SingleAsync(x => x.AppointmentId == a.Id && x.EventType == AppointmentEventTypes.CheckedIn);
        Assert.Equal("Confirmed -> CheckedIn (210 minutes late)", e.Detail);
    }

    // ---------- settings and terminal states ----------

    private async Task SettingsAsync(bool confirmation, bool ready)
    {
        await using var db = _fixture.CreateContext();
        await db.Database.ExecuteSqlRawAsync("DELETE FROM [PracticeSettings]");
        await db.Database.ExecuteSqlRawAsync("INSERT INTO [PracticeSettings] ([Id], [Name], [UpdatedAtUtc], [ConfirmationRequired], [MarkReadyRequired]) VALUES ({0}, N'Practice', SYSUTCDATETIME(), {1}, {2})",
            Architecture.Configuration.PracticeSettings.SingletonId, confirmation, ready);
    }

    [Fact]
    public async Task With_both_settings_required_check_in_needs_confirmation_and_seating_needs_ready()
    {
        var a = await BookAsync();
        Assert.Equal("illegal_transition", (await Refused(() => MoveAsync(a, VisitStates.CheckedIn, _s.Desk))).Code);
        a = await _s.WalkAsync(a, VisitStates.CheckedIn);
        Assert.Equal("illegal_transition", (await Refused(() => MoveAsync(a, VisitStates.Seated, _s.MoverFor(VisitStates.Seated, a)))).Code);
    }

    [Fact]
    public async Task With_both_settings_off_a_visit_may_be_checked_in_unconfirmed_and_seated_without_ready()
    {
        await SettingsAsync(confirmation: false, ready: false);
        var a = await MoveAsync(await BookAsync(), VisitStates.CheckedIn, _s.Desk);
        Assert.Equal(VisitStates.Seated, (await MoveAsync(a, VisitStates.Seated, _s.MoverFor(VisitStates.Seated, a))).FlowState);
    }

    [Fact]
    public async Task A_cancelled_or_completed_visit_is_final()
    {
        var booked = await BookAsync(10);
        var cancelled = await _s.ManageAsync(m => m.CancelAsync(booked.Id, "patient", "Illness", booked.RowVersion, _s.Actor, default));
        Assert.Equal("terminal_state", (await Refused(() => MoveAsync(cancelled, VisitStates.Confirmed, _s.Desk))).Code);
        var done = await _s.WalkAsync(await BookAsync(), VisitStates.Completed);
        Assert.Equal("terminal_state", (await Refused(() => MoveAsync(done, VisitStates.CheckedOut, _s.ProviderOf(_s.ProviderA)))).Code);
    }
}
