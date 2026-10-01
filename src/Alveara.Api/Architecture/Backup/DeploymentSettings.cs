using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Backup;

/// <summary>
/// The NON-SECRET deployment settings without which restored data cannot be interpreted correctly.
/// This is a deliberate ALLOWLIST - never the raw configuration file or environment, which carry
/// credentials and machine-specific paths:
/// <list type="bullet">
/// <item><c>PracticeTimeZone</c> - practice-local appointment/availability times are stored as wall-clock or
///   converted through this zone (ALV-N002/N003 keep it out of the database on purpose), so recovering the data under
///   a different zone silently shifts every schedule.</item>
/// <item><c>DataProtection:ApplicationName</c> - the Data Protection application discriminator; stored MFA secrets only
///   decrypt under the discriminator they were protected with. When no name is configured the framework defaults to the
///   install's content-root path, which a replacement server will not share.</item>
/// </list>
/// Machine-specific paths (database connection, storage, key folder, backup folders) are deliberately NOT captured:
/// they are rebound by the operator on the replacement server (see the runbook).
/// </summary>
public sealed record DeploymentSettings(int FormatVersion, string PracticeTimeZoneId, string DataProtectionApplicationName, string Currency)
{
    public const int CurrentFormatVersion = 1;
    public const string FileName = "deployment-settings.json";
    public const string PracticeTimeZoneSetting = "PracticeTimeZone";
    public const string DataProtectionApplicationNameSetting = "DataProtection:ApplicationName";

    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>Human-readable "set X = Y" lines an operator applies on a replacement server.</summary>
    public IReadOnlyList<(string Setting, string Value)> RequiredSettings() =>
    [
        (PracticeTimeZoneSetting, PracticeTimeZoneId),
        (DataProtectionApplicationNameSetting, DataProtectionApplicationName),
    ];

    /// <summary>The settings that differ between this and <paramref name="other"/> (empty when compatible).</summary>
    public IReadOnlyList<DeploymentMismatch> DifferencesFrom(DeploymentSettings other)
    {
        var differences = new List<DeploymentMismatch>();
        if (!string.Equals(PracticeTimeZoneId, other.PracticeTimeZoneId, StringComparison.Ordinal))
            differences.Add(new DeploymentMismatch(PracticeTimeZoneSetting, PracticeTimeZoneId, other.PracticeTimeZoneId));
        if (!string.Equals(DataProtectionApplicationName, other.DataProtectionApplicationName, StringComparison.Ordinal))
            differences.Add(new DeploymentMismatch(DataProtectionApplicationNameSetting, DataProtectionApplicationName, other.DataProtectionApplicationName));
        return differences;
    }
}

/// <summary>One setting whose recorded value (what the data was created under) differs from the value this server is configured with.</summary>
public sealed record DeploymentMismatch(string Setting, string Recorded, string Current);

/// <summary>Reads the EFFECTIVE settings of the running application (not just what was configured).</summary>
public interface IDeploymentSettingsProvider
{
    DeploymentSettings Current { get; }
}

public sealed class RuntimeDeploymentSettingsProvider(IPracticeClock clock, IOptions<DataProtectionOptions> dataProtection, IServiceProvider services) : IDeploymentSettingsProvider
{
    public DeploymentSettings Current => new(
        DeploymentSettings.CurrentFormatVersion,
        clock.PracticeTimeZone.Id,
        // An explicitly configured name wins; otherwise the framework's own default discriminator (the content-root path) - the value existing protected payloads were made under.
        dataProtection.Value.ApplicationDiscriminator ?? services.GetService<IApplicationDiscriminator>()?.Discriminator ?? "",
        Money.Money.Currency);
}

/// <summary>Captures the deployment settings as an asset class, so a backup that omits them is visibly incomplete.</summary>
public sealed class DeploymentConfigurationSource(IDeploymentSettingsProvider provider) : IBackupAssetSource
{
    public string AssetClass => ManagedAssetClasses.DeploymentConfiguration;

    public async Task<IReadOnlyList<StagedFile>> StageAsync(string stagingDirectory, CancellationToken cancellationToken)
    {
        var directory = Path.Combine(stagingDirectory, AssetClass);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, DeploymentSettings.FileName);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(provider.Current, DeploymentSettings.Json), cancellationToken);
        return [new StagedFile(AssetClass, $"{AssetClass}/{DeploymentSettings.FileName}", await FileTreeAssetSource.HashFileAsync(path, cancellationToken), new FileInfo(path).Length)];
    }
}

