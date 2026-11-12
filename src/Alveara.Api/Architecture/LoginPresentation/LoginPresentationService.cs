using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Configuration;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.LoginPresentation;

public sealed class LoginPresentationException(string code, string message, int statusCode) : Exception(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
}

public static class LoginPresentationAuditEvents
{
    public const string BackgroundAdded = "LoginBackgroundAdded";
    public const string BackgroundInactivated = "LoginBackgroundInactivated";
    public const string BackgroundReactivated = "LoginBackgroundReactivated";
    public const string BackgroundSelected = "LoginBackgroundSelected";
    public const string FooterNoticeChanged = "LoginFooterNoticeChanged";
    public const string FooterNoticeReset = "LoginFooterNoticeReset";
}

/// <summary>What the sign-in page needs, identical for every visitor. <see cref="AddedImageUrl"/> is set only for an added background.</summary>
public sealed record PublicLoginPresentation(string BackgroundKind, string? BundledKey, string? AddedImageUrl, string DisplayMode, string? FooterNotice);

public sealed record AddedBackgroundView(Guid Id, string DisplayName, int Width, int Height, string SuggestedDisplayMode, bool IsActive, DateTimeOffset AddedAtUtc, string RowVersion);

public sealed record AdminLoginPresentation(
    string BackgroundKind, string? BundledKey, Guid? AddedImageId, string DisplayMode, string? FooterNoticeText, bool ShowFooterNotice, string? DeploymentDefaultNotice,
    IReadOnlyList<BundledBackground> Bundled, IReadOnlyList<AddedBackgroundView> Added, int MaxActiveAdded, string RowVersion);

public sealed record SaveLoginPresentation(string? BackgroundKind, string? BundledKey, Guid? AddedImageId, string? DisplayMode, string? FooterNoticeText, bool? UseDefaultNotice, bool? ShowFooterNotice, string? RowVersion);

/// <summary>
/// ALV-N020-C01: the sign-in presentation. Every write is authorized by the caller's controller (ManageLoginPresentation + CSRF), validated here, concurrency-checked
/// against the settings row version, and audited (never image bytes, user-supplied file names or credentials; the notice text itself is not copied into the audit).
/// Failure modes: an invalid stored selection or a missing/unreadable derivative falls back to the bundled default at read time (and is logged); a refused
/// upload stores nothing; a derivative whose row cannot be written is removed again. No retry: each operation is one administrator action.
/// </summary>
public class LoginPresentationService(AlveraDbContext db, LoginBackgroundStore store, IConfiguration configuration, ILogger<LoginPresentationService> log)
{
    public const int MaxActiveAdded = 30;
    public const int DisplayNameMaxLength = 60;

    /// <summary>The deployment default notice (configuration <c>LoginPresentation:DefaultFooterNotice</c>), checked with the same rules; invalid = none.</summary>
    public string? DeploymentDefaultNotice()
    {
        var check = LoginNoticeRules.Check(configuration["LoginPresentation:DefaultFooterNotice"]);
        if (!check.Ok) log.LogWarning("login_default_notice_invalid error_code={Code}", check.ErrorCode);
        return check.Ok ? check.Text : null;
    }

    private Task<LoginPresentationSettings> SettingsAsync(bool track, CancellationToken ct) =>
        (track ? db.LoginPresentationSettings : db.LoginPresentationSettings.AsNoTracking()).SingleAsync(s => s.Id == LoginPresentationSettings.SingletonId, ct);

    public async Task<PublicLoginPresentation> GetPublicAsync(CancellationToken ct)
    {
        var s = await SettingsAsync(false, ct);
        var notice = s.ShowFooterNotice ? s.FooterNoticeText ?? DeploymentDefaultNotice() : null;
        var fallback = new PublicLoginPresentation(LoginBackgroundKinds.Bundled, LoginBackgroundCatalog.DefaultKey, null, LoginDisplayModes.Tile, notice);

        if (s.BackgroundKind == LoginBackgroundKinds.Bundled)
        {
            var bundled = LoginBackgroundCatalog.Find(s.BundledKey);
            if (bundled is null || (s.DisplayMode == LoginDisplayModes.Tile && !bundled.TileAllowed))
            {
                log.LogWarning("login_background_fallback reason=bundled_invalid key={Key}", s.BundledKey);
                return fallback;
            }
            return new(LoginBackgroundKinds.Bundled, bundled.Key, null, s.DisplayMode, notice);
        }

        var image = s.AddedImageId is { } id ? await db.LoginBackgroundImages.AsNoTracking().SingleOrDefaultAsync(i => i.Id == id, ct) : null;
        if (image is null || !image.IsActive || !store.Exists(image.StorageName))
        {
            log.LogWarning("login_background_fallback reason={Reason} image={ImageId}", image is null ? "missing_row" : !image.IsActive ? "inactive" : "missing_file", s.AddedImageId);
            return fallback;
        }
        return new(LoginBackgroundKinds.Added, null, $"/api/public/login-background/{image.Sha256}.{image.Extension}", s.DisplayMode, notice);
    }

