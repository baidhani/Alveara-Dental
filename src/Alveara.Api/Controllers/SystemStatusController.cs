using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.BackgroundWork;
using Alveara.Api.Architecture.Backup;
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
public class SystemStatusController(AlveraDbContext db, BackgroundJobRunnerHeartbeat heartbeat, DeploymentInvariantStatus deployment) : ControllerBase
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
}
