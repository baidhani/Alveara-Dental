using System.Security.Cryptography;
using System.Text;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Alveara.Api.Architecture.Identity;

/// <summary>Setup can no longer be done here: a Deployment Administrator exists or setup was completed once (BootstrapState).</summary>
public class SetupAlreadyCompletedException() : Exception("First-run setup has already been completed.");

/// <summary>Every setup refusal a caller may see is this one, whatever the cause (wrong, expired, used, voided or missing token; setup no longer available).</summary>
public class SetupFailedException() : Exception("Setup failed.");

/// <summary>The token verified but the chosen username or password is not acceptable. Only reachable with a valid token, so it discloses nothing to a caller without one.</summary>
public class SetupInputInvalidException(string code) : Exception(code)
{
    public string Code { get; } = code;
}

/// <summary>The one-time token, returned once to the setup CLI so it can show it on the installer's console. Never logged, stored or audited.</summary>
public sealed record IssuedSetupToken(string Token, DateTimeOffset ExpiresAtUtc);

/// <summary>
/// ALV-N030 (brief sections 3, 4, 6; review-approved R04): first-run setup of a never-set-up installation.
/// <list type="bullet">
/// <item>The installer's CLI command issues a one-time token: 32 random bytes, shown once; only its SHA-256 hash is stored. Issuing voids every earlier unused token.
/// Refused once a Deployment Administrator exists or setup was completed.</item>
/// <item>The setup route checks the token (hash lookup; expiry 15 minutes; single use; five failures void it), then the inputs, then - in one transaction -
/// consumes the token, writes the <see cref="BootstrapState"/> singleton (its primary key makes setup exactly-once under a race), creates the account-only
/// Deployment Administrator (Admin role, <see cref="AccountKind.DeploymentAdministrator"/>; the filtered unique index is a second backstop) with the password the
/// operator chose (no forced change), stages the sign-in presentation administrator as the old bootstrap did, and audits.</item>
/// </list>
/// Failure behaviour: every token failure is the same <see cref="SetupFailedException"/>; a failed transaction commits nothing and leaves the token usable until it
/// expires; a lost token is replaced by issuing a new one. The token, the password and the username tried are never logged or audited.
/// </summary>
public class DeploymentSetupService(AlveraDbContext db, IPracticeClock clock)
{
    public static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(15);
    public const int MaxFailedAttempts = 5;
    public const int MinimumPasswordLength = 12;

    /// <summary>True while this installation has never been set up: no Deployment Administrator and no BootstrapState row.</summary>
    public async Task<bool> SetupAvailableAsync(CancellationToken ct) =>
        !await db.UserAccounts.AnyAsync(u => u.AccountKind == AccountKind.DeploymentAdministrator, ct)
        && !await db.BootstrapStates.AnyAsync(ct);

    public async Task<IssuedSetupToken> IssueTokenAsync(CancellationToken ct)
    {
        if (!await SetupAvailableAsync(ct)) throw new SetupAlreadyCompletedException();
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var now = clock.UtcNow;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.SetupTokens.Where(t => t.ConsumedAtUtc == null && t.VoidedAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.VoidedAtUtc, now), ct);
        var row = new SetupToken { Id = Guid.NewGuid(), TokenHash = Hash(token), IssuedAtUtc = now, ExpiresAtUtc = now + TokenLifetime };
        db.SetupTokens.Add(row);
        AuditService.Record(db, AuditEventTypes.SetupTokenIssued, entityType: "SetupToken", row.Id, null, $"A first-run setup token was issued; it expires at {row.ExpiresAtUtc:O}.");
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new IssuedSetupToken(token, row.ExpiresAtUtc);
    }

    public async Task<UserAccount> CompleteAsync(string? token, string? username, string? password, CancellationToken ct)
    {
        if (!await SetupAvailableAsync(ct)) throw new SetupFailedException();
        var now = clock.UtcNow;
        var hash = Hash(token ?? string.Empty);
        var row = string.IsNullOrEmpty(token) ? null
            : await db.SetupTokens.SingleOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (row is null || row.ConsumedAtUtc is not null || row.VoidedAtUtc is not null || now >= row.ExpiresAtUtc)
        {
            await CountFailureAsync(now, ct);
            throw new SetupFailedException();
        }

        // The token is valid: only now are the inputs checked, so these answers disclose nothing to a caller without one.
        var name = AccountService.NormalizeUsername(username);
        if (name.Length == 0) throw new SetupInputInvalidException("username_required");
        if ((password ?? string.Empty).Length < MinimumPasswordLength) throw new SetupInputInvalidException("password_too_short");

        var account = new UserAccount
        {
            Id = Guid.NewGuid(), Username = name, PasswordHash = Pbkdf2PasswordHasher.Hash(password!), Role = Role.Admin, IsDisabled = false,
            AccountKind = AccountKind.DeploymentAdministrator, MustChangePassword = false, CreatedAtUtc = now,
        };
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var consumed = await db.SetupTokens.Where(t => t.Id == row.Id && t.ConsumedAtUtc == null && t.VoidedAtUtc == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.ConsumedAtUtc, now), ct);
            if (consumed != 1) throw new SetupFailedException();   // a concurrent request used it first
            db.BootstrapStates.Add(new BootstrapState { ConsumedAtUtc = now, ConsumedForUsername = name });
            await db.SaveChangesAsync(ct);                        // the singleton's primary key resolves a race here, before the account exists
            db.UserAccounts.Add(account);
            AuditService.Record(db, AuditEventTypes.DeploymentAdministratorCreated, entityType: "UserAccount", account.Id, null,
                "First-run setup created the Deployment Administrator.");
            AccountPermissionService.StageBootstrapHolder(db, account.Id, name);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return account;
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2627 or 2601 })
        {
            await tx.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            if (await db.UserAccounts.AnyAsync(u => u.Username == name && u.Id != account.Id, ct) && await SetupAvailableAsync(ct))
                throw new SetupInputInvalidException("username_taken");
            throw new SetupFailedException();
        }
    }

    /// <summary>A failed attempt counts against the current (newest usable) token; the fifth failure voids it and records one SetupTokenRejected entry.</summary>
    private async Task CountFailureAsync(DateTimeOffset now, CancellationToken ct)
    {
        var current = await db.SetupTokens.Where(t => t.ConsumedAtUtc == null && t.VoidedAtUtc == null).OrderByDescending(t => t.IssuedAtUtc).FirstOrDefaultAsync(ct);
        if (current is null) return;
        current.FailedAttempts++;
        if (current.FailedAttempts >= MaxFailedAttempts)
        {
            current.VoidedAtUtc = now;
            AuditService.Record(db, AuditEventTypes.SetupTokenRejected, entityType: "SetupToken", current.Id, null,
                $"The first-run setup token was voided after {MaxFailedAttempts} failed attempts.");
        }
        await db.SaveChangesAsync(ct);
    }

    internal static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
}
