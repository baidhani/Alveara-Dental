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
            await db.UserAccounts.Where(u => u.Id == account.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.FailedLoginAttempts, u => u.FailedLoginAttempts + 1), cancellationToken);
            var afterIncrement = await db.UserAccounts.AsNoTracking().SingleAsync(u => u.Id == account.Id, cancellationToken);

            AddAudit(AuditEventTypes.LoginFailed, account.Id, account.Id,
                $"Failed login attempt {afterIncrement.FailedLoginAttempts} of {MaxFailedLoginAttempts} for '{username}'.");

            var justLockedOut = false;
            DateTimeOffset lockoutUntil = default;
            if (afterIncrement.FailedLoginAttempts >= MaxFailedLoginAttempts)
            {
                lockoutUntil = DateTimeOffset.UtcNow.Add(LockoutDuration);
                var rowsLocked = await db.UserAccounts
                    .Where(u => u.Id == account.Id && (u.LockedOutUntilUtc == null || u.LockedOutUntilUtc <= DateTimeOffset.UtcNow))
                    .ExecuteUpdateAsync(s => s.SetProperty(u => u.LockedOutUntilUtc, lockoutUntil), cancellationToken);
                justLockedOut = rowsLocked > 0;
                if (justLockedOut)
                {
                    AddAudit(AuditEventTypes.AccountLockedOut, account.Id, account.Id,
                        $"Account '{username}' locked out until {lockoutUntil:O} after {afterIncrement.FailedLoginAttempts} failed attempts.");
                }
            }

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            if (justLockedOut)
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
            AddAudit(AuditEventTypes.LoginSucceeded, account.Id, account.Id, $"Password verified for '{username}'; MFA challenge issued.");
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            throw new MfaChallengeRequiredException(IssueMfaChallengeToken(refreshed.Id));
        }

        AddAudit(AuditEventTypes.LoginSucceeded, account.Id, account.Id, $"Successful login for '{username}'.");
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return refreshed;
    }

    // ---------- MFA ----------

    public string IssueMfaChallengeToken(Guid userAccountId)
    {
        var payload = JsonSerializer.Serialize(new MfaChallengePayload(userAccountId, DateTimeOffset.UtcNow.Add(MfaChallengeLifetime)));
        return MfaChallengeProtector.Protect(payload);
    }

    private record MfaChallengePayload(Guid UserAccountId, DateTimeOffset ExpiresAtUtc);

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

        var account = await db.UserAccounts.SingleOrDefaultAsync(u => u.Id == payload.UserAccountId, cancellationToken);
        if (account is null || account.IsDisabled || !account.MfaEnabled || account.MfaSecretProtected is null)
        {
            throw new InvalidOrExpiredMfaChallengeException();
        }

        var secret = Convert.FromBase64String(MfaSecretProtector.Unprotect(account.MfaSecretProtected));
        if (Totp.Verify(secret, code))
        {
            return account;
        }

        // Fall back to a one-time recovery code.
        var recoveryCodes = await db.Set<MfaRecoveryCode>()
            .Where(r => r.UserAccountId == account.Id && r.UsedAtUtc == null)
            .ToListAsync(cancellationToken);
        foreach (var recoveryCode in recoveryCodes)
        {
            if (Pbkdf2PasswordHasher.Verify(code, recoveryCode.CodeHash))
            {
                await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
                var rows = await db.Set<MfaRecoveryCode>()
                    .Where(r => r.Id == recoveryCode.Id && r.UsedAtUtc == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(r => r.UsedAtUtc, DateTimeOffset.UtcNow), cancellationToken);
                if (rows == 0)
                {
                    // Another concurrent request already consumed this exact code.
                    await transaction.CommitAsync(cancellationToken);
                    continue;
                }
                await transaction.CommitAsync(cancellationToken);
                return account;
            }
        }

        throw new InvalidMfaCodeException();
    }

    /// <summary>Generates a new TOTP secret and recovery codes for the account. MFA is not yet
    /// active — <see cref="ConfirmMfaEnrollmentAsync"/> must verify one code first.</summary>
    public async Task<(string Base32Secret, IReadOnlyList<string> RecoveryCodes)> BeginMfaEnrollmentAsync(Guid userAccountId, CancellationToken cancellationToken = default)
    {
        var account = await db.UserAccounts.SingleOrDefaultAsync(u => u.Id == userAccountId, cancellationToken)
            ?? throw new AccountNotFoundException(userAccountId);

        var secretBytes = Totp.GenerateSecret();
        account.MfaSecretProtected = MfaSecretProtector.Protect(Convert.ToBase64String(secretBytes));
        account.MfaEnabled = false; // not active until confirmed

        var recoveryCodes = new List<string>(RecoveryCodeCount);
        var existing = db.Set<MfaRecoveryCode>().Where(r => r.UserAccountId == userAccountId);
        db.Set<MfaRecoveryCode>().RemoveRange(existing);
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
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        return (Totp.ToBase32(secretBytes), recoveryCodes);
    }

    public async Task ConfirmMfaEnrollmentAsync(Guid userAccountId, string code, CancellationToken cancellationToken = default)
    {
        var account = await db.UserAccounts.SingleOrDefaultAsync(u => u.Id == userAccountId, cancellationToken)
            ?? throw new AccountNotFoundException(userAccountId);
        if (account.MfaSecretProtected is null)
        {
            throw new MfaNotEnabledException();
        }

        var secret = Convert.FromBase64String(MfaSecretProtector.Unprotect(account.MfaSecretProtected));
        if (!Totp.Verify(secret, code))
        {
            throw new InvalidMfaCodeException();
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        account.MfaEnabled = true;
        BumpSecurityStamp(account);
        AddAudit(AuditEventTypes.MfaEnabled, account.Id, account.Id, $"MFA enabled for '{account.Username}'.");
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
