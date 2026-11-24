using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.LoginPresentation;
using Alveara.Api.Controllers;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N020-C01 API-level proof against the real host and database: the anonymous presentation and image endpoints (shape, headers, selected-image-only), the
/// account-scoped ManageLoginPresentation (bootstrap holder, a second Admin refused, audited grant/revoke by a ManageUsers holder, CSRF), settings
/// (validation, concurrency, audit without the notice text), uploads (type, acknowledgement, duplicate, catalog cap, derivative-only storage) and the
/// selection/availability rules with the read-time fallback. Uploads go to a storage folder owned by the test.
/// </summary>
public class LoginPresentationApiTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private readonly string _storage = Path.Combine(Path.GetTempPath(), $"alv-c01-{Guid.NewGuid():N}");
    private string Backgrounds => Path.Combine(_storage, LoginBackgroundStore.FolderName);

    public Task InitializeAsync() => _fixture.InitializeAsync();

    public async Task DisposeAsync()
    {
        await _fixture.DisposeAsync();
        if (Directory.Exists(_storage)) Directory.Delete(_storage, recursive: true);
    }

    private WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Alveara", _fixture.ConnectionString);
            builder.UseSetting("StorageRoot", _storage);
        });

    private static async Task<string> CsrfAsync(HttpClient client) =>
        (await (await client.GetAsync("/api/auth/csrf-token")).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;

    private static async Task<(HttpClient Client, string Csrf)> BootstrapAdminAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        var username = $"owner-{Guid.NewGuid():N}";
        var created = await TestAccounts.SeedFirstAdminResponseAsync(factory, username, "admin-password");
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();                                 // the installer is told explicitly
        Assert.True(body.GetProperty("presentationAdministrator").GetBoolean());
        Assert.Contains("sign-in presentation administrator", body.GetProperty("notice").GetString());
        Assert.Equal(username, body.GetProperty("username").GetString());
        await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(username, "admin-password"));
        return (client, await CsrfAsync(client));
    }

    private static HttpRequestMessage Req(HttpMethod method, string url, string? csrf, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (csrf is not null) request.Headers.Add("X-CSRF-Token", csrf);
        if (body is not null) request.Content = JsonContent.Create(body);
        return request;
    }

    private static async Task<(HttpClient Client, Guid Id)> UserAsync(WebApplicationFactory<Program> factory, HttpClient admin, string csrf, string role, bool enable = true)
    {
        var username = $"user-{Guid.NewGuid():N}";
        var id = (await (await TestAccounts.SeedRegisteredResponseAsync(factory, username, "password")).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await admin.SendAsync(Req(HttpMethod.Put, $"/api/auth/{id}/role", csrf, new ChangeRoleRequest(role)));
        if (enable) await admin.SendAsync(Req(HttpMethod.Put, $"/api/auth/{id}/enabled", csrf, new SetEnabledRequest(true)));
        var client = factory.CreateClient();
        await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(username, "password"));
        return (client, id);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<string> ErrorCode(HttpResponseMessage response) => (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString()!;

    private static HttpRequestMessage Upload(string csrf, byte[] bytes, string fileName, string displayName = "Practice photo", bool acknowledged = true, string mode = "Fill")
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", fileName);
        form.Add(new StringContent(displayName), "displayName");
        form.Add(new StringContent(mode), "suggestedDisplayMode");
        form.Add(new StringContent(acknowledged ? "true" : "false"), "publicAcknowledged");
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/config/login-presentation/backgrounds") { Content = form };
        request.Headers.Add("X-CSRF-Token", csrf);
        return request;
    }

    private static object Settings(JsonElement current, string kind, string? key, Guid? added, string mode, string? notice = null, bool useDefault = true, bool show = true) =>
        new { backgroundKind = kind, bundledKey = key, addedImageId = added, displayMode = mode, footerNoticeText = notice, useDefaultNotice = useDefault, showFooterNotice = show, rowVersion = current.GetProperty("rowVersion").GetString() };

    private async Task<List<string>> AuditAsync(string eventType)
    {
        await using var db = _fixture.CreateContext();
        return await db.AuditLogEntries.AsNoTracking().Where(a => a.EventType == eventType).Select(a => a.Details).ToListAsync();
    }

    [Fact]
    public async Task The_anonymous_presentation_is_the_teal_tile_default_with_the_USA_notice_and_is_never_cached()
    {
        await using var factory = CreateFactory();
        var response = await factory.CreateClient().GetAsync("/api/public/login-presentation");
        var body = await Json(response);
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
        Assert.Equal("bundled", body.GetProperty("background").GetProperty("kind").GetString());
        Assert.Equal("teal-doodle", body.GetProperty("background").GetProperty("key").GetString());
        Assert.Equal("Tile", body.GetProperty("displayMode").GetString());
        Assert.StartsWith("Please ensure you have taken appropriate precautions", body.GetProperty("footerNotice").GetString());
        Assert.Equal(3, body.EnumerateObject().Count());                                                    // nothing else is exposed
    }

    [Fact]
    public async Task The_bootstrap_administrator_holds_the_permission_and_a_later_Admin_does_not()
    {
        await using var factory = CreateFactory();
        var (owner, csrf) = await BootstrapAdminAsync(factory);
        var (secondAdmin, _) = await UserAsync(factory, owner, csrf, "Admin");

        Assert.Contains("ManageLoginPresentation", (await Json(await owner.GetAsync("/api/auth/permissions"))).GetProperty("accountPermissions").EnumerateArray().Select(p => p.GetString()));
        Assert.Empty((await Json(await secondAdmin.GetAsync("/api/auth/permissions"))).GetProperty("accountPermissions").EnumerateArray());
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync("/api/config/login-presentation")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await secondAdmin.GetAsync("/api/config/login-presentation")).StatusCode);   // the Admin ROLE does not carry it
        Assert.Single(await AuditAsync(AccountPermissionAuditEvents.Granted), d => d.Contains("first administrator created by the installation bootstrap"));
    }

    [Fact]
    public async Task Configuration_needs_the_account_permission_and_CSRF_and_ManageUsers_grants_and_revokes_it_audited()
    {
        await using var factory = CreateFactory();
        var (owner, csrf) = await BootstrapAdminAsync(factory);
        var (frontDesk, _) = await UserAsync(factory, owner, csrf, "FrontDesk");
        var (manager, managerId) = await UserAsync(factory, owner, csrf, "OfficeManager");
        var (_, disabledId) = await UserAsync(factory, owner, csrf, "Dentist", enable: false);

        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/config/login-presentation")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await frontDesk.GetAsync("/api/config/login-presentation")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.GetAsync("/api/config/login-presentation")).StatusCode);

        var current = await Json(await owner.GetAsync("/api/config/login-presentation"));
        var noCsrf = await owner.SendAsync(Req(HttpMethod.Put, "/api/config/login-presentation", null, Settings(current, "Bundled", "blue-doodle", null, "Tile")));
        Assert.False(noCsrf.IsSuccessStatusCode);
        Assert.Equal("teal-doodle", (await Json(await owner.GetAsync("/api/config/login-presentation"))).GetProperty("bundledKey").GetString());

        // the grant path: ManageUsers only, CSRF, audited, never to a disabled account
        Assert.Equal(HttpStatusCode.Forbidden, (await frontDesk.SendAsync(Req(HttpMethod.Put, $"/api/auth/{managerId}/presentation-administrator", await CsrfAsync(frontDesk), new { granted = true }))).StatusCode);
        Assert.Equal("account_disabled", await ErrorCode(await owner.SendAsync(Req(HttpMethod.Put, $"/api/auth/{disabledId}/presentation-administrator", csrf, new { granted = true }))));
        Assert.True((await Json(await owner.SendAsync(Req(HttpMethod.Put, $"/api/auth/{managerId}/presentation-administrator", csrf, new { granted = true })))).GetProperty("changed").GetBoolean());
        Assert.False((await Json(await owner.SendAsync(Req(HttpMethod.Put, $"/api/auth/{managerId}/presentation-administrator", csrf, new { granted = true })))).GetProperty("changed").GetBoolean());   // idempotent
        Assert.Equal(HttpStatusCode.OK, (await manager.GetAsync("/api/config/login-presentation")).StatusCode);
        Assert.True((await Json(await owner.SendAsync(Req(HttpMethod.Put, $"/api/auth/{managerId}/presentation-administrator", csrf, new { granted = false })))).GetProperty("changed").GetBoolean());
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.GetAsync("/api/config/login-presentation")).StatusCode);
        Assert.Equal(2, (await AuditAsync(AccountPermissionAuditEvents.Granted)).Count);                     // bootstrap + the manager
        Assert.Single(await AuditAsync(AccountPermissionAuditEvents.Revoked));
    }

    [Fact]
    public async Task Settings_are_validated_concurrency_checked_and_audited_without_the_notice_text_and_a_disabled_notice_is_not_sent()
    {
        await using var factory = CreateFactory();
        var (owner, csrf) = await BootstrapAdminAsync(factory);
        var anonymous = factory.CreateClient();
        var current = await Json(await owner.GetAsync("/api/config/login-presentation"));

        Assert.Equal("notice_markup", await ErrorCode(await owner.SendAsync(Req(HttpMethod.Put, "/api/config/login-presentation", csrf, Settings(current, "Bundled", "blue-doodle", null, "Tile", "<b>hi</b>", useDefault: false)))));
        Assert.Equal("background_unknown", await ErrorCode(await owner.SendAsync(Req(HttpMethod.Put, "/api/config/login-presentation", csrf, Settings(current, "Bundled", "../etc", null, "Tile")))));
        Assert.Equal("display_mode_invalid", await ErrorCode(await owner.SendAsync(Req(HttpMethod.Put, "/api/config/login-presentation", csrf, Settings(current, "Bundled", "blue-doodle", null, "Centered")))));

        var saved = await Json(await owner.SendAsync(Req(HttpMethod.Put, "/api/config/login-presentation", csrf,
            Settings(current, "Bundled", "blue-operatory", null, "Fill", "Authorized users only.\nActivity is monitored.", useDefault: false, show: false))));
        var stale = await owner.SendAsync(Req(HttpMethod.Put, "/api/config/login-presentation", csrf, Settings(current, "Bundled", "teal-doodle", null, "Tile")));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);                                            // the version read before the save

        var hidden = await Json(await anonymous.GetAsync("/api/public/login-presentation"));
        Assert.Equal(("blue-operatory", "Fill"), (hidden.GetProperty("background").GetProperty("key").GetString(), hidden.GetProperty("displayMode").GetString()));
        Assert.Equal(JsonValueKind.Null, hidden.GetProperty("footerNotice").ValueKind);                     // disabled: not sent at all

        saved = await Json(await owner.SendAsync(Req(HttpMethod.Put, "/api/config/login-presentation", csrf, Settings(saved, "Bundled", "blue-operatory", null, "Fill", "Authorized users only.\nActivity is monitored.", useDefault: false, show: true))));
        Assert.Equal("Authorized users only.\nActivity is monitored.", (await Json(await anonymous.GetAsync("/api/public/login-presentation"))).GetProperty("footerNotice").GetString());
        await Json(await owner.SendAsync(Req(HttpMethod.Put, "/api/config/login-presentation", csrf, Settings(saved, "Bundled", "blue-operatory", null, "Fill"))));
        Assert.StartsWith("Please ensure", (await Json(await anonymous.GetAsync("/api/public/login-presentation"))).GetProperty("footerNotice").GetString());

        Assert.Single(await AuditAsync(LoginPresentationAuditEvents.BackgroundSelected), d => d.Contains("'teal-doodle' (Tile) to bundled 'blue-operatory' (Fill)"));
        var notice = await AuditAsync(LoginPresentationAuditEvents.FooterNoticeChanged);
        Assert.Equal(2, notice.Count);
        Assert.All(notice, d => Assert.DoesNotContain("Authorized users only", d));                         // the notice text is not copied into the audit
        Assert.Single(await AuditAsync(LoginPresentationAuditEvents.FooterNoticeReset));
    }

    [Fact]
    public async Task An_added_background_is_stored_only_as_its_derivative_and_served_only_while_selected_with_strict_headers()
    {
        await using var factory = CreateFactory();
        var (owner, csrf) = await BootstrapAdminAsync(factory);
        var anonymous = factory.CreateClient();

        Assert.Equal("public_acknowledgement_required", await ErrorCode(await owner.SendAsync(Upload(csrf, LoginTestImages.Png(400, 300), "a.png", acknowledged: false))));
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await owner.SendAsync(Upload(csrf, "<svg xmlns='http://www.w3.org/2000/svg'/>"u8.ToArray(), "a.svg"))).StatusCode);
        var added = await Json(await owner.SendAsync(Upload(csrf, LoginTestImages.JpegWithExifXmpIptc(800, 600), "IMG_0042 patient smith.jpg", "Waiting room")));
        var id = added.GetProperty("id").GetGuid();
        Assert.Equal("background_duplicate", await ErrorCode(await owner.SendAsync(Upload(csrf, LoginTestImages.JpegWithExifXmpIptc(800, 600), "again.jpg"))));

        var files = Directory.GetFiles(Backgrounds);
        Assert.Single(files);                                                                               // the derivative only: no original, no temporary file
        Assert.Matches("^[0-9a-f]{32}\\.jpg$", Path.GetFileName(files[0]));                                // a server-chosen name
        Assert.DoesNotContain(await AuditAsync(LoginPresentationAuditEvents.BackgroundAdded), d => d.Contains("IMG_0042") || d.Contains("smith"));

        var current = await Json(await owner.GetAsync("/api/config/login-presentation"));
        current = await Json(await owner.SendAsync(Req(HttpMethod.Put, "/api/config/login-presentation", csrf, Settings(current, "Added", null, id, "Stretch"))));
        var pub = await Json(await anonymous.GetAsync("/api/public/login-presentation"));
        Assert.Equal("Stretch", pub.GetProperty("displayMode").GetString());
        var url = pub.GetProperty("background").GetProperty("url").GetString()!;
        Assert.Matches("^/api/public/login-background/[0-9a-f]{64}\\.jpg$", url);

        var image = await anonymous.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, image.StatusCode);
        Assert.Equal("image/jpeg", image.Content.Headers.ContentType!.MediaType);
        Assert.Equal("nosniff", image.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("same-origin", image.Headers.GetValues("Cross-Origin-Resource-Policy").Single());
        Assert.Equal("sandbox", image.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal("login-background.jpg", image.Content.Headers.ContentDisposition!.FileName!.Trim('"'));               // server-controlled, never the uploaded name
        var bytes = await image.Content.ReadAsByteArrayAsync();
        Assert.DoesNotContain((byte)0xE1, LoginTestImages.JpegHeaderMarkers(bytes));                        // served bytes carry no EXIF/XMP

        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/public/login-background/{new string('a', 64)}.jpg")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/api/public/login-background/..%2F..%2Fappsettings.json")).StatusCode);
        Assert.Equal("background_in_use", await ErrorCode(await owner.SendAsync(Req(HttpMethod.Put, $"/api/config/login-presentation/backgrounds/{id}/active", csrf,
            new { isActive = false, rowVersion = added.GetProperty("rowVersion").GetString() }))));

        current = await Json(await owner.SendAsync(Req(HttpMethod.Put, "/api/config/login-presentation", csrf, Settings(current, "Bundled", "teal-doodle", null, "Tile"))));
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(url)).StatusCode);                 // no longer selected: no longer served
        var inactive = await Json(await owner.SendAsync(Req(HttpMethod.Put, $"/api/config/login-presentation/backgrounds/{id}/active", csrf,
            new { isActive = false, rowVersion = added.GetProperty("rowVersion").GetString() })));
        Assert.False(inactive.GetProperty("isActive").GetBoolean());
        Assert.Equal("background_unavailable", await ErrorCode(await owner.SendAsync(Req(HttpMethod.Put, "/api/config/login-presentation", csrf, Settings(current, "Added", null, id, "Fill")))));
        Assert.Single(await AuditAsync(LoginPresentationAuditEvents.BackgroundInactivated));
    }

    [Fact]
    public async Task A_selected_background_whose_file_is_missing_falls_back_to_the_bundled_default()
    {
        await using var factory = CreateFactory();
        var (owner, csrf) = await BootstrapAdminAsync(factory);
        var id = (await Json(await owner.SendAsync(Upload(csrf, LoginTestImages.Png(500, 500), "a.png")))).GetProperty("id").GetGuid();
        var current = await Json(await owner.GetAsync("/api/config/login-presentation"));
        await Json(await owner.SendAsync(Req(HttpMethod.Put, "/api/config/login-presentation", csrf, Settings(current, "Added", null, id, "Fill"))));

        File.Delete(Directory.GetFiles(Backgrounds).Single());
        var pub = await Json(await factory.CreateClient().GetAsync("/api/public/login-presentation"));
        Assert.Equal(("bundled", "teal-doodle", "Tile"),
            (pub.GetProperty("background").GetProperty("kind").GetString(), pub.GetProperty("background").GetProperty("key").GetString(), pub.GetProperty("displayMode").GetString()));
    }

    [Fact]
    public async Task At_most_30_added_backgrounds_are_available_at_once()
    {
        await using var factory = CreateFactory();
        var (owner, csrf) = await BootstrapAdminAsync(factory);
        for (var i = 0; i < LoginPresentationService.MaxActiveAdded; i++)
            await Json(await owner.SendAsync(Upload(csrf, LoginTestImages.Png(256, 256, new SkiaSharp.SKColor((byte)(i * 8), 40, 90)), $"b{i}.png", $"Background {i}")));
        Assert.Equal("background_catalog_full", await ErrorCode(await owner.SendAsync(Upload(csrf, LoginTestImages.Png(256, 256, new SkiaSharp.SKColor(250, 250, 10)), "c.png"))));
        Assert.Equal(LoginPresentationService.MaxActiveAdded, Directory.GetFiles(Backgrounds).Length);      // the refused one stored nothing
    }
}
