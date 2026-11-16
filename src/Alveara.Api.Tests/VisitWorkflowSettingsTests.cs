using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Configuration;
using Alveara.Api.Architecture.Scheduling;
using Xunit;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N021 A2, the owner decisions the OD mapping found unproven:
/// OD-03/OD-04 - the two visit-workflow settings are changed through configuration, audited with old and new values, the actor and the time,
/// and apply from the next move only; OD-30 - a visit already in Ready for checkout before A2 (no checkout outcomes, no End-treatment event)
/// goes through the same gate; OD-20 - there is no route that corrects a no-show.
/// </summary>
public class VisitWorkflowSettingsTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private SchedulingTestSupport _s = null!;
    private Guid _ann;

    public Task InitializeAsync() => _fixture.InitializeOwnerAsync(async () =>
    {
        _s = new SchedulingTestSupport(_fixture);
        await _s.ArrangeAsync();
        _ann = await _s.PatientAsync();
        await using var db = _fixture.CreateContext();
        await new PracticeConfigurationService(db, VisitDayClock).SavePracticeAsync("Alveara Dental", null, null, null, _s.Actor, default);
    });
    public Task DisposeAsync() => _fixture.DisposeAsync();

    private async Task<PracticeSettingsView> SaveAsync(bool? confirmation, bool? ready, string? rowVersion)
    {
        await using var db = _fixture.CreateContext();
        return await new PracticeConfigurationService(db, VisitDayClock).SaveVisitWorkflowAsync(confirmation, ready, rowVersion, _s.Actor, default);
    }

    private async Task<PracticeSettingsView> ReadAsync()
    {
        await using var db = _fixture.CreateContext();
        return await new PracticeConfigurationService(db, VisitDayClock).GetPracticeAsync(default);
    }

    private async Task<int> AuditCountAsync()
    {
        await using var db = _fixture.CreateContext();
        return await db.AuditLogEntries.CountAsync(e => e.TargetUserAccountId == PracticeSettings.SingletonId);
    }

    private async Task<AppointmentView> BookAsync(int hour) => (await _s.ScheduleAsync(_s.Request(_ann, At(hour, 0)))).Appointment;

    private Task<AppointmentView> CheckInAsync(AppointmentView a) =>
        _s.FlowAsync(f => f.TransitionAsync(a.Id, VisitStates.CheckedIn, a.RowVersion, _s.Desk, default), VisitDayClock);

    // ---------- OD-03 / OD-04 ----------

    [Fact]
    public async Task Both_settings_read_as_required_by_default_and_a_change_is_audited_with_old_and_new_values_actor_and_time()
    {
        var before = await ReadAsync();
        Assert.True(before.ConfirmationRequired);
        Assert.True(before.MarkReadyRequired);
        var audits = await AuditCountAsync();

        var saved = await SaveAsync(false, true, before.RowVersion);
        Assert.False(saved.ConfirmationRequired);
        Assert.True(saved.MarkReadyRequired);
        Assert.False((await ReadAsync()).ConfirmationRequired);

        await using var db = _fixture.CreateContext();
        var entry = await db.AuditLogEntries.Where(e => e.TargetUserAccountId == PracticeSettings.SingletonId).OrderByDescending(e => e.TimestampUtc).FirstAsync();
        Assert.Equal(audits + 1, await AuditCountAsync());
        Assert.Equal("Visit workflow settings changed: confirmation required yes -> no; mark ready required yes -> yes.", entry.Details);
        Assert.Equal(_s.Actor, entry.PerformedByUserAccountId);
        Assert.NotEqual(default, entry.TimestampUtc);
    }

    [Fact]
    public async Task Saving_what_is_already_set_writes_nothing_and_a_stale_or_incomplete_save_is_refused()
    {
        var current = await ReadAsync();
        var audits = await AuditCountAsync();
        Assert.Equal(current.RowVersion, (await SaveAsync(true, true, current.RowVersion)).RowVersion);   // no-op: same version, no audit
        Assert.Equal(audits, await AuditCountAsync());

        var changed = await SaveAsync(true, false, current.RowVersion);
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => SaveAsync(false, false, current.RowVersion));   // stale version
        Assert.Equal("visit_settings_required", (await Assert.ThrowsAsync<ConfigurationException>(() => SaveAsync(null, false, changed.RowVersion))).Code);
        Assert.Equal("row_version_required", (await Assert.ThrowsAsync<ConfigurationException>(() => SaveAsync(false, false, null))).Code);
        var after = await ReadAsync();
        Assert.True(after.ConfirmationRequired);
        Assert.False(after.MarkReadyRequired);
        Assert.Equal(audits + 1, await AuditCountAsync());
    }

    [Fact]
    public async Task Before_the_practice_exists_the_settings_cannot_be_saved()
    {
        await using (var db = _fixture.CreateContext()) await db.Database.ExecuteSqlRawAsync("DELETE FROM [PracticeSettings]");
        var e = await Assert.ThrowsAsync<ConfigurationException>(() => SaveAsync(false, false, "AAAAAAAAAAA="));
        Assert.Equal(("practice_not_configured", 409), (e.Code, e.StatusCode));
    }

    [Fact]
    public async Task A_change_applies_from_the_next_move_and_moves_already_made_stay_as_they_are()
    {
        var first = await BookAsync(9);
        var second = await BookAsync(11);
        Assert.Equal("illegal_transition", (await Assert.ThrowsAsync<SchedulingException>(() => CheckInAsync(first))).Code);   // required: refused from Scheduled

        var off = await SaveAsync(false, true, (await ReadAsync()).RowVersion);
        var checkedIn = await CheckInAsync(first);                                                                           // the very next move follows the new setting
        Assert.Equal(VisitStates.CheckedIn, checkedIn.FlowState);

        await SaveAsync(true, true, off.RowVersion);
        Assert.Equal(VisitStates.CheckedIn, (await _s.ReloadAsync(first.Id)).FlowState);                                   // not re-evaluated
        Assert.Equal("illegal_transition", (await Assert.ThrowsAsync<SchedulingException>(() => CheckInAsync(second))).Code);
    }

    // ---------- OD-30 ----------

    [Fact]
    public async Task A_visit_left_in_ready_for_checkout_before_A2_is_not_grandfathered_and_cannot_complete_until_resolved()
    {
        var a = await BookAsync(9);
        await using (var db = _fixture.CreateContext())   // the shape of a pre-A2 row: CheckedOut, no End-treatment event and no checkout outcomes
            await db.Database.ExecuteSqlRawAsync("UPDATE [Appointments] SET [FlowState] = {0} WHERE [Id] = {1}", VisitStates.CheckedOut, a.Id);
        var legacy = await _s.ReloadAsync(a.Id);

        var e = await Assert.ThrowsAsync<SchedulingException>(() =>
            _s.FlowAsync(f => f.TransitionAsync(a.Id, VisitStates.Completed, legacy.RowVersion, _s.Desk, default), VisitDayClock));
        Assert.Equal((409, "checkout_incomplete"), (e.StatusCode, e.Code));
        Assert.Contains("departure", e.Reason);
        Assert.Contains("follow_up", e.Reason);
        Assert.Equal(VisitStates.CheckedOut, (await _s.ReloadAsync(a.Id)).FlowState);
        await using var check = _fixture.CreateContext();
        Assert.Equal(0, await check.AppointmentEvents.CountAsync(x => x.AppointmentId == a.Id && x.EventType == AppointmentEventTypes.Completed));
    }

    // ---------- OD-20 ----------

    [Fact]
    public void The_only_no_show_route_marks_one_there_is_no_route_that_corrects_or_undoes_a_no_show()
    {
        var routes = typeof(Program).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .SelectMany(m => m.GetCustomAttributes<HttpMethodAttribute>().Select(h => $"{t.Name}.{m.Name} {h.Template}")))
            .ToList();
        Assert.True(routes.Count > 100, "the route scan found the controllers");
        var noShow = routes.Where(r => r.Contains("no-show", StringComparison.OrdinalIgnoreCase) || r.Contains("noshow", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.Equal("AppointmentsController.NoShow {id:guid}/no-show", Assert.Single(noShow));
    }
}
