using Alveara.Api.Architecture.Identity;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-001-C01's "password reset/recovery without storing recoverable passwords" requirement,
/// implemented as an admin-issued one-time token (no email/SMS dependency, matching the same
/// no-public-internet constraint as MFA): an admin issues a raw token exactly once, hands it to
/// the user out-of-band, and the user completes the reset unauthenticated with that token.
/// </summary>
public class AccountServicePasswordResetTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();
    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task An_admin_issued_reset_token_lets_the_user_set_a_new_password_and_the_change_is_audited()
    {
        await using var db = _fixture.CreateContext();
        var service = IdentityTestHelpers.CreateAccountService(db);
        var admin = await IdentityTestHelpers.GetOrBootstrapAdminAsync(service, db);
        var username = $"user-{Guid.NewGuid():N}";
        var account = await IdentityTestHelpers.RegisterEnabledAsync(service, db, username, "old-password", Role.Dentist, admin);

        var rawToken = await service.IssuePasswordResetTokenAsync(account.Id, admin.Id);
        Assert.NotEmpty(rawToken);

        await service.ResetPasswordAsync(account.Id, rawToken, "new-password");

        // The old password no longer works; the new one does.
        await Assert.ThrowsAsync<InvalidLoginException>(() => service.LoginAsync(username, "old-password"));
        var loggedIn = await service.LoginAsync(username, "new-password");
        Assert.Equal(account.Id, loggedIn.Id);

        var auditEntry = db.AuditLogEntries
            .Single(a => a.TargetUserAccountId == account.Id && a.EventType == AuditEventTypes.PasswordReset);
        Assert.Contains("reset", auditEntry.Details, StringComparison.OrdinalIgnoreCase);

        var issueAudit = db.AuditLogEntries
            .Single(a => a.TargetUserAccountId == account.Id && a.EventType == AuditEventTypes.PasswordResetIssued);
        Assert.Equal(admin.Id, issueAudit.PerformedByUserAccountId);
    }

    [Fact]
    public async Task A_reset_token_can_only_be_used_once()
    {
        await using var db = _fixture.CreateContext();
        var service = IdentityTestHelpers.CreateAccountService(db);
        var admin = await IdentityTestHelpers.GetOrBootstrapAdminAsync(service, db);
        var username = $"user-{Guid.NewGuid():N}";
        var account = await IdentityTestHelpers.RegisterEnabledAsync(service, db, username, "old-password", Role.Billing, admin);
        var rawToken = await service.IssuePasswordResetTokenAsync(account.Id, admin.Id);
        await service.ResetPasswordAsync(account.Id, rawToken, "new-password");

        await Assert.ThrowsAsync<InvalidOrExpiredResetTokenException>(
            () => service.ResetPasswordAsync(account.Id, rawToken, "another-password"));
    }

    [Fact]
    public async Task An_unknown_or_garbage_token_is_rejected_and_changes_nothing()
    {
        await using var db = _fixture.CreateContext();
        var service = IdentityTestHelpers.CreateAccountService(db);
        var admin = await IdentityTestHelpers.GetOrBootstrapAdminAsync(service, db);
        var username = $"user-{Guid.NewGuid():N}";
        var account = await IdentityTestHelpers.RegisterEnabledAsync(service, db, username, "old-password", Role.Assistant, admin);

        await Assert.ThrowsAsync<InvalidOrExpiredResetTokenException>(
            () => service.ResetPasswordAsync(account.Id, "not-a-real-token", "new-password"));

        var loggedIn = await service.LoginAsync(username, "old-password"); // still the original password
        Assert.Equal(account.Id, loggedIn.Id);
    }

    [Fact]
    public async Task Two_concurrent_uses_of_the_same_reset_token_leave_exactly_one_password_change()
    {
        await using var setupDb = _fixture.CreateContext();
        var setupService = IdentityTestHelpers.CreateAccountService(setupDb);
        var admin = await IdentityTestHelpers.GetOrBootstrapAdminAsync(setupService, setupDb);
        var username = $"user-{Guid.NewGuid():N}";
        var account = await IdentityTestHelpers.RegisterEnabledAsync(setupService, setupDb, username, "old-password", Role.Hygienist, admin);
        var rawToken = await setupService.IssuePasswordResetTokenAsync(account.Id, admin.Id);

        await using var dbA = _fixture.CreateContext();
        await using var dbB = _fixture.CreateContext();
        var serviceA = IdentityTestHelpers.CreateAccountService(dbA);
        var serviceB = IdentityTestHelpers.CreateAccountService(dbB);

        var taskA = Record.ExceptionAsync(() => serviceA.ResetPasswordAsync(account.Id, rawToken, "password-a"));
        var taskB = Record.ExceptionAsync(() => serviceB.ResetPasswordAsync(account.Id, rawToken, "password-b"));
        var results = await Task.WhenAll(taskA, taskB);

        Assert.Single(results, r => r is null); // exactly one of the two completed the reset

        // Whichever one won, its password (and only its password) now works.
        var winningPassword = results[0] is null ? "password-a" : "password-b";
        var loggedIn = await setupService.LoginAsync(username, winningPassword);
        Assert.Equal(account.Id, loggedIn.Id);
    }
}
