using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Data;

namespace Alveara.Api.Controllers;

public record RegisterRequest(string Username, string Password);
public record BootstrapAdminRequest(string Username, string Password, string Secret);
public record LoginRequest(string Username, string Password);
public record MfaChallengeRequest(string ChallengeToken, string Code);
public record ConfirmMfaRequest(string Code);
public record EnrollMfaRequest(string? CurrentPassword);
public record SetSessionTimeoutRequest(int SessionTimeoutMinutes);
public record ChangeRoleRequest(string Role);
public record SetEnabledRequest(bool Enabled);
public record CompleteResetRequest(Guid UserId, string Token, string NewPassword);
public record AccountResponse(Guid Id, string Username, string Role);
public record AdminUserSummary(Guid Id, string Username, string Role, bool IsDisabled, bool MfaEnabled, int SessionTimeoutMinutes, DateTimeOffset CreatedAtUtc);

/// <summary>
/// STORY-001 + ALV-001-C01: registration, first-admin bootstrap, login (with MFA/lockout/disabled
/// handling), MFA enrollment, and administrative account/role/session management. Every failure
/// path returns a status code and a generic message only — never the underlying exception, a
/// stack trace, or (for login) any detail that would let a caller distinguish account states in a
/// way that aids enumeration.
/// </summary>
[ApiController]
[Route("api/auth")]
public class AuthController(AccountService accountService, IConfiguration configuration, AlveraDbContext db) : ControllerBase
{
    [HttpPost("register")]
    [EnableRateLimiting("AuthAttempts")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var account = await accountService.RegisterAsync(request.Username, request.Password, cancellationToken);
            return StatusCode(StatusCodes.Status201Created, new
            {
                account.Id,
                account.Username,
                Role = account.Role.ToString(),
                message = "Account created. An administrator must enable it and assign a role before you can sign in.",
            });
        }
        catch (UsernameAlreadyRegisteredException)
        {
            return Conflict(new { error = "username_taken", message = "That username is already registered." });
        }
    }

    /// <summary>One-time, offline-capable first-admin provisioning. Disabled forever after the first success.</summary>
    [HttpPost("bootstrap-admin")]
    [EnableRateLimiting("AuthAttempts")]
    public async Task<IActionResult> BootstrapAdmin([FromBody] BootstrapAdminRequest request, CancellationToken cancellationToken)
    {
        var configuredSecret = configuration["AdminBootstrapSecret"];
        if (string.IsNullOrEmpty(configuredSecret))
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "bootstrap_not_configured", message = "AdminBootstrapSecret is not configured on this server." });
        }

        try
        {
            var account = await accountService.BootstrapFirstAdminAsync(request.Username, request.Password, request.Secret, configuredSecret, cancellationToken);
            return StatusCode(StatusCodes.Status201Created, new AccountResponse(account.Id, account.Username, account.Role.ToString()));
        }
        catch (InvalidBootstrapSecretException)
        {
            return Unauthorized(new { error = "invalid_bootstrap_secret" });
        }
        catch (BootstrapAlreadyConsumedException)
        {
            return Conflict(new { error = "bootstrap_already_used" });
        }
        catch (UsernameAlreadyRegisteredException)
        {
            return Conflict(new { error = "username_taken" });
        }
    }

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
    [Authorize]
    [RequireCsrfToken]
    public async Task<IActionResult> Logout()
    {
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
    public IActionResult GetCsrfToken([FromServices] IAntiforgery antiforgery)
    {
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        return Ok(new { token = tokens.RequestToken });
    }

    /// <summary>The current caller's own permissions, for client-side "can I do this" UI decisions
    /// (e.g. hiding/disabling actions a role can't reach) - never the source of truth for access
    /// control itself, which is always re-checked server-side via [RequirePermission].</summary>
    [HttpGet("permissions")]
    [Authorize]
    public IActionResult MyPermissions()
    {
        var roleClaim = User.FindFirstValue(ClaimTypes.Role);
        if (!Enum.TryParse<Role>(roleClaim, out var role))
        {
            return Unauthorized();
        }
        return Ok(new
        {
            role = role.ToString(),
            permissions = PermissionMatrix.PermissionsFor(role).Select(p => p.ToString()).OrderBy(p => p),
        });
    }

    /// <summary>The full role -> permission matrix, for the security-administration UI's
    /// permission-visibility screen.</summary>
    [HttpGet("permission-matrix")]
    [Authorize]
    [RequirePermission(Permission.ViewPermissionMatrix)]
    public IActionResult GetPermissionMatrix()
    {
        var matrix = Enum.GetValues<Role>().Select(role => new
        {
            role = role.ToString(),
            permissions = PermissionMatrix.PermissionsFor(role).Select(p => p.ToString()).OrderBy(p => p),
        });
        return Ok(matrix);
    }

    // ---------- MFA enrollment (self-service, for the current authenticated user) ----------

    [HttpPost("mfa/enroll")]
    [Authorize]
    [RequireCsrfToken]
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
    }

    [HttpPost("mfa/confirm")]
    [Authorize]
    [RequireCsrfToken]
    public async Task<IActionResult> ConfirmMfa([FromBody] ConfirmMfaRequest request, CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        try
        {
            await accountService.ConfirmMfaEnrollmentAsync(userId, request.Code, cancellationToken);
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

    [HttpGet("users")]
    [Authorize]
    [RequirePermission(Permission.ManageUsers)]
    public async Task<IActionResult> ListUsers(CancellationToken cancellationToken)
    {
        var users = await db.UserAccounts
            .OrderBy(u => u.Username)
            .Select(u => new AdminUserSummary(u.Id, u.Username, u.Role.ToString(), u.IsDisabled, u.MfaEnabled, u.SessionTimeoutMinutes, u.CreatedAtUtc))
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
        catch (AccountNotFoundException)
        {
            return NotFound(new { error = "account_not_found" });
        }
    }

    // ---------- helpers ----------

    private async Task SignInAsync(Alveara.Api.Architecture.Identity.UserAccount account)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, account.Id.ToString()),
            new Claim(ClaimTypes.Name, account.Username),
            new Claim(ClaimTypes.Role, account.Role.ToString()),
            new Claim("security_stamp", account.SecurityStamp.ToString()),
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity), new AuthenticationProperties
        {
            IsPersistent = false,
            ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(account.SessionTimeoutMinutes),
        });
    }
}
