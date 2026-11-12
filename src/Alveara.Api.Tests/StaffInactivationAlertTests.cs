using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Alveara.Api.Architecture.Configuration;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Notifications;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N020 (F-021; migration decision Option A): a staff inactivation that cannot disable the linked login sends one Urgent in-app notification to every enabled
/// Admin and OfficeManager account, only after the durable failure record exists and keyed on it (record before notify); repeated sends create at most one per
/// recipient; and when the failure record itself cannot be written, no notification is created and the refusal and the log carry the correlation id.
/// Real SQL Server database.
/// </summary>
public class StaffInactivationAlertTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private static readonly IPracticeClock Clock = new PracticeClock(TimeZoneInfo.FindSystemTimeZoneById("America/Chicago"));

    public Task InitializeAsync() => _fixture.InitializeAsync();
    public Task DisposeAsync() => _fixture.DisposeAsync();

    /// <summary>Makes the failure record's save fail, as if the audit store were unavailable at that moment; every other save goes through.</summary>
    private sealed class FailTheFailureRecord : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<AuditLogEntry>()
                .Any(e => e.State == EntityState.Added && e.Entity.EventType == ConfigurationAuditEvents.StaffInactivationFailed))
                throw new TimeoutException("forced: the audit store did not answer");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private static StaffInactivationService ServiceOn(AlveraDbContext db, Microsoft.Extensions.Logging.ILogger<StaffInactivationService>? log = null) =>
        new(db, new StaffProviderService(db, Clock), IdentityTestHelpers.CreateAccountService(db),
            new InAppStaffInactivationAlert(db, Clock, NullLogger<InAppStaffInactivationAlert>.Instance), log ?? NullLogger<StaffInactivationService>.Instance);

    /// <summary>The only administrator, linked to a staff profile (so inactivating it is refused as the last administrator), plus the other recipients and non-recipients.</summary>
    private async Task<(UserAccount Admin, UserAccount Manager, UserAccount Dentist, Guid DisabledManager, StaffProfile Profile)> SeedAsync()
    {
        await using var db = _fixture.CreateContext();
        var accounts = IdentityTestHelpers.CreateAccountService(db);
        var admin = await IdentityTestHelpers.BootstrapAdminAsync(accounts);
        var manager = await IdentityTestHelpers.RegisterEnabledAsync(accounts, db, $"om-{Guid.NewGuid():N}", "password-1234", Role.OfficeManager, admin);
        var dentist = await IdentityTestHelpers.RegisterEnabledAsync(accounts, db, $"dr-{Guid.NewGuid():N}", "password-1234", Role.Dentist, admin);
        var disabledManager = Guid.NewGuid();
        db.UserAccounts.Add(new UserAccount { Id = disabledManager, Username = $"om-off-{Guid.NewGuid():N}", Role = Role.OfficeManager, IsDisabled = true, CreatedAtUtc = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        var profile = await new StaffProviderService(db, Clock).CreateStaffAsync("Only Admin Person", "Owner", admin.Id, admin.Id, default);
        return (admin, manager, dentist, disabledManager, profile);
    }

    private async Task<List<Notification>> AlertsAsync()
    {
        await using var db = _fixture.CreateContext();
        return await db.Notifications.AsNoTracking().Where(n => n.Kind == NotificationKinds.StaffInactivationFailed).ToListAsync();
    }

    private async Task<List<AuditLogEntry>> FailureRecordsAsync(Guid profileId)
    {
        await using var db = _fixture.CreateContext();
        return await db.AuditLogEntries.AsNoTracking()
            .Where(a => a.TargetUserAccountId == profileId && a.EventType == ConfigurationAuditEvents.StaffInactivationFailed).ToListAsync();
    }

    [Fact]
    public async Task A_failed_inactivation_alerts_each_enabled_admin_and_office_manager_once_as_Urgent_keyed_on_the_failure_record()
    {
        var (admin, manager, dentist, disabledManager, profile) = await SeedAsync();
        await using (var db = _fixture.CreateContext())
        {
            var refused = await Assert.ThrowsAsync<ConfigurationException>(() => ServiceOn(db).InactivateAsync(profile.Id, Convert.ToBase64String(profile.RowVersion), admin.Id, default));
            Assert.Equal("staff_login_last_admin", refused.Code);
            Assert.DoesNotContain("could not be recorded", refused.Message);
        }

        var record = Assert.Single(await FailureRecordsAsync(profile.Id));
        var alerts = await AlertsAsync();
        Assert.Equal(new[] { admin.Id, manager.Id }.OrderBy(x => x), alerts.Select(a => a.RecipientUserId).OrderBy(x => x));   // the acting administrator included
        Assert.DoesNotContain(alerts, a => a.RecipientUserId == dentist.Id || a.RecipientUserId == disabledManager);
        Assert.All(alerts, a =>
        {
            Assert.Equal(NotificationPriorities.Urgent, a.Priority);
            Assert.Equal(InAppStaffInactivationAlert.SourceKeyFor(record.Id), a.SourceKey);                              // keyed on the durable record
            Assert.Null(a.RelatedPatientId);
            Assert.Null(a.MessageId);
            Assert.Null(a.RelatedDocumentId);
        });
    }

    [Fact]
    public async Task Sending_the_alert_again_for_the_same_recorded_failure_creates_nothing_new()
    {
        var (admin, _, _, _, profile) = await SeedAsync();
        await using (var db = _fixture.CreateContext())
            await Assert.ThrowsAsync<ConfigurationException>(() => ServiceOn(db).InactivateAsync(profile.Id, Convert.ToBase64String(profile.RowVersion), admin.Id, default));
        var record = Assert.Single(await FailureRecordsAsync(profile.Id));
        var before = (await AlertsAsync()).Count;
        Assert.Equal(2, before);

        var failure = new StaffInactivationFailure(profile.Id, profile.DisplayName, admin.Id, admin.Id, nameof(LastAdminException), "retry-test", record.Id);
        for (var i = 0; i < 2; i++)
        {
            await using var db = _fixture.CreateContext();
            await new InAppStaffInactivationAlert(db, Clock, NullLogger<InAppStaffInactivationAlert>.Instance).SendAsync(failure, default);
        }
        Assert.Equal(before, (await AlertsAsync()).Count);
    }

    [Fact]
    public async Task When_the_failure_record_cannot_be_written_no_alert_is_created_and_the_refusal_and_log_carry_the_correlation_id()
    {
        var (admin, _, _, _, profile) = await SeedAsync();
        var log = new CapturingLogger<StaffInactivationService>();
        using var activity = new Activity("alv-n020-record-fails").Start();
        var correlation = activity.Id!;

        var options = new DbContextOptionsBuilder<AlveraDbContext>().UseSqlServer(_fixture.ConnectionString).AddInterceptors(new FailTheFailureRecord()).Options;
        await using (var db = new AlveraDbContext(options))
        {
            var refused = await Assert.ThrowsAsync<ConfigurationException>(() => ServiceOn(db, log).InactivateAsync(profile.Id, Convert.ToBase64String(profile.RowVersion), admin.Id, default));
            Assert.Equal("staff_login_last_admin", refused.Code);                                                        // the outcome is still the true one: nothing changed
            Assert.Contains("could not be recorded", refused.Message);
            Assert.Contains(correlation, refused.Message);                                                               // the reference the person can give
        }

        Assert.Empty(await FailureRecordsAsync(profile.Id));
        Assert.Empty(await AlertsAsync());                                                                               // never the only evidence
        var line = Assert.Single(log.Messages, m => m.StartsWith("staff_inactivation_failure_not_recorded"));
        Assert.Contains($"correlation={correlation}", line);
        Assert.Contains("error_class=TimeoutException", line);
        Assert.Contains("alert=not_created", line);
        await using var check = _fixture.CreateContext();
        Assert.True((await check.StaffProfiles.AsNoTracking().SingleAsync(s => s.Id == profile.Id)).IsActive);
        Assert.False((await check.UserAccounts.AsNoTracking().SingleAsync(a => a.Id == admin.Id)).IsDisabled);
    }
}