/// <summary>The database's own record of the deployment settings its data was created under (singleton row).</summary>
public class DeploymentInvariantRecord
{
    public static readonly Guid SingletonId = new("00000000-0000-0000-0000-00000000d3a1");

    public Guid Id { get; set; } = SingletonId;
    public required string PracticeTimeZoneId { get; set; }
    public required string DataProtectionApplicationName { get; set; }
    public DateTimeOffset RecordedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public enum DeploymentInvariantState
{
    /// <summary>Not yet verified (starting up, or the database was unreachable).</summary>
    Unchecked,
    Ok,
    Mismatch,
}

/// <summary>The most recent result of comparing the running configuration with what the data was created under.</summary>
public sealed class DeploymentInvariantStatus
{
    private volatile DeploymentInvariantSnapshot _snapshot = new(DeploymentInvariantState.Unchecked, []);
    public DeploymentInvariantSnapshot Snapshot => _snapshot;
    public void Set(DeploymentInvariantState state, IReadOnlyList<DeploymentMismatch> mismatches) => _snapshot = new(state, mismatches);

    /// <summary>
    /// Runs one verification now. Set by <see cref="DeploymentInvariantMonitor"/>; lets a request that arrives before the
    /// first background check finishes trigger it (and wait, bounded) instead of being served unverified.
    /// </summary>
    public Func<CancellationToken, Task>? Verifier { get; set; }
}

public sealed record DeploymentInvariantSnapshot(DeploymentInvariantState State, IReadOnlyList<DeploymentMismatch> Mismatches);

/// <summary>Reads, or on first use records, the deployment settings the database's data is created under (shared by the monitor and by backup).</summary>
public static class DeploymentInvariantStore
{
    public static async Task<DeploymentInvariantRecord> EnsureRecordedAsync(AlveraDbContext db, DeploymentSettings current, CancellationToken cancellationToken)
    {
        var record = await db.DeploymentInvariants.SingleOrDefaultAsync(cancellationToken);
        if (record is not null) return record;

        var added = new DeploymentInvariantRecord
        {
            PracticeTimeZoneId = current.PracticeTimeZoneId, DataProtectionApplicationName = current.DataProtectionApplicationName,
            RecordedAtUtc = DateTimeOffset.UtcNow, UpdatedAtUtc = DateTimeOffset.UtcNow,
        };
        db.DeploymentInvariants.Add(added);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return added;
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear(); // another instance recorded first: use theirs
            return await db.DeploymentInvariants.SingleAsync(cancellationToken);
        }
    }

    public static DeploymentSettings AsSettings(DeploymentInvariantRecord record, string currency) =>
        new(DeploymentSettings.CurrentFormatVersion, record.PracticeTimeZoneId, record.DataProtectionApplicationName, currency);
}

