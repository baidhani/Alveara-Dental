using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Configuration;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N003: staff/provider configuration, account linkage, inactivation history, availability,
/// and blocked time (practice-local time semantics), against a real SQL Server database.
/// </summary>
public class StaffProviderServiceTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private static readonly IPracticeClock Clock = new PracticeClock(TimeZoneInfo.FindSystemTimeZoneById("America/Chicago"));
    private readonly Guid _actor = Guid.NewGuid();

    public Task InitializeAsync() => _fixture.InitializeAsync();
    public Task DisposeAsync() => _fixture.DisposeAsync();

    private (AlveraDbContext Db, StaffProviderService Service) NewService()
    {
        var db = _fixture.CreateContext();
        return (db, new StaffProviderService(db, Clock));
    }

    private static string Version(byte[] v) => Convert.ToBase64String(v);

    private async Task<UserAccount> SeedAccountAsync(bool enabled)
    {
        await using var db = _fixture.CreateContext();
        var accounts = IdentityTestHelpers.CreateAccountService(db);
        var account = await accounts.RegisterAsync($"user-{Guid.NewGuid():N}", "password-1234");
        if (enabled) account = await accounts.SetAccountEnabledAsync(account.Id, true, _actor);
        return account;
    }

    private async Task<(Guid StaffId, Guid ProviderId)> SeedProviderAsync(string name = "Dr. Rivera")
    {
        var (db, service) = NewService();
        await using (db)
        {
            var staff = await service.CreateStaffAsync(name, "Dentist", null, _actor, default);
            var provider = await service.CreateProviderAsync(staff.Id, "General dentistry", _actor, default);
            return (staff.Id, provider.Id);
        }
    }

    // ---------- Distinct but linkable identities ----------

    [Fact]
    public async Task Account_staff_and_provider_are_distinct_records_that_link_explicitly_and_optionally()
    {
        var account = await SeedAccountAsync(enabled: true);
        var (db, service) = NewService();
        await using (db)
        {
            var linkedStaff = await service.CreateStaffAsync("Dr. Linked", null, account.Id, _actor, default);
            var unlinkedStaff = await service.CreateStaffAsync("Hygienist Unlinked", null, null, _actor, default);
            var provider = await service.CreateProviderAsync(linkedStaff.Id, "Dentistry", _actor, default);

            Assert.Equal(account.Id, linkedStaff.UserAccountId);
            Assert.Null(unlinkedStaff.UserAccountId); // a staff member need not have a login
            Assert.Equal(linkedStaff.Id, provider.StaffProfileId);
            Assert.Equal(3, new[] { account.Id, linkedStaff.Id, provider.Id }.Distinct().Count()); // three distinct identities
        }
    }

    [Fact]
    public async Task Linking_a_staff_profile_to_a_missing_disabled_or_already_linked_account_is_rejected()
    {
        var enabled = await SeedAccountAsync(enabled: true);
        var disabled = await SeedAccountAsync(enabled: false);
        var (db, service) = NewService();
        await using (db)
        {
            var missing = await Assert.ThrowsAsync<ConfigurationException>(() => service.CreateStaffAsync("A", null, Guid.NewGuid(), _actor, default));
            Assert.Equal("account_not_found", missing.Code);

            var inactive = await Assert.ThrowsAsync<ConfigurationException>(() => service.CreateStaffAsync("B", null, disabled.Id, _actor, default));
            Assert.Equal("account_inactive", inactive.Code);

            await service.CreateStaffAsync("C", null, enabled.Id, _actor, default);
            var taken = await Assert.ThrowsAsync<ConfigurationException>(() => service.CreateStaffAsync("D", null, enabled.Id, _actor, default));
            Assert.Equal("account_already_linked", taken.Code);
            Assert.Equal(409, taken.StatusCode);

            // Nothing partial was persisted by the rejected attempts.
            Assert.Equal(1, await db.StaffProfiles.CountAsync());
        }
    }

    [Fact]
    public async Task A_link_to_an_account_that_is_later_disabled_keeps_resolving_and_is_not_silently_severed()
    {
        var account = await SeedAccountAsync(enabled: true);
        Guid staffId;
        var (db, service) = NewService();
        await using (db)
        {
            staffId = (await service.CreateStaffAsync("Dr. Linked", null, account.Id, _actor, default)).Id;
        }

        await using (var accountsDb = _fixture.CreateContext())
        {
            await IdentityTestHelpers.CreateAccountService(accountsDb).SetAccountEnabledAsync(account.Id, false, _actor);
        }

        var (db2, service2) = NewService();
        await using (db2)
        {
            var resolved = await service2.GetStaffAsync(staffId, default);
            Assert.Equal(account.Id, resolved.UserAccountId);

            // Editing something else keeps the existing link without re-validating it as "new".
            var updated = await service2.UpdateStaffAsync(staffId, "Dr. Linked", "Lead", account.Id, Version(resolved.RowVersion), _actor, default);
            Assert.Equal(account.Id, updated.UserAccountId);
            Assert.Equal("Lead", updated.JobTitle);
        }
    }

    [Fact]
    public async Task The_link_picker_offers_enabled_accounts_plus_already_linked_ones_and_says_who_holds_them()
    {
        var free = await SeedAccountAsync(enabled: true);
        var disabledFree = await SeedAccountAsync(enabled: false);
        var linked = await SeedAccountAsync(enabled: true);
        Guid staffId;
        var (db, service) = NewService();
        await using (db)
        {
            staffId = (await service.CreateStaffAsync("Dr. Linked", null, linked.Id, _actor, default)).Id;
        }
        await using (var accountsDb = _fixture.CreateContext())
        {
            await IdentityTestHelpers.CreateAccountService(accountsDb).SetAccountEnabledAsync(linked.Id, false, _actor); // linked, then disabled
        }

        var (db2, service2) = NewService();
        await using (db2)
        {
            var accounts = await service2.ListLinkableAccountsAsync(default);
            Assert.Null(accounts.Single(a => a.Id == free.Id).LinkedStaffId);
            Assert.DoesNotContain(accounts, a => a.Id == disabledFree.Id); // disabled and unlinked: not offered
            var existing = accounts.Single(a => a.Id == linked.Id);          // disabled but linked: still shown so the link is visible
            Assert.Equal(staffId, existing.LinkedStaffId);
            Assert.True(existing.IsDisabled);
        }
    }

    [Fact]
    public async Task Changing_the_account_link_is_audited_as_its_own_event()
    {
        var account = await SeedAccountAsync(enabled: true);
        var (db, service) = NewService();
        await using (db)
        {
            var staff = await service.CreateStaffAsync("Dr. Linked", null, null, _actor, default);
            await service.UpdateStaffAsync(staff.Id, "Dr. Linked", null, account.Id, Version(staff.RowVersion), _actor, default);
            Assert.Contains(await db.AuditLogEntries.Where(a => a.TargetUserAccountId == staff.Id).ToListAsync(),
                a => a.EventType == ConfigurationAuditEvents.StaffAccountLinkChanged && a.EntityType == "StaffProfile");
        }
    }

    [Fact]
    public async Task Staff_display_names_are_unique_so_a_retried_create_cannot_duplicate()
    {
        var (db, service) = NewService();
        await using (db)
        {
            await service.CreateStaffAsync("Dr. Rivera", null, null, _actor, default);
            var ex = await Assert.ThrowsAsync<ConfigurationException>(() => service.CreateStaffAsync("dr. rivera", null, null, _actor, default));
            Assert.Equal("staff_name_taken", ex.Code);
            Assert.Equal(1, await db.StaffProfiles.CountAsync());
        }
    }

    // ---------- Providers + inactivation history ----------

    [Fact]
    public async Task A_provider_needs_an_active_staff_profile_and_each_staff_member_has_at_most_one()
    {
        var (db, service) = NewService();
        await using (db)
        {
            var missing = await Assert.ThrowsAsync<ConfigurationException>(() => service.CreateProviderAsync(Guid.NewGuid(), "Dentistry", _actor, default));
            Assert.Equal("staff_not_found", missing.Code);

            var staff = await service.CreateStaffAsync("Dr. Rivera", null, null, _actor, default);
            await service.CreateProviderAsync(staff.Id, "Dentistry", _actor, default);
            var duplicate = await Assert.ThrowsAsync<ConfigurationException>(() => service.CreateProviderAsync(staff.Id, "Dentistry", _actor, default));
            Assert.Equal("provider_exists", duplicate.Code);
        }
    }

    [Fact]
    public async Task Staff_with_an_active_provider_profile_cannot_be_inactivated_until_the_provider_is()
    {
        var (staffId, providerId) = await SeedProviderAsync();
        var (db, service) = NewService();
        await using (db)
        {
            var staff = await service.GetStaffAsync(staffId, default);
            var refused = await Assert.ThrowsAsync<ConfigurationException>(() => service.SetStaffActiveAsync(staffId, false, Version(staff.RowVersion), _actor, default));
            Assert.Equal("staff_has_active_provider", refused.Code);

            var provider = await service.GetProviderAsync(providerId, default);
            await service.SetProviderActiveAsync(providerId, false, Version(provider.RowVersion), _actor, default);
            await service.SetStaffActiveAsync(staffId, false, Version(staff.RowVersion), _actor, default);
            Assert.False((await service.GetStaffAsync(staffId, default)).IsActive);
        }
    }

    [Fact]
    public async Task An_inactivated_provider_still_resolves_historically_with_its_staff_name_and_availability()
    {
        var (_, providerId) = await SeedProviderAsync("Dr. Historic");
        var (db, service) = NewService();
        await using (db)
        {
            await service.ReplaceCurrentAsync(providerId, [new AvailabilityWindow(DayOfWeek.Tuesday, "09:00", "17:00")], _actor);
            var provider = await service.GetProviderAsync(providerId, default);
            await service.SetProviderActiveAsync(providerId, false, Version(provider.RowVersion), _actor, default);

            var resolved = await service.GetProviderAsync(providerId, default);
            Assert.False(resolved.IsActive);
            Assert.Equal("Dr. Historic", resolved.StaffProfile!.DisplayName);
            Assert.Single(await service.GetAvailabilityAsync(providerId, default)); // history intact
            Assert.DoesNotContain(await service.ListProvidersAsync(false, default), p => p.Id == providerId);
            Assert.Contains(await service.ListProvidersAsync(true, default), p => p.Id == providerId);
        }
    }

    [Fact]
    public async Task A_provider_with_availability_cannot_be_hard_deleted()
    {
        var (_, providerId) = await SeedProviderAsync();
        var (db, service) = NewService();
        await using (db)
        {
            await service.ReplaceCurrentAsync(providerId, [new AvailabilityWindow(DayOfWeek.Monday, "08:00", "12:00")], _actor);
        }

        await using var raw = _fixture.CreateContext();
        raw.ProviderProfiles.Remove(await raw.ProviderProfiles.SingleAsync(p => p.Id == providerId));
        await Assert.ThrowsAsync<DbUpdateException>(() => raw.SaveChangesAsync());

        await using var rawStaff = _fixture.CreateContext();
        rawStaff.StaffProfiles.Remove(await rawStaff.StaffProfiles.SingleAsync());
        await Assert.ThrowsAsync<DbUpdateException>(() => rawStaff.SaveChangesAsync()); // provider references the staff profile
    }

    // ---------- Availability ----------

    [Fact]
    public async Task Weekly_availability_is_persisted_as_practice_local_wall_clock_times()
    {
        var (_, providerId) = await SeedProviderAsync();
        var (db, service) = NewService();
        await using (db)
        {
            var saved = await service.ReplaceCurrentAsync(providerId,
                [new AvailabilityWindow(DayOfWeek.Tuesday, "09:00", "12:00"), new AvailabilityWindow(DayOfWeek.Tuesday, "13:00", "17:30")], _actor);
            Assert.Equal(2, saved.Windows.Count);
            Assert.Equal(new TimeOnly(9, 0), saved.Windows[0].StartLocal);
            Assert.Equal(new TimeOnly(17, 30), saved.Windows[1].EndLocal);
        }

        await using var verify = _fixture.CreateContext();
        var stored = await verify.ProviderWeeklyAvailabilities.Where(a => a.ProviderProfileId == providerId).OrderBy(a => a.StartLocal).ToListAsync();
        Assert.Equal(new TimeOnly(13, 0), stored[1].StartLocal); // a wall-clock time, not an instant
    }

    [Theory]
    [InlineData("17:00", "09:00", "invalid_range")]
    [InlineData("09:00", "09:00", "invalid_range")]
    [InlineData("9am", "17:00", "invalid_time")]
    [InlineData("09:00", "25:00", "invalid_time")]
    public async Task Invalid_availability_ranges_and_times_are_rejected(string start, string end, string expectedCode)
    {
        var (_, providerId) = await SeedProviderAsync();
        var (db, service) = NewService();
        await using (db)
        {
            var ex = await Assert.ThrowsAsync<ConfigurationException>(
                () => service.ReplaceCurrentAsync(providerId, [new AvailabilityWindow(DayOfWeek.Monday, start, end)], _actor));
            Assert.Equal(expectedCode, ex.Code);
        }
    }

    [Fact]
    public async Task Overlapping_windows_are_rejected_and_a_rejected_replacement_leaves_the_existing_schedule_untouched()
    {
        var (_, providerId) = await SeedProviderAsync();
        var (db, service) = NewService();
        await using (db)
        {
            await service.ReplaceCurrentAsync(providerId, [new AvailabilityWindow(DayOfWeek.Monday, "08:00", "12:00")], _actor);

            var ex = await Assert.ThrowsAsync<ConfigurationException>(() => service.ReplaceCurrentAsync(providerId,
                [new AvailabilityWindow(DayOfWeek.Friday, "08:00", "12:00"), new AvailabilityWindow(DayOfWeek.Friday, "11:00", "15:00")], _actor));
            Assert.Equal("availability_overlap", ex.Code);

            await using var verify = _fixture.CreateContext();
            var stored = await verify.ProviderWeeklyAvailabilities.Where(a => a.ProviderProfileId == providerId).ToListAsync();
            var only = Assert.Single(stored);
            Assert.Equal(DayOfWeek.Monday, only.DayOfWeek);
        }
    }

    [Fact]
    public async Task Availability_cannot_be_changed_for_an_inactive_provider()
    {
        var (_, providerId) = await SeedProviderAsync();
        var (db, service) = NewService();
        await using (db)
        {
            var provider = await service.GetProviderAsync(providerId, default);
            await service.SetProviderActiveAsync(providerId, false, Version(provider.RowVersion), _actor, default);
            var ex = await Assert.ThrowsAsync<ConfigurationException>(
                () => service.ReplaceCurrentAsync(providerId, [new AvailabilityWindow(DayOfWeek.Monday, "08:00", "12:00")], _actor));
            Assert.Equal("provider_inactive", ex.Code);
        }
    }

    // ---------- Blocked time + time zone semantics ----------

    [Theory]
    [InlineData("2026-01-15T09:00", "2026-01-15T17:00", 15)] // CST, UTC-6: 09:00 local = 15:00Z
    [InlineData("2026-07-15T09:00", "2026-07-15T17:00", 14)] // CDT, UTC-5: 09:00 local = 14:00Z
    public async Task Blocked_time_is_entered_in_practice_local_time_and_stored_as_the_correct_utc_instant(string start, string end, int expectedUtcStartHour)
    {
        var (_, providerId) = await SeedProviderAsync();
        var (db, service) = NewService();
        await using (db)
        {
            var block = await service.AddBlockedTimeAsync(providerId, DateTime.Parse(start), DateTime.Parse(end), "Conference", _actor, default);
            Assert.Equal(expectedUtcStartHour, block.StartUtc.UtcDateTime.Hour);
            // Round trip back to practice-local wall-clock for display.
            Assert.Equal(9, Clock.ToPracticeLocal(block.StartUtc).Hour);
        }
    }

    [Fact]
    public async Task Blocked_time_in_a_DST_gap_or_overlap_is_rejected_not_guessed()
    {
        var (_, providerId) = await SeedProviderAsync();
        var (db, service) = NewService();
        await using (db)
        {
            // 2026-03-08 02:30 does not exist in America/Chicago (spring forward).
            var gap = await Assert.ThrowsAsync<ConfigurationException>(
                () => service.AddBlockedTimeAsync(providerId, new DateTime(2026, 3, 8, 2, 30, 0), new DateTime(2026, 3, 8, 4, 0, 0), null, _actor, default));
            Assert.Equal("invalid_local_time", gap.Code);

            // 2026-11-01 01:30 occurs twice (fall back).
            var overlap = await Assert.ThrowsAsync<ConfigurationException>(
                () => service.AddBlockedTimeAsync(providerId, new DateTime(2026, 11, 1, 1, 30, 0), new DateTime(2026, 11, 1, 3, 0, 0), null, _actor, default));
            Assert.Equal("invalid_local_time", overlap.Code);

            Assert.Empty(await service.ListBlockedTimeAsync(providerId, default));
        }
    }

    [Fact]
    public async Task Blocked_time_must_end_after_it_starts_and_can_be_removed_with_an_audit_trail()
    {
        var (_, providerId) = await SeedProviderAsync();
        var (db, service) = NewService();
        await using (db)
        {
            var bad = await Assert.ThrowsAsync<ConfigurationException>(
                () => service.AddBlockedTimeAsync(providerId, new DateTime(2026, 5, 4, 10, 0, 0), new DateTime(2026, 5, 4, 9, 0, 0), null, _actor, default));
            Assert.Equal("invalid_range", bad.Code);

            var block = await service.AddBlockedTimeAsync(providerId, new DateTime(2026, 5, 4, 9, 0, 0), new DateTime(2026, 5, 4, 10, 0, 0), "Meeting", _actor, default);
            await service.RemoveBlockedTimeAsync(providerId, block.Id, _actor, default);
            Assert.Empty(await service.ListBlockedTimeAsync(providerId, default));

            var events = (await db.AuditLogEntries.Where(a => a.TargetUserAccountId == providerId).ToListAsync()).Select(a => a.EventType).ToList();
            Assert.Contains(ConfigurationAuditEvents.ProviderBlockedTimeAdded, events);
            Assert.Contains(ConfigurationAuditEvents.ProviderBlockedTimeRemoved, events);
        }
    }

    [Fact]
    public async Task The_same_weekly_window_means_the_same_wall_clock_hours_in_winter_and_summer()
    {
        var (_, providerId) = await SeedProviderAsync();
        var (db, service) = NewService();
        await using (db)
        {
            await service.ReplaceCurrentAsync(providerId, [new AvailabilityWindow(DayOfWeek.Tuesday, "09:00", "17:00")], _actor);
            var scheduling = new SchedulingConfiguration(db, Clock);

            // Tuesday 2026-01-13 09:00 CST = 15:00Z and Tuesday 2026-07-14 09:00 CDT = 14:00Z are both 09:00 local.
            Assert.True((await scheduling.CheckProviderAvailabilityAsync(providerId, DateTimeOffset.Parse("2026-01-13T15:00:00Z"), 30, default)).Available);
            Assert.True((await scheduling.CheckProviderAvailabilityAsync(providerId, DateTimeOffset.Parse("2026-07-14T14:00:00Z"), 30, default)).Available);

            // 14:00Z is 08:00 local in winter (before opening) but 09:00 local in summer - the instant differs, the wall clock does not.
            var winterEarly = await scheduling.CheckProviderAvailabilityAsync(providerId, DateTimeOffset.Parse("2026-01-13T14:00:00Z"), 30, default);
            Assert.False(winterEarly.Available);
            Assert.Equal("outside_working_hours", winterEarly.Reason);
        }
    }
}
