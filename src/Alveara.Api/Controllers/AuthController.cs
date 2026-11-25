using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Architecture.Permissions;
using Alveara.Api.Data;

namespace Alveara.Api.Controllers;

public record LoginRequest(string Username, string Password);
public record MfaChallengeRequest(string ChallengeToken, string Code);
public record ConfirmMfaRequest(string Code);
public record EnrollMfaRequest(string? CurrentPassword);
public record SetSessionTimeoutRequest(int SessionTimeoutMinutes);
public record ChangeRoleRequest(string Role);
public record SetEnabledRequest(bool Enabled);
public record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);
public record CompleteResetRequest(Guid UserId, string Token, string NewPassword);
public record AccountResponse(Guid Id, string Username, string Role);
/// <summary>ALV-N020 (F-021): <see cref="LinkedStaffDisplayName"/> and <see cref="LinkedStaffIsActive"/> name the staff profile linked to the account, when there is one.</summary>
public record AdminUserSummary(Guid Id, string Username, string Role, bool IsDisabled, bool MfaEnabled, int SessionTimeoutMinutes, DateTimeOffset CreatedAtUtc,
    string? LinkedStaffDisplayName = null, bool? LinkedStaffIsActive = null, string AccountKind = "Staff", bool MustChangePassword = false);