/// <summary>
/// ALV-N004 R02 (review finding ALV-N004-R01-04): makes the deployment invariants enforceable on a RECOVERED
/// application. The first start records the effective settings in the database; every later start (and a
/// periodic re-check) compares them. A database restored onto a server configured with a different practice
/// time zone - or a different Data Protection application name - is therefore recognised, reported on the
/// status endpoint, and every domain API call is refused (see <see cref="DeploymentGuardMiddleware"/>) until the
/// operator applies the settings the backup recorded. An INTENTIONAL change is made explicit with the one-shot
/// setting <c>Deployment:AdoptCurrentSettings=true</c>, which re-records the current values and is logged.
/// </summary>
public sealed class DeploymentInvariantMonitor(
    IServiceScopeFactory scopeFactory, IDeploymentSettingsProvider settings, DeploymentInvariantStatus status,
    IConfiguration configuration, ILogger<DeploymentInvariantMonitor> logger) : BackgroundService
{
    private static readonly TimeSpan RecheckInterval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(5);
    private readonly SemaphoreSlim _checkLock = new(1, 1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        status.Verifier = CheckOnceAsync;
        while (!stoppingToken.IsCancellationRequested)
        {
            var checkedOk = false;
            try
            {
                await CheckOnceAsync(stoppingToken);
                checkedOk = true;
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                // The database may be unreachable at startup (or not yet migrated): stay Unchecked and retry; never crash the host.
                logger.LogWarning("Deployment invariant check could not run ({ExceptionType}); will retry.", ex.GetType().Name);
            }

            try { await Task.Delay(checkedOk ? RecheckInterval : RetryInterval, stoppingToken); }
            catch (TaskCanceledException) { /* shutdown */ }
        }
    }

    public async Task CheckOnceAsync(CancellationToken cancellationToken)
    {
        await _checkLock.WaitAsync(cancellationToken); // the background loop and an early request never check at once
        try
        {
            await CheckLockedAsync(cancellationToken);
        }
        finally
        {
            _checkLock.Release();
        }
    }

    private async Task CheckLockedAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AlveraDbContext>();
        var current = settings.Current;

        var record = await DeploymentInvariantStore.EnsureRecordedAsync(db, current, cancellationToken);

        var recorded = DeploymentInvariantStore.AsSettings(record, current.Currency);
        var differences = recorded.DifferencesFrom(current).ToList(); // "this" = what the data was created under, "other" = what this server is configured with

        if (differences.Count > 0 && configuration.GetValue<bool>("Deployment:AdoptCurrentSettings"))
        {
            logger.LogWarning("Deployment:AdoptCurrentSettings is set: re-recording the deployment settings ({Settings}). Remove the setting after this start.", string.Join(", ", differences.Select(d => d.Setting)));
            record.PracticeTimeZoneId = current.PracticeTimeZoneId;
            record.DataProtectionApplicationName = current.DataProtectionApplicationName;
            record.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            differences.Clear();
        }

        if (differences.Count == 0)
        {
            status.Set(DeploymentInvariantState.Ok, []);
        }
        else
        {
            logger.LogCritical("DEPLOYMENT MISMATCH: this server is configured differently from the settings its data was created under ({Settings}). Domain API calls are refused until the recorded settings are applied.",
                string.Join(", ", differences.Select(d => d.Setting)));
            status.Set(DeploymentInvariantState.Mismatch, differences);
        }
    }
}

/// <summary>
/// Domain API calls are served ONLY once the deployment invariants have been positively verified as matching the data.
/// While they are unverified (startup, database not yet reachable, check failing) the answer is 503 `deployment_unverified`;
/// while they mismatch it is 503 `deployment_mismatch` - in both cases except the endpoints an operator needs to see and
/// fix the problem (health, system status, sign-in, and the backup/recovery tooling). A request that arrives before the
/// first background check finished triggers that check itself and waits for it (bounded), so a healthy server is not
/// needlessly refused. Better an explicit refusal than appointments silently reinterpreted in the wrong time zone or
/// accounts that cannot complete MFA.
/// </summary>
public sealed class DeploymentGuardMiddleware(RequestDelegate next, DeploymentInvariantStatus status, ILogger<DeploymentGuardMiddleware>? logger = null)
{
    private static readonly string[] Allowed = ["/api/health", "/api/systemstatus", "/api/auth", "/api/backup"];
    private static readonly TimeSpan EarlyVerificationTimeout = TimeSpan.FromSeconds(5);

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";
        var guarded = path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) && !Allowed.Any(a => path.StartsWith(a, StringComparison.OrdinalIgnoreCase));
        if (!guarded)
        {
            await next(context);
            return;
        }

        var snapshot = status.Snapshot;
        if (snapshot.State == DeploymentInvariantState.Unchecked && status.Verifier is { } verify)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
                timeout.CancelAfter(EarlyVerificationTimeout);
                await verify(timeout.Token);
            }
            catch (Exception ex) when (ex is OperationCanceledException or Microsoft.Data.SqlClient.SqlException or DbUpdateException or InvalidOperationException)
            {
                logger?.LogWarning("Early deployment verification did not complete ({ExceptionType}); refusing the request as unverified.", ex.GetType().Name);
            }
            snapshot = status.Snapshot;
        }

        switch (snapshot.State)
        {
            case DeploymentInvariantState.Ok:
                await next(context);
                return;
            case DeploymentInvariantState.Mismatch:
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "deployment_mismatch",
                    message = "This server is configured differently from the settings its data was created under. Apply the recorded settings (see System Status) and restart.",
                    mismatches = snapshot.Mismatches,
                });
                return;
            default:
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                context.Response.Headers.RetryAfter = "5";
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "deployment_unverified",
                    message = "The server is still verifying that its configuration matches the data (or could not yet). Try again shortly; see System Status.",
                });
                return;
        }
    }
}
