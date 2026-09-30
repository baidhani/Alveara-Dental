using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Identity;

public class UsernameAlreadyRegisteredException(string username) : Exception($"Username '{username}' is already registered.");
public class InvalidLoginException() : Exception("Invalid username or password.");

public class AccountLockedOutException(DateTimeOffset lockedOutUntilUtc)
    : Exception($"Account is locked until {lockedOutUntilUtc:O}.")
{
    public DateTimeOffset LockedOutUntilUtc { get; } = lockedOutUntilUtc;
}

public class AccountDisabledException() : Exception("This account has been disabled.");
public class AccountNotFoundException(Guid userAccountId) : Exception($"No account with id '{userAccountId}' exists.");
public class InvalidBootstrapSecretException() : Exception("Invalid bootstrap secret.");
public class BootstrapAlreadyConsumedException() : Exception("The first-admin bootstrap has already been used.");
public class InvalidOrExpiredMfaChallengeException() : Exception("Invalid or expired MFA challenge.");
public class InvalidMfaCodeException() : Exception("Invalid MFA code.");
public class MfaNotEnabledException() : Exception("MFA is not enabled for this account.");
public class InvalidOrExpiredResetTokenException() : Exception("Invalid or expired password reset token.");

// R02 (review finding ALV-001-C01-R01-01): replacing an already-established MFA factor requires
// step-up reauthentication (the caller's current password), not just an active session.
public class CurrentPasswordRequiredToReplaceMfaException() : Exception("Current password is required to replace an existing MFA factor.");
public class InvalidCurrentPasswordException() : Exception("Current password is incorrect.");
public class InvalidSessionTimeoutException(int minutes) : Exception($"Session timeout of {minutes} minutes is outside the allowed range.");

/// <summary>An in-progress login that has passed the password check but still needs an MFA code.</summary>
public class MfaChallengeRequiredException(string challengeToken) : Exception("MFA code required.")
{
    public string ChallengeToken { get; } = challengeToken;
}

/// <summary>
/// ALV-001-C01 / STORY-001: account registration, authentication, lockout, MFA, first-admin
/// bootstrap, and administrative account/role management. Every state-changing operation that
/// touches account or role state writes its audit entry in the same transaction as the change,
/// so an audit write can never succeed or fail independently of what it documents.
/// </summary>
public class AccountService(AlveraDbContext db, IDataProtectionProvider dataProtectionProvider)
{
    private const int UniqueConstraintViolation = 2627;
    private const int UniqueIndexViolation = 2601;
    private const int MaxFailedLoginAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan MfaChallengeLifetime = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ResetTokenLifetime = TimeSpan.FromHours(1);
    private const int RecoveryCodeCount = 10;

    private IDataProtector MfaSecretProtector => dataProtectionProvider.CreateProtector("Alveara.MfaSecret.v1");
    private IDataProtector MfaChallengeProtector => dataProtectionProvider.CreateProtector("Alveara.MfaChallenge.v1");

    // R06 (review finding ALV-001-C01-R05-01/-02): narrow, test-only coordination seams - null and
    // therefore a no-op in production - that let a test deterministically pause execution at an
    // exact point in a race window instead of relying on uncontrolled task scheduling. Each is
    // awaited only when a test has set it; nothing about the production code path changes.
    internal Func<Task>? TestHook_BeforeStepUpConditionalReset { get; set; }
    internal Func<Task>? TestHook_AfterRecoveryCodeVerifiedBeforeConsumption { get; set; }
    internal Func<Task>? TestHook_AfterRecoveryCodeConsumedBeforeCommit { get; set; }

    // ---------- Registration (self-service, no role/privilege) ----------

