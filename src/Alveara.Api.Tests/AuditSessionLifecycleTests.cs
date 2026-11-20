using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Controllers;
using Session = Alveara.Api.Tests.SchedulingApiHarness.Session;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N025 slice 5 (PD-20, BR-01; the session-lifecycle clarification SES-1, SESSION-CLARIFICATION-R01) against real SQL Server and the real HTTP pipeline:
/// - one sid and one deadline: a sign-in writes one open lifecycle row whose deadline is the cookie's own, and signing out with that cookie closes exactly that row;
/// - exactly once: sign-out, the expiry pass and revocation each close a row only if it is still open, and only that close writes the event (a repeat or a race writes
///   nothing; an expiry is dated at the fixed deadline; a revoked session is never later reported expired);
/// - guard 51301: a closed row and the identity of a row never change, and no row is deleted;
/// - SES-1: a failed lifecycle write does not block sign-in (LoginSucceeded is written, no row, no sign-out event);
/// - the MFA re-issue: confirming enrollment revokes the current row and the re-issued cookie starts a new one.
/// </summary>
public class AuditSessionLifecycleTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private SchedulingApiHarness _api = null!;
    private Session _admin = null!;

    public Task InitializeAsync() => _fixture.InitializeOwnerAsync(async () =>
    {
        _api = new SchedulingApiHarness(_fixture);
        _admin = await _api.SessionAsync("Admin");
    });

    public Task DisposeAsync() { _api.Dispose(); return _fixture.DisposeAsync(); }

    private async Task<List<AuditSession>> RowsAsync(Guid user) { await using var db = _fixture.CreateContext(); return await db.AuditSessions.AsNoTracking().Where(s => s.UserId == user).ToListAsync(); }
    private async Task<int> EventsAsync(string type, Guid user) { await using var db = _fixture.CreateContext(); return await db.AuditLogEntries.CountAsync(a => a.EventType == type && a.TargetUserAccountId == user); }
    private async Task SqlAsync(string sql) { await using var db = _fixture.CreateContext(); await db.Database.ExecuteSqlRawAsync(sql); }

    private static int SqlNumber(Exception e)
    {
        for (Exception? x = e; x is not null; x = x.InnerException) if (x is SqlException s) return s.Number;
        return 0;
    }

    private async Task<AuditSession> OpenRowAsync(Guid user, DateTimeOffset expires)
    {
        var row = new AuditSession { SessionId = Guid.NewGuid(), UserId = user, IssuedAtUtc = expires.AddMinutes(-30), ExpiresAtUtc = expires };
        await using var db = _fixture.CreateContext();
        db.AuditSessions.Add(row);
        await db.SaveChangesAsync();
        return row;
    }

    [Fact]
    public async Task A_sign_in_writes_one_open_row_with_the_cookies_own_deadline_and_signing_out_closes_exactly_that_row_once()
    {
        var open = Assert.Single(await RowsAsync(_admin.UserId));
        Assert.Null(open.EndedAtUtc);
        var whoami = JsonDocument.Parse(await _admin.Client.GetStringAsync("/api/auth/permissions")).RootElement;
        Assert.Equal(open.ExpiresAtUtc, whoami.GetProperty("sessionExpiresAtUtc").GetDateTimeOffset());   // the same instant, to the tick (whole seconds both)

        Assert.Equal(HttpStatusCode.OK, (await _admin.SendAsync(HttpMethod.Post, "/api/auth/logout")).Status);
        var closed = Assert.Single(await RowsAsync(_admin.UserId));
        Assert.Equal((open.SessionId, AuditSessionEndKinds.SignedOut, true), (closed.SessionId, closed.EndKind, closed.EndAuditWritten));   // the cookie's sid is the row's
        Assert.Equal(1, await EventsAsync(PatientAccessEvents.UserSignedOut, _admin.UserId));

        await using var db = _fixture.CreateContext();
        Assert.False(await new AuditSessionStore(db).SignOutAsync(open.SessionId, _admin.UserId, DateTimeOffset.UtcNow, default));   // a repeat closes nothing ...
        Assert.Equal(1, await EventsAsync(PatientAccessEvents.UserSignedOut, _admin.UserId));                                         // ... and writes nothing
    }

    [Fact]
    public async Task The_expiry_pass_closes_a_row_at_its_deadline_once_and_a_later_sign_out_writes_nothing()
    {
        var deadline = new DateTimeOffset(2026, 11, 20, 8, 0, 0, TimeSpan.Zero);
        var row = await OpenRowAsync(_admin.UserId, deadline);
        await using (var db = _fixture.CreateContext())
        {
            var first = await new AuditSessionStore(db).ExpirePassAsync(DateTimeOffset.UtcNow, default);
            Assert.True(first.Expired >= 1);
        }
        await using (var db = _fixture.CreateContext())
            Assert.Equal(0, (await new AuditSessionStore(db).ExpirePassAsync(DateTimeOffset.UtcNow, default)).Expired);   // a retried pass writes nothing twice

        var closed = (await RowsAsync(_admin.UserId)).Single(s => s.SessionId == row.SessionId);
        Assert.Equal((AuditSessionEndKinds.Expired, (DateTimeOffset?)deadline), (closed.EndKind, closed.EndedAtUtc));
        await using (var db = _fixture.CreateContext())
        {
            var e = await db.AuditLogEntries.SingleAsync(a => a.EventType == PatientAccessEvents.SessionExpired && a.CorrelationId == row.SessionId);
            Assert.Equal(deadline, e.TimestampUtc);                                                                       // dated at the fixed end time
            Assert.False(await new AuditSessionStore(db).SignOutAsync(row.SessionId, _admin.UserId, DateTimeOffset.UtcNow, default));
        }
        Assert.Equal(0, await EventsAsync(PatientAccessEvents.UserSignedOut, _admin.UserId));
    }

    [Fact]
    public async Task Revoking_sessions_closes_the_open_rows_as_revoked_with_one_event_each_and_they_are_never_later_expired()
    {
        var desk = await _api.SessionAsync("FrontDesk");
        var expiring = await OpenRowAsync(desk.UserId, DateTimeOffset.UtcNow.AddMinutes(-1));                            // already past its deadline, not yet processed
        Assert.Equal(2, (await RowsAsync(desk.UserId)).Count(s => s.EndedAtUtc == null));

        Assert.Equal(HttpStatusCode.OK, (await _admin.SendAsync(HttpMethod.Post, $"/api/auth/{desk.UserId}/revoke-sessions")).Status);

        Assert.All(await RowsAsync(desk.UserId), s => Assert.Equal(AuditSessionEndKinds.Revoked, s.EndKind));
        Assert.Equal(2, await EventsAsync(PatientAccessEvents.SessionRevoked, desk.UserId));
        await using (var db = _fixture.CreateContext()) await new AuditSessionStore(db).ExpirePassAsync(DateTimeOffset.UtcNow, default);
        Assert.Equal(0, await EventsAsync(PatientAccessEvents.SessionExpired, desk.UserId));
        Assert.Equal(AuditSessionEndKinds.Revoked, (await RowsAsync(desk.UserId)).Single(s => s.SessionId == expiring.SessionId).EndKind);
    }

    [Fact]
    public async Task Guard_51301_a_closed_row_and_the_identity_never_change_and_no_row_is_deleted()
    {
        var open = await OpenRowAsync(_admin.UserId, DateTimeOffset.UtcNow.AddHours(1));
        Assert.Equal(51301, SqlNumber(await Assert.ThrowsAnyAsync<Exception>(() => SqlAsync($"UPDATE [AuditSessions] SET [ExpiresAtUtc] = DATEADD(hour, 5, [ExpiresAtUtc]) WHERE [SessionId] = '{open.SessionId}'"))));
        Assert.Equal(51301, SqlNumber(await Assert.ThrowsAnyAsync<Exception>(() => SqlAsync($"DELETE FROM [AuditSessions] WHERE [SessionId] = '{open.SessionId}'"))));
        await using (var db = _fixture.CreateContext()) await new AuditSessionStore(db).SignOutAsync(open.SessionId, _admin.UserId, DateTimeOffset.UtcNow, default);
        Assert.Equal(51301, SqlNumber(await Assert.ThrowsAnyAsync<Exception>(() => SqlAsync($"UPDATE [AuditSessions] SET [EndKind] = N'Expired' WHERE [SessionId] = '{open.SessionId}'"))));
        var another = await OpenRowAsync(_admin.UserId, DateTimeOffset.UtcNow.AddHours(1));
        Assert.Equal(547, SqlNumber(await Assert.ThrowsAnyAsync<Exception>(() => SqlAsync($"UPDATE [AuditSessions] SET [EndedAtUtc] = SYSDATETIMEOFFSET() WHERE [SessionId] = '{another.SessionId}'"))));   // CK: an end time without its kind
    }

    [Fact]
    public async Task SES1_a_failed_lifecycle_write_does_not_block_sign_in_and_that_session_writes_no_sign_out_event()
    {
        var username = $"user-{Guid.NewGuid():N}";
        var id = (await (await _admin.Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(username, "password"))).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await _admin.SendAsync(HttpMethod.Put, $"/api/auth/{id}/enabled", new SetEnabledRequest(true));
        // test-only fault: this user's lifecycle row cannot be inserted
        await SqlAsync($"CREATE TRIGGER [TR_Test_AuditSessionsFault] ON [AuditSessions] INSTEAD OF INSERT AS BEGIN IF EXISTS (SELECT 1 FROM inserted WHERE [UserId] = '{id}') THROW 59999, 'test fault', 1; INSERT INTO [AuditSessions] SELECT * FROM inserted; END");
        try
        {
            var client = _api.Factory.CreateClient();
            var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(username, "password"));
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);                                                           // signed in anyway
            Assert.Equal(1, await EventsAsync(AuditEventTypes.LoginSucceeded, id));
            Assert.Empty(await RowsAsync(id));

            var csrf = (await (await client.GetAsync("/api/auth/csrf-token")).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
            var logout = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
            logout.Headers.Add("X-CSRF-Token", csrf);
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(logout)).StatusCode);
            Assert.Equal(0, await EventsAsync(PatientAccessEvents.UserSignedOut, id));
        }
        finally { await SqlAsync("DROP TRIGGER [TR_Test_AuditSessionsFault]"); }
    }

    [Fact]
    public async Task Confirming_mfa_revokes_the_current_row_and_the_reissued_cookie_starts_a_new_one()
    {
        var before = Assert.Single(await RowsAsync(_admin.UserId));
        var enroll = await _admin.SendAsync(HttpMethod.Post, "/api/auth/mfa/enroll");
        Assert.Equal(HttpStatusCode.OK, enroll.Status);
        var code = Totp.GenerateCodeForTests(Totp.FromBase32(enroll.Body.GetProperty("base32Secret").GetString()!));
        Assert.Equal(HttpStatusCode.OK, (await _admin.SendAsync(HttpMethod.Post, "/api/auth/mfa/confirm", new { code })).Status);

        var rows = await RowsAsync(_admin.UserId);
        Assert.Equal(AuditSessionEndKinds.Revoked, rows.Single(r => r.SessionId == before.SessionId).EndKind);
        var fresh = Assert.Single(rows, r => r.EndedAtUtc == null);
        Assert.NotEqual(before.SessionId, fresh.SessionId);
        Assert.Equal(1, await EventsAsync(PatientAccessEvents.SessionRevoked, _admin.UserId));
        Assert.Equal(HttpStatusCode.OK, (await _admin.Client.GetAsync("/api/auth/permissions")).StatusCode);              // still signed in
    }
}
