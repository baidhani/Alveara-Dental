using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Identity;

/// <summary>Thrown when registration is attempted for a username that already exists.</summary>
public class UsernameAlreadyRegisteredException(string username) : Exception($"Username '{username}' is already registered.");

/// <summary>
/// Thrown for a login attempt against an unknown username or an incorrect password. Deliberately
/// gives no indication which of the two was wrong, so a login endpoint can't be used to enumerate
/// valid usernames.
/// </summary>
public class InvalidLoginException() : Exception("Invalid username or password.");

/// <summary>Thrown when login is attempted against an account currently in lockout.</summary>
public class AccountLockedOutException(DateTimeOffset lockedOutUntilUtc)
    : Exception($"Account is locked until {lockedOutUntilUtc:O}.")
{
    public DateTimeOffset LockedOutUntilUtc { get; } = lockedOutUntilUtc;
}

/// <summary>Thrown when an operation targets a user account id that does not exist.</summary>
public class AccountNotFoundException(Guid userAccountId) : Exception($"No account with id '{userAccountId}' exists.");

/// <summary>
/// STORY-001: account registration. The new account and its registration audit entry are
/// committed in one transaction — an audit write can never succeed or fail separately from the
/// change it documents. Username uniqueness is enforced by the database's unique index, not a
/// check-then-insert race (the release note explicitly calls out holding under concurrent
/// access), so two simultaneous registrations for the same username reliably leave exactly one
/// account and raise a clear exception for the loser rather than silently duplicating or
/// corrupting state.
/// </summary>
public class AccountService(AlveraDbContext db)
{
    private const int UniqueConstraintViolation = 2627;
    private const int UniqueIndexViolation = 2601;
    private const int MaxFailedLoginAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    public async Task<UserAccount> RegisterAsync(string username, string password, Role role, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(role))
        {
            throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown role.");
        }

        var account = new UserAccount
        {
            Id = Guid.NewGuid(),
            Username = username,
            PasswordHash = Pbkdf2PasswordHasher.Hash(password),
            Role = role,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            db.UserAccounts.Add(account);
            db.AuditLogEntries.Add(new AuditLogEntry
            {
                Id = Guid.NewGuid(),
                EventType = AuditEventTypes.AccountRegistered,
                TargetUserAccountId = account.Id,
                PerformedByUserAccountId = null,
                Details = $"Account '{username}' registered with role {role}.",
                TimestampUtc = DateTimeOffset.UtcNow,
            });
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

    /// <summary>
    /// STORY-001 login + lockout. The failed-attempt counter is incremented with an atomic
    /// <c>ExecuteUpdateAsync</c> (not load-modify-save), so two simultaneous wrong-password
    /// attempts against the same account can never lose an increment to a race — the same
    /// concurrency pattern ALV-N002's background-job claiming already established in this
    /// codebase. A locked-out account rejects login — including with the correct password —
    /// until <see cref="UserAccount.LockedOutUntilUtc"/> passes. Never reveals whether a failure
    /// was "unknown username" or "wrong password", to avoid username enumeration.
    /// </summary>
    public async Task<UserAccount> LoginAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        var account = await db.UserAccounts.AsNoTracking().SingleOrDefaultAsync(u => u.Username == username, cancellationToken);
        if (account is null)
        {
            throw new InvalidLoginException();
        }

        if (account.LockedOutUntilUtc is { } lockedUntil && lockedUntil > DateTimeOffset.UtcNow)
        {
            throw new AccountLockedOutException(lockedUntil);
        }

        var passwordCorrect = account.PasswordHash is not null && Pbkdf2PasswordHasher.Verify(password, account.PasswordHash);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        if (!passwordCorrect)
        {
            await db.UserAccounts.Where(u => u.Id == account.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.FailedLoginAttempts, u => u.FailedLoginAttempts + 1), cancellationToken);
            var afterIncrement = await db.UserAccounts.AsNoTracking().SingleAsync(u => u.Id == account.Id, cancellationToken);

            db.AuditLogEntries.Add(new AuditLogEntry
            {
                Id = Guid.NewGuid(),
                EventType = AuditEventTypes.LoginFailed,
                TargetUserAccountId = account.Id,
                PerformedByUserAccountId = account.Id,
                Details = $"Failed login attempt {afterIncrement.FailedLoginAttempts} of {MaxFailedLoginAttempts} for '{username}'.",
                TimestampUtc = DateTimeOffset.UtcNow,
            });

            if (afterIncrement.FailedLoginAttempts >= MaxFailedLoginAttempts && afterIncrement.LockedOutUntilUtc is null)
            {
                var lockoutUntil = DateTimeOffset.UtcNow.Add(LockoutDuration);
                await db.UserAccounts.Where(u => u.Id == account.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(u => u.LockedOutUntilUtc, lockoutUntil), cancellationToken);
                db.AuditLogEntries.Add(new AuditLogEntry
                {
                    Id = Guid.NewGuid(),
                    EventType = AuditEventTypes.AccountLockedOut,
                    TargetUserAccountId = account.Id,
                    PerformedByUserAccountId = account.Id,
                    Details = $"Account '{username}' locked out until {lockoutUntil:O} after {afterIncrement.FailedLoginAttempts} failed attempts.",
                    TimestampUtc = DateTimeOffset.UtcNow,
                });
            }

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            throw new InvalidLoginException();
        }

        await db.UserAccounts.Where(u => u.Id == account.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.FailedLoginAttempts, 0)
                .SetProperty(u => u.LockedOutUntilUtc, (DateTimeOffset?)null), cancellationToken);
        db.AuditLogEntries.Add(new AuditLogEntry
        {
            Id = Guid.NewGuid(),
            EventType = AuditEventTypes.LoginSucceeded,
            TargetUserAccountId = account.Id,
            PerformedByUserAccountId = account.Id,
            Details = $"Successful login for '{username}'.",
            TimestampUtc = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await db.UserAccounts.AsNoTracking().SingleAsync(u => u.Id == account.Id, cancellationToken);
    }

    /// <summary>
    /// STORY-001: "All account and role changes are audited." Registration and login/lockout
    /// audit account changes; this is the role-change half. A no-op change (new role equals the
    /// current one) writes nothing — there is no actual change to audit.
    /// </summary>
    public async Task<UserAccount> ChangeRoleAsync(Guid targetUserAccountId, Role newRole, Guid performedByUserAccountId, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(newRole))
        {
            throw new ArgumentOutOfRangeException(nameof(newRole), newRole, "Unknown role.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var account = await db.UserAccounts.SingleOrDefaultAsync(u => u.Id == targetUserAccountId, cancellationToken);
        if (account is null)
        {
            throw new AccountNotFoundException(targetUserAccountId);
        }

        var previousRole = account.Role;
        if (previousRole != newRole)
        {
            account.Role = newRole;
            db.AuditLogEntries.Add(new AuditLogEntry
            {
                Id = Guid.NewGuid(),
                EventType = AuditEventTypes.RoleChanged,
                TargetUserAccountId = account.Id,
                PerformedByUserAccountId = performedByUserAccountId,
                Details = $"Role changed for '{account.Username}' from {previousRole} to {newRole}.",
                TimestampUtc = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return account;
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex) =>
        ex.InnerException is SqlException sqlEx &&
        sqlEx.Errors.Cast<SqlError>().Any(e => e.Number is UniqueConstraintViolation or UniqueIndexViolation);
}
