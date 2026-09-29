using Alveara.Api.Architecture.Identity;
using Xunit;

namespace Alveara.Api.Tests;

public class AccountServiceRegistrationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public AccountServiceRegistrationTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Registering_a_new_user_creates_an_account_with_a_securely_hashed_password()
    {
        await using var db = _fixture.CreateContext();
        var service = new AccountService(db);
        var username = $"dr.chen-{Guid.NewGuid():N}";

        var account = await service.RegisterAsync(username, "correct horse battery staple", Role.Dentist);

        Assert.Equal(username, account.Username);
        Assert.Equal(Role.Dentist, account.Role);
        Assert.NotNull(account.PasswordHash);
        Assert.DoesNotContain("correct horse battery staple", account.PasswordHash);
        Assert.True(Pbkdf2PasswordHasher.Verify("correct horse battery staple", account.PasswordHash!));

        var persisted = await db.UserAccounts.FindAsync(account.Id);
        Assert.NotNull(persisted);
    }

    [Fact]
    public async Task Registering_a_new_user_writes_an_audit_entry_for_the_registration()
    {
        await using var db = _fixture.CreateContext();
        var service = new AccountService(db);
        var username = $"hygienist-{Guid.NewGuid():N}";

        var account = await service.RegisterAsync(username, "another-strong-password", Role.Hygienist);

        var auditEntry = db.AuditLogEntries.Single(a => a.TargetUserAccountId == account.Id);
        Assert.Equal(AuditEventTypes.AccountRegistered, auditEntry.EventType);
        Assert.Null(auditEntry.PerformedByUserAccountId);
        Assert.Contains(username, auditEntry.Details);
        Assert.True((DateTimeOffset.UtcNow - auditEntry.TimestampUtc) < TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Registering_a_username_that_already_exists_fails_cleanly_and_creates_no_duplicate()
    {
        await using var db = _fixture.CreateContext();
        var service = new AccountService(db);
        var username = $"duplicate-{Guid.NewGuid():N}";
        await service.RegisterAsync(username, "first-password", Role.Assistant);

        await Assert.ThrowsAsync<UsernameAlreadyRegisteredException>(
            () => service.RegisterAsync(username, "second-password", Role.Assistant));

        var matchingAccounts = db.UserAccounts.Count(u => u.Username == username);
        Assert.Equal(1, matchingAccounts);
    }

    [Fact]
    public async Task Two_concurrent_registrations_for_the_same_username_leave_exactly_one_account()
    {
        var username = $"concurrent-{Guid.NewGuid():N}";

        await using var dbA = _fixture.CreateContext();
        await using var dbB = _fixture.CreateContext();
        var serviceA = new AccountService(dbA);
        var serviceB = new AccountService(dbB);

        var taskA = serviceA.RegisterAsync(username, "password-a", Role.FrontDesk);
        var taskB = serviceB.RegisterAsync(username, "password-b", Role.FrontDesk);

        var results = await Task.WhenAll(
            taskA.ContinueWith(t => t.IsCompletedSuccessfully),
            taskB.ContinueWith(t => t.IsCompletedSuccessfully));

        Assert.Single(results, r => r); // exactly one of the two succeeded

        await using var verifyDb = _fixture.CreateContext();
        Assert.Equal(1, verifyDb.UserAccounts.Count(u => u.Username == username));
    }

    [Fact]
    public async Task Registering_with_an_undefined_role_value_is_rejected()
    {
        await using var db = _fixture.CreateContext();
        var service = new AccountService(db);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => service.RegisterAsync($"user-{Guid.NewGuid():N}", "password", (Role)999));
    }
}