    /// <summary>Opens the derivative only when <paramref name="sha256"/> is the CURRENT, active, added selection; anything else is null (the caller answers 404).</summary>
    public async Task<(Stream Stream, string ContentType, string Extension)?> OpenSelectedAsync(string sha256, string extension, CancellationToken ct)
    {
        var s = await SettingsAsync(false, ct);
        if (s.BackgroundKind != LoginBackgroundKinds.Added || s.AddedImageId is not { } id) return null;
        var image = await db.LoginBackgroundImages.AsNoTracking().SingleOrDefaultAsync(i => i.Id == id, ct);
        if (image is null || !image.IsActive || image.Sha256 != sha256 || image.Extension != extension || !store.Exists(image.StorageName)) return null;
        return (store.OpenRead(image.StorageName), image.ContentType, image.Extension);
    }

    /// <summary>The derivative of any added image, for the presentation administrator's own thumbnails (authorized by the caller).</summary>
    public async Task<(Stream Stream, string ContentType, string Extension)?> OpenForAdministratorAsync(Guid id, CancellationToken ct)
    {
        var image = await db.LoginBackgroundImages.AsNoTracking().SingleOrDefaultAsync(i => i.Id == id, ct);
        if (image is null || !store.Exists(image.StorageName)) return null;
        return (store.OpenRead(image.StorageName), image.ContentType, image.Extension);
    }

    public async Task<AdminLoginPresentation> GetAdminAsync(CancellationToken ct)
    {
        var s = await SettingsAsync(false, ct);
        var added = await db.LoginBackgroundImages.AsNoTracking().OrderByDescending(i => i.AddedAtUtc).ToListAsync(ct);
        return new(s.BackgroundKind, s.BundledKey, s.AddedImageId, s.DisplayMode, s.FooterNoticeText, s.ShowFooterNotice, DeploymentDefaultNotice(),
            LoginBackgroundCatalog.Bundled,
            added.Select(i => new AddedBackgroundView(i.Id, i.DisplayName, i.Width, i.Height, i.SuggestedDisplayMode, i.IsActive, i.AddedAtUtc, Convert.ToBase64String(i.RowVersion))).ToList(),
            MaxActiveAdded, Convert.ToBase64String(s.RowVersion));
    }

