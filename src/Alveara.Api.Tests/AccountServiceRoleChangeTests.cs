using Alveara.Api.Architecture.Identity;
using Xunit;

namespace Alveara.Api.Tests;

public class AccountServiceRoleChangeTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public AccountServiceRoleChangeTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Changing_a_user_s_role_updates_it_and_writes_an_audit_entry_naming_the_actor()
    {
        await using var db = _fixture.CreateContext();
        var service = new AccountService(db);
        var target = await service.RegisterAsync($"user-{Guid.NewGuid():N}", "password", Role.Assistant);
        var admin = await service.RegisterAsync($"admin-{Guid.NewGuid():N}", "password", Role.Admin);

        var updated = await service.ChangeRoleAsync(target.Id, Role.Hygienist, admin.Id);

        Assert.Equal(Role.Hygienist, updated.Role);

        var auditEntry = db.AuditLogEntries.Single(a => a.TargetUserAccountId == target.Id && a.EventType == AuditEventTypes.RoleChanged);
        Assert.Equal(admin.Id, auditEntry.PerformedByUserAccountId);
        Assert.Contains("Assistant", auditEntry.Details);
        Assert.Contains("Hygienist", auditEntry.Details);
    }

    [Fact]
    public async Task Changing_a_role_to_its_current_value_is_a_no_op_and_writes_no_audit_entry()
    {
        await using var db = _fixture.CreateContext();
        var service = new AccountService(db);
        var target = await service.RegisterAsync($"user-{Guid.NewGuid():N}", "password", Role.Billing);
        var admin = await service.RegisterAsync($"admin-{Guid.NewGuid():N}", "password", Role.Admin);

        await service.ChangeRoleAsync(target.Id, Role.Billing, admin.Id);

        var auditCount = db.AuditLogEntries.Count(a => a.TargetUserAccountId == target.Id && a.EventType == AuditEventTypes.RoleChanged);
        Assert.Equal(0, auditCount);
    }

    [Fact]
    public async Task Changing_the_role_of_an_unknown_account_throws_a_clear_exception()
    {
        await using var db = _fixture.CreateContext();
        var service = new AccountService(db);
        var admin = await service.RegisterAsync($"admin-{Guid.NewGuid():N}", "password", Role.Admin);

        await Assert.ThrowsAsync<AccountNotFoundException>(
            () => service.ChangeRoleAsync(Guid.NewGuid(), Role.Dentist, admin.Id));
    }
}
