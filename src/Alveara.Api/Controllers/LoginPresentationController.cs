using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Configuration;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.LoginPresentation;

namespace Alveara.Api.Controllers;

public record SetLoginBackgroundActiveRequest(bool? IsActive, string? RowVersion);

/// <summary>
/// ALV-N020-C01. Anonymous: <c>GET api/public/login-presentation</c> (what the sign-in page shows; <c>no-store</c>) and
/// <c>GET api/public/login-background/{sha256}.{png|jpg}</c> (ONLY the currently selected added derivative; 404 for anything else; fixed type, nosniff,
/// same-origin resource policy, sandbox CSP, server-controlled name, immutable cache because the URL is content-addressed). Both are rate-limited per IP.
/// Configuration (signed in + <see cref="AccountPermission.ManageLoginPresentation"/>; CSRF on every change): read, save, add a background, change availability,
/// and the administrator's own thumbnails.
/// </summary>
[ApiController]
[Route("api")]
public partial class LoginPresentationController(LoginPresentationService presentation) : ControllerBase
{
    public const string ReadRateLimitPolicy = "LoginPresentationReads";

    /// <summary>The upload request may hold the 5 MB image plus the small form fields around it.</summary>
    private const long UploadRequestLimit = LoginBackgroundPipeline.MaxInputBytes + 64 * 1024;

    [GeneratedRegex("^(?<sha>[0-9a-f]{64})\\.(?<ext>png|jpg)$", RegexOptions.CultureInvariant)]
    private static partial Regex PublicImageName();

    private Guid Actor => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet("public/login-presentation")]
    [AllowAnonymous]
    [EnableRateLimiting(ReadRateLimitPolicy)]
    public async Task<IActionResult> GetPublic(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var p = await presentation.GetPublicAsync(ct);
        return Ok(new
        {
            background = p.BackgroundKind == LoginBackgroundKinds.Bundled
                ? (object)new { kind = "bundled", key = p.BundledKey }
                : new { kind = "added", url = p.AddedImageUrl },
            displayMode = p.DisplayMode,
            footerNotice = p.FooterNotice,
        });
    }

    [HttpGet("public/login-background/{name}")]
    [AllowAnonymous]
    [EnableRateLimiting(ReadRateLimitPolicy)]
    public async Task<IActionResult> GetPublicImage(string name, CancellationToken ct)
    {
        var match = PublicImageName().Match(name ?? string.Empty);
        if (!match.Success) return NotFound();
        var opened = await presentation.OpenSelectedAsync(match.Groups["sha"].Value, match.Groups["ext"].Value, ct);
        if (opened is not { } file) return NotFound();
        Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        return ImageResult(file.Stream, file.ContentType, file.Extension);
    }

    [HttpGet("config/login-presentation")]
    [Authorize]
    [RequireAccountPermission(AccountPermission.ManageLoginPresentation)]
    public async Task<IActionResult> GetAdmin(CancellationToken ct) => Ok(await presentation.GetAdminAsync(ct));

    [HttpPut("config/login-presentation")]
    [Authorize]
    [RequireAccountPermission(AccountPermission.ManageLoginPresentation)]
    [RequireCsrfToken]
    public Task<IActionResult> Save([FromBody] SaveLoginPresentation request, CancellationToken ct) =>
        Run(async () => Ok(await presentation.SaveAsync(request, Actor, ct)));

    [HttpPost("config/login-presentation/backgrounds")]
    [Authorize]
    [RequireAccountPermission(AccountPermission.ManageLoginPresentation)]
    [RequireCsrfToken]
    [RequestSizeLimit(UploadRequestLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = UploadRequestLimit)]
    public Task<IActionResult> AddBackground([FromForm] IFormFile? file, [FromForm] string? displayName, [FromForm] string? suggestedDisplayMode,
        [FromForm] bool publicAcknowledged, CancellationToken ct) => Run(async () =>
    {
        if (file is null || file.Length == 0) return BadRequest(new { error = "background_file_required", message = "Choose an image file." });
        if (file.Length > LoginBackgroundPipeline.MaxInputBytes) return StatusCode(413, new { error = "background_too_large", message = "The image can be at most 5 MB." });
        using var buffer = new MemoryStream((int)file.Length);
        await file.CopyToAsync(buffer, ct);                                                                  // bounded by the checks above; never written to disk
        var added = await presentation.AddImageAsync(buffer.ToArray(), file.FileName, displayName, suggestedDisplayMode, publicAcknowledged, Actor, ct);
        return StatusCode(StatusCodes.Status201Created, added);
    });

    [HttpPut("config/login-presentation/backgrounds/{id:guid}/active")]
    [Authorize]
    [RequireAccountPermission(AccountPermission.ManageLoginPresentation)]
    [RequireCsrfToken]
    public Task<IActionResult> SetActive(Guid id, [FromBody] SetLoginBackgroundActiveRequest request, CancellationToken ct) => Run(async () =>
    {
        if (request.IsActive is not { } active) return BadRequest(new { error = "is_active_required", message = "Say whether the background is available." });
        return Ok(await presentation.SetImageActiveAsync(id, active, request.RowVersion, Actor, ct));
    });

    [HttpGet("config/login-presentation/backgrounds/{id:guid}/image")]
    [Authorize]
    [RequireAccountPermission(AccountPermission.ManageLoginPresentation)]
    public async Task<IActionResult> GetAdminImage(Guid id, CancellationToken ct)
    {
        var opened = await presentation.OpenForAdministratorAsync(id, ct);
        if (opened is not { } file) return NotFound();
        Response.Headers.CacheControl = "private, no-store";
        return ImageResult(file.Stream, file.ContentType, file.Extension);
    }

    private FileStreamResult ImageResult(Stream stream, string contentType, string extension)
    {
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers["Cross-Origin-Resource-Policy"] = "same-origin";
        Response.Headers.ContentSecurityPolicy = "sandbox";
        Response.Headers.ContentDisposition = $"inline; filename=\"login-background.{extension}\"";
        return File(stream, contentType);
    }

    private async Task<IActionResult> Run(Func<Task<IActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (LoginBackgroundRefusedException ex)
        {
            return StatusCode(ex.StatusCode, new { error = ex.Code, message = ex.Message });
        }
        catch (LoginPresentationException ex)
        {
            return StatusCode(ex.StatusCode, new { error = ex.Code, message = ex.Message });
        }
        catch (ConfigurationException ex)
        {
            return StatusCode(ex.StatusCode, new { error = ex.Code, message = ex.Message });
        }
        catch (ConcurrencyConflictException ex)
        {
            return Conflict(ex.ToProblem());
        }
    }
}