    /// <summary>
    /// ALV-001-C01: self-service registration can never grant a working role. The account is
    /// created as Role.Unassigned and disabled; only an authorized administrator (via
    /// <see cref="ChangeRoleAsync"/> and <see cref="SetAccountEnabledAsync"/>) can make it usable.
    /// </summary>
    public async Task<UserAccount> RegisterAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        var account = new UserAccount
        {
            Id = Guid.NewGuid(),
            Username = username,
            PasswordHash = Pbkdf2PasswordHasher.Hash(password),
            Role = Role.Unassigned,
            IsDisabled = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            db.UserAccounts.Add(account);
            AddAudit(AuditEventTypes.AccountRegistered, account.Id, null,
                $"Account '{username}' self-registered (Unassigned, disabled, pending administrator approval).");
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return account;
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new UsernameAlreadyRegisteredException(username);
        }
    }

    // ---------- First-admin bootstrap ----------

    /// <summary>
    /// ALV-001-C01: the only way to create an enabled Admin account with no prior administrator.
    /// Succeeds exactly once, offline, protected by <paramref name="bootstrapSecret"/> (matched
    /// against server-side configuration — never source control). Atomicity under a concurrent
    /// race comes from the database's own primary-key uniqueness on <see cref="BootstrapState"/>'s
    /// fixed singleton id, not application-level check-then-act: two simultaneous callers both
    /// attempt the same INSERT; exactly one can ever succeed.
    /// </summary>
    public async Task<UserAccount> BootstrapFirstAdminAsync(string username, string password, string suppliedSecret, string configuredSecret, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(configuredSecret) ||
            !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(suppliedSecret ?? ""), Encoding.UTF8.GetBytes(configuredSecret)))
        {
            throw new InvalidBootstrapSecretException();
        }

        var account = new UserAccount
        {
            Id = Guid.NewGuid(),
            Username = username,
            PasswordHash = Pbkdf2PasswordHasher.Hash(password),
            Role = Role.Admin,
            IsDisabled = false,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            db.Set<BootstrapState>().Add(new BootstrapState
            {
                ConsumedAtUtc = DateTimeOffset.UtcNow,
                ConsumedForUsername = username,
            });
            await db.SaveChangesAsync(cancellationToken); // forces the PK-conflict race to resolve here, before the account exists

            db.UserAccounts.Add(account);
            AddAudit(AuditEventTypes.AccountRegistered, account.Id, null,
                $"First administrator '{username}' provisioned via one-time bootstrap.");
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return account;
        }
        catch (DbUpdateException ex) when (IsPrimaryKeyViolation(ex))
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new BootstrapAlreadyConsumedException();
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new UsernameAlreadyRegisteredException(username);
        }
    }

    // ---------- Login, lockout (rearm-safe, concurrency-safe), MFA gate ----------

    /// <summary>
    /// Password check, disabled-account check, and lockout with correct rearm-after-expiry and
    /// race-safety, all as a single flow:
    ///
    /// 1. A stale (expired) lockout is atomically cleared as the very first step of every login
    ///    attempt — via a conditional UPDATE, not load-modify-save — so a fresh failure streak
    ///    after expiry starts counting from zero and can trigger a genuinely new lockout
    ///    ("rearm"). Two callers racing this same clear cannot double-apply it: the WHERE clause
    ///    only matches while a stale lock still stands, so the second racer's UPDATE simply
    ///    affects zero rows.
    /// 2. A wrong password increments the counter with a single atomic UPDATE (col = col + 1,
    ///    evaluated server-side under row locking — no lost increments under concurrent
    ///    failures), then — only if not already actively locked — atomically sets a new lockout.
    /// 3. A correct password does not simply "succeed": it attempts an atomic conditional clear
    ///    (reset counter, clear lock) that only applies while the account is NOT currently
    ///    actively locked. If a concurrent failing request won the race and set an active lock in
    ///    the meantime, this conditional UPDATE affects zero rows and the correct-password login
    ///    is rejected — the specific fix for "a stale successful-login request cannot clear a
    ///    lockout triggered concurrently."
    ///
    /// Unknown usernames run an equivalent PBKDF2 verification against a fixed dummy hash before
    /// failing, so the unknown-user and wrong-password paths perform the same cryptographic work
    /// and do not differ in a way that leaks account existence via response timing.
    /// </summary>
    public async Task<UserAccount> LoginAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        var account = await db.UserAccounts.AsNoTracking().SingleOrDefaultAsync(u => u.Username == username, cancellationToken);
        if (account is null)
        {
            Pbkdf2PasswordHasher.Verify(password, Pbkdf2PasswordHasher.DummyHashForUnknownUser);
            throw new InvalidLoginException();
        }

        if (account.IsDisabled)
        {
            // Still pays the real PBKDF2 cost so a disabled-account probe isn't itself a distinct
            // timing signal from a normal wrong-password attempt.
            if (account.PasswordHash is not null) Pbkdf2PasswordHasher.Verify(password, account.PasswordHash);
            throw new AccountDisabledException();
        }

        var now = DateTimeOffset.UtcNow;

        // Step 1: rearm a stale lock before doing anything else.
        await db.UserAccounts.Where(u => u.Id == account.Id && u.LockedOutUntilUtc != null && u.LockedOutUntilUtc <= now)
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.FailedLoginAttempts, 0)
                .SetProperty(u => u.LockedOutUntilUtc, (DateTimeOffset?)null), cancellationToken);

        var current = await db.UserAccounts.AsNoTracking().SingleAsync(u => u.Id == account.Id, cancellationToken);
        if (current.LockedOutUntilUtc is { } lockedUntil && lockedUntil > now)
        {
            throw new AccountLockedOutException(lockedUntil); // fast-path reject; final authority is step 3 below for the success path
        }

        var passwordCorrect = account.PasswordHash is not null && Pbkdf2PasswordHasher.Verify(password, account.PasswordHash);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        if (!passwordCorrect)
        {
            var justLockedOutUntil = await RecordFailedAuthenticationAttemptAsync(
                account.Id, username, AuditEventTypes.LoginFailed, "login", cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            if (justLockedOutUntil is { } lockoutUntil)
            {
                throw new AccountLockedOutException(lockoutUntil);
            }
            throw new InvalidLoginException();
        }

        // Correct password: only actually succeeds if the atomic conditional clear affects a row.
        var rowsCleared = await db.UserAccounts
            .Where(u => u.Id == account.Id && (u.LockedOutUntilUtc == null || u.LockedOutUntilUtc <= DateTimeOffset.UtcNow))
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.FailedLoginAttempts, 0)
                .SetProperty(u => u.LockedOutUntilUtc, (DateTimeOffset?)null), cancellationToken);

        if (rowsCleared == 0)
        {
            // A concurrent failing request won the race and locked the account after our earlier
            // check. This correct-password attempt must not be admitted.
            var nowLocked = await db.UserAccounts.AsNoTracking().SingleAsync(u => u.Id == account.Id, cancellationToken);
            await transaction.CommitAsync(cancellationToken); // nothing of ours to persist; just release cleanly
            throw new AccountLockedOutException(nowLocked.LockedOutUntilUtc ?? DateTimeOffset.UtcNow.Add(LockoutDuration));
        }

        var refreshed = await db.UserAccounts.AsNoTracking().SingleAsync(u => u.Id == account.Id, cancellationToken);

        if (refreshed.MfaEnabled)
        {
            // R02 (review finding ALV-001-C01-R01-04): password verification alone is not a
            // successful login while MFA is enabled - that used to be recorded as LoginSucceeded,
            // which was misleading. The real success event now fires only from
            // CompleteMfaChallengeAsync, once the second factor is actually verified.
            var challengeToken = IssueMfaChallenge(refreshed.Id, refreshed.SecurityStamp);
            AddAudit(AuditEventTypes.PasswordVerifiedMfaPending, account.Id, account.Id, $"Password verified for '{username}'; MFA challenge issued.");
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            throw new MfaChallengeRequiredException(challengeToken);
        }

        AddAudit(AuditEventTypes.LoginSucceeded, account.Id, account.Id, $"Successful login for '{username}'.");
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return refreshed;
    }

    // ---------- MFA ----------

    /// <summary>
    /// The challenge is bound to the account's SecurityStamp at issue time (review finding
    /// ALV-001-C01-R01-02): any stamp-rotating operation implicitly invalidates every outstanding
    /// challenge. R03 (review finding ALV-001-C01-R02-01): the challenge is also now backed by a
    /// durable, one-time-consumable <see cref="MfaChallenge"/> row - the protected token carries
    /// only its id (plus a stamp/expiry copy for a fast DB-free rejection of an obviously stale
    /// token), and that row, not the token's mere validity, is what <see cref="CompleteMfaChallengeAsync"/>
    /// atomically consumes on success. Adds to <c>db</c>'s pending change set; the caller is
    /// responsible for calling <c>SaveChangesAsync</c> (matches <c>AddAudit</c>'s existing
    /// convention in this class).
    /// </summary>
    private string IssueMfaChallenge(Guid userAccountId, Guid securityStamp)
    {
        var challengeId = Guid.NewGuid();
        var expiresAtUtc = DateTimeOffset.UtcNow.Add(MfaChallengeLifetime);
        db.Set<MfaChallenge>().Add(new MfaChallenge
        {
            Id = challengeId,
            UserAccountId = userAccountId,
            SecurityStamp = securityStamp,
            ExpiresAtUtc = expiresAtUtc,
        });
        var payload = JsonSerializer.Serialize(new MfaChallengePayload(challengeId, userAccountId, securityStamp, expiresAtUtc));
        return MfaChallengeProtector.Protect(payload);
    }

    private record MfaChallengePayload(Guid ChallengeId, Guid UserAccountId, Guid SecurityStamp, DateTimeOffset ExpiresAtUtc);

    /// <summary>Completes a login that <see cref="LoginAsync"/> paused for an MFA code.</summary>
    public async Task<UserAccount> CompleteMfaChallengeAsync(string challengeToken, string code, CancellationToken cancellationToken = default)
    {
        MfaChallengePayload payload;
        try
        {
            payload = JsonSerializer.Deserialize<MfaChallengePayload>(MfaChallengeProtector.Unprotect(challengeToken))
                ?? throw new InvalidOrExpiredMfaChallengeException();
        }
        catch (Exception ex) when (ex is not InvalidOrExpiredMfaChallengeException)
        {
            throw new InvalidOrExpiredMfaChallengeException();
        }

        if (payload.ExpiresAtUtc < DateTimeOffset.UtcNow)
        {
            throw new InvalidOrExpiredMfaChallengeException();
        }

        // AsNoTracking: an earlier tracked load of this same account elsewhere in this DbContext
        // (e.g. ChangeRoleAsync/SetAccountEnabledAsync during setup) would otherwise shadow this
        // read with a stale SecurityStamp, defeating the very check this method exists to make.
        var account = await db.UserAccounts.AsNoTracking().SingleOrDefaultAsync(u => u.Id == payload.UserAccountId, cancellationToken);
        if (account is null || account.IsDisabled || !account.MfaEnabled || account.MfaSecretProtected is null
            || account.SecurityStamp != payload.SecurityStamp)
        {
            // A stamp mismatch is treated identically to expiry/invalidity - it never reveals
            // *why* the challenge no longer works (e.g. "your session was revoked"), which would
            // itself be an information leak about account state.
            throw new InvalidOrExpiredMfaChallengeException();
        }

        var secret = Convert.FromBase64String(MfaSecretProtector.Unprotect(account.MfaSecretProtected));
        if (Totp.Verify(secret, code))
        {
            // R04 (review finding ALV-001-C01-R03-02): challenge consumption now happens inside
            // the same transaction as the audit write (success or rejection), not as a
            // separately-committing call beforehand - a failure between the two can no longer
            // leave a consumed challenge with no corresponding audit trail.
            await using var successTransaction = await db.Database.BeginTransactionAsync(cancellationToken);
            if (!await TryConsumeChallengeAsync(payload.ChallengeId, account.Id, cancellationToken))
            {
                // Correct code, but this exact challenge was already consumed by an earlier (or
                // concurrently racing) completion - reject the replay rather than mint a second
                // session. Same public exception as any other invalid/expired challenge: it never
                // distinguishes "replayed" from "just expired." The audit trail is privacy-safe:
                // it never logs the token or code.
                AddAudit(AuditEventTypes.MfaChallengeReplayRejected, account.Id, account.Id,
                    $"Rejected an MFA challenge completion attempt for '{account.Username}': the challenge was already consumed, expired, or revoked.");
                await db.SaveChangesAsync(cancellationToken);
                await successTransaction.CommitAsync(cancellationToken);
                throw new InvalidOrExpiredMfaChallengeException();
            }

            AddAudit(AuditEventTypes.MfaChallengeSucceeded, account.Id, account.Id, $"MFA challenge completed via authenticator code for '{account.Username}'.");
            AddAudit(AuditEventTypes.LoginSucceeded, account.Id, account.Id, $"Successful login for '{account.Username}' (MFA).");
            await db.SaveChangesAsync(cancellationToken);
            await successTransaction.CommitAsync(cancellationToken);
            return account;
        }

        // Fall back to a one-time recovery code.
        var recoveryCodes = await db.Set<MfaRecoveryCode>()
            .Where(r => r.UserAccountId == account.Id && r.UsedAtUtc == null && !r.IsPending)
            .ToListAsync(cancellationToken);
        foreach (var recoveryCode in recoveryCodes)
        {
            if (Pbkdf2PasswordHasher.Verify(code, recoveryCode.CodeHash))
            {
                if (TestHook_AfterRecoveryCodeVerifiedBeforeConsumption is not null)
                {
                    await TestHook_AfterRecoveryCodeVerifiedBeforeConsumption();
                }

                // Consume the challenge itself, the specific recovery code, and the resulting
                // audit entry all inside one transaction (R04, review finding
                // ALV-001-C01-R03-02) - a rollback restores all three together.
                await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

                if (!await TryConsumeChallengeAsync(payload.ChallengeId, account.Id, cancellationToken))
                {
                    AddAudit(AuditEventTypes.MfaChallengeReplayRejected, account.Id, account.Id,
                        $"Rejected an MFA challenge completion attempt for '{account.Username}': the challenge was already consumed, expired, or revoked.");
                    await db.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    throw new InvalidOrExpiredMfaChallengeException();
                }

                var rows = await db.Set<MfaRecoveryCode>()
                    .Where(r => r.Id == recoveryCode.Id && r.UsedAtUtc == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(r => r.UsedAtUtc, DateTimeOffset.UtcNow), cancellationToken);
                if (rows == 0)
                {
                    // R05 (review finding ALV-001-C01-R04-02): a DIFFERENT concurrently-completing
                    // challenge already consumed this exact recovery code before this one's
                    // conditional update ran. Rolling back (rather than committing) undoes THIS
                    // challenge's own consumption too, so it is never left partially consumed with
                    // no owning success/failure audit - this attempt genuinely failed with this
                    // code and must not burn the challenge for it. The loop's fallthrough below
                    // (or the next matching code, if any) records the one failure audit.
                    await transaction.RollbackAsync(cancellationToken);
                    continue;
                }

                if (TestHook_AfterRecoveryCodeConsumedBeforeCommit is not null)
                {
                    await TestHook_AfterRecoveryCodeConsumedBeforeCommit();
                }

                AddAudit(AuditEventTypes.MfaRecoveryCodeUsed, account.Id, account.Id, $"One-time recovery code consumed for '{account.Username}'.");
                AddAudit(AuditEventTypes.LoginSucceeded, account.Id, account.Id, $"Successful login for '{account.Username}' (MFA recovery code).");
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return account;
            }
        }

        await using var failureTransaction = await db.Database.BeginTransactionAsync(cancellationToken);
        AddAudit(AuditEventTypes.MfaChallengeFailed, account.Id, account.Id, $"MFA challenge failed (invalid code) for '{account.Username}'.");
        await db.SaveChangesAsync(cancellationToken);
        await failureTransaction.CommitAsync(cancellationToken);
        throw new InvalidMfaCodeException();
    }

    /// <summary>Atomically marks an MFA challenge consumed, gated on it not already being consumed
    /// (or expired/belonging to a different account) - a single conditional UPDATE, not
    /// read-then-write, so two concurrent completions of the exact same challenge can affect at
    /// most one row between them. Returns false if the challenge was already consumed, doesn't
    /// exist, has expired, or belongs to a different account.</summary>
    private async Task<bool> TryConsumeChallengeAsync(Guid challengeId, Guid userAccountId, CancellationToken cancellationToken)
    {
        var rows = await db.Set<MfaChallenge>()
            .Where(c => c.Id == challengeId && c.UserAccountId == userAccountId
                && c.ConsumedAtUtc == null && c.ExpiresAtUtc > DateTimeOffset.UtcNow)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.ConsumedAtUtc, DateTimeOffset.UtcNow), cancellationToken);
        return rows > 0;
    }

    /// <summary>
    /// R02 (review finding ALV-001-C01-R01-01): generates a new TOTP secret and recovery codes as
    /// a PENDING set only - the account's active factor (<see cref="UserAccount.MfaSecretProtected"/>,
    /// <see cref="UserAccount.MfaEnabled"/>) and existing (non-pending) recovery codes are left
    /// completely untouched until <see cref="ConfirmMfaEnrollmentAsync"/> verifies a code against
    /// the new secret. Starting (and abandoning) enrollment can therefore never disable an
    /// established factor. Replacing an already-active factor additionally requires the caller's
    /// current password as step-up reauthentication.
    /// </summary>
    public async Task<(string Base32Secret, IReadOnlyList<string> RecoveryCodes)> BeginMfaEnrollmentAsync(Guid userAccountId, string? currentPassword = null, CancellationToken cancellationToken = default)
    {
        // AsNoTracking + ExecuteUpdateAsync throughout (matching ConfirmMfaEnrollmentAsync's
        // style) rather than a tracked load/mutate/SaveChanges: this method and Confirm can be
        // called back-to-back against the *same* DbContext (every test in this file does exactly
        // that), and a tracked entity left in the change tracker here would shadow Confirm's own
        // fresh reads of the same row with stale in-memory values.
        var account = await db.UserAccounts.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userAccountId, cancellationToken)
            ?? throw new AccountNotFoundException(userAccountId);

        var isReplacement = account.MfaEnabled;
        if (isReplacement)
        {
            if (string.IsNullOrEmpty(currentPassword))
            {
                throw new CurrentPasswordRequiredToReplaceMfaException();
            }

            // R03 (review finding ALV-001-C01-R02-02): the step-up password check shares the
            // exact same lockout boundary as an ordinary login - a stolen authenticated cookie
            // cannot use this endpoint as an unthrottled password oracle. A currently-locked
            // account is rejected here too, before even attempting the comparison.
            if (await RearmAndCheckLockoutAsync(account.Id, cancellationToken) is { } lockedUntil)
            {
                throw new AccountLockedOutException(lockedUntil);
            }

            if (account.PasswordHash is null || !Pbkdf2PasswordHasher.Verify(currentPassword, account.PasswordHash))
            {
                await using var failTransaction = await db.Database.BeginTransactionAsync(cancellationToken);
                var justLockedOutUntil = await RecordFailedAuthenticationAttemptAsync(
                    account.Id, account.Username, AuditEventTypes.MfaStepUpFailed, "MFA step-up reauthentication", cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
                await failTransaction.CommitAsync(cancellationToken);

                if (justLockedOutUntil is { } newLockoutUntil)
                {
                    throw new AccountLockedOutException(newLockoutUntil);
                }
                throw new InvalidCurrentPasswordException();
            }

            if (TestHook_BeforeStepUpConditionalReset is not null)
            {
                await TestHook_BeforeStepUpConditionalReset();
            }

            // Correct step-up password: reset the counter, matching a successful login's
            // convention exactly - the conditional clear's affected-row count is checked (R04,
            // review finding ALV-001-C01-R03-01), not just its WHERE clause. A concurrent failing
            // request that crosses the lockout threshold between the check above and this update
            // wins the race: this correct-password attempt is rejected and no pending MFA
            // material is created or replaced.
            var rowsCleared = await db.UserAccounts
                .Where(u => u.Id == account.Id && (u.LockedOutUntilUtc == null || u.LockedOutUntilUtc <= DateTimeOffset.UtcNow))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(u => u.FailedLoginAttempts, 0)
                    .SetProperty(u => u.LockedOutUntilUtc, (DateTimeOffset?)null), cancellationToken);

            if (rowsCleared == 0)
            {
                var nowLocked = await db.UserAccounts.AsNoTracking().SingleAsync(u => u.Id == account.Id, cancellationToken);
                throw new AccountLockedOutException(nowLocked.LockedOutUntilUtc ?? DateTimeOffset.UtcNow.Add(LockoutDuration));
            }
        }

        var secretBytes = Totp.GenerateSecret();
        var protectedSecret = MfaSecretProtector.Protect(Convert.ToBase64String(secretBytes));

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        await db.UserAccounts.Where(u => u.Id == userAccountId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.PendingMfaSecretProtected, protectedSecret), cancellationToken);

        var recoveryCodes = new List<string>(RecoveryCodeCount);
        var existingPending = db.Set<MfaRecoveryCode>().Where(r => r.UserAccountId == userAccountId && r.IsPending);
        db.Set<MfaRecoveryCode>().RemoveRange(existingPending); // only ever removes a prior, still-unconfirmed pending set
        for (var i = 0; i < RecoveryCodeCount; i++)
        {
            var raw = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(6));
            recoveryCodes.Add(raw);
            db.Set<MfaRecoveryCode>().Add(new MfaRecoveryCode
            {
                Id = Guid.NewGuid(),
                UserAccountId = userAccountId,
                CodeHash = Pbkdf2PasswordHasher.Hash(raw),
                CreatedAtUtc = DateTimeOffset.UtcNow,
                IsPending = true,
            });
        }

        AddAudit(isReplacement ? AuditEventTypes.MfaReplacementStarted : AuditEventTypes.MfaEnrollmentStarted, account.Id, account.Id,
            isReplacement ? $"MFA factor replacement started for '{account.Username}'; existing factor remains active until confirmed." : $"MFA enrollment started for '{account.Username}'.");
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (Totp.ToBase32(secretBytes), recoveryCodes);
    }

    public async Task ConfirmMfaEnrollmentAsync(Guid userAccountId, string code, CancellationToken cancellationToken = default)
    {
        // AsNoTracking deliberately: every mutation below goes through ExecuteUpdateAsync (raw
        // SQL, bypassing the change tracker) for atomicity, so a tracked copy of this entity would
        // sit stale in the context's identity map afterward and shadow fresh reads by any other
        // code sharing this DbContext instance - the same reason LoginAsync loads untracked too.
        var account = await db.UserAccounts.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userAccountId, cancellationToken)
            ?? throw new AccountNotFoundException(userAccountId);
        if (account.PendingMfaSecretProtected is null)
        {
            throw new MfaNotEnabledException();
        }

        var wasAlreadyEnabled = account.MfaEnabled;
        var secret = Convert.FromBase64String(MfaSecretProtector.Unprotect(account.PendingMfaSecretProtected));
        if (!Totp.Verify(secret, code))
        {
            throw new InvalidMfaCodeException();
        }

        // Atomic, concurrency-safe promotion: gated on PendingMfaSecretProtected still being set,
        // so two racing confirmations (or a confirmation racing a fresh BeginMfaEnrollmentAsync
        // call) can promote at most once. A losing racer's 0-row update is treated the same as
        // "nothing pending" rather than silently double-applying.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var pendingSecretAtStart = account.PendingMfaSecretProtected;
        var rows = await db.UserAccounts
            .Where(u => u.Id == userAccountId && u.PendingMfaSecretProtected == pendingSecretAtStart)
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.MfaSecretProtected, u => u.PendingMfaSecretProtected)
                .SetProperty(u => u.PendingMfaSecretProtected, (string?)null)
                .SetProperty(u => u.MfaEnabled, true)
                .SetProperty(u => u.SecurityStamp, Guid.NewGuid()), cancellationToken);
        if (rows == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new MfaNotEnabledException(); // already confirmed (or replaced again) by a concurrent request
        }

        var oldActiveCodes = db.Set<MfaRecoveryCode>().Where(r => r.UserAccountId == userAccountId && !r.IsPending);
        db.Set<MfaRecoveryCode>().RemoveRange(oldActiveCodes);
        await db.Set<MfaRecoveryCode>()
            .Where(r => r.UserAccountId == userAccountId && r.IsPending)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.IsPending, false), cancellationToken);

        AddAudit(wasAlreadyEnabled ? AuditEventTypes.MfaReplaced : AuditEventTypes.MfaEnabled, account.Id, account.Id,
            wasAlreadyEnabled ? $"MFA factor replaced for '{account.Username}'; all sessions and prior challenges invalidated." : $"MFA enabled for '{account.Username}'.");
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    // ---------- Administrative operations ----------

    public async Task<UserAccount> ChangeRoleAsync(Guid targetUserAccountId, Role newRole, Guid performedByUserAccountId, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(newRole))
        {
            throw new ArgumentOutOfRangeException(nameof(newRole), newRole, "Unknown role.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var account = await db.UserAccounts.SingleOrDefaultAsync(u => u.Id == targetUserAccountId, cancellationToken)
            ?? throw new AccountNotFoundException(targetUserAccountId);

        var previousRole = account.Role;
        if (previousRole != newRole)
        {
            account.Role = newRole;
            BumpSecurityStamp(account);
            AddAudit(AuditEventTypes.RoleChanged, account.Id, performedByUserAccountId,
                $"Role changed for '{account.Username}' from {previousRole} to {newRole}.");
            await db.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return account;
    }

    public async Task<UserAccount> SetAccountEnabledAsync(Guid targetUserAccountId, bool enabled, Guid performedByUserAccountId, CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var account = await db.UserAccounts.SingleOrDefaultAsync(u => u.Id == targetUserAccountId, cancellationToken)
            ?? throw new AccountNotFoundException(targetUserAccountId);

        if (account.IsDisabled != !enabled)
        {
            account.IsDisabled = !enabled;
            BumpSecurityStamp(account);
            AddAudit(enabled ? AuditEventTypes.AccountEnabled : AuditEventTypes.AccountDisabled, account.Id, performedByUserAccountId,
                $"Account '{account.Username}' {(enabled ? "enabled" : "disabled")}.");
            await db.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return account;
    }

    private const int MinSessionTimeoutMinutes = 5;
    private const int MaxSessionTimeoutMinutes = 1440; // 24 hours

    /// <summary>R02 (review finding ALV-001-C01-R01-05): an authorized, validated way to configure
    /// an account's session timeout - previously the field existed and was enforced but had no
    /// API/UI to actually set it.</summary>
    public async Task<UserAccount> SetSessionTimeoutAsync(Guid targetUserAccountId, int sessionTimeoutMinutes, Guid performedByUserAccountId, CancellationToken cancellationToken = default)
    {
        if (sessionTimeoutMinutes < MinSessionTimeoutMinutes || sessionTimeoutMinutes > MaxSessionTimeoutMinutes)
        {
            throw new InvalidSessionTimeoutException(sessionTimeoutMinutes);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var account = await db.UserAccounts.SingleOrDefaultAsync(u => u.Id == targetUserAccountId, cancellationToken)
            ?? throw new AccountNotFoundException(targetUserAccountId);

        if (account.SessionTimeoutMinutes != sessionTimeoutMinutes)
        {
            var previous = account.SessionTimeoutMinutes;
            account.SessionTimeoutMinutes = sessionTimeoutMinutes;
            AddAudit(AuditEventTypes.SessionTimeoutChanged, account.Id, performedByUserAccountId,
                $"Session timeout for '{account.Username}' changed from {previous} to {sessionTimeoutMinutes} minutes.");
            await db.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return account;
    }

    /// <summary>Admin-issued, one-time, short-lived reset token. The raw token is returned once and never stored.</summary>
    public async Task<string> IssuePasswordResetTokenAsync(Guid targetUserAccountId, Guid performedByUserAccountId, CancellationToken cancellationToken = default)
    {
        _ = await db.UserAccounts.SingleOrDefaultAsync(u => u.Id == targetUserAccountId, cancellationToken)
            ?? throw new AccountNotFoundException(targetUserAccountId);

        var rawToken = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24));

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        db.Set<PasswordResetToken>().Add(new PasswordResetToken
        {
            Id = Guid.NewGuid(),
            UserAccountId = targetUserAccountId,
            TokenHash = Pbkdf2PasswordHasher.Hash(rawToken),
            ExpiresAtUtc = DateTimeOffset.UtcNow.Add(ResetTokenLifetime),
            CreatedAtUtc = DateTimeOffset.UtcNow,
        });
        AddAudit(AuditEventTypes.PasswordResetIssued, targetUserAccountId, performedByUserAccountId, "Password reset token issued.");
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return rawToken;
    }

    public async Task ResetPasswordAsync(Guid targetUserAccountId, string rawToken, string newPassword, CancellationToken cancellationToken = default)
    {
        var candidates = await db.Set<PasswordResetToken>()
            .Where(t => t.UserAccountId == targetUserAccountId && t.UsedAtUtc == null && t.ExpiresAtUtc > DateTimeOffset.UtcNow)
            .ToListAsync(cancellationToken);

        var match = candidates.FirstOrDefault(t => Pbkdf2PasswordHasher.Verify(rawToken, t.TokenHash));
        if (match is null)
        {
            throw new InvalidOrExpiredResetTokenException();
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var rows = await db.Set<PasswordResetToken>()
            .Where(t => t.Id == match.Id && t.UsedAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.UsedAtUtc, DateTimeOffset.UtcNow), cancellationToken);
        if (rows == 0)
        {
            await transaction.CommitAsync(cancellationToken);
            throw new InvalidOrExpiredResetTokenException(); // consumed by a concurrent request
        }

        var account = await db.UserAccounts.SingleAsync(u => u.Id == targetUserAccountId, cancellationToken);
        account.PasswordHash = Pbkdf2PasswordHasher.Hash(newPassword);
        account.FailedLoginAttempts = 0;
        account.LockedOutUntilUtc = null;
        BumpSecurityStamp(account);
        AddAudit(AuditEventTypes.PasswordReset, account.Id, account.Id, "Password reset completed.");
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>Invalidates every existing session for the account by rotating its security stamp.</summary>
    public async Task RevokeAllSessionsAsync(Guid targetUserAccountId, Guid performedByUserAccountId, CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var account = await db.UserAccounts.SingleOrDefaultAsync(u => u.Id == targetUserAccountId, cancellationToken)
            ?? throw new AccountNotFoundException(targetUserAccountId);
        BumpSecurityStamp(account);
        AddAudit(AuditEventTypes.SessionsRevoked, account.Id, performedByUserAccountId, $"All sessions revoked for '{account.Username}'.");
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    // ---------- helpers ----------

    /// <summary>
    /// R03 (review finding ALV-001-C01-R02-02): the shared lockout boundary an authentication-
    /// adjacent failure counts against - extracted from <see cref="LoginAsync"/>'s original
    /// inline logic so <see cref="BeginMfaEnrollmentAsync"/>'s step-up password check can share
    /// the exact same threshold/lockout state rather than being a separate, unthrottled password
    /// oracle. Increments the account's <see cref="UserAccount.FailedLoginAttempts"/> counter
    /// (atomically, via <c>ExecuteUpdateAsync</c>) and locks the account if the threshold is
    /// reached. Returns the new lockout timestamp if this call is the one that crossed the
    /// threshold, otherwise null. Caller is responsible for wrapping this in a transaction and
    /// calling <c>SaveChangesAsync</c> (this method only adds the audit entry to the pending
    /// change set, matching <c>AddAudit</c>'s existing convention).
    /// </summary>
    private async Task<DateTimeOffset?> RecordFailedAuthenticationAttemptAsync(
        Guid accountId, string username, string auditEventType, string attemptKind, CancellationToken cancellationToken)
    {
        await db.UserAccounts.Where(u => u.Id == accountId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.FailedLoginAttempts, u => u.FailedLoginAttempts + 1), cancellationToken);
        var afterIncrement = await db.UserAccounts.AsNoTracking().SingleAsync(u => u.Id == accountId, cancellationToken);

        AddAudit(auditEventType, accountId, accountId,
            $"Failed {attemptKind} attempt {afterIncrement.FailedLoginAttempts} of {MaxFailedLoginAttempts} for '{username}'.");

        if (afterIncrement.FailedLoginAttempts >= MaxFailedLoginAttempts)
        {
            var lockoutUntil = DateTimeOffset.UtcNow.Add(LockoutDuration);
            var rowsLocked = await db.UserAccounts
                .Where(u => u.Id == accountId && (u.LockedOutUntilUtc == null || u.LockedOutUntilUtc <= DateTimeOffset.UtcNow))
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.LockedOutUntilUtc, lockoutUntil), cancellationToken);
            if (rowsLocked > 0)
            {
                AddAudit(AuditEventTypes.AccountLockedOut, accountId, accountId,
                    $"Account '{username}' locked out until {lockoutUntil:O} after {afterIncrement.FailedLoginAttempts} failed attempts.");
                return lockoutUntil;
            }
        }
        return null;
    }

    /// <summary>Rearms a stale lock (clears it if expired) and returns the account's current
    /// lockout timestamp, if still actively locked. Shared by <see cref="LoginAsync"/> and
    /// <see cref="BeginMfaEnrollmentAsync"/>'s step-up check - both must rearm before deciding
    /// whether an attempt is currently blocked.</summary>
    private async Task<DateTimeOffset?> RearmAndCheckLockoutAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        await db.UserAccounts.Where(u => u.Id == accountId && u.LockedOutUntilUtc != null && u.LockedOutUntilUtc <= now)
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.FailedLoginAttempts, 0)
                .SetProperty(u => u.LockedOutUntilUtc, (DateTimeOffset?)null), cancellationToken);
        var current = await db.UserAccounts.AsNoTracking().SingleAsync(u => u.Id == accountId, cancellationToken);
        return current.LockedOutUntilUtc is { } lockedUntil && lockedUntil > now ? lockedUntil : null;
    }

    private static void BumpSecurityStamp(UserAccount account) => account.SecurityStamp = Guid.NewGuid();

    private void AddAudit(string eventType, Guid targetUserAccountId, Guid? performedByUserAccountId, string details)
    {
        db.AuditLogEntries.Add(new AuditLogEntry
        {
            Id = Guid.NewGuid(),
            EventType = eventType,
            TargetUserAccountId = targetUserAccountId,
            PerformedByUserAccountId = performedByUserAccountId,
            Details = details,
            TimestampUtc = DateTimeOffset.UtcNow,
        });
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex) =>
        ex.InnerException is SqlException sqlEx &&
        sqlEx.Errors.Cast<SqlError>().Any(e => e.Number is UniqueConstraintViolation or UniqueIndexViolation);

    private const int PrimaryKeyViolation = 2627; // SQL Server reuses 2627 for both PK and unique-constraint violations

    private static bool IsPrimaryKeyViolation(DbUpdateException ex) =>
        ex.InnerException is SqlException sqlEx &&
        sqlEx.Errors.Cast<SqlError>().Any(e => e.Number == PrimaryKeyViolation);
}
