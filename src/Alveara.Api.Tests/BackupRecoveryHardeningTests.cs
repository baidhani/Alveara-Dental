using Microsoft.AspNetCore.Hosting;
using System.Net.Http.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Alveara.Api.Architecture.Backup;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Controllers;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N004 R02 (review findings R01-01, R01-03, R01-04): recovery without any backup history (retained archive
/// on a fresh installation), Data Protection compatibility across the upgrade and onto a different server
/// identity, and the practice time zone / application name as enforced deployment invariants.
/// </summary>
public class BackupRecoveryHardeningTests : IClassFixture<TestDatabaseFixture>, IDisposable
{
    private readonly BackupTestEnvironment _env;
    private readonly Guid _actor = Guid.NewGuid();
    private readonly List<string> _restoredDatabases = [];

    public BackupRecoveryHardeningTests(TestDatabaseFixture fixture)
    {
        _env = new BackupTestEnvironment(fixture);
        _env.ResetBackupStateAsync().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        foreach (var name in _restoredDatabases) _env.RestoreProvider.DropDatabaseAsync(name, default).GetAwaiter().GetResult();
        _env.Dispose();
    }

    private async Task<(Guid AccountId, string Username, string Password, string ProtectedSecret)> SeedLoginAccountAsync()
    {
        var (accountId, _, secret) = await _env.SeedAccountsAndAuditAsync();
        const string password = "recovered-app-password-1";
        await using var db = _env.NewDb();
        var accounts = new AccountService(db, _env.DataProtection);
        var loginUser = $"drill-login-{Guid.NewGuid():N}";
        var created = await accounts.RegisterAsync(loginUser, password);
        await accounts.ChangeRoleAsync(created.Id, Role.Admin, accountId);
        await accounts.SetAccountEnabledAsync(created.Id, true, accountId);
        return (accountId, loginUser, password, secret);
    }

    private async Task<BackupRecord> BackupAsync()
    {
        await using var db = _env.NewDb();
        return await _env.NewBackupService(db).RunBackupAsync(BackupKind.Manual, $"manual:{Guid.NewGuid():N}", _actor, default);
    }

    private async Task<RestoreDrillRecord> ArchiveDrillAsync(string archiveRef, string? privateKey = null, string? passphrase = null)
    {
        await using var db = _env.NewDb();
        var drill = await _env.NewRestoreService(db).RestoreDrillFromArchiveAsync(archiveRef, privateKey ?? _env.Key.EncryptedPrivateKeyPem, passphrase ?? BackupTestEnvironment.Passphrase, _actor, default);
        if (drill.TargetDatabase is not null) _restoredDatabases.Add(drill.TargetDatabase);
        return drill;
    }

    private static IReadOnlyList<ValidationCheck> ChecksOf(RestoreDrillRecord drill) =>
        System.Text.Json.JsonSerializer.Deserialize<List<ValidationCheck>>(drill.ValidationJson!, BackupManifest.Json)!;

    /// <summary>Copies the retained archive to the import folder (what an operator does on a replacement server) and erases ALL backup history, as on a fresh installation.</summary>
    private async Task<string> ImportArchiveAndLoseHistoryAsync(BackupRecord record)
    {
        Directory.CreateDirectory(_env.ImportRoot);
        File.Copy(BackupService.ResolveBackupPath(record), Path.Combine(_env.ImportRoot, record.FileName!));
        await using var db = _env.NewDb();
        await db.Database.ExecuteSqlRawAsync("DELETE FROM BackupNotifications; DELETE FROM RestoreDrills; DELETE FROM BackupRecords; DELETE FROM BackupSettings;");
        foreach (var f in Directory.EnumerateFiles(_env.SetsDirectory)) File.Delete(f); // the original destination is gone too: only the imported copy remains
        return $"import:{record.FileName}";
    }

    // ---------- R01-01: recovery with NO backup history ----------

    [Fact]
    public async Task A_retained_archive_is_discovered_preflighted_and_restored_with_no_history_and_the_recovered_app_logs_in_with_MFA_data_intact()
    {
        await _env.ConfigureRecoveryKeyAsync();
        var (accountId, username, password, secret) = await SeedLoginAccountAsync();
        var blobs = await _env.SeedBlobsAsync(2, 300_000);
        var record = await BackupAsync();
        var archiveRef = await ImportArchiveAndLoseHistoryAsync(record);

        await using (var db = _env.NewDb())
        {
            Assert.Equal(0, await db.BackupRecords.CountAsync()); // nothing in the database to recover FROM
            var service = _env.NewRestoreService(db);

            var archives = await service.ListArchivesAsync(default);
            var listed = Assert.Single(archives);
            Assert.Equal(archiveRef, listed.Ref);
            Assert.True(listed.Readable);
            Assert.False(listed.InHistory);
            Assert.Equal(_env.Key.Fingerprint[^16..], listed.RecipientKeyId, ignoreCase: true);

            var preflight = await service.PreflightArchiveAsync(archiveRef, _env.Key.EncryptedPrivateKeyPem, BackupTestEnvironment.Passphrase, default);
            Assert.True(preflight.CanRestore, string.Join("; ", preflight.Checks.Where(c => !c.Passed).Select(c => c.Detail)));

            var wrong = await service.PreflightArchiveAsync(archiveRef, _env.Key.EncryptedPrivateKeyPem, "an entirely different passphrase", default);
            Assert.False(wrong.CanRestore);
        }

        var drill = await ArchiveDrillAsync(archiveRef);

        Assert.Equal("Succeeded", drill.Outcome);
        Assert.Null(drill.BackupRecordId);
        Assert.Equal("Archive", drill.SourceKind);
        Assert.Equal(record.FileName, drill.ArchiveFileName);
        Assert.Equal(64, drill.ArchiveSha256!.Length);
        Assert.All(ChecksOf(drill), c => Assert.True(c.Passed || !c.Blocking, c.Name + ": " + c.Detail));

        // Start the recovered application on the recovered state and prove sign-in + restored MFA secret + documents.
        var extracted = Path.Combine(drill.TargetDirectory!, "extracted");
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Alveara", _env.RestoreProvider.ConnectionStringFor(drill.TargetDatabase!));
            builder.UseSetting("StorageRoot", Path.Combine(extracted, ManagedAssetClasses.Documents));
            builder.UseSetting("DataProtectionKeysPath", Path.Combine(extracted, ManagedAssetClasses.DataProtectionKeys));
            builder.UseSetting("Backup:Root", Path.Combine(_env.Root, "recovered-app-backup"));
            builder.UseSetting("DataProtection:ApplicationName", BackupTestEnvironment.DataProtectionApplicationName);
            builder.UseSetting("AdminBootstrapSecret", IdentityTestHelpers.TestBootstrapSecret);
        });
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(username, password));
        Assert.True(login.IsSuccessStatusCode, await login.Content.ReadAsStringAsync());

        using var scope = factory.Services.CreateScope();
        var protector = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>().CreateProtector(AccountService.MfaSecretProtectorPurpose);
        Assert.Equal(Convert.ToBase64String(new byte[20]), protector.Unprotect(secret));
        Assert.NotNull(await scope.ServiceProvider.GetRequiredService<Alveara.Api.Data.AlveraDbContext>().UserAccounts.SingleAsync(u => u.Id == accountId));
        var storage = scope.ServiceProvider.GetRequiredService<Alveara.Api.Architecture.Storage.IBlobStorage>();
        foreach (var blob in blobs) Assert.True(await storage.VerifyIntegrityAsync(blob.Id, blob.Sha256Hash));
    }

    [Theory]
    [InlineData("import:../escape.abk")]
    [InlineData("import:sub/dir.abk")]
    [InlineData("bogus:file.abk")]
    [InlineData("import:notabackup.txt")]
    [InlineData("no-colon.abk")]
    [InlineData("")]
    public async Task An_archive_can_only_be_chosen_from_the_two_archive_folders_never_by_arbitrary_path(string archiveRef)
    {
        await _env.ConfigureRecoveryKeyAsync();
        await using var db = _env.NewDb();
        var ex = await Assert.ThrowsAsync<BackupException>(() => _env.NewRestoreService(db).PreflightArchiveAsync(archiveRef, _env.Key.EncryptedPrivateKeyPem, BackupTestEnvironment.Passphrase, default));
        Assert.Equal("archive_unknown", ex.Code);
    }

    [Fact]
    public async Task A_file_that_is_not_a_backup_is_listed_as_unreadable_and_a_tampered_archive_fails_the_drill_without_leaving_a_target()
    {
        await _env.ConfigureRecoveryKeyAsync();
        await SeedLoginAccountAsync();
        var record = await BackupAsync();
        var archiveRef = await ImportArchiveAndLoseHistoryAsync(record);
        File.WriteAllText(Path.Combine(_env.ImportRoot, "junk.abk"), "this is not a backup");
        var path = Path.Combine(_env.ImportRoot, record.FileName!);
        var bytes = File.ReadAllBytes(path);
        bytes[bytes.Length / 2] ^= 0xFF; // flip one byte deep inside the encrypted payload
        File.WriteAllBytes(path, bytes);

        await using (var db = _env.NewDb())
        {
            var archives = await _env.NewRestoreService(db).ListArchivesAsync(default);
            Assert.False(archives.Single(a => a.FileName == "junk.abk").Readable);
        }

        var drill = await ArchiveDrillAsync(archiveRef);

        Assert.Equal("Failed", drill.Outcome);
        Assert.Equal("corrupt_or_tampered", drill.FailureCode);
        Assert.False(await _env.RestoreProvider.DatabaseExistsAsync(drill.TargetDatabase!, default));
    }

    // ---------- R01-04: deployment settings are part of the backup and are applied / verified on recovery ----------

    [Fact]
    public async Task A_backup_made_under_a_non_default_time_zone_records_it_and_recovery_is_blocked_until_the_server_is_configured_with_it()
    {
        await _env.ConfigureRecoveryKeyAsync();
        await SeedLoginAccountAsync();
        _env.Deployment.Current = _env.Deployment.Current with { PracticeTimeZoneId = "Asia/Tokyo" };
        var record = await BackupAsync();
        Assert.Contains(ManagedAssetClasses.DeploymentConfiguration, record.IncludedAssetClasses!.Split(','));
        var archiveRef = await ImportArchiveAndLoseHistoryAsync(record);

        // A replacement server still on the default zone: refused, with the exact setting to apply.
        _env.Deployment.Current = _env.Deployment.Current with { PracticeTimeZoneId = BackupTestEnvironment.DefaultTimeZoneId };
        var blocked = await ArchiveDrillAsync(archiveRef);
        Assert.Equal("Failed", blocked.Outcome);
        Assert.Equal("deployment_settings_match_this_server", blocked.FailureCode);
        Assert.Contains("PracticeTimeZone = Asia/Tokyo", blocked.FailureMessage);
        Assert.False(await _env.RestoreProvider.DatabaseExistsAsync(blocked.TargetDatabase!, default));

        // Applying the recorded setting makes the same archive recoverable.
        _env.Deployment.Current = _env.Deployment.Current with { PracticeTimeZoneId = "Asia/Tokyo" };
        var recovered = await ArchiveDrillAsync(archiveRef);
        Assert.Equal("Succeeded", recovered.Outcome);
        Assert.Contains(ChecksOf(recovered), c => c.Name == "deployment_settings_match_this_server" && c.Passed);
    }

    [Fact]
    public async Task A_recovered_application_resolves_practice_local_times_identically_only_with_the_recorded_time_zone_and_refuses_data_requests_otherwise()
    {
        const string tokyo = "Asia/Tokyo";
        await _env.ConfigureRecoveryKeyAsync();
        _env.Deployment.Current = _env.Deployment.Current with { PracticeTimeZoneId = tokyo };
        // The original server ran under Tokyo: its database carries that as the recorded invariant.
        await using (var db = _env.NewDb())
            await db.Database.ExecuteSqlRawAsync(
                "DELETE FROM DeploymentInvariants; INSERT INTO DeploymentInvariants (Id, PracticeTimeZoneId, DataProtectionApplicationName, RecordedAtUtc, UpdatedAtUtc) VALUES ({0}, {1}, {2}, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET())",
                DeploymentInvariantRecord.SingletonId, tokyo, BackupTestEnvironment.DataProtectionApplicationName);
        var (_, username, password, _) = await SeedLoginAccountAsync();
        var record = await BackupAsync();
        var archiveRef = await ImportArchiveAndLoseHistoryAsync(record);
        var drill = await ArchiveDrillAsync(archiveRef);
        Assert.Equal("Succeeded", drill.Outcome);
        var extracted = Path.Combine(drill.TargetDirectory!, "extracted");

        WebApplicationFactory<Program> Recovered(string? practiceTimeZone) => new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Alveara", _env.RestoreProvider.ConnectionStringFor(drill.TargetDatabase!));
            builder.UseSetting("StorageRoot", Path.Combine(extracted, ManagedAssetClasses.Documents));
            builder.UseSetting("DataProtectionKeysPath", Path.Combine(extracted, ManagedAssetClasses.DataProtectionKeys));
            builder.UseSetting("Backup:Root", Path.Combine(_env.Root, $"recovered-{Guid.NewGuid():N}"));
            builder.UseSetting("DataProtection:ApplicationName", BackupTestEnvironment.DataProtectionApplicationName);
            builder.UseSetting("AdminBootstrapSecret", IdentityTestHelpers.TestBootstrapSecret);
            if (practiceTimeZone is not null) builder.UseSetting("PracticeTimeZone", practiceTimeZone);
        });

        async Task<System.Text.Json.JsonElement> DeploymentStateAsync(HttpClient client, string expected)
        {
            for (var i = 0; i < 60; i++)
            {
                var body = await client.GetFromJsonAsync<System.Text.Json.JsonElement>("/api/systemstatus");
                if (body.GetProperty("deployment").GetProperty("state").GetString() == expected) return body.GetProperty("deployment");
                await Task.Delay(500);
            }
            throw new Xunit.Sdk.XunitException($"deployment state never became {expected}");
        }

        // Recovered under the DEFAULT zone: the mismatch is detected, reported, and the data is not served under the wrong zone.
        await using (var wrong = Recovered(null))
        {
            var client = wrong.CreateClient();
            Assert.True((await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(username, password))).IsSuccessStatusCode);
            var deployment = await DeploymentStateAsync(client, "mismatch");
            Assert.Equal("PracticeTimeZone", deployment.GetProperty("mismatches")[0].GetProperty("setting").GetString());
            Assert.Equal(tokyo, deployment.GetProperty("mismatches")[0].GetProperty("recorded").GetString());
            var refused = await client.GetAsync("/api/config/practice");
            Assert.Equal(System.Net.HttpStatusCode.ServiceUnavailable, refused.StatusCode);
            Assert.Contains("deployment_mismatch", await refused.Content.ReadAsStringAsync());
        }

        // Recovered with the recorded zone applied: same practice-local -> UTC result as the original, and data is served.
        await using (var right = Recovered(tokyo))
        {
            var client = right.CreateClient();
            Assert.True((await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(username, password))).IsSuccessStatusCode);
            await DeploymentStateAsync(client, "ok");
            var clock = right.Services.GetRequiredService<Alveara.Api.Architecture.Time.IPracticeClock>();
            Assert.Equal(tokyo, clock.PracticeTimeZone.Id);
            Assert.Equal(new DateTimeOffset(2026, 10, 5, 0, 0, 0, TimeSpan.Zero), clock.FromPracticeLocal(new DateTime(2026, 10, 5, 9, 0, 0))); // 09:00 Tokyo = 00:00Z
            Assert.NotEqual(System.Net.HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/api/config/practice")).StatusCode);
        }
    }

    [Fact]
    public async Task A_backup_with_no_recorded_deployment_settings_is_not_recoverable()
    {
        await _env.ConfigureRecoveryKeyAsync();
        await SeedLoginAccountAsync();
        var record = await BackupAsync();
        var crafted = await _env.CraftAlteredBackupAsync(record.Id, content =>
        {
            var manifest = BackupTestEnvironment.ReadManifest(content);
            BackupTestEnvironment.WriteManifest(content, manifest with { Deployment = null });
        });

        await using var db = _env.NewDb();
        var drill = await _env.NewRestoreService(db).RestoreDrillAsync(crafted, _env.Key.EncryptedPrivateKeyPem, BackupTestEnvironment.Passphrase, _actor, default);
        if (drill.TargetDatabase is not null) _restoredDatabases.Add(drill.TargetDatabase);

        Assert.Equal(("Failed", "deployment_settings_recorded"), (drill.Outcome, drill.FailureCode));
    }

    [Fact]
    public async Task The_coverage_check_reports_a_backup_without_deployment_configuration_as_incomplete()
    {
        await _env.ConfigureRecoveryKeyAsync();
        var record = await BackupAsync();
        await using var db = _env.NewDb();
        await db.BackupRecords.Where(r => r.Id == record.Id).ExecuteUpdateAsync(s => s.SetProperty(r => r.IncludedAssetClasses, "database,documents,dataProtectionKeys"));

        var status = await _env.NewBackupService(db).GetStatusAsync(default);

        Assert.False(status.LastSuccessCoversAllAssetClasses);
        Assert.Equal([ManagedAssetClasses.DeploymentConfiguration], status.MissingAssetClasses);
    }

    [Fact]
    public async Task The_monitor_records_the_settings_on_first_start_flags_a_later_mismatch_and_the_guard_refuses_domain_calls_until_fixed()
    {
        await using (var db = _env.NewDb()) await db.Database.ExecuteSqlRawAsync("DELETE FROM DeploymentInvariants;");
        var status = new DeploymentInvariantStatus();
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddDbContext<Alveara.Api.Data.AlveraDbContext>(o => o.UseSqlServer(_env.Fixture.ConnectionString));
        await using var provider = services.BuildServiceProvider();
        var monitor = new DeploymentInvariantMonitor(provider.GetRequiredService<IServiceScopeFactory>(), _env.Deployment, status, configuration, NullLogger<DeploymentInvariantMonitor>.Instance);

        await monitor.CheckOnceAsync(default);
        Assert.Equal(DeploymentInvariantState.Ok, status.Snapshot.State);

        _env.Deployment.Current = _env.Deployment.Current with { PracticeTimeZoneId = "Asia/Tokyo" };
        await monitor.CheckOnceAsync(default);
        Assert.Equal(DeploymentInvariantState.Mismatch, status.Snapshot.State);
        var mismatch = Assert.Single(status.Snapshot.Mismatches);
        Assert.Equal(("PracticeTimeZone", BackupTestEnvironment.DefaultTimeZoneId, "Asia/Tokyo"), (mismatch.Setting, mismatch.Recorded, mismatch.Current));

        var middleware = new DeploymentGuardMiddleware(_ => Task.CompletedTask, status);
        async Task<int> StatusFor(string path)
        {
            var context = new DefaultHttpContext { Response = { Body = new MemoryStream() } };
            context.Request.Path = path;
            await middleware.InvokeAsync(context);
            return context.Response.StatusCode;
        }
        Assert.Equal(503, await StatusFor("/api/appointments"));
        Assert.Equal(503, await StatusFor("/api/patients/search"));
        Assert.Equal(200, await StatusFor("/api/health"));
        Assert.Equal(200, await StatusFor("/api/systemstatus"));
        Assert.Equal(200, await StatusFor("/api/backup/status"));
        Assert.Equal(200, await StatusFor("/api/auth/login"));

        // Putting the recorded value back clears it; an INTENTIONAL change needs the explicit adopt setting.
        _env.Deployment.Current = _env.Deployment.Current with { PracticeTimeZoneId = BackupTestEnvironment.DefaultTimeZoneId };
        await monitor.CheckOnceAsync(default);
        Assert.Equal(DeploymentInvariantState.Ok, status.Snapshot.State);

        _env.Deployment.Current = _env.Deployment.Current with { PracticeTimeZoneId = "Asia/Tokyo" };
        var adopting = new DeploymentInvariantMonitor(provider.GetRequiredService<IServiceScopeFactory>(), _env.Deployment, status,
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Deployment:AdoptCurrentSettings"] = "true" }).Build(), NullLogger<DeploymentInvariantMonitor>.Instance);
        await adopting.CheckOnceAsync(default);
        Assert.Equal(DeploymentInvariantState.Ok, status.Snapshot.State);
        await monitor.CheckOnceAsync(default);
        Assert.Equal(DeploymentInvariantState.Ok, status.Snapshot.State); // the adopted value is now the recorded one
    }

    // ---------- R01-03: Data Protection compatibility across the upgrade and onto a different server identity ----------

    [Fact]
    public async Task An_existing_installation_keeps_decrypting_its_enrolled_MFA_secrets_after_upgrade_and_recovery_onto_a_different_folder_needs_exactly_the_recorded_name()
    {
        var keys = Path.Combine(_env.Root, "legacy-keys");
        Directory.CreateDirectory(keys);

        // "Before the upgrade": an installation that never configured an application name (framework default = content-root path).
        string protectedSecret;
        string legacyDiscriminator;
        await using (var legacy = new WebApplicationFactory<Program>().WithWebHostBuilder(b => ConfigureApp(b, keys, applicationName: null)))
        {
            legacy.CreateClient();
            legacyDiscriminator = legacy.Services.GetRequiredService<IApplicationDiscriminator>().Discriminator!;
            protectedSecret = legacy.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector(AccountService.MfaSecretProtectorPurpose).Protect("pre-implementation-secret");
            // The upgrade did not change the effective discriminator: it is what a backup will record.
            Assert.Equal(legacyDiscriminator, legacy.Services.GetRequiredService<IDeploymentSettingsProvider>().Current.DataProtectionApplicationName);
        }

        // "After the upgrade": same install location and keys - the enrolled secret still decrypts.
        await using (var upgraded = new WebApplicationFactory<Program>().WithWebHostBuilder(b => ConfigureApp(b, keys, applicationName: null)))
        {
            upgraded.CreateClient();
            var protector = upgraded.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector(AccountService.MfaSecretProtectorPurpose);
            Assert.Equal("pre-implementation-secret", protector.Unprotect(protectedSecret));
        }

        // Recovery onto a DIFFERENT folder / server identity: without the recorded name the secret is unreadable...
        var otherRoot = Path.Combine(_env.Root, "replacement-server");
        Directory.CreateDirectory(otherRoot);
        await using (var replacement = new WebApplicationFactory<Program>().WithWebHostBuilder(b => { b.UseContentRoot(otherRoot); ConfigureApp(b, keys, applicationName: null); }))
        {
            replacement.CreateClient();
            Assert.NotEqual(legacyDiscriminator, replacement.Services.GetRequiredService<IDeploymentSettingsProvider>().Current.DataProtectionApplicationName);
            var protector = replacement.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector(AccountService.MfaSecretProtectorPurpose);
            Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(() => protector.Unprotect(protectedSecret));
        }

        // ...and with the recorded name applied (what the recovery check tells the operator to set) it decrypts.
        await using (var configured = new WebApplicationFactory<Program>().WithWebHostBuilder(b => { b.UseContentRoot(otherRoot); ConfigureApp(b, keys, applicationName: legacyDiscriminator); }))
        {
            configured.CreateClient();
            var protector = configured.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector(AccountService.MfaSecretProtectorPurpose);
            Assert.Equal("pre-implementation-secret", protector.Unprotect(protectedSecret));
            Assert.Equal(legacyDiscriminator, configured.Services.GetRequiredService<IDeploymentSettingsProvider>().Current.DataProtectionApplicationName);
        }
    }

    private void ConfigureApp(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder, string keys, string? applicationName)
    {
        builder.UseSetting("ConnectionStrings:Alveara", _env.Fixture.ConnectionString);
        builder.UseSetting("StorageRoot", Path.Combine(_env.Root, "legacy-blobs"));
        builder.UseSetting("DataProtectionKeysPath", keys);
        builder.UseSetting("Backup:Root", Path.Combine(_env.Root, "legacy-backup"));
        builder.UseSetting("AdminBootstrapSecret", IdentityTestHelpers.TestBootstrapSecret);
        builder.UseSetting("Deployment:AdoptCurrentSettings", "true"); // these apps share the fixture database; this test is about Data Protection, not the invariant monitor
        if (applicationName is not null) builder.UseSetting("DataProtection:ApplicationName", applicationName);
    }
}
