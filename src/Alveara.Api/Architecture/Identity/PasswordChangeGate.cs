using System.Security.Claims;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Alveara.Api.Architecture.Identity;

/// <summary>
/// ALV-N030 (brief section 5): marks the only signed-in actions a caller may use while its account must change its password. The set is fixed and a test
/// asserts it exactly: who-am-I (<c>GET /api/auth/permissions</c>, the client's identity call), <c>GET /api/auth/csrf-token</c>,
/// <c>POST /api/auth/password/change</c>, <c>POST /api/auth/logout</c> and <c>POST /api/auth/mfa/challenge</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class AllowedWhilePasswordChangeRequiredAttribute : Attribute;

/// <summary>
/// ALV-N030: the server-side forced-change gate. The cookie check (Program.cs, OnValidatePrincipal) reads <see cref="UserAccount.MustChangePassword"/> in the
/// query it already makes and hands it over in <see cref="HttpContext.Items"/>; this middleware then refuses every endpoint that needs a signed-in user
/// (carries <see cref="IAuthorizeData"/>) and is not marked <see cref="AllowedWhilePasswordChangeRequiredAttribute"/>, with 403 <c>password_change_required</c>.
/// Anonymous endpoints are unaffected: they give a signed-in caller nothing beyond what anyone has. One <see cref="AuditEventTypes.PasswordChangeRequiredRefusal"/>
/// row is written per session (not per request). The client's redirect to "Choose a new password" is presentation only; this is the control.
/// Failure behaviour: if the audit write fails the request is still refused (the refusal is the control; the audit failure is logged, not swallowed).
/// </summary>
public sealed class PasswordChangeGateMiddleware(RequestDelegate next, ILogger<PasswordChangeGateMiddleware> logger)
{
    public const string ItemKey = "alveara.must_change_password";
    public const string ErrorCode = "password_change_required";

    public async Task InvokeAsync(HttpContext context)
    {
        var endpoint = context.GetEndpoint();
        if (context.Items.ContainsKey(ItemKey)
            && endpoint?.Metadata.GetMetadata<IAuthorizeData>() is not null
            && endpoint.Metadata.GetMetadata<AllowedWhilePasswordChangeRequiredAttribute>() is null)
        {
            await RecordOncePerSessionAsync(context);
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.Headers.CacheControl = "no-store";
            await context.Response.WriteAsJsonAsync(new { error = ErrorCode });
            return;
        }
        await next(context);
    }

    private async Task RecordOncePerSessionAsync(HttpContext context)
    {
        if (!Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return;
        var sid = SessionCookies.SessionIdOf(context.User);
        try
        {
            var db = context.RequestServices.GetRequiredService<AlveraDbContext>();
            // Keyed on the session id (or the account, for a cookie from before session ids existed): at most one row per session in the normal case.
            var key = sid ?? userId;
            if (await db.AuditLogEntries.AnyAsync(a => a.EventType == AuditEventTypes.PasswordChangeRequiredRefusal && a.TargetUserAccountId == key, context.RequestAborted)) return;
            AuditService.Record(db, AuditEventTypes.PasswordChangeRequiredRefusal, entityType: sid is null ? "UserAccount" : "AuditSession", key, userId,
                "A request was refused because the account must change its password first.");
            await db.SaveChangesAsync(context.RequestAborted);
        }
        catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException or Microsoft.Data.SqlClient.SqlException)
        {
            logger.LogWarning("password_change_gate_audit_failed {ErrorClass}", ex.GetType().Name);
        }
    }
}
