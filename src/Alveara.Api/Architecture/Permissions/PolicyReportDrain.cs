using Microsoft.Extensions.Hosting;

namespace Alveara.Api.Architecture.Permissions;

/// <summary>
/// ALV-017-C01 R02: lets a stopping host finish the invalid-row report that <see cref="PermissionPolicyStore"/> started in the background. The store reports a degraded or invalid policy after the response
/// has gone (a <c>Task.Run</c> that opens a database connection through <see cref="AuditingPolicyObserver"/>), so nothing tied the report to the host's lifecycle: a host that stopped right after such a
/// request could still be reading and writing the database. That was the holder named by the teardown diagnostic when a test database could not be dropped (SQL error 3702). A stop now waits for the reports
/// started so far, for as long as the host's shutdown token allows; if that runs out the host stops anyway and the timeout is logged. Failure modes handled: a report that is still running at stop, and several
/// reports in turn. Not handled: a request that arrives after this service has stopped (the web server stops later), which starts a report this service does not wait for.
/// </summary>
public sealed class PolicyReportDrain(PermissionPolicyStore store, ILogger<PolicyReportDrain> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            Task report;
            while (!(report = store.ReportsIdle()).IsCompleted) await report.WaitAsync(cancellationToken);   // a report that started meanwhile is a new task: look again until none is running
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("permission_policy_report_drain_timeout outcome=failure error_class=OperationCanceledException");
        }
    }
}
