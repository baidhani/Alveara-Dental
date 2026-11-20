using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Auditing;

/// <summary>The result of one expiry pass.</summary>
public sealed record AuditSessionExpiryResult(int Examined, int Expired);

/// <summary>
/// ALV-N025 (PD-20, BR-01, SES-1; brief section 4 and the session-lifecycle clarification): the audit-only session lifecycle. Every close is ONE conditional update
/// (<c>WHERE SessionId = ... AND EndedAtUtc IS NULL</c>) and its event is written only by the transaction whose update changed the row, so a session gets exactly one
/// end event however sign-out, the expiry pass and a revocation race; guard 51301 refuses any change to a row already closed.
/// - <see cref="StartAsync"/>: at sign-in, in its own save after LoginSucceeded; a failure is logged (audit_session_write_failed) and never blocks sign-in (SES-1).
/// - <see cref="SignOutAsync"/>: explicit sign-out writes UserSignedOut.
/// - <see cref="RevokeOpenAsync"/>: when the security stamp rotates, inside the caller's transaction, each open row closes as Revoked with one SessionRevoked.
/// - <see cref="ExpirePassAsync"/>: rows past their fixed deadline close as Expired at that deadline, with SessionExpired dated at it; at most 500 per pass.
/// Events name the account (entity UserAccount) and the session id; they hold no patient data.
/// </summary>
public sealed class AuditSessionStore(AlveraDbContext db, ILogger<AuditSessionStore>? logger = null)
{
    public const int ExpiryBatch = 500;
    private const string EntityType = "UserAccount";

    /// <summary>Writes the lifecycle row for a cookie being issued. True when written; false (logged, never thrown) when the save failed.</summary>
    public async Task<bool> StartAsync(Guid sessionId, Guid userId, DateTimeOffset issuedAtUtc, DateTimeOffset expiresAtUtc, CancellationToken ct)
    {
        try
        {
            db.AuditSessions.Add(new AuditSession { SessionId = sessionId, UserId = userId, IssuedAtUtc = issuedAtUtc, ExpiresAtUtc = expiresAtUtc });
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException or Microsoft.Data.SqlClient.SqlException or TimeoutException)
        {
            // SES-1: audit-lifecycle tracking never prevents a sign-in; this one session will have no lifecycle event (the help text says so)
            db.ChangeTracker.Clear();
            logger?.LogError("audit_session_write_failed error_class={ErrorClass} user_id={UserId} session_id={SessionId}", ex.GetType().Name, userId, sessionId);
            return false;
        }
    }

    /// <summary>Closes the session as SignedOut and writes UserSignedOut - only if this call closed it. True when it did.</summary>
    public async Task<bool> SignOutAsync(Guid sessionId, Guid userId, DateTimeOffset nowUtc, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var closed = await db.AuditSessions
            .Where(s => s.SessionId == sessionId && s.UserId == userId && s.EndedAtUtc == null)
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.EndedAtUtc, nowUtc).SetProperty(s => s.EndKind, AuditSessionEndKinds.SignedOut).SetProperty(s => s.EndAuditWritten, true), ct);
        if (closed == 0) { await tx.RollbackAsync(ct); return false; }
        AuditService.Record(db, PatientAccessEvents.UserSignedOut, EntityType, userId, userId, $"Signed out (session {sessionId}).", correlationId: sessionId, timestampUtc: nowUtc);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return true;
    }

    /// <summary>
    /// Inside the caller's transaction (the security-stamp rotation): closes every open session of the account as Revoked and stages one SessionRevoked each, which the
    /// caller's own SaveChanges writes with the rotation - the same save. Returns how many were closed.
    /// </summary>
    public static async Task<int> RevokeOpenAsync(AlveraDbContext db, Guid userId, Guid? performedBy, DateTimeOffset nowUtc, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Sessions are revoked inside the security-stamp rotation's transaction.");
        // OUTPUT INTO a table variable: the table has a trigger (51301), and SQL Server refuses a bare OUTPUT on such a table
        var closed = await db.Database.SqlQuery<Guid>($@"DECLARE @closed TABLE ([SessionId] uniqueidentifier NOT NULL);
UPDATE [AuditSessions] SET [EndedAtUtc] = {nowUtc}, [EndKind] = {AuditSessionEndKinds.Revoked}, [EndAuditWritten] = CAST(1 AS bit)
OUTPUT inserted.[SessionId] INTO @closed WHERE [UserId] = {userId} AND [EndedAtUtc] IS NULL;
SELECT [SessionId] AS [Value] FROM @closed;").ToListAsync(ct);
        foreach (var sessionId in closed)
            AuditService.Record(db, PatientAccessEvents.SessionRevoked, EntityType, userId, performedBy, $"Session revoked (session {sessionId}).", correlationId: sessionId, timestampUtc: nowUtc);
        return closed.Count;
    }

    /// <summary>One expiry pass: up to <see cref="ExpiryBatch"/> open rows past their deadline, each closed in its own transaction at its deadline.</summary>
    public async Task<AuditSessionExpiryResult> ExpirePassAsync(DateTimeOffset nowUtc, CancellationToken ct)
    {
        var due = await db.AuditSessions.AsNoTracking().Where(s => s.EndedAtUtc == null && s.ExpiresAtUtc <= nowUtc)
            .OrderBy(s => s.ExpiresAtUtc).ThenBy(s => s.SessionId).Take(ExpiryBatch).Select(s => new { s.SessionId, s.UserId, s.ExpiresAtUtc }).ToListAsync(ct);
        var expired = 0;
        foreach (var s in due)
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var closed = await db.AuditSessions
                .Where(x => x.SessionId == s.SessionId && x.EndedAtUtc == null && x.ExpiresAtUtc <= nowUtc)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.EndedAtUtc, x => x.ExpiresAtUtc).SetProperty(x => x.EndKind, AuditSessionEndKinds.Expired).SetProperty(x => x.EndAuditWritten, true), ct);
            if (closed == 0) { await tx.RollbackAsync(ct); continue; }                    // signed out or revoked meanwhile: that close wrote its own event
            AuditService.Record(db, PatientAccessEvents.SessionExpired, EntityType, s.UserId, null, $"Session expired at its fixed end time (session {s.SessionId}).", correlationId: s.SessionId, timestampUtc: s.ExpiresAtUtc);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            db.ChangeTracker.Clear();
            expired++;
        }
        return new AuditSessionExpiryResult(due.Count, expired);
    }
}
