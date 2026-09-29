using Alveara.Api.Architecture.Identity;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Alveara.Api.Tests;

public class AccountServiceLoginTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public AccountServiceLoginTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Login_with_the_correct_password_succeeds_and_audits_the_success()
    {
        await using var db = _fixture.CreateContext();
        var service = new AccountService(db);
        var username = $"user-{Guid.NewGuid():N}";
        var registered = await service.RegisterAsync(username, "right-password", Role.OfficeManager);

        var loggedIn = await service.LoginAsync(username, "right-password");

        Assert.Equal(registered.Id, loggedIn.Id);
        var auditEntry = db.AuditLogEntries
            .Where(a => a.TargetUserAccountId == registered.Id && a.EventType == AuditEventTypes.LoginSucceeded)
            .Single();
        Assert.Contains(username, auditEntry.Details);
    }

    [Fact]
    public async Task Login_with_an_incorrect_password_fails_and_audits_the_failure()
    {
        await using var db = _fixture.CreateContext();
        var service = new AccountService(db);
        var username = $"user-{Guid.NewGuid():N}";
        var registered = await service.RegisterAsync(username, "right-password", Role.Billing);

        await Assert.ThrowsAsync<InvalidLoginException>(() => service.LoginAsync(username, "wrong-password"));

        var auditEntry = db.AuditLogEntries
            .Where(a => a.TargetUserAccountId == registered.Id && a.EventType == AuditEventTypes.LoginFailed)
            .Single();
        Assert.Contains("1 of 5", auditEntry.Details);
    }

    [Fact]
    public async Task Login_with_an_unknown_username_fails_the_same_way_as_a_wrong_password_no_enumeration()
    {
        await using var db = _fixture.CreateContext();
        var service = new AccountService(db);

        var ex = await Assert.ThrowsAsync<InvalidLoginException>(
            () => service.LoginAsync($"nobody-{Guid.NewGuid():N}", "irrelevant"));

        Assert.Equal("Invalid username or password.", ex.Message);
    }

    [Fact]
    public async Task Five_consecutive_failed_logins_lock_the_account_and_a_sixth_attempt_with_the_correct_password_is_rejected()
    {
        await using var db = _fixture.CreateContext();
        var service = new AccountService(db);
        var username = $"user-{Guid.NewGuid():N}";
        await service.RegisterAsync(username, "right-password", Role.Admin);

        for (var i = 0; i < 5; i++)
        {
            await Assert.ThrowsAsync<InvalidLoginException>(() => service.LoginAsync(username, "wrong-password"));
        }

        var lockedEx = await Assert.ThrowsAsync<AccountLockedOutException>(
            () => service.LoginAsync(username, "right-password")); // correct password, but still locked out

        Assert.True(lockedEx.LockedOutUntilUtc > DateTimeOffset.UtcNow);

        await using var verifyDb = _fixture.CreateContext();
        var lockoutAudit = verifyDb.AuditLogEntries
            .Where(a => a.TargetUserAccountId != Guid.Empty && a.EventType == AuditEventTypes.AccountLockedOut)
            .Where(a => a.Details.Contains(username))
            .Single();
        Assert.Contains("5 failed attempts", lockoutAudit.Details);
    }

    [Fact]
    public async Task A_successful_login_resets_the_failed_attempt_counter()
    {
        await using var db = _fixture.CreateContext();
        var service = new AccountService(db);
        var username = $"user-{Guid.NewGuid():N}";
        await service.RegisterAsync(username, "right-password", Role.FrontDesk);

        await Assert.ThrowsAsync<InvalidLoginException>(() => service.LoginAsync(username, "wrong-1"));
        await Assert.ThrowsAsync<InvalidLoginException>(() => service.LoginAsync(username, "wrong-2"));
        await service.LoginAsync(username, "right-password"); // resets the counter

        // Three more failures after the reset should not reach the 5-attempt lockout threshold.
        await Assert.ThrowsAsync<InvalidLoginException>(() => service.LoginAsync(username, "wrong-3"));
        await Assert.ThrowsAsync<InvalidLoginException>(() => service.LoginAsync(username, "wrong-4"));
        await Assert.ThrowsAsync<InvalidLoginException>(() => service.LoginAsync(username, "wrong-5"));

        // Still not locked: the correct password works.
        var loggedIn = await service.LoginAsync(username, "right-password");
        Assert.Equal(username, loggedIn.Username);
    }

    [Fact]
    public async Task Concurrent_failed_logins_against_the_same_account_do_not_lose_increments_to_a_race()
    {
        await using var setupDb = _fixture.CreateContext();
        var setupService = new AccountService(setupDb);
        var username = $"user-{Guid.NewGuid():N}";
        await setupService.RegisterAsync(username, "right-password", Role.Hygienist);

        await using var dbA = _fixture.CreateContext();
        await using var dbB = _fixture.CreateContext();
        var serviceA = new AccountService(dbA);
        var serviceB = new AccountService(dbB);

        var taskA = Record.ExceptionAsync(() => serviceA.LoginAsync(username, "wrong-a"));
        var taskB = Record.ExceptionAsync(() => serviceB.LoginAsync(username, "wrong-b"));
        await Task.WhenAll(taskA, taskB);

        await using var verifyDb = _fixture.CreateContext();
        var account = await verifyDb.UserAccounts.SingleAsync(u => u.Username == username);
        Assert.Equal(2, account.FailedLoginAttempts); // neither increment was lost to the race
    }
}