    public async Task<AdminLoginPresentation> SaveAsync(SaveLoginPresentation request, Guid actor, CancellationToken ct)
    {
        var mode = request.DisplayMode;
        if (mode is null || !LoginDisplayModes.All.Contains(mode)) throw Bad("display_mode_invalid", "Choose Tile, Stretch or Fill.");
        if (request.ShowFooterNotice is not { } show) throw Bad("show_notice_required", "Say whether the footer notice is shown.");

        string? bundledKey = null;
        Guid? addedId = null;
        if (request.BackgroundKind == LoginBackgroundKinds.Bundled)
        {
            var bundled = LoginBackgroundCatalog.Find(request.BundledKey) ?? throw Bad("background_unknown", "That background is not in the catalog.");
            if (mode == LoginDisplayModes.Tile && !bundled.TileAllowed) throw Bad("tile_not_allowed", "This background cannot be tiled. Choose Stretch or Fill.");
            bundledKey = bundled.Key;
        }
        else if (request.BackgroundKind == LoginBackgroundKinds.Added)
        {
            var image = request.AddedImageId is { } id ? await db.LoginBackgroundImages.AsNoTracking().SingleOrDefaultAsync(i => i.Id == id, ct) : null;
            if (image is null) throw Bad("background_unknown", "That background is not in the catalog.");
            if (!image.IsActive) throw new LoginPresentationException("background_unavailable", "That background has been made unavailable. Make it available first.", 409);
            addedId = image.Id;
        }
        else throw Bad("background_kind_invalid", "Choose a background from the catalog.");

        string? noticeText;
        if (request.UseDefaultNotice == true) noticeText = null;
        else
        {
            var check = LoginNoticeRules.Check(request.FooterNoticeText);
            if (!check.Ok) throw Bad(check.ErrorCode!, check.ErrorMessage!);
            noticeText = check.Text;                                                                        // empty text also means "use the deployment default"
        }

        var s = await SettingsAsync(true, ct);
        ConfigurationWrite.ApplyExpectedVersion(db, s, request.RowVersion);
        var beforeRef = Describe(s.BackgroundKind, s.BundledKey, s.AddedImageId);
        var afterRef = Describe(request.BackgroundKind!, bundledKey, addedId);
        var beforeMode = s.DisplayMode;
        var beforeShow = s.ShowFooterNotice;
        var beforeText = s.FooterNoticeText;

        s.BackgroundKind = request.BackgroundKind!;
        s.BundledKey = bundledKey;
        s.AddedImageId = addedId;
        s.DisplayMode = mode;
        s.FooterNoticeText = noticeText;
        s.ShowFooterNotice = show;
        s.UpdatedAtUtc = DateTimeOffset.UtcNow;
        s.UpdatedByUserAccountId = actor;

        if (beforeRef != afterRef || beforeMode != mode)
            Audit(LoginPresentationAuditEvents.BackgroundSelected, actor, $"Sign-in background changed from {beforeRef} ({beforeMode}) to {afterRef} ({mode}).");
        if (beforeText != noticeText && noticeText is null)
            Audit(LoginPresentationAuditEvents.FooterNoticeReset, actor, $"Sign-in footer notice reset to the deployment default; shown: {Show(beforeShow)} -> {Show(show)}.");
        else if (beforeText != noticeText || beforeShow != show)
            Audit(LoginPresentationAuditEvents.FooterNoticeChanged, actor,
                $"Sign-in footer notice: shown {Show(beforeShow)} -> {Show(show)}; text {(beforeText == noticeText ? "unchanged" : "changed")} ({(noticeText is null ? "deployment default" : $"custom, {noticeText.Length} characters")}).");

        await SaveGuardedAsync(ct);
        return await GetAdminAsync(ct);
    }

