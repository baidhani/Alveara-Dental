using Alveara.Api.Architecture.Identity;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>Each [Fact] gets its own fresh LocalDB database (rather than sharing one via
/// IClassFixture) since every test here bootstraps the one-time first-admin itself.</summary>
public class AccountServiceRoleChangeTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();
    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task Changing_a_user_s_role_updates_it_and_writes_an_audit_entry_naming_the_actor()
    {
        await using var db = _fixture.CreateContext();
        var service = IdentityTestHelpers.CreateAccountService(db);
        var target = await service.RegisterAsync($"user-{Guid.NewGuid():N}", "password");
        var admin = await IdentityTestHelpers.GetOrBootstrapAdminAsync(service, db);
        await service.ChangeRoleAsync(target.Id, Role.Assistant, admin.Id); // give it a starting role to change away from

        var updated = await service.ChangeRoleAsync(target.Id, Role.Hygienist, admin.Id);

        Assert.Equal(Role.Hygienist, updated.Role);

        var auditEntry = db.AuditLogEntries
            .Where(a => a.TargetUserAccountId == target.Id && a.EventType == AuditEventTypes.RoleChanged)
            .OrderByDescending(a => a.TimestampUtc)
            .First();
        Assert.Equal(admin.Id, auditEntry.PerformedByUserAccountId);
        Assert.Contains("Assistant", auditEntry.Details);
        Assert.Contains("Hygienist", auditEntry.Details);
    }

    [Fact]
    public async Task Changing_a_role_to_its_current_value_is_a_no_op_and_writes_no_audit_entry()
    {
        await using var db = _fixture.CreateContext();
        var service = IdentityTestHelpers.CreateAccountService(db);
        var target = await service.RegisterAsync($"user-{Guid.NewGuid():N}", "password");
        var admin = await IdentityTestHelpers.GetOrBootstrapAdminAsync(service, db);
        await service.ChangeRoleAsync(target.Id, Role.Billing, admin.Id);

        var auditCountBefore = db.AuditLogEntries.Count(a => a.TargetUserAccountId == target.Id && a.EventType == AuditEventTypes.RoleChanged);
        await service.ChangeRoleAsync(target.Id, Role.Billing, admin.Id); // same role again

        var auditCountAfter = db.AuditLogEntries.Count(a => a.TargetUserAccountId == target.Id && a.EventType == AuditEventTypes.RoleChanged);
        Assert.Equal(auditCountBefore, auditCountAfter);
    }

    [Fact]
    public async Task Changing_the_role_of_an_unknown_account_throws_a_clear_exception()
    {
        await using var db = _fixture.CreateContext();
        var service = IdentityTestHelpers.CreateAccountService(db);
        var admin = await IdentityTestHelpers.GetOrBootstrapAdminAsync(service, db);

        await Assert.ThrowsAsync<AccountNotFoundException>(
            () => service.ChangeRoleAsync(Guid.NewGuid(), Role.Dentist, admin.Id));
    }
}
