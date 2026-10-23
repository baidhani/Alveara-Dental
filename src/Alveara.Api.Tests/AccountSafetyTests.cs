using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Permissions;
using Alveara.Api.Data;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N017 on the account operations, against a real database. The last enabled administrator can never be removed, by the application or by raw SQL, under concurrency, with a stable refusal that leaves no
/// partial change and is audited. A non-administrator who holds <c>ManageAccountStatus</c> or <c>RevokeSessions</c> (granted here through a controlled policy source, since no write API exists yet) may act only on a
/// non-administrator whose complete effective permission set is a subset of theirs, may not disable themselves, and every refusal and success is audited with the rule.
/// </summary>
public class AccountSafetyTests : SafetyTestBase
{
    // ---------- helpers ----------

    private async Task<Guid> NewUserAsync(Role role, bool disabled = false)
    {
        var id = Guid.NewGuid();
        await using var db = Fixture.CreateContext();
        db.UserAccounts.Add(new UserAccount { Id = id, Username = $"u-{Guid.NewGuid():N}", Role = role, IsDisabled = disabled, CreatedAtUtc = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        return id;
    }

    private static AccountService Service(AlveraDbContext db, IEffectivePermissions? permissions = null) =>
        new(db, new EphemeralDataProtectionProvider(), permissions ?? PolicyTestSupport.Defaults());

    /// <summary>An effective-permission service in which the office manager has been granted the two Admin-assignable permissions (what an administrator will be able to do in ALV-N018).</summary>
    private static IEffectivePermissions WithOfficeManagerHolding(params Permission[] permissions)
    {
        var source = new ControlledPolicySource();
        foreach (var p in permissions) source.Overrides.Add(PolicyTestSupport.Override("OfficeManager", p.ToString(), "Grant"));
        source.Version = permissions.Length;
        return new EffectivePermissionService(PolicyTestSupport.Store(source));
    }

    private async Task<(Role Role, bool Disabled)> StateAsync(Guid id)
    {
        await using var db = Fixture.CreateContext();
        var a = await db.UserAccounts.AsNoTracking().SingleAsync(u => u.Id == id);
        return (a.Role, a.IsDisabled);
    }

    private async Task<int> AvailableAdminsAsync()
    {
        await using var db = Fixture.CreateContext();
        return await db.UserAccounts.CountAsync(u => u.Role == Role.Admin && !u.IsDisabled);
    }

    private async Task<List<(string Event, string Details, Guid? By)>> AuditAsync(string eventType)
    {
        await using var db = Fixture.CreateContext();
        return (await db.AuditLogEntries.AsNoTracking().Where(a => a.EventType == eventType).OrderBy(a => a.TimestampUtc).Select(a => new { a.EventType, a.Details, a.PerformedByUserAccountId }).ToListAsync())
            .Select(a => (a.EventType, a.Details, a.PerformedByUserAccountId)).ToList();
    }

    private async Task<SqlException> SqlRefusalAsync(FormattableString sql)
    {
        await using var db = Fixture.CreateContext();
        var e = await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlAsync(sql));
        return Assert.IsType<SqlException>(e is DbUpdateException { InnerException: not null } d ? d.InnerException : e);
    }

    // ---------- the last-enabled-administrator invariant ----------

    [Fact]
    public async Task The_only_enabled_administrator_cannot_be_demoted_or_disabled_and_nothing_changes_but_the_audit()
    {
        var admin = await NewUserAsync(Role.Admin);
        await using (var db = Fixture.CreateContext()) await Assert.ThrowsAsync<LastAdminException>(() => Service(db).ChangeRoleAsync(admin, Role.Dentist, admin));
        await using (var db = Fixture.CreateContext()) await Assert.ThrowsAsync<LastAdminException>(() => Service(db).SetAccountEnabledAsync(admin, false, admin));
        Assert.Equal((Role.Admin, false), await StateAsync(admin));
        var refusals = await AuditAsync(AuditEventTypes.AccountActionRefused);
        Assert.Equal(2, refusals.Count);
        Assert.All(refusals, r => Assert.Contains("last_admin", r.Details));
        Assert.Empty(await AuditAsync(AuditEventTypes.RoleChanged));
        Assert.Empty(await AuditAsync(AuditEventTypes.AccountDisabled));
    }

    [Fact]
    public async Task With_two_administrators_one_can_be_demoted_or_disabled_but_not_both()
    {
        var a = await NewUserAsync(Role.Admin);
        var b = await NewUserAsync(Role.Admin);
        await using (var db = Fixture.CreateContext()) await Service(db).ChangeRoleAsync(a, Role.Dentist, b);
        Assert.Equal(1, await AvailableAdminsAsync());
        await using (var db = Fixture.CreateContext()) await Assert.ThrowsAsync<LastAdminException>(() => Service(db).SetAccountEnabledAsync(b, false, b));
        await using (var db = Fixture.CreateContext()) await Service(db).SetAccountEnabledAsync(a, true, b);      // enabling or re-roling a non-administrator is unaffected
        Assert.Equal(1, await AvailableAdminsAsync());
    }

    [Fact]
    public async Task A_disabled_administrator_does_not_count_and_an_already_disabled_one_can_be_demoted()
    {
        var enabled = await NewUserAsync(Role.Admin);
        var disabled = await NewUserAsync(Role.Admin, disabled: true);
        await using (var db = Fixture.CreateContext()) await Assert.ThrowsAsync<LastAdminException>(() => Service(db).ChangeRoleAsync(enabled, Role.Billing, enabled));
        await using (var db = Fixture.CreateContext()) await Service(db).ChangeRoleAsync(disabled, Role.Billing, enabled);   // removes no available administrator
        Assert.Equal(1, await AvailableAdminsAsync());
    }

    [Fact]
    public async Task Two_administrators_demoting_each_other_at_once_leave_exactly_one()
    {
        var a = await NewUserAsync(Role.Admin);
        var b = await NewUserAsync(Role.Admin);
        var results = await RunTogetherAsync(
            db => Service(db).ChangeRoleAsync(a, Role.Dentist, b),
            db => Service(db).ChangeRoleAsync(b, Role.Dentist, a));
        Assert.Equal(1, results.Count(r => r is null));
        Assert.Single(results.Where(r => r is not null), r => r is LastAdminException);
        Assert.Equal(1, await AvailableAdminsAsync());
    }

    [Fact]
    public async Task Two_administrators_disabling_each_other_at_once_leave_exactly_one()
    {
        var a = await NewUserAsync(Role.Admin);
        var b = await NewUserAsync(Role.Admin);
        var results = await RunTogetherAsync(
            db => Service(db).SetAccountEnabledAsync(a, false, b),
            db => Service(db).SetAccountEnabledAsync(b, false, a));
        Assert.Equal(1, results.Count(r => r is null));
        Assert.Equal(1, await AvailableAdminsAsync());
    }

    [Fact]
    public async Task One_demoting_while_the_other_disables_leaves_exactly_one()
    {
        var a = await NewUserAsync(Role.Admin);
        var b = await NewUserAsync(Role.Admin);
        var results = await RunTogetherAsync(
            db => Service(db).ChangeRoleAsync(a, Role.Dentist, b),
            db => Service(db).SetAccountEnabledAsync(b, false, a));
        Assert.Equal(1, results.Count(r => r is null));
        Assert.Equal(1, await AvailableAdminsAsync());
    }

    [Fact]
    public async Task Twenty_mixed_concurrent_attempts_across_three_administrators_always_leave_at_least_one()
    {
        var admins = new[] { await NewUserAsync(Role.Admin), await NewUserAsync(Role.Admin), await NewUserAsync(Role.Admin) };
        var calls = new List<Func<AlveraDbContext, Task>>();
        for (var i = 0; i < 20; i++)
        {
            var target = admins[i % 3];
            var actor = admins[(i + 1) % 3];
            calls.Add(i % 2 == 0 ? db => Service(db).ChangeRoleAsync(target, Role.Dentist, actor) : db => Service(db).SetAccountEnabledAsync(target, false, actor));
        }
        var results = await RunTogetherAsync(calls.ToArray());
        Assert.All(results.Where(r => r is not null), r => Assert.True(r is LastAdminException or AdminGuardBusyException or AccountTargetNotAllowedException, $"unexpected {r!.GetType().Name}: {r.Message}"));   // an actor demoted earlier in the run is no longer an administrator, so the target rules refuse it too
        Assert.True(await AvailableAdminsAsync() >= 1);
        Assert.True(results.Count(r => r is null) >= 1);
    }

    private async Task<Exception?[]> RunTogetherAsync(params Func<AlveraDbContext, Task>[] work)
    {
        using var barrier = new Barrier(work.Length);
        var tasks = work.Select(w => Task.Run(async () =>
        {
            await using var db = Fixture.CreateContext();
            barrier.SignalAndWait(TimeSpan.FromSeconds(20));
            try { await w(db); return (Exception?)null; }
            catch (Exception ex) { return ex; }
        })).ToArray();
        return await Task.WhenAll(tasks);
    }

    [Fact]
    public async Task Raw_sql_cannot_remove_the_last_administrator_by_update_bulk_update_or_delete()
    {
        var admin = await NewUserAsync(Role.Admin);
        Assert.Equal(51207, (await SqlRefusalAsync($"UPDATE [UserAccounts] SET [Role] = {(int)Role.Dentist} WHERE [Id] = {admin}")).Number);
        Assert.Equal(51207, (await SqlRefusalAsync($"UPDATE [UserAccounts] SET [IsDisabled] = 1 WHERE [Role] = {(int)Role.Admin}")).Number);
        Assert.Equal(51207, (await SqlRefusalAsync($"DELETE FROM [UserAccounts] WHERE [Id] = {admin}")).Number);
        Assert.Equal((Role.Admin, false), await StateAsync(admin));

        var second = await NewUserAsync(Role.Admin);
        await using var db = Fixture.CreateContext();
        Assert.Equal(1, await db.Database.ExecuteSqlAsync($"UPDATE [UserAccounts] SET [Role] = {(int)Role.Dentist} WHERE [Id] = {second}"));   // one of two may go
        Assert.Equal(51207, (await SqlRefusalAsync($"UPDATE [UserAccounts] SET [IsDisabled] = 1 WHERE [Role] = {(int)Role.Admin}")).Number);
    }

    [Fact]
    public async Task Raw_sql_that_removes_both_of_two_administrators_in_one_statement_is_refused_and_one_that_removes_one_is_not()
    {
        var a = await NewUserAsync(Role.Admin);
        var b = await NewUserAsync(Role.Admin);
        Assert.Equal(51207, (await SqlRefusalAsync($"UPDATE [UserAccounts] SET [Role] = {(int)Role.Billing} WHERE [Id] IN ({a}, {b})")).Number);
        Assert.Equal(2, await AvailableAdminsAsync());
        await using var db = Fixture.CreateContext();
        Assert.Equal(1, await db.Database.ExecuteSqlAsync($"DELETE FROM [UserAccounts] WHERE [Id] = {a}"));
        Assert.Equal(1, await AvailableAdminsAsync());
    }

    [Fact]
    public async Task Ordinary_updates_to_accounts_and_the_first_administrator_bootstrap_are_unaffected()
    {
        await using (var db = Fixture.CreateContext())
        {
            var first = await Service(db).BootstrapFirstAdminAsync($"first-{Guid.NewGuid():N}", "bootstrap-admin-password", IdentityTestHelpers.TestBootstrapSecret, IdentityTestHelpers.TestBootstrapSecret);
            Assert.Equal(Role.Admin, first.Role);
        }
        var user = await NewUserAsync(Role.Dentist);
        await using var db2 = Fixture.CreateContext();
        Assert.Equal(1, await db2.Database.ExecuteSqlAsync($"UPDATE [UserAccounts] SET [FailedLoginAttempts] = 3 WHERE [Id] = {user}"));
        Assert.Equal(1, await AvailableAdminsAsync());
    }

    [Fact]
    public async Task When_the_administrator_guard_is_busy_the_request_waits_five_seconds_then_changes_nothing_and_the_same_request_works_afterwards()
    {
        var a = await NewUserAsync(Role.Admin);
        var b = await NewUserAsync(Role.Admin);
        await using var holder = Fixture.CreateContext();
        await using (var tx = await holder.Database.BeginTransactionAsync())
        {
            await AdminAvailabilityGuard.AcquireAsync(holder, default);                                                // another administrator operation is in progress
            var started = DateTime.UtcNow;
            await using (var db = Fixture.CreateContext()) await Assert.ThrowsAsync<AdminGuardBusyException>(() => Service(db).ChangeRoleAsync(a, Role.Dentist, b));
            Assert.InRange(DateTime.UtcNow - started, TimeSpan.FromSeconds(4.5), TimeSpan.FromSeconds(15));
            await tx.RollbackAsync();
        }
        Assert.Equal((Role.Admin, false), await StateAsync(a));                                                         // nothing changed
        Assert.Contains((await AuditAsync(AuditEventTypes.AccountActionRefused)), r => r.Details.Contains("admin_guard_busy"));
        await using (var db = Fixture.CreateContext()) await Service(db).ChangeRoleAsync(a, Role.Dentist, b);          // retry-safe
        Assert.Equal(Role.Dentist, (await StateAsync(a)).Role);
    }

    // ---------- the target rules ----------

    private static readonly Permission[] Holds = [Permission.ManageAccountStatus, Permission.RevokeSessions];

    [Fact]
    public async Task A_non_administrator_holder_can_never_act_on_an_administrator()
    {
        var holder = await NewUserAsync(Role.OfficeManager);
        var admin = await NewUserAsync(Role.Admin);
        var policy = WithOfficeManagerHolding(Holds);
        foreach (var act in new Func<AccountService, Task>[]
        {
            s => s.SetAccountEnabledAsync(admin, false, holder),
            s => s.SetAccountEnabledAsync(admin, true, holder),
            s => s.SetSessionTimeoutAsync(admin, 60, holder),
            s => s.RevokeAllSessionsAsync(admin, holder),
        })
        {
            await using var db = Fixture.CreateContext();
            var e = await Assert.ThrowsAsync<AccountTargetNotAllowedException>(() => act(Service(db, policy)));
            Assert.Equal(AccountTargetRules.TargetIsAdmin, e.Rule);
        }
        Assert.Equal((Role.Admin, false), await StateAsync(admin));
        var refusals = await AuditAsync(AuditEventTypes.AccountActionRefused);
        Assert.Equal(4, refusals.Count);
        Assert.All(refusals, r => { Assert.Contains(AccountTargetRules.TargetIsAdmin, r.Details); Assert.Equal(holder, r.By); });
    }

    [Theory]
    [InlineData(Role.Dentist)]
    [InlineData(Role.Hygienist)]
    [InlineData(Role.Assistant)]
    public async Task A_holder_cannot_act_on_a_role_that_holds_a_permission_the_holder_lacks(Role targetRole)
    {
        var holder = await NewUserAsync(Role.OfficeManager);
        var target = await NewUserAsync(targetRole);
        Assert.False(PermissionMatrix.PermissionsFor(targetRole).IsSubsetOf(PermissionMatrix.PermissionsFor(Role.OfficeManager).Concat(Holds).ToHashSet()));
        await using var db = Fixture.CreateContext();
        var e = await Assert.ThrowsAsync<AccountTargetNotAllowedException>(() => Service(db, WithOfficeManagerHolding(Holds)).SetAccountEnabledAsync(target, false, holder));
        Assert.Equal(AccountTargetRules.TargetExceedsActor, e.Rule);
        Assert.Equal((targetRole, false), await StateAsync(target));
    }

    [Theory]
    [InlineData(Role.FrontDesk)]
    [InlineData(Role.Billing)]
    [InlineData(Role.OfficeManager)]
    [InlineData(Role.Unassigned)]
    public async Task A_holder_can_act_on_a_role_whose_permissions_are_a_subset_of_their_own_including_a_peer_and_an_unassigned_account(Role targetRole)
    {
        var holder = await NewUserAsync(Role.OfficeManager);
        var target = await NewUserAsync(targetRole);
        var policy = WithOfficeManagerHolding(Holds);
        await using (var db = Fixture.CreateContext()) await Service(db, policy).SetAccountEnabledAsync(target, false, holder);
        Assert.True((await StateAsync(target)).Disabled);
        await using (var db = Fixture.CreateContext()) await Service(db, policy).SetAccountEnabledAsync(target, true, holder);
        await using (var db = Fixture.CreateContext()) await Service(db, policy).SetSessionTimeoutAsync(target, 45, holder);
        await using (var db = Fixture.CreateContext()) await Service(db, policy).RevokeAllSessionsAsync(target, holder);
        Assert.False((await StateAsync(target)).Disabled);
        var allowed = (await AuditAsync(AuditEventTypes.AccountDisabled)).Concat(await AuditAsync(AuditEventTypes.AccountEnabled)).Concat(await AuditAsync(AuditEventTypes.SessionsRevoked)).ToList();
        Assert.All(allowed, a => Assert.Contains("Performed by a OfficeManager under the account target rules.", a.Details));
    }

    [Fact]
    public async Task A_holder_may_not_disable_their_own_account_but_may_revoke_their_own_sessions()
    {
        var holder = await NewUserAsync(Role.OfficeManager);
        var policy = WithOfficeManagerHolding(Holds);
        await using (var db = Fixture.CreateContext())
        {
            var e = await Assert.ThrowsAsync<AccountTargetNotAllowedException>(() => Service(db, policy).SetAccountEnabledAsync(holder, false, holder));
            Assert.Equal(AccountTargetRules.SelfDisable, e.Rule);
        }
        Assert.False((await StateAsync(holder)).Disabled);
        await using var db2 = Fixture.CreateContext();
        await Service(db2, policy).RevokeAllSessionsAsync(holder, holder);
    }

    [Fact]
    public async Task An_administrator_actor_is_not_restricted_by_the_target_rules()
    {
        var admin = await NewUserAsync(Role.Admin);
        var other = await NewUserAsync(Role.Admin);
        var dentist = await NewUserAsync(Role.Dentist);
        await using (var db = Fixture.CreateContext()) await Service(db).SetAccountEnabledAsync(dentist, false, admin);
        await using (var db = Fixture.CreateContext()) await Service(db).SetAccountEnabledAsync(other, false, admin);     // another administrator: allowed while one remains
        await using (var db = Fixture.CreateContext()) await Service(db).RevokeAllSessionsAsync(other, admin);
        Assert.True((await StateAsync(dentist)).Disabled);
    }

    [Fact]
    public async Task A_change_to_the_holders_effective_grants_changes_the_outcome_on_the_very_next_request()
    {
        var holder = await NewUserAsync(Role.OfficeManager);
        var dentist = await NewUserAsync(Role.Dentist);
        var source = new ControlledPolicySource();
        foreach (var p in Holds) source.Overrides.Add(PolicyTestSupport.Override("OfficeManager", p.ToString(), "Grant"));
        source.Version = 1;
        var store = PolicyTestSupport.Store(source);
        await using (var db = Fixture.CreateContext())
            await Assert.ThrowsAsync<AccountTargetNotAllowedException>(() => Service(db, new EffectivePermissionService(store)).SetAccountEnabledAsync(dentist, false, holder));

        var missing = PermissionMatrix.PermissionsFor(Role.Dentist).Except(PermissionMatrix.PermissionsFor(Role.OfficeManager)).Except(Holds).ToList();
        Assert.NotEmpty(missing);
        foreach (var p in missing) source.Overrides.Add(PolicyTestSupport.Override("OfficeManager", p.ToString(), "Grant"));
        source.Version = 2;                                                                                            // the office manager now holds everything a dentist holds
        await using (var db = Fixture.CreateContext()) await Service(db, new EffectivePermissionService(store)).SetAccountEnabledAsync(dentist, false, holder);
        Assert.True((await StateAsync(dentist)).Disabled);

        source.Overrides.RemoveAll(o => missing.Any(m => m.ToString() == o.Permission));
        source.Version = 3;                                                                                            // revert: refused again, at once
        await using (var db = Fixture.CreateContext())
            await Assert.ThrowsAsync<AccountTargetNotAllowedException>(() => Service(db, new EffectivePermissionService(store)).SetAccountEnabledAsync(dentist, true, holder));
    }

    [Fact]
    public async Task Concurrent_and_repeated_refused_requests_cannot_get_past_a_rule()
    {
        var holder = await NewUserAsync(Role.OfficeManager);
        var admin = await NewUserAsync(Role.Admin);
        var dentist = await NewUserAsync(Role.Dentist);
        var policy = WithOfficeManagerHolding(Holds);
        var calls = Enumerable.Range(0, 10).Select<int, Func<AlveraDbContext, Task>>(i => i % 2 == 0
            ? db => Service(db, policy).SetAccountEnabledAsync(admin, false, holder)
            : db => Service(db, policy).SetAccountEnabledAsync(dentist, false, holder)).ToArray();
        var results = await RunTogetherAsync(calls);
        Assert.All(results, r => Assert.IsType<AccountTargetNotAllowedException>(r));
        Assert.Equal((Role.Admin, false), await StateAsync(admin));
        Assert.Equal((Role.Dentist, false), await StateAsync(dentist));
        Assert.Equal(10, (await AuditAsync(AuditEventTypes.AccountActionRefused)).Count);
    }

    [Fact]
    public async Task A_missing_target_is_still_not_found_and_the_audit_of_a_refusal_holds_ids_and_the_rule_only()
    {
        var holder = await NewUserAsync(Role.OfficeManager);
        var admin = await NewUserAsync(Role.Admin);
        await using (var db = Fixture.CreateContext()) await Assert.ThrowsAsync<AccountNotFoundException>(() => Service(db).SetAccountEnabledAsync(Guid.NewGuid(), false, holder));
        await using (var db = Fixture.CreateContext()) await Assert.ThrowsAsync<AccountTargetNotAllowedException>(() => Service(db, WithOfficeManagerHolding(Holds)).SetAccountEnabledAsync(admin, false, holder));
        await using var check = Fixture.CreateContext();
        var entry = await check.AuditLogEntries.AsNoTracking().SingleAsync(a => a.EventType == AuditEventTypes.AccountActionRefused);
        Assert.Equal((admin, holder, "UserAccount"), (entry.TargetUserAccountId, entry.PerformedByUserAccountId, entry.EntityType));
        Assert.Equal("An account action by a OfficeManager was refused: target_is_admin.", entry.Details);
    }
}
