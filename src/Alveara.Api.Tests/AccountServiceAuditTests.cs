using Alveara.Api.Architecture.Identity;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>ALV-001-C01's "security administration changes are audited" requirement, covering the
/// two account-status events not already exercised by the MFA/role-change/password-reset test
/// files (AccountServiceMfaTests writes AuditEventTypes.MfaEnabled; AccountServiceRoleChangeTests
/// writes RoleChanged; AccountServicePasswordResetTests writes PasswordResetIssued/PasswordReset).</summary>
public class AccountServiceAuditTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();
    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task Disabling_an_account_writes_an_AccountDisabled_audit_entry_naming_the_actor()
    {
        await using var db = _fixture.CreateContext();
        var service = IdentityTestHelpers.CreateAccountService(db);
        var admin = await IdentityTestHelpers.GetOrBootstrapAdminAsync(service, db);
        var target = await IdentityTestHelpers.RegisterEnabledAsync(service, db, $"user-{Guid.NewGuid():N}", "password", Role.Assistant, admin);

        await service.SetAccountEnabledAsync(target.Id, enabled: false, admin.Id);

        var auditEntry = db.AuditLogEntries
            .Single(a => a.TargetUserAccountId == target.Id && a.EventType == AuditEventTypes.AccountDisabled);
        Assert.Equal(admin.Id, auditEntry.PerformedByUserAccountId);
    }

    [Fact]
    public async Task Re_enabling_a_disabled_account_writes_an_AccountEnabled_audit_entry_naming_the_actor()
    {
        await using var db = _fixture.CreateContext();
        var service = IdentityTestHelpers.CreateAccountService(db);
        var admin = await IdentityTestHelpers.GetOrBootstrapAdminAsync(service, db);
        var target = await IdentityTestHelpers.RegisterEnabledAsync(service, db, $"user-{Guid.NewGuid():N}", "password", Role.Assistant, admin);
        await service.SetAccountEnabledAsync(target.Id, enabled: false, admin.Id);

        await service.SetAccountEnabledAsync(target.Id, enabled: true, admin.Id);

        // RegisterEnabledAsync itself already wrote one AccountEnabled entry at setup time, so
        // this looks at the latest one - the one the re-enable above actually produced.
        var auditEntry = db.AuditLogEntries
            .Where(a => a.TargetUserAccountId == target.Id && a.EventType == AuditEventTypes.AccountEnabled)
            .OrderByDescending(a => a.TimestampUtc)
            .First();
        Assert.Equal(admin.Id, auditEntry.PerformedByUserAccountId);
    }

    [Fact]
    public async Task Toggling_enabled_to_its_current_value_is_a_no_op_and_writes_no_extra_audit_entry()
    {
        await using var db = _fixture.CreateContext();
        var service = IdentityTestHelpers.CreateAccountService(db);
        var admin = await IdentityTestHelpers.GetOrBootstrapAdminAsync(service, db);
        var target = await IdentityTestHelpers.RegisterEnabledAsync(service, db, $"user-{Guid.NewGuid():N}", "password", Role.Assistant, admin);

        var countBefore = db.AuditLogEntries.Count(a => a.TargetUserAccountId == target.Id && a.EventType == AuditEventTypes.AccountEnabled);
        await service.SetAccountEnabledAsync(target.Id, enabled: true, admin.Id); // already enabled
        var countAfter = db.AuditLogEntries.Count(a => a.TargetUserAccountId == target.Id && a.EventType == AuditEventTypes.AccountEnabled);

        Assert.Equal(countBefore, countAfter);
    }

    [Fact]
    public async Task Revoking_sessions_writes_a_SessionsRevoked_audit_entry_naming_the_actor()
    {
        await using var db = _fixture.CreateContext();
        var service = IdentityTestHelpers.CreateAccountService(db);
        var admin = await IdentityTestHelpers.GetOrBootstrapAdminAsync(service, db);
        var target = await IdentityTestHelpers.RegisterEnabledAsync(service, db, $"user-{Guid.NewGuid():N}", "password", Role.Assistant, admin);

        await service.RevokeAllSessionsAsync(target.Id, admin.Id);

        var auditEntry = db.AuditLogEntries
            .Single(a => a.TargetUserAccountId == target.Id && a.EventType == AuditEventTypes.SessionsRevoked);
        Assert.Equal(admin.Id, auditEntry.PerformedByUserAccountId);
    }
}
