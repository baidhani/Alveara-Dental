using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Configuration;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N020 (F-021, PKG-03): inactivating a staff profile disables its linked login and ends its sessions in one transaction; reactivation never re-enables the
/// login; a refusal or failure of the account step rolls everything back, leaves a durable failure record and attempts the alert; an administrator can unlink a
/// login from an inactive profile with a reason; an inactive profile's link cannot be swapped by an ordinary edit. Real SQL Server database.
/// </summary>
public class StaffInactivationServiceTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private static readonly IPracticeClock Clock = new PracticeClock(TimeZoneInfo.FindSystemTimeZoneById("America/Chicago"));

    public Task InitializeAsync() => _fixture.InitializeAsync();
    public Task DisposeAsync() => _fixture.DisposeAsync();

    private sealed class RecordingAlert : IStaffInactivationAlert
    {
        public List<StaffInactivationFailure> Sent { get; } = [];
        public bool Throw { get; init; }
        public Task SendAsync(StaffInactivationFailure failure, CancellationToken ct)
        {
            Sent.Add(failure);
            if (Throw) throw new InvalidOperationException("alert channel down");
            return Task.CompletedTask;
        }
    }

    private (AlveraDbContext Db, StaffInactivationService Service, StaffProviderService Staff) NewService(IStaffInactivationAlert alert)
    {
        var db = _fixture.CreateContext();
        var staff = new StaffProviderService(db, Clock);
        var accounts = IdentityTestHelpers.CreateAccountService(db);
        return (db, new StaffInactivationService(db, staff, accounts, alert, NullLogger<StaffInactivationService>.Instance), staff);
    }

    /// <summary>An administrator (the actor) and an enabled working account with a staff profile linked to it.</summary>
    private async Task<(UserAccount Admin, UserAccount Login, StaffProfile Profile)> SeedLinkedAsync(Role role = Role.Dentist)
    {
        await using var db = _fixture.CreateContext();
        var accounts = IdentityTestHelpers.CreateAccountService(db);
        var admin = await IdentityTestHelpers.BootstrapAdminAsync(accounts);
        var login = await IdentityTestHelpers.RegisterEnabledAsync(accounts, db, $"staff-{Guid.NewGuid():N}", "password-1234", role, admin);
        var profile = await new StaffProviderService(db, Clock).CreateStaffAsync($"Dr. {Guid.NewGuid():N}"[..20], "Dentist", login.Id, admin.Id, default);
        return (admin, login, profile);
    }

    private async Task<UserAccount> AccountAsync(Guid id)
    {
        await using var db = _fixture.CreateContext();
        return await db.UserAccounts.AsNoTracking().SingleAsync(a => a.Id == id);
    }

    private async Task<StaffProfile> ProfileAsync(Guid id)
    {
        await using var db = _fixture.CreateContext();
        return await db.StaffProfiles.AsNoTracking().SingleAsync(s => s.Id == id);
    }

    private async Task<List<string>> AuditEventsForAsync(Guid entityId)
    {
        await using var db = _fixture.CreateContext();
        return await db.AuditLogEntries.AsNoTracking().Where(a => a.TargetUserAccountId == entityId).OrderBy(a => a.TimestampUtc).Select(a => a.EventType).ToListAsync();
    }

    private static string Version(byte[] v) => Convert.ToBase64String(v);

    [Fact]
    public async Task Inactivating_a_linked_profile_disables_the_login_rotates_its_stamp_and_records_both_audit_entries()
    {
        var (admin, login, profile) = await SeedLinkedAsync();
        var stampBefore = login.SecurityStamp;
        var alert = new RecordingAlert();
        var (db, service, _) = NewService(alert);
        await using (db)
        {
            var result = await service.InactivateAsync(profile.Id, Version(profile.RowVersion), admin.Id, default);
            Assert.False(result.IsActive);
        }

        var account = await AccountAsync(login.Id);
        Assert.True(account.IsDisabled);
        Assert.NotEqual(stampBefore, account.SecurityStamp);                                       // every existing cookie for this login now fails the stamp check (401)
        Assert.False((await ProfileAsync(profile.Id)).IsActive);
        Assert.Contains(AuditEventTypes.AccountDisabled, await AuditEventsForAsync(login.Id));
        Assert.Contains(ConfigurationAuditEvents.StaffLoginDisabledOnInactivation, await AuditEventsForAsync(profile.Id));
        Assert.Empty(alert.Sent);
    }

    [Fact]
    public async Task Reactivating_the_profile_never_re_enables_the_login()
    {
        var (admin, login, profile) = await SeedLinkedAsync();
        var (db, service, staff) = NewService(new RecordingAlert());
        await using (db)
        {
            var inactive = await service.InactivateAsync(profile.Id, Version(profile.RowVersion), admin.Id, default);
            var active = await staff.SetStaffActiveAsync(profile.Id, true, Version(inactive.RowVersion), admin.Id, default);
            Assert.True(active.IsActive);
        }
        Assert.True((await AccountAsync(login.Id)).IsDisabled);
    }

    [Fact]
    public async Task Inactivating_twice_changes_nothing_the_second_time()
    {
        var (admin, login, profile) = await SeedLinkedAsync();
        var (db, service, _) = NewService(new RecordingAlert());
        await using (db)
        {
            await service.InactivateAsync(profile.Id, Version(profile.RowVersion), admin.Id, default);
        }
        var stamp = (await AccountAsync(login.Id)).SecurityStamp;
        var audits = (await AuditEventsForAsync(profile.Id)).Count;

        var (db2, service2, _) = NewService(new RecordingAlert());
        await using (db2)
        {
            var again = await service2.InactivateAsync(profile.Id, Version(profile.RowVersion), admin.Id, default);
            Assert.False(again.IsActive);
        }
        Assert.Equal(stamp, (await AccountAsync(login.Id)).SecurityStamp);
        Assert.Equal(audits, (await AuditEventsForAsync(profile.Id)).Count);
    }

    [Fact]
    public async Task A_profile_without_a_login_is_inactivated_without_touching_any_account()
    {
        var (admin, _, _) = await SeedLinkedAsync();
        StaffProfile plain;
        await using (var setup = _fixture.CreateContext())
            plain = await new StaffProviderService(setup, Clock).CreateStaffAsync("No Login Person", null, null, admin.Id, default);

        var (db, service, _) = NewService(new RecordingAlert());
        await using (db)
        {
            var result = await service.InactivateAsync(plain.Id, Version(plain.RowVersion), admin.Id, default);
            Assert.False(result.IsActive);
        }
        Assert.DoesNotContain(ConfigurationAuditEvents.StaffLoginDisabledOnInactivation, await AuditEventsForAsync(plain.Id));
    }

    [Fact]
    public async Task When_the_login_is_the_last_administrator_nothing_changes_a_failure_is_recorded_and_the_alert_is_attempted()
    {
        await using var setup = _fixture.CreateContext();
        var accounts = IdentityTestHelpers.CreateAccountService(setup);
        var admin = await IdentityTestHelpers.BootstrapAdminAsync(accounts);                         // the only administrator
        var profile = await new StaffProviderService(setup, Clock).CreateStaffAsync("Only Admin Person", "Owner", admin.Id, admin.Id, default);

        var alert = new RecordingAlert();
        var (db, service, _) = NewService(alert);
        await using (db)
        {
            var refused = await Assert.ThrowsAsync<ConfigurationException>(() => service.InactivateAsync(profile.Id, Version(profile.RowVersion), admin.Id, default));
            Assert.Equal("staff_login_last_admin", refused.Code);
            Assert.Equal(409, refused.StatusCode);
        }

        Assert.True((await ProfileAsync(profile.Id)).IsActive);                                      // rolled back: the profile is still active
        Assert.False((await AccountAsync(admin.Id)).IsDisabled);                                     // and the login is untouched
        var events = await AuditEventsForAsync(profile.Id);
        Assert.Contains(ConfigurationAuditEvents.StaffInactivationFailed, events);
        Assert.DoesNotContain(ConfigurationAuditEvents.StaffLoginDisabledOnInactivation, events);    // the record never claims it happened
        var failure = Assert.Single(alert.Sent);
        Assert.Equal(nameof(LastAdminException), failure.ErrorClass);
        Assert.Equal(admin.Id, failure.LinkedAccountId);
        await using var check = _fixture.CreateContext();                                            // record before notify: the alert names the stored failure record
        var recordId = await check.AuditLogEntries.AsNoTracking()
            .Where(a => a.TargetUserAccountId == profile.Id && a.EventType == ConfigurationAuditEvents.StaffInactivationFailed).Select(a => a.Id).SingleAsync();
        Assert.Equal(recordId, failure.FailureRecordId);
    }

    [Fact]
    public async Task A_failing_alert_does_not_change_the_outcome_or_hide_the_failure_record()
    {
        await using var setup = _fixture.CreateContext();
        var accounts = IdentityTestHelpers.CreateAccountService(setup);
        var admin = await IdentityTestHelpers.BootstrapAdminAsync(accounts);
        var profile = await new StaffProviderService(setup, Clock).CreateStaffAsync("Alert Down Person", null, admin.Id, admin.Id, default);

        var (db, service, _) = NewService(new RecordingAlert { Throw = true });
        await using (db)
        {
            var refused = await Assert.ThrowsAsync<ConfigurationException>(() => service.InactivateAsync(profile.Id, Version(profile.RowVersion), admin.Id, default));
            Assert.Equal("staff_login_last_admin", refused.Code);
        }
        Assert.Contains(ConfigurationAuditEvents.StaffInactivationFailed, await AuditEventsForAsync(profile.Id));
    }

    [Fact]
    public async Task A_stale_version_is_an_ordinary_conflict_that_changes_nothing_and_is_not_a_failure()
    {
        var (admin, login, profile) = await SeedLinkedAsync();
        await using (var other = _fixture.CreateContext())
            await new StaffProviderService(other, Clock).UpdateStaffAsync(profile.Id, profile.DisplayName, "Changed elsewhere", login.Id, Version(profile.RowVersion), admin.Id, default);

        var alert = new RecordingAlert();
        var (db, service, _) = NewService(alert);
        await using (db)
        {
            await Assert.ThrowsAsync<ConcurrencyConflictException>(() => service.InactivateAsync(profile.Id, Version(profile.RowVersion), admin.Id, default));
        }
        Assert.True((await ProfileAsync(profile.Id)).IsActive);
        Assert.False((await AccountAsync(login.Id)).IsDisabled);                                     // the account step was rolled back too
        Assert.DoesNotContain(ConfigurationAuditEvents.StaffInactivationFailed, await AuditEventsForAsync(profile.Id));
        Assert.Empty(alert.Sent);
    }

    [Fact]
    public async Task An_inactive_profile_s_login_link_cannot_be_swapped_by_an_ordinary_edit()
    {
        var (admin, _, profile) = await SeedLinkedAsync();
        UserAccount other;
        await using (var setup = _fixture.CreateContext())
            other = await IdentityTestHelpers.RegisterEnabledAsync(IdentityTestHelpers.CreateAccountService(setup), setup, $"other-{Guid.NewGuid():N}", "password-1234", Role.Dentist, admin);

        var (db, service, staff) = NewService(new RecordingAlert());
        await using (db)
        {
            var inactive = await service.InactivateAsync(profile.Id, Version(profile.RowVersion), admin.Id, default);
            var refused = await Assert.ThrowsAsync<ConfigurationException>(() =>
                staff.UpdateStaffAsync(profile.Id, inactive.DisplayName, inactive.JobTitle, other.Id, Version(inactive.RowVersion), admin.Id, default));
            Assert.Equal("staff_inactive_link", refused.Code);
        }
    }

    [Fact]
    public async Task An_administrator_unlinks_an_inactive_profile_s_login_with_a_reason_and_the_reason_is_kept()
    {
        var (admin, login, profile) = await SeedLinkedAsync();
        var (db, service, staff) = NewService(new RecordingAlert());
        await using (db)
        {
            var inactive = await service.InactivateAsync(profile.Id, Version(profile.RowVersion), admin.Id, default);
            var empty = await Assert.ThrowsAsync<ConfigurationException>(() => staff.UnlinkAccountFromInactiveStaffAsync(profile.Id, "   ", Version(inactive.RowVersion), admin.Id, default));
            Assert.Equal("reason_required", empty.Code);
            var unlinked = await staff.UnlinkAccountFromInactiveStaffAsync(profile.Id, "Left the practice on 2026-11-11", Version(inactive.RowVersion), admin.Id, default);
            Assert.Null(unlinked.UserAccountId);
        }
        await using var check = _fixture.CreateContext();
        var entry = await check.AuditLogEntries.AsNoTracking().SingleAsync(a => a.TargetUserAccountId == profile.Id && a.EventType == ConfigurationAuditEvents.StaffAccountUnlinked);
        Assert.Equal("Left the practice on 2026-11-11", entry.Reason);
        Assert.True((await AccountAsync(login.Id)).IsDisabled);                                      // unlinking does not change the account
    }

    [Fact]
    public async Task An_active_profile_s_login_cannot_be_unlinked_through_the_reasoned_path()
    {
        var (admin, _, profile) = await SeedLinkedAsync();
        var (db, _, staff) = NewService(new RecordingAlert());
        await using (db)
        {
            var refused = await Assert.ThrowsAsync<ConfigurationException>(() => staff.UnlinkAccountFromInactiveStaffAsync(profile.Id, "reason", Version(profile.RowVersion), admin.Id, default));
            Assert.Equal("staff_active", refused.Code);
        }
    }
}
