using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Identity;

/// <summary>
/// The sign-in cookie, issued in one place (moved out of AuthController by ALV-N025, which had reached the 500-line ceiling; the claims and properties are unchanged
/// apart from the new <c>sid</c>).
/// ALV-N025 (SES-1, SESSION-CLARIFICATION-R01): one session id and one fixed deadline are computed ONCE and used for both the cookie (the <c>sid</c> claim, the ticket's
/// <c>ExpiresUtc</c>) and the AuditSessions row, which is written in its own save - after LoginSucceeded, before the cookie; a failed row write is logged
/// (audit_session_write_failed) and the cookie is issued anyway.
/// </summary>
public static class SessionCookies
{
    /// <summary>ALV-N025 (PD-20): the session id claim - one random id per issued cookie, the key of its audit-only lifecycle row and of its record opens.</summary>
    public const string SessionIdClaim = "sid";

    public static async Task IssueAsync(HttpContext http, UserAccount account, AlveraDbContext db, ILogger<AuditSessionStore> logger)
    {
        var sessionId = Guid.NewGuid();
        var issuedAtUtc = DateTimeOffset.UtcNow;
        var deadline = issuedAtUtc.AddMinutes(account.SessionTimeoutMinutes);
        // whole seconds: the cookie ticket keeps its expiry to the second, so the row and the cookie hold exactly the same instant
        var expiresAtUtc = new DateTimeOffset(deadline.UtcTicks - deadline.UtcTicks % TimeSpan.TicksPerSecond, TimeSpan.Zero);
        await new AuditSessionStore(db, logger).StartAsync(sessionId, account.Id, issuedAtUtc, expiresAtUtc, http.RequestAborted);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, account.Id.ToString()),
            new Claim(ClaimTypes.Name, account.Username),
            new Claim(ClaimTypes.Role, account.Role.ToString()),
            new Claim("security_stamp", account.SecurityStamp.ToString()),
            new Claim(SessionIdClaim, sessionId.ToString()),
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity), new AuthenticationProperties
        {
            IsPersistent = false,
            ExpiresUtc = expiresAtUtc,
        });
    }

    /// <summary>The session id of the signed-in user's cookie; null for a cookie issued before ALV-N025 (SES-2).</summary>
    public static Guid? SessionIdOf(ClaimsPrincipal user) => Guid.TryParse(user.FindFirstValue(SessionIdClaim), out var sid) ? sid : null;
}
