using Alveara.Api.Architecture.Identity;
using Microsoft.AspNetCore.DataProtection;
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

        var refreshed = await db.UserAccounts.AsNoTracking().SingleAsync(u => u.Id == account.Id);
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

        var refreshed = await db.UserAccounts.AsNoTracking().SingleAsync(u => u.Id == account.Id);
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

    // ---------- R02 additions (review finding ALV-001-C01-R01-01: re-enrollment lifecycle) ----------

    [Fact]
    public async Task Starting_re_enrollment_does_not_disable_the_already_established_factor()
    {
        await using var db = _fixture.CreateContext();
        var service = IdentityTestHelpers.CreateAccountService(db);
        var username = $"user-{Guid.NewGuid():N}";
        var account = await IdentityTestHelpers.RegisterEnabledAsync(service, db, username, "password", Role.Dentist);
        var (firstSecretB32, _) = await service.BeginMfaEnrollmentAsync(account.Id);
        var firstSecret = Totp.FromBase32(firstSecretB32);
        await service.ConfirmMfaEnrollmentAsync(account.Id, Totp.GenerateCodeForTests(firstSecret));

        // Start replacing the factor but never confirm it.
        await service.BeginMfaEnrollmentAsync(account.Id, currentPassword: "password");

        var refreshed = await db.UserAccounts.AsNoTracking().SingleAsync(u => u.Id == account.Id);
        Assert.True(refreshed.MfaEnabled); // still active
        Assert.NotNull(refreshed.MfaSecretProtected); // the ORIGINAL secret, untouched

        // The original factor still logs the user in - abandoning re-enrollment cost nothing.
        var challenge = await Assert.ThrowsAsync<MfaChallengeRequiredException>(() => service.LoginAsync(username, "password"));
        var loggedIn = await service.CompleteMfaChallengeAsync(challenge.ChallengeToken, Totp.GenerateCodeForTests(firstSecret));
        Assert.Equal(account.Id, loggedIn.Id);
    }

    [Fact]
    public async Task Replacing_MFA_requires_the_current_password_and_the_wrong_password_is_rejected()
    {
        await using var db = _fixture.CreateContext();
        var service = IdentityTestHelpers.CreateAccountService(db);
        var username = $"user-{Guid.NewGuid():N}";
        var account = await IdentityTestHelpers.RegisterEnabledAsync(service, db, username, "password", Role.Hygienist);
        var (secretB32, _) = await service.BeginMfaEnrollmentAsync(account.Id);
        await service.ConfirmMfaEnrollmentAsync(account.Id, Totp.GenerateCodeForTests(Totp.FromBase32(secretB32)));

        await Assert.ThrowsAsync<CurrentPasswordRequiredToReplaceMfaException>(
            () => service.BeginMfaEnrollmentAsync(account.Id)); // no password supplied at all

        await Assert.ThrowsAsync<InvalidCurrentPasswordException>(
            () => service.BeginMfaEnrollmentAsync(account.Id, currentPassword: "wrong-password"));
    }

    [Fact]
    public async Task Confirming_a_replacement_atomically_swaps_the_factor_and_recovery_codes_and_rotates_the_security_stamp()
    {
        await using var db = _fixture.CreateContext();
        var service = IdentityTestHelpers.CreateAccountService(db);
        var username = $"user-{Guid.NewGuid():N}";
        var account = await IdentityTestHelpers.RegisterEnabledAsync(service, db, username, "password", Role.Assistant);
        var (firstSecretB32, firstRecoveryCodes) = await service.BeginMfaEnrollmentAsync(account.Id);
        var firstSecret = Totp.FromBase32(firstSecretB32);
        await service.ConfirmMfaEnrollmentAsync(account.Id, Totp.GenerateCodeForTests(firstSecret));
        var stampAfterFirstEnrollment = (await db.UserAccounts.AsNoTracking().SingleAsync(u => u.Id == account.Id)).SecurityStamp;

        var (secondSecretB32, secondRecoveryCodes) = await service.BeginMfaEnrollmentAsync(account.Id, currentPassword: "password");
        var secondSecret = Totp.FromBase32(secondSecretB32);
        await service.ConfirmMfaEnrollmentAsync(account.Id, Totp.GenerateCodeForTests(secondSecret));

        var refreshed = await db.UserAccounts.AsNoTracking().SingleAsync(u => u.Id == account.Id);
        Assert.NotEqual(stampAfterFirstEnrollment, refreshed.SecurityStamp); // rotated - old sessions/challenges invalidated
        Assert.Null(refreshed.PendingMfaSecretProtected);

        // The OLD factor no longer works; the NEW one does.
        var challenge = await Assert.ThrowsAsync<MfaChallengeRequiredException>(() => service.LoginAsync(username, "password"));
        await Assert.ThrowsAsync<InvalidMfaCodeException>(
            () => service.CompleteMfaChallengeAsync(challenge.ChallengeToken, Totp.GenerateCodeForTests(firstSecret)));
        await Assert.ThrowsAsync<InvalidMfaCodeException>(
            () => service.CompleteMfaChallengeAsync(challenge.ChallengeToken, firstRecoveryCodes[0])); // old recovery codes gone too

        var newChallenge = await Assert.ThrowsAsync<MfaChallengeRequiredException>(() => service.LoginAsync(username, "password"));
        var loggedIn = await service.CompleteMfaChallengeAsync(newChallenge.ChallengeToken, secondRecoveryCodes[0]);
        Assert.Equal(account.Id, loggedIn.Id);

        var auditEntry = db.AuditLogEntries
            .Single(a => a.TargetUserAccountId == account.Id && a.EventType == AuditEventTypes.MfaReplaced);
        Assert.Contains(username, auditEntry.Details);
    }

    [Fact]
    public async Task Two_concurrent_confirmations_of_the_same_pending_enrollment_apply_at_most_once()
    {
        // A shared Data Protection provider across all three services below - matching production,
        // where it's a singleton shared by every scoped DbContext - since ConfirmMfaEnrollmentAsync
        // must decrypt a secret that BeginMfaEnrollmentAsync protected under the same key ring.
        var sharedProvider = new EphemeralDataProtectionProvider();

        await using var setupDb = _fixture.CreateContext();
        var setupService = IdentityTestHelpers.CreateAccountService(setupDb, sharedProvider);
        var username = $"user-{Guid.NewGuid():N}";
        var account = await IdentityTestHelpers.RegisterEnabledAsync(setupService, setupDb, username, "password", Role.Billing);
        var (secretB32, _) = await setupService.BeginMfaEnrollmentAsync(account.Id);
        var code = Totp.GenerateCodeForTests(Totp.FromBase32(secretB32));

        await using var dbA = _fixture.CreateContext();
        await using var dbB = _fixture.CreateContext();
        var serviceA = IdentityTestHelpers.CreateAccountService(dbA, sharedProvider);
        var serviceB = IdentityTestHelpers.CreateAccountService(dbB, sharedProvider);

        var taskA = Record.ExceptionAsync(() => serviceA.ConfirmMfaEnrollmentAsync(account.Id, code));
        var taskB = Record.ExceptionAsync(() => serviceB.ConfirmMfaEnrollmentAsync(account.Id, code));
        var results = await Task.WhenAll(taskA, taskB);

        Assert.Single(results, r => r is null); // exactly one confirmation actually applied
        var refreshed = await setupDb.UserAccounts.AsNoTracking().SingleAsync(u => u.Id == account.Id);
        Assert.True(refreshed.MfaEnabled);
        Assert.Null(refreshed.PendingMfaSecretProtected);
    }

    // ---------- R02 additions (review finding ALV-001-C01-R01-02: challenge bound to SecurityStamp) ----------

    [Fact]
    public async Task An_MFA_challenge_issued_before_an_explicit_session_revocation_is_rejected_afterward()
    {
        await using var db = _fixture.CreateContext();
        var service = IdentityTestHelpers.CreateAccountService(db);
        var admin = await IdentityTestHelpers.GetOrBootstrapAdminAsync(service, db);
        var username = $"user-{Guid.NewGuid():N}";
        var account = await IdentityTestHelpers.RegisterEnabledAsync(service, db, username, "password", Role.FrontDesk, admin);
        var (secretB32, _) = await service.BeginMfaEnrollmentAsync(account.Id);
        var secret = Totp.FromBase32(secretB32);
        await service.ConfirmMfaEnrollmentAsync(account.Id, Totp.GenerateCodeForTests(secret));

        var challenge = await Assert.ThrowsAsync<MfaChallengeRequiredException>(() => service.LoginAsync(username, "password"));

        await service.RevokeAllSessionsAsync(account.Id, admin.Id);

        await Assert.ThrowsAsync<InvalidOrExpiredMfaChallengeException>(
            () => service.CompleteMfaChallengeAsync(challenge.ChallengeToken, Totp.GenerateCodeForTests(secret)));
    }

    [Fact]
    public async Task An_MFA_challenge_issued_before_a_password_reset_is_rejected_afterward()
    {
        await using var db = _fixture.CreateContext();
        var service = IdentityTestHelpers.CreateAccountService(db);
        var admin = await IdentityTestHelpers.GetOrBootstrapAdminAsync(service, db);
        var username = $"user-{Guid.NewGuid():N}";
        var account = await IdentityTestHelpers.RegisterEnabledAsync(service, db, username, "password", Role.Billing, admin);
        var (secretB32, _) = await service.BeginMfaEnrollmentAsync(account.Id);
        var secret = Totp.FromBase32(secretB32);
        await service.ConfirmMfaEnrollmentAsync(account.Id, Totp.GenerateCodeForTests(secret));

        var challenge = await Assert.ThrowsAsync<MfaChallengeRequiredException>(() => service.LoginAsync(username, "password"));

        var resetToken = await service.IssuePasswordResetTokenAsync(account.Id, admin.Id);
        await service.ResetPasswordAsync(account.Id, resetToken, "new-password");

        await Assert.ThrowsAsync<InvalidOrExpiredMfaChallengeException>(
            () => service.CompleteMfaChallengeAsync(challenge.ChallengeToken, Totp.GenerateCodeForTests(secret)));
    }

    [Fact]
    public async Task An_MFA_challenge_issued_before_a_role_change_is_rejected_afterward()
    {
        await using var db = _fixture.CreateContext();
        var service = IdentityTestHelpers.CreateAccountService(db);
        var admin = await IdentityTestHelpers.GetOrBootstrapAdminAsync(service, db);
        var username = $"user-{Guid.NewGuid():N}";
        var account = await IdentityTestHelpers.RegisterEnabledAsync(service, db, username, "password", Role.Assistant, admin);
        var (secretB32, _) = await service.BeginMfaEnrollmentAsync(account.Id);
        var secret = Totp.FromBase32(secretB32);
        await service.ConfirmMfaEnrollmentAsync(account.Id, Totp.GenerateCodeForTests(secret));

        var challenge = await Assert.ThrowsAsync<MfaChallengeRequiredException>(() => service.LoginAsync(username, "password"));

        await service.ChangeRoleAsync(account.Id, Role.Hygienist, admin.Id);

        await Assert.ThrowsAsync<InvalidOrExpiredMfaChallengeException>(
            () => service.CompleteMfaChallengeAsync(challenge.ChallengeToken, Totp.GenerateCodeForTests(secret)));
    }

    // ---------- R02 additions (review finding ALV-001-C01-R01-04: MFA/recovery lifecycle audit) ----------

    [Fact]
    public async Task Beginning_enrollment_writes_an_MfaEnrollmentStarted_audit_entry()
    {
        await using var db = _fixture.CreateContext();
        var service = IdentityTestHelpers.CreateAccountService(db);
        var username = $"user-{Guid.NewGuid():N}";
        var account = await IdentityTestHelpers.RegisterEnabledAsync(service, db, username, "password", Role.OfficeManager);

        await service.BeginMfaEnrollmentAsync(account.Id);

        var auditEntry = db.AuditLogEntries
            .Single(a => a.TargetUserAccountId == account.Id && a.EventType == AuditEventTypes.MfaEnrollmentStarted);
        Assert.Contains(username, auditEntry.Details);
    }

    [Fact]
    public async Task Consuming_a_recovery_code_writes_an_MfaRecoveryCodeUsed_audit_entry_and_the_final_LoginSucceeded_only_fires_after_MFA()
    {
        await using var db = _fixture.CreateContext();
        var service = IdentityTestHelpers.CreateAccountService(db);
        var username = $"user-{Guid.NewGuid():N}";
        var account = await IdentityTestHelpers.RegisterEnabledAsync(service, db, username, "password", Role.Dentist);
        var (secretB32, recoveryCodes) = await service.BeginMfaEnrollmentAsync(account.Id);
        await service.ConfirmMfaEnrollmentAsync(account.Id, Totp.GenerateCodeForTests(Totp.FromBase32(secretB32)));

        var challenge = await Assert.ThrowsAsync<MfaChallengeRequiredException>(() => service.LoginAsync(username, "password"));

        // Password-verified-but-MFA-pending must NOT already be recorded as a successful login.
        Assert.Empty(db.AuditLogEntries.Where(a => a.TargetUserAccountId == account.Id && a.EventType == AuditEventTypes.LoginSucceeded));
        Assert.Single(db.AuditLogEntries.Where(a => a.TargetUserAccountId == account.Id && a.EventType == AuditEventTypes.PasswordVerifiedMfaPending));

        await service.CompleteMfaChallengeAsync(challenge.ChallengeToken, recoveryCodes[0]);

        Assert.Single(db.AuditLogEntries.Where(a => a.TargetUserAccountId == account.Id && a.EventType == AuditEventTypes.MfaRecoveryCodeUsed));
        Assert.Single(db.AuditLogEntries.Where(a => a.TargetUserAccountId == account.Id && a.EventType == AuditEventTypes.LoginSucceeded));
    }

    [Fact]
    public async Task A_failed_MFA_challenge_writes_an_MfaChallengeFailed_audit_entry()
    {
        await using var db = _fixture.CreateContext();
        var service = IdentityTestHelpers.CreateAccountService(db);
        var username = $"user-{Guid.NewGuid():N}";
        var account = await IdentityTestHelpers.RegisterEnabledAsync(service, db, username, "password", Role.Hygienist);
        var (secretB32, _) = await service.BeginMfaEnrollmentAsync(account.Id);
        await service.ConfirmMfaEnrollmentAsync(account.Id, Totp.GenerateCodeForTests(Totp.FromBase32(secretB32)));
        var challenge = await Assert.ThrowsAsync<MfaChallengeRequiredException>(() => service.LoginAsync(username, "password"));

        await Assert.ThrowsAsync<InvalidMfaCodeException>(
            () => service.CompleteMfaChallengeAsync(challenge.ChallengeToken, "000000"));

        var auditEntry = db.AuditLogEntries
            .Single(a => a.TargetUserAccountId == account.Id && a.EventType == AuditEventTypes.MfaChallengeFailed);
        Assert.Contains(username, auditEntry.Details);
    }
}
