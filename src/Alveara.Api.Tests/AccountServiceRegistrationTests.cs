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
    public async Task Registering_a_new_user_creates_a_securely_hashed_disabled_unassigned_account()
    {
        await using var db = _fixture.CreateContext();
        var service = IdentityTestHelpers.CreateAccountService(db);
        var username = $"user-{Guid.NewGuid():N}";

        var account = await service.RegisterAsync(username, "correct horse battery staple");

        Assert.Equal(username, account.Username);
        // ALV-001-C01: self-service registration can never grant a working role or an enabled account.
        Assert.Equal(Role.Unassigned, account.Role);
        Assert.True(account.IsDisabled);
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
        var service = IdentityTestHelpers.CreateAccountService(db);
        var username = $"user-{Guid.NewGuid():N}";

        var account = await service.RegisterAsync(username, "another-strong-password");

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
        var service = IdentityTestHelpers.CreateAccountService(db);
        var username = $"duplicate-{Guid.NewGuid():N}";
        await service.RegisterAsync(username, "first-password");

        await Assert.ThrowsAsync<UsernameAlreadyRegisteredException>(
            () => service.RegisterAsync(username, "second-password"));

        var matchingAccounts = db.UserAccounts.Count(u => u.Username == username);
        Assert.Equal(1, matchingAccounts);
    }

    [Fact]
    public async Task Two_concurrent_registrations_for_the_same_username_leave_exactly_one_account()
    {
        var username = $"concurrent-{Guid.NewGuid():N}";

        await using var dbA = _fixture.CreateContext();
        await using var dbB = _fixture.CreateContext();
        var serviceA = IdentityTestHelpers.CreateAccountService(dbA);
        var serviceB = IdentityTestHelpers.CreateAccountService(dbB);

        var taskA = serviceA.RegisterAsync(username, "password-a");
        var taskB = serviceB.RegisterAsync(username, "password-b");

        var results = await Task.WhenAll(
            taskA.ContinueWith(t => t.IsCompletedSuccessfully),
            taskB.ContinueWith(t => t.IsCompletedSuccessfully));

        Assert.Single(results, r => r); // exactly one of the two succeeded

        await using var verifyDb = _fixture.CreateContext();
        Assert.Equal(1, verifyDb.UserAccounts.Count(u => u.Username == username));
    }

    [Fact]
    public async Task An_unauthenticated_registration_request_cannot_select_a_role_or_produce_an_enabled_account()
    {
        // ALV-001-C01's core correction: there is no way to pass a role into RegisterAsync at
        // all anymore (the method signature itself no longer accepts one) - this test documents
        // that guarantee at the type level in addition to the behavioral assertion above.
        await using var db = _fixture.CreateContext();
        var service = IdentityTestHelpers.CreateAccountService(db);

        var account = await service.RegisterAsync($"self-service-{Guid.NewGuid():N}", "password");

        Assert.NotEqual(Role.Admin, account.Role);
        Assert.True(account.IsDisabled);
    }
}
