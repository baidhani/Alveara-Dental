using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.BackgroundWork;
using Alveara.Api.Architecture.Backup;
using Alveara.Api.Architecture.Documents;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Storage;
using Alveara.Api.Data;

namespace Alveara.Api.Controllers;

/// <summary>
/// Truthful app/server/database/background-runner status for the admin-visible System Status
/// page (ALV-N002). Every field reflects a real check performed at request time — never an
/// assumed-healthy default — and a database outage degrades this endpoint's response rather than
/// throwing a 500, so the status page itself stays usable when the thing it reports on is down.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class SystemStatusController(AlveraDbContext db, BackgroundJobRunnerHeartbeat heartbeat, DeploymentInvariantStatus deployment, IServiceProvider services, ILogger<SystemStatusController> log) : ControllerBase
{
    private static readonly TimeSpan RunnerStaleThreshold = TimeSpan.FromSeconds(30);

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var version = typeof(SystemStatusController).Assembly.GetName().Version?.ToString() ?? "unknown";

        bool databaseReachable;
        try
        {
            // A trivial round-trip query, not a schema assumption — this must degrade gracefully
            // rather than throw when the database is unreachable, per the story's failure-path
            // requirement ("Database unavailable/corrupt" must not crash the status endpoint).
            databaseReachable = await db.Database.CanConnectAsync(cancellationToken);
        }
        catch
        {
            databaseReachable = false;
        }

        var lastPoll = heartbeat.LastPollUtc;
        var runnerStatus = lastPoll is null
            ? "not_yet_polled"
            : DateTimeOffset.UtcNow - lastPoll < RunnerStaleThreshold
                ? "healthy"
                : "stale";

        return Ok(new
        {
            appVersion = version,
            localServerReachable = true, // trivially true: we're answering this request
            database = new { reachable = databaseReachable },
            backgroundRunner = new { status = runnerStatus, lastPollUtc = lastPoll },
            // ALV-N004 R02: does this server's configuration match the settings its data was created under?
            deployment = new { state = deployment.Snapshot.State.ToString().ToLowerInvariant(), mismatches = deployment.Snapshot.Mismatches },
        });
    }
    /// <summary>
    /// ALV-N026 (D-06, PD-25): the administrator's sections - backup and recovery, storage, staff accounts. Administrators only, by role. Counts and states only: no file path, recovery key,
    /// username or document name. Each section is read on its own, so one that cannot be read is reported as unavailable without hiding the others.
    /// </summary>
    [HttpGet("admin")]
    [Authorize(Roles = nameof(Role.Admin))]
    public async Task<IActionResult> Admin(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        object? backup = null, storage = null, accounts = null;
        try
        {
            backup = await services.GetRequiredService<BackupHealth>().ReadAsync(now, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogError("event=system_status_backup_unavailable outcome=failure error_class={ErrorClass}", ex.GetType().Name);
        }
        try
        {
            var root = services.GetRequiredService<StorageRootInfo>().Path;
            var reachable = Directory.Exists(root);
            long? free = null;
            if (reachable && Path.GetPathRoot(Path.GetFullPath(root)) is { Length: > 0 } drive) free = new DriveInfo(drive).AvailableFreeSpace;
            var integrity = await db.DocumentIntegrity.AsNoTracking().GroupBy(i => i.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
            int Of(string status) => integrity.FirstOrDefault(i => i.Key == status)?.Count ?? 0;
            storage = new { documentFolderReachable = reachable, freeBytes = free, missingDocuments = Of(IntegrityStatuses.Missing), damagedDocuments = Of(IntegrityStatuses.Corrupt) };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogError("event=system_status_storage_unavailable outcome=failure error_class={ErrorClass}", ex.GetType().Name);
        }
        try
        {
            var rows = await db.UserAccounts.AsNoTracking().Select(a => new { a.IsDisabled, a.LockedOutUntilUtc, a.MfaEnabled }).ToListAsync(ct);
            accounts = new
            {
                enabled = rows.Count(a => !a.IsDisabled), disabled = rows.Count(a => a.IsDisabled), locked = rows.Count(a => !a.IsDisabled && a.LockedOutUntilUtc > now),
                enabledWithoutMfa = rows.Count(a => !a.IsDisabled && !a.MfaEnabled),
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogError("event=system_status_accounts_unavailable outcome=failure error_class={ErrorClass}", ex.GetType().Name);
        }
        return Ok(new { backup, storage, accounts });
    }
}
