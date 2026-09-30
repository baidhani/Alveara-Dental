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

    // ---------- R03 additions (review finding ALV-001-C01-R02-01: challenge replay) ----------

    [Fact]
    public async Task Replaying_the_exact_same_successful_TOTP_challenge_submission_is_rejected()
    {
        await using var db = _fixture.CreateContext();
        var service = IdentityTestHelpers.CreateAccountService(db);
        var username = $"user-{Guid.NewGuid():N}";
        var account = await IdentityTestHelpers.RegisterEnabledAsync(service, db, username, "password", Role.Dentist);
        var (secretB32, _) = await service.BeginMfaEnrollmentAsync(account.Id);
        var secret = Totp.FromBase32(secretB32);
        await service.ConfirmMfaEnrollmentAsync(account.Id, Totp.GenerateCodeForTests(secret));
        var challenge = await Assert.ThrowsAsync<MfaChallengeRequiredException>(() => service.LoginAsync(username, "password"));
        var code = Totp.GenerateCodeForTests(secret);

        var first = await service.CompleteMfaChallengeAsync(challenge.ChallengeToken, code);
        Assert.Equal(account.Id, first.Id);

        // The identical challenge-token/code submission, replayed, must not mint a second session.
        await Assert.ThrowsAsync<InvalidOrExpiredMfaChallengeException>(
            () => service.CompleteMfaChallengeAsync(challenge.ChallengeToken, code));
    }

    [Fact]
    public async Task Two_concurrent_submissions_of_the_same_successful_challenge_produce_exactly_one_success()
    {
        // A shared Data Protection provider across all three services - matching production
        // (a singleton shared by every scoped DbContext) - since CompleteMfaChallengeAsync must
        // unprotect a challenge token that a *different* service instance issued.
        var sharedProvider = new EphemeralDataProtectionProvider();

        await using var setupDb = _fixture.CreateContext();
        var setupService = IdentityTestHelpers.CreateAccountService(setupDb, sharedProvider);
        var username = $"user-{Guid.NewGuid():N}";
        var account = await IdentityTestHelpers.RegisterEnabledAsync(setupService, setupDb, username, "password", Role.Hygienist);
        var (secretB32, _) = await setupService.BeginMfaEnrollmentAsync(account.Id);
        var secret = Totp.FromBase32(secretB32);
        await setupService.ConfirmMfaEnrollmentAsync(account.Id, Totp.GenerateCodeForTests(secret));
        var challenge = await Assert.ThrowsAsync<MfaChallengeRequiredException>(() => setupService.LoginAsync(username, "password"));
        var code = Totp.GenerateCodeForTests(secret);

        await using var dbA = _fixture.CreateContext();
        await using var dbB = _fixture.CreateContext();
        var serviceA = IdentityTestHelpers.CreateAccountService(dbA, sharedProvider);
        var serviceB = IdentityTestHelpers.CreateAccountService(dbB, sharedProvider);

        var taskA = Record.ExceptionAsync(() => serviceA.CompleteMfaChallengeAsync(challenge.ChallengeToken, code));
        var taskB = Record.ExceptionAsync(() => serviceB.CompleteMfaChallengeAsync(challenge.ChallengeToken, code));
        var results = await Task.WhenAll(taskA, taskB);

        Assert.Single(results, r => r is null); // exactly one of the two racing completions actually succeeded
        Assert.Single(results, r => r is InvalidOrExpiredMfaChallengeException);
    }

    [Fact]
    public async Task Replaying_a_challenge_already_completed_via_a_recovery_code_is_rejected()
    {
        await using var db = _fixture.CreateContext();
        var service = IdentityTestHelpers.CreateAccountService(db);
        var username = $"user-{Guid.NewGuid():N}";
        var account = await IdentityTestHelpers.RegisterEnabledAsync(service, db, username, "password", Role.Assistant);
        var (secretB32, recoveryCodes) = await service.BeginMfaEnrollmentAsync(account.Id);
        await service.ConfirmMfaEnrollmentAsync(account.Id, Totp.GenerateCodeForTests(Totp.FromBase32(secretB32)));
        var challenge = await Assert.ThrowsAsync<MfaChallengeRequiredException>(() => service.LoginAsync(username, "password"));

        var recoveryCode = recoveryCodes[0];
        var first = await service.CompleteMfaChallengeAsync(challenge.ChallengeToken, recoveryCode);
        Assert.Equal(account.Id, first.Id);

        // Replaying the same challenge with a DIFFERENT unused recovery code must also fail - the
        // challenge itself, not just the individual recovery code, is what's now spent.
        var secondRecoveryCode = recoveryCodes[1];
        await Assert.ThrowsAsync<InvalidOrExpiredMfaChallengeException>(
            () => service.CompleteMfaChallengeAsync(challenge.ChallengeToken, secondRecoveryCode));
    }

    [Fact]
    public async Task An_expired_challenge_cannot_be_completed_even_with_the_correct_code()
    {
        await using var db = _fixture.CreateContext();
        var service = IdentityTestHelpers.CreateAccountService(db);
        var username = $"user-{Guid.NewGuid():N}";
        var account = await IdentityTestHelpers.RegisterEnabledAsync(service, db, username, "password", Role.Billing);
        var (secretB32, _) = await service.BeginMfaEnrollmentAsync(account.Id);
        var secret = Totp.FromBase32(secretB32);
        await service.ConfirmMfaEnrollmentAsync(account.Id, Totp.GenerateCodeForTests(secret));
        var challenge = await Assert.ThrowsAsync<MfaChallengeRequiredException>(() => service.LoginAsync(username, "password"));

        // Simulate the challenge having already expired server-side (rather than waiting out the
        // real 5-minute lifetime): the durable MfaChallenge row is the authority Complete checks,
        // so backdating its ExpiresAtUtc directly proves the DB-side expiry gate, independent of
        // the token's own embedded (and here, still-valid) expiry claim.
        await using (var mutateDb = _fixture.CreateContext())
        {
            var row = await mutateDb.Set<MfaChallenge>().SingleAsync(c => c.UserAccountId == account.Id);
            row.ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1);
            await mutateDb.SaveChangesAsync();
        }

        await Assert.ThrowsAsync<InvalidOrExpiredMfaChallengeException>(
            () => service.CompleteMfaChallengeAsync(challenge.ChallengeToken, Totp.GenerateCodeForTests(secret)));
    }

    [Fact]
    public async Task A_revoked_challenge_row_cannot_be_completed()
    {
        // Distinct from the existing SecurityStamp-mismatch tests: this proves the durable
        // MfaChallenge row's own ConsumedAtUtc gate independently, by marking it consumed
        // directly rather than via a stamp-rotating operation.
        await using var db = _fixture.CreateContext();
        var service = IdentityTestHelpers.CreateAccountService(db);
        var username = $"user-{Guid.NewGuid():N}";
        var account = await IdentityTestHelpers.RegisterEnabledAsync(service, db, username, "password", Role.OfficeManager);
        var (secretB32, _) = await service.BeginMfaEnrollmentAsync(account.Id);
        var secret = Totp.FromBase32(secretB32);
        await service.ConfirmMfaEnrollmentAsync(account.Id, Totp.GenerateCodeForTests(secret));
        var challenge = await Assert.ThrowsAsync<MfaChallengeRequiredException>(() => service.LoginAsync(username, "password"));

        await using (var mutateDb = _fixture.CreateContext())
        {
            var row = await mutateDb.Set<MfaChallenge>().SingleAsync(c => c.UserAccountId == account.Id);
            row.ConsumedAtUtc = DateTimeOffset.UtcNow;
            await mutateDb.SaveChangesAsync();
        }

        await Assert.ThrowsAsync<InvalidOrExpiredMfaChallengeException>(
            () => service.CompleteMfaChallengeAsync(challenge.ChallengeToken, Totp.GenerateCodeForTests(secret)));
    }

    // ---------- R03 additions (review finding ALV-001-C01-R02-02: step-up throttling) ----------

    [Fact]
    public async Task Repeated_wrong_current_passwords_during_MFA_replacement_lock_the_account_like_a_failed_login_would()
    {
        await using var db = _fixture.CreateContext();
        var service = IdentityTestHelpers.CreateAccountService(db);
        var username = $"user-{Guid.NewGuid():N}";
        var account = await IdentityTestHelpers.RegisterEnabledAsync(service, db, username, "password", Role.FrontDesk);
        var (secretB32, _) = await service.BeginMfaEnrollmentAsync(account.Id);
        await service.ConfirmMfaEnrollmentAsync(account.Id, Totp.GenerateCodeForTests(Totp.FromBase32(secretB32)));

        for (var i = 0; i < 4; i++)
        {
            await Assert.ThrowsAsync<InvalidCurrentPasswordException>(
                () => service.BeginMfaEnrollmentAsync(account.Id, currentPassword: "wrong"));
        }
        // The 5th wrong step-up attempt crosses the SAME threshold a wrong login password would.
        var locked = await Assert.ThrowsAsync<AccountLockedOutException>(
            () => service.BeginMfaEnrollmentAsync(account.Id, currentPassword: "wrong"));
        Assert.True(locked.LockedOutUntilUtc > DateTimeOffset.UtcNow);

        // The lock is the real, shared account lockout - a subsequent ordinary login with the
        // CORRECT password is rejected too, proving this endpoint cannot bypass login lockout.
        await Assert.ThrowsAsync<AccountLockedOutException>(() => service.LoginAsync(username, "password"));

        var auditEntry = db.AuditLogEntries
            .Where(a => a.TargetUserAccountId == account.Id && a.EventType == AuditEventTypes.MfaStepUpFailed)
            .OrderByDescending(a => a.TimestampUtc)
            .First();
        Assert.Contains(username, auditEntry.Details);
    }

    [Fact]
    public async Task Failed_login_attempts_and_failed_MFA_step_up_attempts_share_the_same_lockout_counter()
    {
        await using var db = _fixture.CreateContext();
        var service = IdentityTestHelpers.CreateAccountService(db);
        var username = $"user-{Guid.NewGuid():N}";
        var account = await IdentityTestHelpers.RegisterEnabledAsync(service, db, username, "password", Role.Billing);
        var (secretB32, _) = await service.BeginMfaEnrollmentAsync(account.Id);
        await service.ConfirmMfaEnrollmentAsync(account.Id, Totp.GenerateCodeForTests(Totp.FromBase32(secretB32)));

        // Two wrong login passwords, then three wrong step-up passwords - a caller spreading
        // failures across both endpoints still hits the shared threshold on the 5th.
        await Assert.ThrowsAsync<InvalidLoginException>(() => service.LoginAsync(username, "wrong-1"));
        await Assert.ThrowsAsync<InvalidLoginException>(() => service.LoginAsync(username, "wrong-2"));
        await Assert.ThrowsAsync<InvalidCurrentPasswordException>(
            () => service.BeginMfaEnrollmentAsync(account.Id, currentPassword: "wrong-3"));
        await Assert.ThrowsAsync<InvalidCurrentPasswordException>(
            () => service.BeginMfaEnrollmentAsync(account.Id, currentPassword: "wrong-4"));
        var locked = await Assert.ThrowsAsync<AccountLockedOutException>(
            () => service.BeginMfaEnrollmentAsync(account.Id, currentPassword: "wrong-5"));
        Assert.True(locked.LockedOutUntilUtc > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task A_currently_locked_account_cannot_use_MFA_replacement_step_up_either()
    {
        await using var db = _fixture.CreateContext();
        var service = IdentityTestHelpers.CreateAccountService(db);
        var username = $"user-{Guid.NewGuid():N}";
        var account = await IdentityTestHelpers.RegisterEnabledAsync(service, db, username, "password", Role.Dentist);
        var (secretB32, _) = await service.BeginMfaEnrollmentAsync(account.Id);
        await service.ConfirmMfaEnrollmentAsync(account.Id, Totp.GenerateCodeForTests(Totp.FromBase32(secretB32)));

        for (var i = 0; i < 5; i++)
        {
            await Record.ExceptionAsync(() => service.LoginAsync(username, "wrong-password"));
        }

        // Even with the CORRECT current password, a currently-locked account can't step up.
        await Assert.ThrowsAsync<AccountLockedOutException>(
            () => service.BeginMfaEnrollmentAsync(account.Id, currentPassword: "password"));
    }

    [Fact]
    public async Task A_correct_step_up_password_resets_the_shared_failed_attempt_counter()
    {
        await using var db = _fixture.CreateContext();
        var service = IdentityTestHelpers.CreateAccountService(db);
        var username = $"user-{Guid.NewGuid():N}";
        var account = await IdentityTestHelpers.RegisterEnabledAsync(service, db, username, "password", Role.Hygienist);
        var (secretB32, _) = await service.BeginMfaEnrollmentAsync(account.Id);
        await service.ConfirmMfaEnrollmentAsync(account.Id, Totp.GenerateCodeForTests(Totp.FromBase32(secretB32)));

        await Assert.ThrowsAsync<InvalidCurrentPasswordException>(
            () => service.BeginMfaEnrollmentAsync(account.Id, currentPassword: "wrong-1"));
        await Assert.ThrowsAsync<InvalidCurrentPasswordException>(
            () => service.BeginMfaEnrollmentAsync(account.Id, currentPassword: "wrong-2"));

        await service.BeginMfaEnrollmentAsync(account.Id, currentPassword: "password"); // correct - resets the counter

        // Three more wrong attempts afterward should not reach the 5-attempt threshold, since the
        // counter was genuinely reset rather than merely paused.
        for (var i = 0; i < 3; i++)
        {
            await Assert.ThrowsAsync<InvalidCurrentPasswordException>(
                () => service.BeginMfaEnrollmentAsync(account.Id, currentPassword: $"wrong-{i}"));
        }
        var refreshed = await db.UserAccounts.AsNoTracking().SingleAsync(u => u.Id == account.Id);
        Assert.Null(refreshed.LockedOutUntilUtc);
    }
}
