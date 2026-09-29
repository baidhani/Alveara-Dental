using Alveara.Api.Architecture.Identity;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-001-C01's "at least one MFA factor that works with no public internet" requirement: TOTP
/// is computed entirely from the shared secret and the clock, with no network call at enroll,
/// challenge, or verify time - these tests prove the full flow using only the secret returned at
/// enrollment (exactly what a local authenticator app would hold), never a live device or service.
/// </summary>
public class AccountServiceMfaTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();
    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task Enrolling_and_confirming_with_the_correct_code_activates_MFA_and_audits_it()
    {
        await using var db = _fixture.CreateContext();
        var service = IdentityTestHelpers.CreateAccountService(db);
        var username = $"user-{Guid.NewGuid():N}";
        var account = await IdentityTestHelpers.RegisterEnabledAsync(service, db, username, "password", Role.Dentist);

        var (base32Secret, recoveryCodes) = await service.BeginMfaEnrollmentAsync(account.Id);
        Assert.Equal(10, recoveryCodes.Count);
        Assert.NotEmpty(base32Secret);

        var code = Totp.GenerateCodeForTests(Totp.FromBase32(base32Secret));
        await service.ConfirmMfaEnrollmentAsync(account.Id, code);

        var refreshed = await db.UserAccounts.SingleAsync(u => u.Id == account.Id);
        Assert.True(refreshed.MfaEnabled);

        var auditEntry = db.AuditLogEntries
            .Single(a => a.TargetUserAccountId == account.Id && a.EventType == AuditEventTypes.MfaEnabled);
        Assert.Contains(username, auditEntry.Details);
    }

    [Fact]
    public async Task Confirming_enrollment_with_the_wrong_code_fails_and_MFA_stays_inactive()
    {
        await using var db = _fixture.CreateContext();
        var service = IdentityTestHelpers.CreateAccountService(db);
        var username = $"user-{Guid.NewGuid():N}";
        var account = await IdentityTestHelpers.RegisterEnabledAsync(service, db, username, "password", Role.Hygienist);
        await service.BeginMfaEnrollmentAsync(account.Id);

        await Assert.ThrowsAsync<InvalidMfaCodeException>(
            () => service.ConfirmMfaEnrollmentAsync(account.Id, "000000"));

        var refreshed = await db.UserAccounts.SingleAsync(u => u.Id == account.Id);
        Assert.False(refreshed.MfaEnabled);
    }

    [Fact]
    public async Task Logging_in_with_MFA_enabled_pauses_for_a_challenge_instead_of_succeeding_outright()
    {
        await using var db = _fixture.CreateContext();
        var service = IdentityTestHelpers.CreateAccountService(db);
        var username = $"user-{Guid.NewGuid():N}";
        var account = await IdentityTestHelpers.RegisterEnabledAsync(service, db, username, "password", Role.Assistant);
        var (base32Secret, _) = await service.BeginMfaEnrollmentAsync(account.Id);
        var secret = Totp.FromBase32(base32Secret);
        await service.ConfirmMfaEnrollmentAsync(account.Id, Totp.GenerateCodeForTests(secret));

        var ex = await Assert.ThrowsAsync<MfaChallengeRequiredException>(() => service.LoginAsync(username, "password"));

        Assert.NotEmpty(ex.ChallengeToken);
    }

    [Fact]
    public async Task Completing_the_MFA_challenge_with_the_correct_TOTP_code_logs_the_user_in()
    {
        await using var db = _fixture.CreateContext();
        var service = IdentityTestHelpers.CreateAccountService(db);
        var username = $"user-{Guid.NewGuid():N}";
        var account = await IdentityTestHelpers.RegisterEnabledAsync(service, db, username, "password", Role.Billing);
        var (base32Secret, _) = await service.BeginMfaEnrollmentAsync(account.Id);
        var secret = Totp.FromBase32(base32Secret);
        await service.ConfirmMfaEnrollmentAsync(account.Id, Totp.GenerateCodeForTests(secret));
        var challenge = await Assert.ThrowsAsync<MfaChallengeRequiredException>(() => service.LoginAsync(username, "password"));

        var loggedIn = await service.CompleteMfaChallengeAsync(challenge.ChallengeToken, Totp.GenerateCodeForTests(secret));

        Assert.Equal(account.Id, loggedIn.Id);
    }

    [Fact]
    public async Task Completing_the_MFA_challenge_with_a_wrong_code_and_no_matching_recovery_code_fails()
    {
        await using var db = _fixture.CreateContext();
        var service = IdentityTestHelpers.CreateAccountService(db);
        var username = $"user-{Guid.NewGuid():N}";
        var account = await IdentityTestHelpers.RegisterEnabledAsync(service, db, username, "password", Role.OfficeManager);
        var (base32Secret, _) = await service.BeginMfaEnrollmentAsync(account.Id);
        var secret = Totp.FromBase32(base32Secret);
        await service.ConfirmMfaEnrollmentAsync(account.Id, Totp.GenerateCodeForTests(secret));
        var challenge = await Assert.ThrowsAsync<MfaChallengeRequiredException>(() => service.LoginAsync(username, "password"));

        await Assert.ThrowsAsync<InvalidMfaCodeException>(
            () => service.CompleteMfaChallengeAsync(challenge.ChallengeToken, "not-a-real-code"));
    }

    [Fact]
    public async Task A_recovery_code_completes_the_MFA_challenge_exactly_once_no_public_internet_needed()
    {
        // The recovery path a real deployment relies on if the authenticator device is lost - it
        // must work standalone (no email/SMS round trip) and must not be reusable.
        await using var db = _fixture.CreateContext();
        var service = IdentityTestHelpers.CreateAccountService(db);
        var username = $"user-{Guid.NewGuid():N}";
        var account = await IdentityTestHelpers.RegisterEnabledAsync(service, db, username, "password", Role.FrontDesk);
        var (base32Secret, recoveryCodes) = await service.BeginMfaEnrollmentAsync(account.Id);
        var secret = Totp.FromBase32(base32Secret);
        await service.ConfirmMfaEnrollmentAsync(account.Id, Totp.GenerateCodeForTests(secret));
        var recoveryCode = recoveryCodes[0];

        var firstChallenge = await Assert.ThrowsAsync<MfaChallengeRequiredException>(() => service.LoginAsync(username, "password"));
        var loggedIn = await service.CompleteMfaChallengeAsync(firstChallenge.ChallengeToken, recoveryCode);
        Assert.Equal(account.Id, loggedIn.Id);

        // Using the same recovery code again must fail - it's one-time, not a second password.
        var secondChallenge = await Assert.ThrowsAsync<MfaChallengeRequiredException>(() => service.LoginAsync(username, "password"));
        await Assert.ThrowsAsync<InvalidMfaCodeException>(
            () => service.CompleteMfaChallengeAsync(secondChallenge.ChallengeToken, recoveryCode));
    }

    [Fact]
    public async Task An_invalid_or_tampered_challenge_token_is_rejected()
    {
        await using var db = _fixture.CreateContext();
        var service = IdentityTestHelpers.CreateAccountService(db);

        await Assert.ThrowsAsync<InvalidOrExpiredMfaChallengeException>(
            () => service.CompleteMfaChallengeAsync("not-a-real-token", "123456"));
    }
}
