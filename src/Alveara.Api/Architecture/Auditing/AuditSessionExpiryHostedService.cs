using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Auditing;

/// <summary>
/// ALV-N025 (BR-01): runs the session-expiry pass (<see cref="AuditSessionStore.ExpirePassAsync"/>) in the background, the same pattern as the follow-up reminder pass.
/// <c>AuditSessions:ExpiryPollSeconds</c> sets the interval (default 60, so an expiry is recorded within about a minute of the fixed end time); 0 turns the loop off
/// (tests drive the pass themselves). A crash between passes loses nothing: the next pass finds the same rows. A failure is logged with its error class only, the loop
/// waits longer after each consecutive failure (capped) and carries on.
/// </summary>
public sealed class AuditSessionExpiryHostedService(IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<AuditSessionExpiryHostedService> logger) : BackgroundService
{
    private const int MaxBackoffSeconds = 300;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var poll = configuration.GetValue<int?>("AuditSessions:ExpiryPollSeconds") ?? 60;
        if (poll <= 0) return;
        var failures = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var started = DateTimeOffset.UtcNow;
                await using var scope = scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AlveraDbContext>();
                var result = await new AuditSessionStore(db, scope.ServiceProvider.GetService<ILogger<AuditSessionStore>>()).ExpirePassAsync(DateTimeOffset.UtcNow, stoppingToken);
                failures = 0;
                if (result.Expired > 0)
                    logger.LogInformation("audit_session_expiry_pass outcome=success examined={Examined} expired={Expired} duration_ms={DurationMs}",
                        result.Examined, result.Expired, (int)(DateTimeOffset.UtcNow - started).TotalMilliseconds);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                failures++;
                logger.LogWarning("audit_session_expiry_pass outcome=failure error_class={ErrorClass} consecutive_failures={Failures}", ex.GetType().Name, failures);
            }

            try { await Task.Delay(TimeSpan.FromSeconds(Math.Min(poll * (1 << Math.Min(failures, 4)), MaxBackoffSeconds)), stoppingToken); }
            catch (TaskCanceledException) { /* shutdown */ }
        }
    }
}