    public async Task<AddedBackgroundView> AddImageAsync(byte[] input, string? fileName, string? displayName, string? suggestedMode, bool publicAcknowledged, Guid actor, CancellationToken ct)
    {
        if (!publicAcknowledged)
            throw Bad("public_acknowledgement_required", "Confirm that the image is public and contains no patient or other confidential information.");
        var name = (displayName ?? string.Empty).Trim();
        if (name.Length == 0 || name.Length > DisplayNameMaxLength || name.Any(char.IsControl) || name.Contains('<') || name.Contains('>'))
            throw Bad("display_name_invalid", $"Give the background a plain name of 1 to {DisplayNameMaxLength} characters.");
        var mode = suggestedMode ?? LoginDisplayModes.Fill;
        if (!LoginDisplayModes.All.Contains(mode)) throw Bad("display_mode_invalid", "Choose Tile, Stretch or Fill.");

        var derivative = LoginBackgroundPipeline.Process(input, fileName);                                  // refusals throw LoginBackgroundRefusedException
        if (await db.LoginBackgroundImages.AsNoTracking().AnyAsync(i => i.Sha256 == derivative.Sha256, ct))
            throw new LoginPresentationException("background_duplicate", "This image is already in the collection.", 409);
        if (await db.LoginBackgroundImages.CountAsync(i => i.IsActive, ct) >= MaxActiveAdded)
            throw new LoginPresentationException("background_catalog_full", $"The collection already has {MaxActiveAdded} available added backgrounds. Make one unavailable first.", 409);

        var storageName = LoginBackgroundStore.NewStorageName(derivative.Extension);
        await store.SaveAsync(storageName, derivative.Bytes, ct);
        var image = new LoginBackgroundImage
        {
            Id = Guid.NewGuid(), DisplayName = name, Sha256 = derivative.Sha256, ContentType = derivative.ContentType, Extension = derivative.Extension,
            Width = derivative.Width, Height = derivative.Height, SizeBytes = derivative.Bytes.Length, StorageName = storageName, SuggestedDisplayMode = mode,
            IsActive = true, AddedByUserAccountId = actor, AddedAtUtc = DateTimeOffset.UtcNow,
        };
        db.LoginBackgroundImages.Add(image);
        Audit(LoginPresentationAuditEvents.BackgroundAdded, actor,
            $"Sign-in background '{name}' added: {derivative.ContentType}, {derivative.Width} x {derivative.Height}, {derivative.Bytes.Length} bytes, sha256 {derivative.Sha256}.", image.Id);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            db.ChangeTracker.Clear();
            store.DeleteUnreferenced(storageName);                                                          // nothing references the file: never leave it behind
            if (ex.InnerException is SqlException { Number: 2601 or 2627 })
                throw new LoginPresentationException("background_duplicate", "This image is already in the collection.", 409);
            throw;
        }
        return new(image.Id, image.DisplayName, image.Width, image.Height, image.SuggestedDisplayMode, true, image.AddedAtUtc, Convert.ToBase64String(image.RowVersion));
    }

    public async Task<AddedBackgroundView> SetImageActiveAsync(Guid id, bool active, string? rowVersion, Guid actor, CancellationToken ct)
    {
        var image = await db.LoginBackgroundImages.SingleOrDefaultAsync(i => i.Id == id, ct)
            ?? throw new LoginPresentationException("background_not_found", "That background was not found.", 404);
        if (image.IsActive != active)
        {
            if (!active)
            {
                var s = await SettingsAsync(false, ct);
                if (s.BackgroundKind == LoginBackgroundKinds.Added && s.AddedImageId == id)
                    throw new LoginPresentationException("background_in_use", "This background is the one shown now. Choose another background first.", 409);
            }
            else if (await db.LoginBackgroundImages.CountAsync(i => i.IsActive, ct) >= MaxActiveAdded)
                throw new LoginPresentationException("background_catalog_full", $"The collection already has {MaxActiveAdded} available added backgrounds. Make one unavailable first.", 409);

            ConfigurationWrite.ApplyExpectedVersion(db, image, rowVersion);
            image.IsActive = active;
            Audit(active ? LoginPresentationAuditEvents.BackgroundReactivated : LoginPresentationAuditEvents.BackgroundInactivated, actor,
                $"Sign-in background '{image.DisplayName}' made {(active ? "available" : "unavailable")}.", image.Id);
            await SaveGuardedAsync(ct);
        }
        return new(image.Id, image.DisplayName, image.Width, image.Height, image.SuggestedDisplayMode, image.IsActive, image.AddedAtUtc, Convert.ToBase64String(image.RowVersion));
    }

    /// <summary>Saves; the database triggers are the backstop for a selection/availability race (51277, 51279) and become the same clear 409s.</summary>
    private async Task SaveGuardedAsync(CancellationToken ct)
    {
        try
        {
            await ConfigurationWrite.SaveAsync(db, nameof(LoginPresentationSettings), LoginPresentationSettings.SingletonId, ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 51277 })
        {
            db.ChangeTracker.Clear();
            throw new LoginPresentationException("background_in_use", "This background is the one shown now. Choose another background first.", 409);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 51279 })
        {
            db.ChangeTracker.Clear();
            throw new LoginPresentationException("background_unavailable", "That background has been made unavailable. Make it available first.", 409);
        }
    }

    private void Audit(string eventType, Guid actor, string details, Guid? entityId = null) =>
        AuditService.Record(db, eventType, entityId is null ? nameof(LoginPresentationSettings) : nameof(LoginBackgroundImage), entityId ?? LoginPresentationSettings.SingletonId, actor, details);

    private static string Describe(string kind, string? bundledKey, Guid? addedId) => kind == LoginBackgroundKinds.Bundled ? $"bundled '{bundledKey}'" : $"added image {addedId}";
    private static string Show(bool shown) => shown ? "yes" : "no";
    private static LoginPresentationException Bad(string code, string message) => new(code, message, 400);
}
