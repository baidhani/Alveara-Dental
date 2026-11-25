using System.Security.Cryptography;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Alveara.Api.Architecture.Identity;

/// <summary>Recovery is only for a set-up installation; before setup, first-run setup is the way in. The CLI exits 2.</summary>
public sealed class RecoveryNotAvailableException() : Exception("This installation has not been set up yet; use first-run setup.");

/// <summary>Every token failure, and a username that is not the existing Deployment Administrator's, is the same answer (400 <c>recovery_failed</c>).</summary>
public sealed class RecoveryFailedException() : Exception("Recovery failed.");

public enum DeploymentAdministratorRecoveryMethod { Created, Reset }

public sealed record IssuedRecoveryToken(string Token, DateTimeOffset ExpiresAtUtc);

/// <summary>
/// ALV-N030-C01 (brief 18.6, approved R10): Deployment Administrator recovery with a one-time console token - the same trust model as first-run setup: whoever can
/// run the installer's command on the server machine holds the authority.
/// <list type="bullet">
/// <item><see cref="IssueTokenAsync"/> (CLI <c>recovery issue-token</c>): only once the installation is set up. 32 random bytes shown once; only the SHA-256 is
/// stored, with <see cref="SetupTokenPurpose.Recovery"/>; 15 minutes; voids earlier unused recovery tokens. Audited with the expiry only.</item>
/// <item><see cref="RecoverAsync"/>: token first (every failure the same; five failures void it, atomically, shared with setup), then the inputs, then in one
/// transaction: consume the token; with no Deployment Administrator create one (R1); otherwise the username must be the existing one's and it is reset (R2,
/// R3): new password, enabled, lockout cleared, MFA turned off and recovery codes removed, stamp rotated, sessions closed (Q6). It never touches the Practice
/// Owner, a staff account or a permission, and never creates a second Deployment Administrator (the filtered unique index is the backstop).</item>
/// </list>
/// The token, the username tried and the password are never logged or audited. The route is answered in Normal mode only (the start-up mode middleware closes
/// every other /api route outside Normal). C01 adds no MFA re-enrolment gate; that is ALV-N030-C02 Stage 2.
/// </summary>
public class DeploymentRecoveryService(AlveraDbContext db, AccountService accounts, IPracticeClock clock)
{
    public async Task<IssuedRecoveryToken> IssueTokenAsync(CancellationToken ct)
    {
        var setUp = await db.UserAccounts.AnyAsync(u => u.AccountKind == AccountKind.DeploymentAdministrator, ct) || await db.BootstrapStates.AnyAsync(ct);
        if (!setUp) throw new RecoveryNotAvailableException();
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var now = clock.UtcNow;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.SetupTokens.Where(t => t.Purpose == SetupTokenPurpose.Recovery && t.ConsumedAtUtc == null && t.VoidedAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.VoidedAtUtc, now), ct);
        var row = new SetupToken
        {
            Id = Guid.NewGuid(), TokenHash = DeploymentSetupService.Hash(token), IssuedAtUtc = now, ExpiresAtUtc = now + DeploymentSetupService.TokenLifetime,
            Purpose = SetupTokenPurpose.Recovery,
        };
        db.SetupTokens.Add(row);
        AuditService.Record(db, AuditEventTypes.RecoveryTokenIssued, entityType: "SetupToken", row.Id, null, $"A recovery token was issued; it expires at {row.ExpiresAtUtc:O}.");
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new IssuedRecoveryToken(token, row.ExpiresAtUtc);
    }

    public async Task<(UserAccount Account, DeploymentAdministratorRecoveryMethod Method)> RecoverAsync(string? token, string? username, string? newPassword, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var hash = DeploymentSetupService.Hash(token ?? string.Empty);
        var row = string.IsNullOrEmpty(token) ? null
            : await db.SetupTokens.AsNoTracking().SingleOrDefaultAsync(t => t.TokenHash == hash && t.Purpose == SetupTokenPurpose.Recovery, ct);
        if (row is null || row.ConsumedAtUtc is not null || row.VoidedAtUtc is not null || now >= row.ExpiresAtUtc)
        {
            await DeploymentSetupService.CountFailureAsync(db, SetupTokenPurpose.Recovery, now, ct);
            throw new RecoveryFailedException();
        }
        // The token is valid: only now are the inputs checked, so these answers disclose nothing to a caller without one.
        var name = AccountService.NormalizeUsername(username);
        if (name.Length == 0) throw new SetupInputInvalidException("username_required");
        if ((newPassword ?? string.Empty).Length < DeploymentSetupService.MinimumPasswordLength) throw new SetupInputInvalidException("password_too_short");

        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        try
        {
            await AdminAvailabilityGuard.AcquireAsync(db, ct);   // 18.3a: the first lock of every owner/administrator-affecting operation
            var consumed = await db.SetupTokens.Where(t => t.Id == row.Id && t.ConsumedAtUtc == null && t.VoidedAtUtc == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.ConsumedAtUtc, now), ct);
            if (consumed != 1) throw new RecoveryFailedException();   // a concurrent request used it first

            var existing = await db.UserAccounts.AsNoTracking().Where(u => u.AccountKind == AccountKind.DeploymentAdministrator)
                .Select(u => new { u.Id, u.Username }).SingleOrDefaultAsync(ct);
            UserAccount account;
            DeploymentAdministratorRecoveryMethod method;
            var details = "";
            if (existing is null)
            {
                if (await db.UserAccounts.AnyAsync(u => u.Username == name, ct)) throw new SetupInputInvalidException("username_taken");
                account = new UserAccount
                {
                    Id = Guid.NewGuid(), Username = name, PasswordHash = Pbkdf2PasswordHasher.Hash(newPassword!), Role = Role.Admin, IsDisabled = false,
                    AccountKind = AccountKind.DeploymentAdministrator, MustChangePassword = false, CreatedAtUtc = now,
                };
                db.UserAccounts.Add(account);
                method = DeploymentAdministratorRecoveryMethod.Created;
                details = $"accountId={account.Id}; method=Created";
            }
            else
            {
                if (!string.Equals(existing.Username, name, StringComparison.Ordinal)) throw new WrongUsernameException();
                var (revoked, mfaOff) = await accounts.ResetForRecoveryInTransactionAsync(existing.Id, newPassword!, ct);
                account = await db.UserAccounts.AsNoTracking().SingleAsync(u => u.Id == existing.Id, ct);
                method = DeploymentAdministratorRecoveryMethod.Reset;
                details = $"accountId={existing.Id}; method=Reset; mfaTurnedOff={(mfaOff ? "true" : "false")}; recoveryCodesRevoked={revoked}";
            }
            AuditService.Record(db, AuditEventTypes.DeploymentAdministratorRecovered, entityType: "UserAccount", account.Id, null, details);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return (account, method);
        }
        catch (WrongUsernameException)
        {
            // the token was valid but the username is not the Deployment Administrator's: the same answer as a bad token, and it counts as a failure
            await tx.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            await DeploymentSetupService.CountFailureAsync(db, SetupTokenPurpose.Recovery, now, ct);
            throw new RecoveryFailedException();
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2627 or 2601 })
        {
            await tx.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            throw new RecoveryFailedException();   // a concurrent recovery created the Deployment Administrator first
        }
    }

    private sealed class WrongUsernameException() : Exception("not the Deployment Administrator's username");
}