/// <summary>
/// STORY-001 + ALV-001-C01: login (with MFA/lockout/disabled; ALV-N030 removed sign-up and the first-admin bootstrap route - first-run setup is SetupController
/// handling), MFA enrollment, and administrative account/role/session management. Every failure
/// path returns a status code and a generic message only — never the underlying exception, a
/// stack trace, or (for login) any detail that would let a caller distinguish account states in a
/// way that aids enumeration.
/// </summary>
[ApiController]
[Route("api/auth")]
public class AuthController(AccountService accountService, AlveraDbContext db, IEffectivePermissions permissions, IPracticeClock clock,
    ILogger<Alveara.Api.Architecture.Auditing.AuditSessionStore> sessionLogger) : ControllerBase
{
    [HttpPost("login")]
    [EnableRateLimiting("AuthAttempts")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var account = await accountService.LoginAsync(request.Username, request.Password, cancellationToken);
            await SignInAsync(account);
            return Ok(new AccountResponse(account.Id, account.Username, account.Role.ToString()));
        }
        catch (MfaChallengeRequiredException ex)
        {
            return StatusCode(StatusCodes.Status202Accepted, new { error = "mfa_required", challengeToken = ex.ChallengeToken });
        }
        catch (AccountLockedOutException ex)
        {
            return StatusCode(StatusCodes.Status423Locked, new { error = "account_locked", lockedUntilUtc = ex.LockedOutUntilUtc });
        }
        catch (AccountDisabledException)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "account_disabled", message = "This account has been disabled." });
        }
        catch (InvalidLoginException)
        {
            return Unauthorized(new { error = "invalid_credentials", message = "Invalid username or password." });
        }
    }

    [HttpPost("mfa/challenge")]
    [AllowedWhilePasswordChangeRequired]
    [EnableRateLimiting("AuthAttempts")]
    public async Task<IActionResult> CompleteMfaChallenge([FromBody] MfaChallengeRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var account = await accountService.CompleteMfaChallengeAsync(request.ChallengeToken, request.Code, cancellationToken);
            await SignInAsync(account);
            return Ok(new AccountResponse(account.Id, account.Username, account.Role.ToString()));
        }
        catch (InvalidOrExpiredMfaChallengeException)
        {
            return Unauthorized(new { error = "invalid_or_expired_challenge" });
        }
        catch (InvalidMfaCodeException)
        {
            return Unauthorized(new { error = "invalid_mfa_code" });
        }
    }

    [HttpPost("logout")]
    [AllowedWhilePasswordChangeRequired]
    [Authorize]
    [RequireCsrfToken]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        // ALV-N025 (BR-01): close this session's lifecycle row and write UserSignedOut - only if this sign-out closed it (a repeat, a race with the expiry pass,
        // a cookie from before the release without a sid (SES-2) or a session whose row could not be written (SES-1) writes nothing)
        if (SessionCookies.SessionIdOf(User) is { } sid && Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            await new Alveara.Api.Architecture.Auditing.AuditSessionStore(db, sessionLogger).SignOutAsync(sid, userId, DateTimeOffset.UtcNow, cancellationToken);
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Ok();
    }

    [HttpGet("whoami")]
    [Authorize(Roles = nameof(Role.Admin))]
    public IActionResult WhoAmI()
    {
        var username = User.FindFirstValue(ClaimTypes.Name);
        var role = User.FindFirstValue(ClaimTypes.Role);
        return Ok(new { username, role });
    }

    /// <summary>Issues (and cookie-sets) a CSRF token pair the client echoes back in the X-CSRF-Token header.</summary>
    [HttpGet("csrf-token")]
    [AllowedWhilePasswordChangeRequired]
    public IActionResult GetCsrfToken([FromServices] IAntiforgery antiforgery)
    {
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        return Ok(new { token = tokens.RequestToken });
    }

    /// <summary>The current caller's own identity/permissions, for client-side "who am I, can I do
    /// this" UI decisions (nav filtering, account menu, session-expiry UX) - never the source of
    /// truth for access control itself, which is always re-checked server-side via
    /// [RequirePermission]. ALV-N009: also returns the real cookie expiry (read from the
    /// authentication ticket, not recomputed client-side) so the shell can warn before the session
    /// actually expires rather than only reacting after the fact.</summary>
    [HttpGet("permissions")]
    [AllowedWhilePasswordChangeRequired]
    [Authorize]
    public async Task<IActionResult> MyPermissions()
    {
        var roleClaim = User.FindFirstValue(ClaimTypes.Role);
        if (!Enum.TryParse<Role>(roleClaim, out var role))
        {
            return Unauthorized();
        }
        var authenticateResult = await HttpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        var myId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var me = await db.UserAccounts.AsNoTracking().Where(u => u.Id == myId).Select(u => new
        {
            u.MustChangePassword,
            u.AccountKind,
            IsPracticeOwner = db.PracticeOwnerDesignations.Any(d => d.EndedAtUtc == null && d.StaffProfile!.UserAccountId == u.Id),
            PracticeOwnerExists = db.PracticeOwnerDesignations.Any(d => d.EndedAtUtc == null),
        }).SingleAsync(HttpContext.RequestAborted);
        return Ok(new
        {
            username = User.FindFirstValue(ClaimTypes.Name),
            role = role.ToString(),
            permissions = (await permissions.PermissionsForAsync(role, HttpContext.RequestAborted)).Select(p => p.ToString()).OrderBy(p => p),
            // ALV-N020-C01: permissions held by this ACCOUNT (never by its role), e.g. ManageLoginPresentation.
            accountPermissions = await HttpContext.RequestServices.GetRequiredService<AccountPermissionService>().ForAccountAsync(Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!), HttpContext.RequestAborted),
            sessionExpiresAtUtc = authenticateResult.Properties?.ExpiresUtc,
            // ALV-N021 A3 (briefing 4.1-1, BR-5): the practice zone every client renders instants in - canonical IANA, the same for every signed-in user, no permission beyond sign-in
            practiceTimeZoneId = clock.PracticeTimeZoneIanaId,
            // ALV-N030: the client's identity call is this route (whoami is Administrator-only), so the forced-change and account-kind facts ride here
            mustChangePassword = me.MustChangePassword,
            accountKind = me.AccountKind.ToString(),
            isPracticeOwner = me.IsPracticeOwner,
            practiceOwnerExists = me.PracticeOwnerExists,
            // ALV-N030-C01 (brief 18.7): none | valid | degraded - the dashboard tells an Administrator how to fix a degraded owner
            practiceOwnerStatus = await OwnerInvariantAudit.StatusAsync(db, HttpContext.RequestAborted),
        });
    }

    /// <summary>The full role -> permission matrix, for the security-administration UI's
    /// permission-visibility screen.</summary>
    [HttpGet("permission-matrix")]
    [Authorize]
    [RequirePermission(Permission.ViewPermissionMatrix)]
    public async Task<IActionResult> GetPermissionMatrix()
    {
        var all = await permissions.MatrixAsync(HttpContext.RequestAborted);
        var matrix = Enum.GetValues<Role>().Select(role => new
        {
            role = role.ToString(),
            permissions = all[role].Select(p => p.ToString()).OrderBy(p => p),
        });
        return Ok(matrix);
    }


    // ---------- MFA enrollment (self-service, for the current authenticated user) ----------

    [HttpPost("mfa/enroll")]
    [Authorize]
    [RequireCsrfToken]
    [EnableRateLimiting("AuthAttempts")]
    public async Task<IActionResult> EnrollMfa([FromBody] EnrollMfaRequest? request, CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        try
        {
            var (secret, recoveryCodes) = await accountService.BeginMfaEnrollmentAsync(userId, request?.CurrentPassword, cancellationToken);
            return Ok(new { base32Secret = secret, recoveryCodes });
        }
        catch (CurrentPasswordRequiredToReplaceMfaException)
        {
            return StatusCode(StatusCodes.Status400BadRequest, new { error = "current_password_required", message = "Enter your current password to replace an existing MFA factor." });
        }
        catch (InvalidCurrentPasswordException)
        {
            return Unauthorized(new { error = "invalid_current_password" });
        }
        catch (AccountLockedOutException ex)
        {
            return StatusCode(StatusCodes.Status423Locked, new { error = "account_locked", lockedUntilUtc = ex.LockedOutUntilUtc });
        }
    }

    [HttpPost("mfa/confirm")]
    [Authorize]
    [RequireCsrfToken]
    public async Task<IActionResult> ConfirmMfa([FromBody] ConfirmMfaRequest request, CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        try
        {
            var account = await accountService.ConfirmMfaEnrollmentAsync(userId, request.Code, cancellationToken);
            // ALV-N009: confirmation rotates SecurityStamp (by design - it invalidates every OTHER
            // outstanding session), which would otherwise also invalidate the caller's own current
            // cookie on their very next request. Re-issuing the cookie here with the fresh stamp
            // keeps the confirming caller's own session genuinely valid, rather than silently
            // signing them out as a side effect of the action they just took.
            await SignInAsync(account);
            return Ok();
        }
        catch (InvalidMfaCodeException)
        {
            return BadRequest(new { error = "invalid_mfa_code" });
        }
        catch (MfaNotEnabledException)
        {
            return BadRequest(new { error = "no_pending_enrollment", message = "There is no MFA enrollment in progress to confirm." });
        }
    }

    // ---------- Admin-only account/role/session administration ----------

    /// <summary>
    /// The account list. An administrator (the only holder of <see cref="Permission.ManageUsers"/>) sees every account. ALV-N018: a holder of <see cref="Permission.ManageAccountStatus"/> or
    /// <see cref="Permission.RevokeSessions"/> sees only the accounts the full-subset target rule lets that holder act on (never an administrator; only roles whose complete effective permission set is a subset of the
    /// holder's, the holder's own role and an unassigned account included). Everyone else is refused with the same 403 body as before (<c>required: ManageUsers</c>).
    /// </summary>
    [HttpGet("users")]
    [Authorize]
    [RequireAnyPermission(Permission.ManageUsers, Permission.ManageAccountStatus, Permission.RevokeSessions, RequiredText = nameof(Permission.ManageUsers))]
    public async Task<IActionResult> ListUsers(CancellationToken cancellationToken)
    {
        IQueryable<UserAccount> query = db.UserAccounts;
        if (Enum.TryParse<Role>(User.FindFirstValue(ClaimTypes.Role), out var actorRole) && actorRole != Role.Admin)
        {
            var actorSet = await permissions.PermissionsForAsync(actorRole, cancellationToken);
            var allowed = new List<Role>();
            foreach (var role in Enum.GetValues<Role>())
                if (role != Role.Admin && (await permissions.PermissionsForAsync(role, cancellationToken)).IsSubsetOf(actorSet)) allowed.Add(role);
            query = query.Where(u => allowed.Contains(u.Role));
        }
        var users = await query
            .OrderBy(u => u.Username)
            .Select(u => new AdminUserSummary(u.Id, u.Username, u.Role.ToString(), u.IsDisabled, u.MfaEnabled, u.SessionTimeoutMinutes, u.CreatedAtUtc,
                db.StaffProfiles.Where(s => s.UserAccountId == u.Id).Select(s => s.DisplayName).FirstOrDefault(),
                db.StaffProfiles.Where(s => s.UserAccountId == u.Id).Select(s => (bool?)s.IsActive).FirstOrDefault(),
                u.AccountKind.ToString(), u.MustChangePassword))   // ALV-N030: Security Administration labels the Deployment Administrator, legacy rows and pending first sign-ins
            .ToListAsync(cancellationToken);
        return Ok(users);
    }

    [HttpPut("{userId:guid}/role")]
    [Authorize]
    [RequirePermission(Permission.ManageRoles)]
    [RequireCsrfToken]
    public async Task<IActionResult> ChangeRole(Guid userId, [FromBody] ChangeRoleRequest request, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<Role>(request.Role, ignoreCase: true, out var newRole) || !Enum.IsDefined(newRole))
        {
            return BadRequest(new { error = "invalid_role", message = $"'{request.Role}' is not a recognized role." });
        }

        var performedByUserId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        try
        {
            var account = await accountService.ChangeRoleAsync(userId, newRole, performedByUserId, cancellationToken);
            return Ok(new AccountResponse(account.Id, account.Username, account.Role.ToString()));
        }
        catch (OwnerTransferRequiredException ex)   // ALV-N030-C01 E3/E5
        {
            return Conflict(new { error = PracticeOwnerGuard.TransferRequired, message = ex.Message });
        }
        catch (LastAdminException)
        {
            return Conflict(new { error = "last_admin", message = "At least one enabled administrator must remain." });
        }
        catch (AdminGuardBusyException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "admin_guard_busy", message = "The administrator guard is busy. Try again." });
        }
        catch (AccountTargetNotAllowedException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "account_target_not_allowed", rule = ex.Rule, message = ex.Message });
        }
        catch (AccountNotFoundException)
        {
            return NotFound(new { error = "account_not_found" });
        }
    }

    [HttpPut("{userId:guid}/enabled")]
    [Authorize]
    [RequirePermission(Permission.ManageAccountStatus)]
    [RequireCsrfToken]
    public async Task<IActionResult> SetEnabled(Guid userId, [FromBody] SetEnabledRequest request, CancellationToken cancellationToken)
    {
        var performedByUserId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        try
        {
            var account = await accountService.SetAccountEnabledAsync(userId, request.Enabled, performedByUserId, cancellationToken);
            return Ok(new { account.Id, account.Username, account.IsDisabled });
        }
        catch (OwnerTransferRequiredException ex)   // ALV-N030-C01 E3/E5
        {
            return Conflict(new { error = PracticeOwnerGuard.TransferRequired, message = ex.Message });
        }
        catch (LastAdminException)
        {
            return Conflict(new { error = "last_admin", message = "At least one enabled administrator must remain." });
        }
        catch (AdminGuardBusyException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "admin_guard_busy", message = "The administrator guard is busy. Try again." });
        }
        catch (AccountTargetNotAllowedException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "account_target_not_allowed", rule = ex.Rule, message = ex.Message });
        }
        catch (AccountNotFoundException)
        {
            return NotFound(new { error = "account_not_found" });
        }
    }

    [HttpPut("{userId:guid}/session-timeout")]
    [Authorize]
    [RequirePermission(Permission.ManageAccountStatus)]
    [RequireCsrfToken]
    public async Task<IActionResult> SetSessionTimeout(Guid userId, [FromBody] SetSessionTimeoutRequest request, CancellationToken cancellationToken)
    {
        var performedByUserId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        try
        {
            var account = await accountService.SetSessionTimeoutAsync(userId, request.SessionTimeoutMinutes, performedByUserId, cancellationToken);
            return Ok(new { account.Id, account.Username, account.SessionTimeoutMinutes });
        }
        catch (LastAdminException)
        {
            return Conflict(new { error = "last_admin", message = "At least one enabled administrator must remain." });
        }
        catch (AdminGuardBusyException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "admin_guard_busy", message = "The administrator guard is busy. Try again." });
        }
        catch (AccountTargetNotAllowedException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "account_target_not_allowed", rule = ex.Rule, message = ex.Message });
        }
        catch (AccountNotFoundException)
        {
            return NotFound(new { error = "account_not_found" });
        }
        catch (InvalidSessionTimeoutException)
        {
            return BadRequest(new { error = "invalid_session_timeout", message = "Session timeout must be between 5 and 1440 minutes." });
        }
    }

    [HttpPost("{userId:guid}/reset-password")]
    [Authorize]
    [RequirePermission(Permission.IssuePasswordResets)]
    [RequireCsrfToken]
    public async Task<IActionResult> IssuePasswordReset(Guid userId, CancellationToken cancellationToken)
    {
        var performedByUserId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        try
        {
            var token = await accountService.IssuePasswordResetTokenAsync(userId, performedByUserId, cancellationToken);
            // Returned exactly once, to the admin who requested it — never stored or logged.
            return Ok(new { userId, token });
        }
        catch (AccountNotFoundException)
        {
            return NotFound(new { error = "account_not_found" });
        }
    }

    /// <summary>ALV-N030: the signed-in user changes their own password; the way out of a forced change. On success the session is re-issued under the new
    /// security stamp (every other session of the account ends). Request bodies here are never logged.</summary>
    [HttpPost("password/change")]
    [Authorize]
    [RequireCsrfToken]
    [AllowedWhilePasswordChangeRequired]
    [EnableRateLimiting("AuthAttempts")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        try
        {
            var account = await accountService.ChangeOwnPasswordAsync(userId, request.CurrentPassword, request.NewPassword, cancellationToken);
            await SignInAsync(account);
            return Ok();
        }
        catch (InvalidCurrentPasswordException)
        {
            return BadRequest(new { error = "invalid_current_password" });
        }
        catch (NewPasswordRejectedException ex)
        {
            return BadRequest(new { error = ex.Code, minimumPasswordLength = DeploymentSetupService.MinimumPasswordLength });
        }
    }

    [HttpPost("reset-password/complete")]
    [EnableRateLimiting("AuthAttempts")]
    public async Task<IActionResult> CompletePasswordReset([FromBody] CompleteResetRequest request, CancellationToken cancellationToken)
    {
        try
        {
            await accountService.ResetPasswordAsync(request.UserId, request.Token, request.NewPassword, cancellationToken);
            return Ok();
        }
        catch (InvalidOrExpiredResetTokenException)
        {
            return Unauthorized(new { error = "invalid_or_expired_token" });
        }
    }

    [HttpPost("{userId:guid}/revoke-sessions")]
    [Authorize]
    [RequirePermission(Permission.RevokeSessions)]
    [RequireCsrfToken]
    public async Task<IActionResult> RevokeSessions(Guid userId, CancellationToken cancellationToken)
    {
        var performedByUserId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        try
        {
            await accountService.RevokeAllSessionsAsync(userId, performedByUserId, cancellationToken);
            return Ok();
        }
        catch (LastAdminException)
        {
            return Conflict(new { error = "last_admin", message = "At least one enabled administrator must remain." });
        }
        catch (AdminGuardBusyException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "admin_guard_busy", message = "The administrator guard is busy. Try again." });
        }
        catch (AccountTargetNotAllowedException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { error = "account_target_not_allowed", rule = ex.Rule, message = ex.Message });
        }
        catch (AccountNotFoundException)
        {
            return NotFound(new { error = "account_not_found" });
        }
    }

    // ---------- helpers ----------

    /// <summary>Issues the sign-in cookie (and, ALV-N025, its audit-only session row): see <see cref="SessionCookies.IssueAsync"/>.</summary>
    private Task SignInAsync(Alveara.Api.Architecture.Identity.UserAccount account) => SessionCookies.IssueAsync(HttpContext, account, db, sessionLogger);
}
