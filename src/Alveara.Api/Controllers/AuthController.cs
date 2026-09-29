using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Alveara.Api.Architecture.Identity;

namespace Alveara.Api.Controllers;

public record RegisterRequest(string Username, string Password, string Role);
public record LoginRequest(string Username, string Password);
public record AccountResponse(Guid Id, string Username, string Role);
public record ChangeRoleRequest(string Role);

/// <summary>
/// STORY-001: registration and login. Every failure path returns a status code and a generic
/// message only — never the underlying exception, a stack trace, or (for login) any detail that
/// would let a caller distinguish "unknown username" from "wrong password".
/// </summary>
[ApiController]
[Route("api/auth")]
public class AuthController(AccountService accountService) : ControllerBase
{
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<Role>(request.Role, ignoreCase: true, out var role) || !Enum.IsDefined(role))
        {
            return BadRequest(new { error = "invalid_role", message = $"'{request.Role}' is not a recognized role." });
        }

        try
        {
            var account = await accountService.RegisterAsync(request.Username, request.Password, role, cancellationToken);
            // No GET-by-id endpoint exists yet to point a Location header at, so this returns
            // 201 with the created resource in the body rather than via CreatedAtAction.
            return StatusCode(StatusCodes.Status201Created, new AccountResponse(account.Id, account.Username, account.Role.ToString()));
        }
        catch (UsernameAlreadyRegisteredException)
        {
            return Conflict(new { error = "username_taken", message = "That username is already registered." });
        }
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var account = await accountService.LoginAsync(request.Username, request.Password, cancellationToken);

            // REQ-001's "configurable session timeout": each account's own SessionTimeoutMinutes
            // drives this cookie's absolute expiration, not a single hardcoded value for everyone.
            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, account.Id.ToString()),
                new Claim(ClaimTypes.Name, account.Username),
                new Claim(ClaimTypes.Role, account.Role.ToString()),
            };
            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity), new AuthenticationProperties
            {
                IsPersistent = false,
                ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(account.SessionTimeoutMinutes),
            });

            return Ok(new AccountResponse(account.Id, account.Username, account.Role.ToString()));
        }
        catch (AccountLockedOutException ex)
        {
            return StatusCode(StatusCodes.Status423Locked, new { error = "account_locked", lockedUntilUtc = ex.LockedOutUntilUtc });
        }
        catch (InvalidLoginException)
        {
            return Unauthorized(new { error = "invalid_credentials", message = "Invalid username or password." });
        }
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Ok();
    }

    /// <summary>
    /// RBAC demonstration endpoint: proves [Authorize(Roles=...)] actually enforces the role
    /// assigned at login, not just that a session cookie exists. Admin-only is an arbitrary but
    /// meaningful choice — every other role must be rejected.
    /// </summary>
    [HttpGet("whoami")]
    [Authorize(Roles = nameof(Role.Admin))]
    public IActionResult WhoAmI()
    {
        var username = User.FindFirstValue(ClaimTypes.Name);
        var role = User.FindFirstValue(ClaimTypes.Role);
        return Ok(new { username, role });
    }

    /// <summary>Admin-only role change, satisfying the "role changes are audited" half of REQ-002/003.</summary>
    [HttpPut("{userId:guid}/role")]
    [Authorize(Roles = nameof(Role.Admin))]
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
}
