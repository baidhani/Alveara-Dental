using Alveara.Api.Architecture.Identity;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>Each [Fact] gets its own fresh LocalDB database (rather than sharing one via
/// IClassFixture) since several tests here bootstrap the one-time first-admin themselves.</summary>
[Collection(ParallelismCollections.SerialServer)]
public class AccountServiceLoginTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();
    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task Login_with_the_correct_password_succeeds_and_audits_the_success()
    {
        await using var db = _fixture.CreateContext();
        var service = IdentityTestHelpers.CreateAccountService(db);
        var username = $"user-{Guid.NewGuid():N}";
        var registered = await IdentityTestHelpers.RegisterEnabledAsync(service, db, username, "right-password", Role.OfficeManager);

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
        var service = IdentityTestHelpers.CreateAccountService(db);
        var username = $"user-{Guid.NewGuid():N}";
        var registered = await IdentityTestHelpers.RegisterEnabledAsync(service, db, username, "right-password", Role.Billing);

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
        var service = IdentityTestHelpers.CreateAccountService(db);

        var ex = await Assert.ThrowsAsync<InvalidLoginException>(
            () => service.LoginAsync($"nobody-{Guid.NewGuid():N}", "irrelevant"));

        Assert.Equal("Invalid username or password.", ex.Message);
    }

    [Fact]
    public async Task Unknown_username_and_wrong_password_perform_the_same_number_of_password_KDF_verifications()
    {
        // Structural proof of timing-equivalence (not a flaky wall-clock assertion): both paths
        // must invoke Pbkdf2PasswordHasher.Verify exactly once, so neither is cheaper to execute
        // in a way that would leak account existence via response timing.
        await using var db = _fixture.CreateContext();
        var service = IdentityTestHelpers.CreateAccountService(db);
        var username = $"user-{Guid.NewGuid():N}";
        await IdentityTestHelpers.RegisterEnabledAsync(service, db, username, "right-password", Role.Assistant);

        Pbkdf2PasswordHasher.ResetVerifyCallCountForTests();
        await Assert.ThrowsAsync<InvalidLoginException>(() => service.LoginAsync(username, "wrong-password"));
        var callsForKnownUser = Pbkdf2PasswordHasher.VerifyCallCount;

        Pbkdf2PasswordHasher.ResetVerifyCallCountForTests();
        await Assert.ThrowsAsync<InvalidLoginException>(() => service.LoginAsync($"nobody-{Guid.NewGuid():N}", "irrelevant"));
        var callsForUnknownUser = Pbkdf2PasswordHasher.VerifyCallCount;

        Assert.Equal(1, callsForKnownUser);
        Assert.Equal(callsForKnownUser, callsForUnknownUser);
    }

    [Fact]
    public async Task A_disabled_account_cannot_log_in_even_with_the_correct_password()
    {
        await using var db = _fixture.CreateContext();
        var service = IdentityTestHelpers.CreateAccountService(db);
        var username = $"user-{Guid.NewGuid():N}";
        var admin = await IdentityTestHelpers.GetOrBootstrapAdminAsync(service, db);
        var account = await service.RegisterAsync(username, "right-password");
        await service.ChangeRoleAsync(account.Id, Role.Dentist, admin.Id);
        // Deliberately left disabled (default state) - never enabled.

        await Assert.ThrowsAsync<AccountDisabledException>(() => service.LoginAsync(username, "right-password"));
    }

    [Fact]
    public async Task Five_consecutive_failed_logins_lock_the_account_and_a_sixth_attempt_with_the_correct_password_is_rejected()
    {
        await using var db = _fixture.CreateContext();
        var service = IdentityTestHelpers.CreateAccountService(db);
        var username = $"user-{Guid.NewGuid():N}";
        await IdentityTestHelpers.RegisterEnabledAsync(service, db, username, "right-password", Role.Admin);

        for (var i = 0; i < 4; i++)
        {
            await Assert.ThrowsAsync<InvalidLoginException>(() => service.LoginAsync(username, "wrong-password"));
        }
        // The 5th failure is the one that crosses the threshold and locks the account.
        var lockedOnFifth = await Assert.ThrowsAsync<AccountLockedOutException>(() => service.LoginAsync(username, "wrong-password"));
        Assert.True(lockedOnFifth.LockedOutUntilUtc > DateTimeOffset.UtcNow);

        var lockedEx = await Assert.ThrowsAsync<AccountLockedOutException>(
            () => service.LoginAsync(username, "right-password")); // correct password, but still locked out
        Assert.True(lockedEx.LockedOutUntilUtc > DateTimeOffset.UtcNow);

        await using var verifyDb = _fixture.CreateContext();
        var lockoutAudit = verifyDb.AuditLogEntries
            .Where(a => a.EventType == AuditEventTypes.AccountLockedOut)
            .Where(a => a.Details.Contains(username))
            .Single();
        Assert.Contains("5 failed attempts", lockoutAudit.Details);
    }

    [Fact]
    public async Task A_successful_login_resets_the_failed_attempt_counter()
    {
        await using var db = _fixture.CreateContext();
        var service = IdentityTestHelpers.CreateAccountService(db);
        var username = $"user-{Guid.NewGuid():N}";
        await IdentityTestHelpers.RegisterEnabledAsync(service, db, username, "right-password", Role.FrontDesk);

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
    public async Task A_lockout_rearms_after_it_expires_new_failures_trigger_a_new_lockout()
    {
        // ALV-001-C01's core lockout-rearm correction. The story reviewer found the original
        // implementation could never re-lock an account after its first lockout expired, since
        // the trigger condition only checked "LockedOutUntilUtc is null" - a field nothing but a
        // successful login ever cleared. This test proves the fix: an expired (but still
        // present) lockout timestamp is treated as "not currently locked" and a fresh failure
        // streak can trigger a genuinely new lockout.
        await using var db = _fixture.CreateContext();
        var service = IdentityTestHelpers.CreateAccountService(db);
        var username = $"user-{Guid.NewGuid():N}";
        var account = await IdentityTestHelpers.RegisterEnabledAsync(service, db, username, "right-password", Role.Hygienist);

        for (var i = 0; i < 4; i++)
        {
            await Assert.ThrowsAsync<InvalidLoginException>(() => service.LoginAsync(username, "wrong-password"));
        }
        // The 5th failure is the one that crosses the threshold and locks the account.
        await Assert.ThrowsAsync<AccountLockedOutException>(() => service.LoginAsync(username, "wrong-password"));

        // Simulate the lockout having already expired (rather than sleeping 15 real minutes).
        await using (var mutateDb = _fixture.CreateContext())
        {
            var row = await mutateDb.UserAccounts.SingleAsync(u => u.Id == account.Id);
            row.LockedOutUntilUtc = DateTimeOffset.UtcNow.AddMinutes(-1); // already in the past
            await mutateDb.SaveChangesAsync();
        }

        // A single wrong password now: since the stale lock rearms first, this is attempt 1 of a
        // new streak, not attempt 6 of the old one - so it must NOT re-lock immediately.
        await Assert.ThrowsAsync<InvalidLoginException>(() => service.LoginAsync(username, "still-wrong"));

        await using (var verifyDb = _fixture.CreateContext())
        {
            var row = await verifyDb.UserAccounts.SingleAsync(u => u.Id == account.Id);
            Assert.Equal(1, row.FailedLoginAttempts); // counting from zero again, not six
            Assert.Null(row.LockedOutUntilUtc);
        }

        // Four more failures should now trigger a brand-new lockout - proving rearm, not just reset.
        for (var i = 0; i < 3; i++)
        {
            await Assert.ThrowsAsync<InvalidLoginException>(() => service.LoginAsync(username, "still-wrong"));
        }
        var newLockout = await Assert.ThrowsAsync<AccountLockedOutException>(() => service.LoginAsync(username, "still-wrong"));
        Assert.True(newLockout.LockedOutUntilUtc > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Concurrent_failed_logins_against_the_same_account_do_not_lose_increments_to_a_race()
    {
        await using var setupDb = _fixture.CreateContext();
        var setupService = IdentityTestHelpers.CreateAccountService(setupDb);
        var username = $"user-{Guid.NewGuid():N}";
        await IdentityTestHelpers.RegisterEnabledAsync(setupService, setupDb, username, "right-password", Role.Hygienist);

        await using var dbA = _fixture.CreateContext();
        await using var dbB = _fixture.CreateContext();
        var serviceA = IdentityTestHelpers.CreateAccountService(dbA);
        var serviceB = IdentityTestHelpers.CreateAccountService(dbB);

        var taskA = Record.ExceptionAsync(() => serviceA.LoginAsync(username, "wrong-a"));
        var taskB = Record.ExceptionAsync(() => serviceB.LoginAsync(username, "wrong-b"));
        await Task.WhenAll(taskA, taskB);

        await using var verifyDb = _fixture.CreateContext();
        var account = await verifyDb.UserAccounts.SingleAsync(u => u.Username == username);
        Assert.Equal(2, account.FailedLoginAttempts); // neither increment was lost to the race
    }

    [Fact]
    public async Task A_correct_password_login_racing_the_failure_that_triggers_lockout_cannot_be_admitted()
    {
        // ALV-001-C01's other core concurrency correction: a successful (correct-password) login
        // request racing the exact failed request that crosses the lockout threshold must not be
        // let in just because it read the account before the lock was set.
        await using var setupDb = _fixture.CreateContext();
        var setupService = IdentityTestHelpers.CreateAccountService(setupDb);
        var username = $"user-{Guid.NewGuid():N}";
        await IdentityTestHelpers.RegisterEnabledAsync(setupService, setupDb, username, "right-password", Role.Billing);

        // Four failures first, so the account is one failure away from the lockout threshold.
        for (var i = 0; i < 4; i++)
        {
            await Assert.ThrowsAsync<InvalidLoginException>(() => setupService.LoginAsync(username, "wrong-password"));
        }

        await using var dbA = _fixture.CreateContext();
        await using var dbB = _fixture.CreateContext();
        var failingService = IdentityTestHelpers.CreateAccountService(dbA);
        var correctService = IdentityTestHelpers.CreateAccountService(dbB);

        var failingTask = Record.ExceptionAsync(() => failingService.LoginAsync(username, "wrong-password")); // the 5th failure - triggers lockout
        var correctTask = Record.ExceptionAsync(() => correctService.LoginAsync(username, "right-password")); // races it
        var correctLoginException = (await Task.WhenAll(failingTask, correctTask))[1];

        // SQL Server's row locking means the database serializes the two transactions into exactly
        // one of two legitimate orderings - which one is nondeterministic, but both are safe:
        //
        //   (a) the correct-password clear commits first, genuinely ahead of the failing request
        //       that would have crossed the threshold - a real (non-racy) success, and the "5th
        //       failure" that follows now lands as attempt #1 of a fresh streak, not a lockout.
        //   (b) the failing request's increment-and-lock commits first - the account is now
        //       genuinely locked, and the correct-password attempt's conditional clear (scoped to
        //       "only while not locked") affects zero rows and is correctly rejected.
        //
        // What must never happen, in either ordering, is data loss or a stale success silently
        // clearing a lock that was already genuinely in force - so assert the safety invariant
        // that actually matches whichever ordering occurred, rather than assuming one.
        await using var verifyDb = _fixture.CreateContext();
        var account = await verifyDb.UserAccounts.SingleAsync(u => u.Username == username);

        if (correctLoginException is null)
        {
            // Ordering (a): the correct password genuinely got there first.
            Assert.Null(account.LockedOutUntilUtc);
            Assert.True(account.FailedLoginAttempts <= 1); // either reset to 0 by the clear, or one fresh failure after it
        }
        else
        {
            // Ordering (b): the failing request's lock-set won the race.
            Assert.IsType<AccountLockedOutException>(correctLoginException);
            Assert.NotNull(account.LockedOutUntilUtc);
            Assert.True(account.LockedOutUntilUtc > DateTimeOffset.UtcNow);

            // And the lock genuinely holds afterward - no stale success left a back door open.
            await Assert.ThrowsAsync<AccountLockedOutException>(() => setupService.LoginAsync(username, "right-password"));
        }
    }
}
