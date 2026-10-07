using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Backup;
using Alveara.Api.Controllers;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N004 API-level proof: authorization (401/403 per role), CSRF, step-up reauthentication,
/// audit of every sensitive action, that recovery material is shown once and never echoed, that
/// failures surface only stable safe codes, and that there is no endpoint that restores over live data.
/// </summary>
[Collection(ParallelismCollections.SerialServer)]
public class BackupApiTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"alveara-backup-api-{Guid.NewGuid():N}");
    private const string AdminPassword = "admin-password";
    
    public Task InitializeAsync() => _fixture.InitializeAsync();

    public async Task DisposeAsync()
    {
        // Drop any isolated restore databases this test created before the fixture drops the main one.
        try
        {
            await using var db = _fixture.CreateContext();
            var provider = new SqlServerRestoreProvider(_fixture.ConnectionString);
            foreach (var name in await db.RestoreDrills.Select(d => d.TargetDatabase!).ToListAsync()) await provider.DropDatabaseAsync(name, default);
        }
        catch (Exception ex) when (ex is Microsoft.Data.SqlClient.SqlException or InvalidOperationException) { /* nothing to clean */ }
        await _fixture.DisposeAsync();
        try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch (IOException) { /* temp */ }
    }

    private WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Alveara", _fixture.ConnectionString);
            builder.UseSetting("AdminBootstrapSecret", IdentityTestHelpers.TestBootstrapSecret);
            builder.UseSetting("Backup:Root", Path.Combine(_root, "backup"));
            builder.UseSetting("StorageRoot", Path.Combine(_root, "blobs"));
            builder.UseSetting("DataProtectionKeysPath", Path.Combine(_root, "keys"));
        });

    private static async Task<string> CsrfAsync(HttpClient client) =>
        (await (await client.GetAsync("/api/auth/csrf-token")).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;

    private static async Task<(HttpClient Client, string Csrf)> AdminAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        var username = $"admin-{Guid.NewGuid():N}";
        await client.PostAsJsonAsync("/api/auth/bootstrap-admin", new BootstrapAdminRequest(username, AdminPassword, IdentityTestHelpers.TestBootstrapSecret));
        await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(username, AdminPassword));
        return (client, await CsrfAsync(client));
    }

    private static HttpRequestMessage Req(HttpMethod method, string url, string? csrf, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (csrf is not null) request.Headers.Add("X-CSRF-Token", csrf);
        if (body is not null) request.Content = JsonContent.Create(body);
        return request;
    }

    private static async Task<HttpClient> UserWithRoleAsync(WebApplicationFactory<Program> factory, HttpClient admin, string adminCsrf, string role)
    {
        var username = $"user-{Guid.NewGuid():N}";
        var registered = await (await admin.PostAsJsonAsync("/api/auth/register", new RegisterRequest(username, "password"))).Content.ReadFromJsonAsync<JsonElement>();
        var id = registered.GetProperty("id").GetGuid();
        await admin.SendAsync(Req(HttpMethod.Put, $"/api/auth/{id}/role", adminCsrf, new ChangeRoleRequest(role)));
        await admin.SendAsync(Req(HttpMethod.Put, $"/api/auth/{id}/enabled", adminCsrf, new SetEnabledRequest(true)));
        var client = factory.CreateClient();
        await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(username, "password"));
        return client;
    }

    private static async Task<JsonElement> JsonOf(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    /// <summary>Sets up the recovery key through the API, returning the private key file contents the administrator would store offline.</summary>
    private static async Task<(string Key, string Passphrase)> SetUpRecoveryKeyAsync(HttpClient admin, string csrf)
    {
        var response = await admin.SendAsync(Req(HttpMethod.Post, "/api/backup/recovery-key", csrf, new ConfigureRecoveryKeyRequest(AdminPassword, false)));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await JsonOf(response);
        return (body.GetProperty("recoveryKey").GetString()!, body.GetProperty("passphrase").GetString()!);
    }

    // ---------- authorization ----------

    [Fact]
    public async Task Backup_endpoints_require_authentication_and_the_right_permission_per_role()
    {
        await using var factory = CreateFactory();
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().GetAsync("/api/backup/status")).StatusCode);

        var (admin, csrf) = await AdminAsync(factory);
        var frontDesk = await UserWithRoleAsync(factory, admin, csrf, "FrontDesk");
        var dentist = await UserWithRoleAsync(factory, admin, csrf, "Dentist");
        var manager = await UserWithRoleAsync(factory, admin, csrf, "OfficeManager");

        foreach (var url in new[] { "/api/backup/status", "/api/backup/history", "/api/backup/notifications" })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await frontDesk.GetAsync(url)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await dentist.GetAsync(url)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await manager.GetAsync(url)).StatusCode);   // the practice manager can SEE status and failures...
            Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync(url)).StatusCode);
        }

        // ...but cannot manage anything.
        var managerCsrf = await CsrfAsync(manager);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.GetAsync("/api/backup/restore-drills")).StatusCode);
        foreach (var (method, url, body) in new (HttpMethod, string, object?)[]
        {
            (HttpMethod.Post, "/api/backup/backups", null),
            (HttpMethod.Post, "/api/backup/recovery-key", new ConfigureRecoveryKeyRequest("x", false)),
            (HttpMethod.Put, "/api/backup/settings", new SaveBackupSettingsRequest(false, 24, 7, null, 2, 30, null)),
            (HttpMethod.Post, $"/api/backup/backups/{Guid.NewGuid()}/verify", new RecoveryMaterialRequest("x", "k", "p")),
            (HttpMethod.Post, $"/api/backup/backups/{Guid.NewGuid()}/restore-drill", new RecoveryMaterialRequest("x", "k", "p")),
            (HttpMethod.Post, "/api/backup/notifications/test", null),
        })
        {
            var denied = await manager.SendAsync(Req(method, url, managerCsrf, body));
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            Assert.Equal("ManageBackups", (await JsonOf(denied)).GetProperty("required").GetString());
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await frontDesk.SendAsync(Req(HttpMethod.Post, "/api/backup/backups", await CsrfAsync(frontDesk)))).StatusCode);
    }

    [Fact]
    public async Task State_changing_backup_calls_require_a_csrf_token()
    {
        await using var factory = CreateFactory();
        var (admin, _) = await AdminAsync(factory);
        var response = await admin.SendAsync(Req(HttpMethod.Post, "/api/backup/backups", null));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("csrf_token_invalid", (await JsonOf(response)).GetProperty("error").GetString());
    }

    [Fact]
    public async Task There_is_no_endpoint_that_restores_over_the_live_data()
    {
        await using var factory = CreateFactory();
        var (admin, csrf) = await AdminAsync(factory);
        foreach (var url in new[] { "/api/backup/restore", $"/api/backup/backups/{Guid.NewGuid()}/restore", $"/api/backup/backups/{Guid.NewGuid()}/promote" })
            Assert.Contains((await admin.SendAsync(Req(HttpMethod.Post, url, csrf, new RecoveryMaterialRequest(AdminPassword, "k", "p")))).StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed });
    }

    // ---------- step-up reauthentication ----------

    [Fact]
    public async Task Sensitive_actions_require_the_current_password_and_a_wrong_one_is_refused_and_audited()
    {
        await using var factory = CreateFactory();
        var (admin, csrf) = await AdminAsync(factory);

        foreach (var password in new string?[] { null, "", "not-my-password" })
        {
            var response = await admin.SendAsync(Req(HttpMethod.Post, "/api/backup/recovery-key", csrf, new ConfigureRecoveryKeyRequest(password, false)));
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal("step_up_failed", (await JsonOf(response)).GetProperty("error").GetString());
        }

        var status = await JsonOf(await admin.GetAsync("/api/backup/status"));
        Assert.False(status.GetProperty("settings").GetProperty("recoveryKeyConfigured").GetBoolean()); // nothing was configured by the refused calls

        var audit = await JsonOf(await admin.GetAsync("/api/auth/audit-log?take=500"));
        Assert.Contains(audit.EnumerateArray(), e => e.GetProperty("eventType").GetString() == "SensitiveActionStepUpFailed");

        // Verify/drill/remove-target are protected the same way.
        var id = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.SendAsync(Req(HttpMethod.Post, $"/api/backup/backups/{id}/verify", csrf, new RecoveryMaterialRequest("wrong", "k", "p")))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.SendAsync(Req(HttpMethod.Post, $"/api/backup/backups/{id}/restore-drill", csrf, new RecoveryMaterialRequest("wrong", "k", "p")))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.SendAsync(Req(HttpMethod.Post, $"/api/backup/restore-drills/{id}/remove-target", csrf, new StepUpRequest("wrong")))).StatusCode);
    }

    [Fact]
    public async Task Repeated_wrong_passwords_on_a_sensitive_action_lock_the_account_so_the_step_up_is_not_a_password_oracle()
    {
        await using var factory = CreateFactory();
        var (admin, csrf) = await AdminAsync(factory);

        HttpResponseMessage last = null!;
        for (var i = 0; i < 5; i++)
            last = await admin.SendAsync(Req(HttpMethod.Post, "/api/backup/recovery-key", csrf, new ConfigureRecoveryKeyRequest($"wrong-{i}", false)));
        Assert.Equal(HttpStatusCode.TooManyRequests, last.StatusCode);
        Assert.Equal("account_locked", (await JsonOf(last)).GetProperty("error").GetString());

        // Even the CORRECT password is now refused until the lockout passes.
        var correct = await admin.SendAsync(Req(HttpMethod.Post, "/api/backup/recovery-key", csrf, new ConfigureRecoveryKeyRequest(AdminPassword, false)));
        Assert.Equal(HttpStatusCode.TooManyRequests, correct.StatusCode);
        Assert.False((await JsonOf(await admin.GetAsync("/api/backup/status"))).GetProperty("settings").GetProperty("recoveryKeyConfigured").GetBoolean());
    }

    // ---------- recovery key ----------

    [Fact]
    public async Task The_recovery_key_and_its_generated_passphrase_are_shown_once_never_cached_never_echoed_and_replacing_it_is_explicit()
    {
        await using var factory = CreateFactory();
        var (admin, csrf) = await AdminAsync(factory);

        var response = await admin.SendAsync(Req(HttpMethod.Post, "/api/backup/recovery-key", csrf, new ConfigureRecoveryKeyRequest(AdminPassword, false)));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString());
        var body = await JsonOf(response);
        var privateKey = body.GetProperty("recoveryKey").GetString()!;
        var passphrase = body.GetProperty("passphrase").GetString()!;
        Assert.StartsWith("-----BEGIN PGP PRIVATE KEY BLOCK-----", privateKey);
        Assert.Matches("^([A-Z2-7]{4}-){5}[A-Z2-7]{4}$", passphrase); // generated server-side: 120 bits, never person-chosen
        Assert.Contains("OFFLINE", body.GetProperty("warning").GetString());

        // Nothing the API serves afterwards contains the private key or the passphrase.
        foreach (var url in new[] { "/api/backup/status", "/api/backup/history", "/api/backup/notifications", "/api/auth/audit-log?take=500" })
        {
            var text = await (await admin.GetAsync(url)).Content.ReadAsStringAsync();
            Assert.DoesNotContain("PRIVATE KEY", text);
            Assert.DoesNotContain(passphrase, text);
        }
        var status = await JsonOf(await admin.GetAsync("/api/backup/status"));
        Assert.True(status.GetProperty("settings").GetProperty("recoveryKeyConfigured").GetBoolean());
        Assert.Equal(body.GetProperty("fingerprint").GetString(), status.GetProperty("settings").GetProperty("recoveryKeyFingerprint").GetString());

        var again = await admin.SendAsync(Req(HttpMethod.Post, "/api/backup/recovery-key", csrf, new ConfigureRecoveryKeyRequest(AdminPassword, false)));
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("recovery_key_exists", (await JsonOf(again)).GetProperty("error").GetString());

        var replaced = await admin.SendAsync(Req(HttpMethod.Post, "/api/backup/recovery-key", csrf, new ConfigureRecoveryKeyRequest(AdminPassword, true)));
        Assert.Equal(HttpStatusCode.OK, replaced.StatusCode);
        var replacement = await JsonOf(replaced);
        Assert.NotEqual(passphrase, replacement.GetProperty("passphrase").GetString());
        Assert.NotEqual(body.GetProperty("fingerprint").GetString(), replacement.GetProperty("fingerprint").GetString());
    }

    [Fact]
    public async Task A_backup_cannot_be_made_before_the_recovery_key_is_set_up()
    {
        await using var factory = CreateFactory();
        var (admin, csrf) = await AdminAsync(factory);
        var response = await admin.SendAsync(Req(HttpMethod.Post, "/api/backup/backups", csrf));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("recovery_key_required", (await JsonOf(response)).GetProperty("error").GetString());
    }

    // ---------- the whole flow over HTTP ----------

    [Fact]
    public async Task Manual_backup_verification_and_restore_drill_work_end_to_end_over_the_api_and_are_all_audited()
    {
        await using var factory = CreateFactory();
        var (admin, csrf) = await AdminAsync(factory);
        var (privateKey, passphrase) = await SetUpRecoveryKeyAsync(admin, csrf);

        var created = await admin.SendAsync(Req(HttpMethod.Post, "/api/backup/backups", csrf));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var backup = await JsonOf(created);
        var id = backup.GetProperty("id").GetGuid();
        Assert.Equal("Succeeded", backup.GetProperty("status").GetString());
        Assert.Equal("HashVerified", backup.GetProperty("verificationStatus").GetString());
        Assert.Equal(4, backup.GetProperty("includedAssetClasses").GetArrayLength());
        Assert.DoesNotContain("PRIVATE", backup.ToString());

        var history = await JsonOf(await admin.GetAsync("/api/backup/history"));
        Assert.Contains(history.EnumerateArray(), h => h.GetProperty("id").GetGuid() == id);

        Assert.Equal("HashVerified", (await JsonOf(await admin.SendAsync(Req(HttpMethod.Post, $"/api/backup/backups/{id}/verify-hash", csrf)))).GetProperty("verificationStatus").GetString());

        var material = new RecoveryMaterialRequest(AdminPassword, privateKey, passphrase);
        var preflight = await JsonOf(await admin.SendAsync(Req(HttpMethod.Post, $"/api/backup/backups/{id}/preflight", csrf, material)));
        Assert.True(preflight.GetProperty("canRestore").GetBoolean());

        var verified = await JsonOf(await admin.SendAsync(Req(HttpMethod.Post, $"/api/backup/backups/{id}/verify", csrf, material)));
        Assert.Equal("FullyVerified", verified.GetProperty("verificationStatus").GetString());

        var drillResponse = await admin.SendAsync(Req(HttpMethod.Post, $"/api/backup/backups/{id}/restore-drill", csrf, material));
        Assert.Equal(HttpStatusCode.Created, drillResponse.StatusCode);
        var drill = await JsonOf(drillResponse);
        Assert.Equal("Succeeded", drill.GetProperty("outcome").GetString());
        Assert.All(drill.GetProperty("checks").EnumerateArray().Where(c => c.GetProperty("blocking").GetBoolean()), c => Assert.True(c.GetProperty("passed").GetBoolean()));
        Assert.StartsWith("AlveraRestore_", drill.GetProperty("targetDatabase").GetString());

        var drills = await JsonOf(await admin.GetAsync("/api/backup/restore-drills"));
        var drillId = drill.GetProperty("id").GetGuid();
        Assert.Contains(drills.EnumerateArray(), d => d.GetProperty("id").GetGuid() == drillId);

        var removed = await JsonOf(await admin.SendAsync(Req(HttpMethod.Post, $"/api/backup/restore-drills/{drillId}/remove-target", csrf, new StepUpRequest(AdminPassword))));
        Assert.True(removed.GetProperty("targetRemoved").GetBoolean());

        var status = await JsonOf(await admin.GetAsync("/api/backup/status"));
        Assert.Equal(id, status.GetProperty("lastSuccess").GetProperty("id").GetGuid());
        Assert.True(status.GetProperty("lastSuccessCoversAllAssetClasses").GetBoolean());
        Assert.True(status.GetProperty("successfulVerificationCount").GetInt32() >= 1);
        Assert.False(status.GetProperty("latestAttemptFailed").GetBoolean());

        var audit = (await JsonOf(await admin.GetAsync("/api/auth/audit-log?take=500"))).EnumerateArray().ToList();
        foreach (var (eventType, entity) in new[]
        {
            (BackupAuditEvents.RecoveryKeyConfigured, "BackupSettings"), (BackupAuditEvents.Created, "BackupRecord"), (BackupAuditEvents.HashVerified, "BackupRecord"),
            (BackupAuditEvents.FullyVerified, "BackupRecord"), (BackupAuditEvents.RestoreDrillCompleted, "RestoreDrillRecord"), (BackupAuditEvents.RestoreTargetRemoved, "RestoreDrillRecord"),
        })
            Assert.Contains(audit, e => e.GetProperty("eventType").GetString() == eventType && e.GetProperty("entityType").GetString() == entity);
    }

    [Fact]
    public async Task A_drill_with_the_wrong_key_returns_a_safe_failure_and_never_echoes_the_key_material()
    {
        await using var factory = CreateFactory();
        var (admin, csrf) = await AdminAsync(factory);
        var (_, passphrase) = await SetUpRecoveryKeyAsync(admin, csrf);
        var id = (await JsonOf(await admin.SendAsync(Req(HttpMethod.Post, "/api/backup/backups", csrf)))).GetProperty("id").GetGuid();
        var foreign = BackupCrypto.GenerateRecoveryKey();

        var response = await admin.SendAsync(Req(HttpMethod.Post, $"/api/backup/backups/{id}/restore-drill", csrf, new RecoveryMaterialRequest(AdminPassword, foreign.EncryptedPrivateKeyPem, foreign.Passphrase)));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        Assert.Equal("wrong_recovery_key", JsonDocument.Parse(text).RootElement.GetProperty("failureCode").GetString());
        Assert.DoesNotContain("PRIVATE", text);
        Assert.DoesNotContain(foreign.Passphrase, text);
        Assert.DoesNotContain(passphrase, text);

        var notifications = await JsonOf(await admin.GetAsync("/api/backup/notifications"));
        Assert.Contains(notifications.EnumerateArray(), n => n.GetProperty("kind").GetString() == "RestoreDrillFailed");
    }

    [Fact]
    public async Task A_failed_backup_returns_only_a_stable_code_and_message_and_shows_as_the_latest_failure()
    {
        await using var factory = CreateFactory();
        var (admin, csrf) = await AdminAsync(factory);
        await SetUpRecoveryKeyAsync(admin, csrf);

        // Point the destination at a place that is a FILE, then make it unusable at run time.
        var destination = Path.Combine(_root, "dest");
        var settings = (await JsonOf(await admin.GetAsync("/api/backup/status"))).GetProperty("settings");
        var saved = await admin.SendAsync(Req(HttpMethod.Put, "/api/backup/settings", csrf,
            new SaveBackupSettingsRequest(false, 24, 7, destination, 2, 30, settings.GetProperty("rowVersion").GetString())));
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Directory.Delete(destination, true);
        await File.WriteAllTextAsync(destination, "now a file");

        var failed = await admin.SendAsync(Req(HttpMethod.Post, "/api/backup/backups", csrf));

        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        var text = await failed.Content.ReadAsStringAsync();
        Assert.Equal("destination_unavailable", JsonDocument.Parse(text).RootElement.GetProperty("error").GetString());
        Assert.DoesNotContain(_root, text);

        var status = await JsonOf(await admin.GetAsync("/api/backup/status"));
        Assert.True(status.GetProperty("latestAttemptFailed").GetBoolean());
        Assert.Equal("destination_unavailable", status.GetProperty("lastFailure").GetProperty("failureCode").GetString());
    }

    // ---------- settings + notifications ----------

    [Fact]
    public async Task Settings_are_validated_versioned_and_audited_over_the_api()
    {
        await using var factory = CreateFactory();
        var (admin, csrf) = await AdminAsync(factory);
        var version = (await JsonOf(await admin.GetAsync("/api/backup/status"))).GetProperty("settings").GetProperty("rowVersion").GetString();

        var invalid = await admin.SendAsync(Req(HttpMethod.Put, "/api/backup/settings", csrf, new SaveBackupSettingsRequest(false, 0, 7, null, 2, 30, version)));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal("invalid_interval", (await JsonOf(invalid)).GetProperty("error").GetString());

        var needsKey = await admin.SendAsync(Req(HttpMethod.Put, "/api/backup/settings", csrf, new SaveBackupSettingsRequest(true, 24, 7, null, 2, 30, version)));
        Assert.Equal("recovery_key_required", (await JsonOf(needsKey)).GetProperty("error").GetString());

        // First save creates the settings row; every later save must present the version it read.
        var first = await admin.SendAsync(Req(HttpMethod.Put, "/api/backup/settings", csrf, new SaveBackupSettingsRequest(false, 12, 5, null, 3, 14, version)));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var firstVersion = (await JsonOf(first)).GetProperty("rowVersion").GetString();

        var noVersion = await admin.SendAsync(Req(HttpMethod.Put, "/api/backup/settings", csrf, new SaveBackupSettingsRequest(false, 6, 5, null, 3, 14, null)));
        Assert.Equal("row_version_required", (await JsonOf(noVersion)).GetProperty("error").GetString());

        var second = await admin.SendAsync(Req(HttpMethod.Put, "/api/backup/settings", csrf, new SaveBackupSettingsRequest(false, 8, 5, null, 3, 14, firstVersion)));
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var stale = await admin.SendAsync(Req(HttpMethod.Put, "/api/backup/settings", csrf, new SaveBackupSettingsRequest(false, 6, 5, null, 3, 14, firstVersion)));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("concurrency_conflict", (await JsonOf(stale)).GetProperty("error").GetString());

        var audit = await JsonOf(await admin.GetAsync("/api/auth/audit-log?take=500"));
        Assert.Contains(audit.EnumerateArray(), e => e.GetProperty("eventType").GetString() == BackupAuditEvents.SettingsChanged);
    }

    [Fact]
    public async Task The_test_notification_endpoint_makes_notification_delivery_testable_and_visible()
    {
        await using var factory = CreateFactory();
        var (admin, csrf) = await AdminAsync(factory);

        var sent = await JsonOf(await admin.SendAsync(Req(HttpMethod.Post, "/api/backup/notifications/test", csrf)));
        Assert.Equal(("TestNotification", "Delivered"), (sent.GetProperty("kind").GetString(), sent.GetProperty("delivery").GetString()));
        var drop = Path.Combine(_root, "backup", "notifications");
        Assert.Single(Directory.EnumerateFiles(drop));
        var notifications = await JsonOf(await admin.GetAsync("/api/backup/notifications"));
        Assert.Contains(notifications.EnumerateArray(), n => n.GetProperty("kind").GetString() == "TestNotification");
    }
}
