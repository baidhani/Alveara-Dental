using Alveara.Api.Architecture.Time;

namespace Alveara.Api.Architecture.FollowUps;

/// <summary>
/// STORY-009: runs the reminder pass in the background. <c>FollowUps:ReminderPollSeconds</c> sets the interval (default 60); 0 turns the loop off (tests that drive the pass themselves). A pass repeats while it still
/// finds reminders owed, up to a cap per wake-up, so a long outage is caught up in a few wake-ups. A failure is logged with its error class only (never a task, title or note), the loop waits longer after each
/// consecutive failure (capped) and carries on; the tasks themselves are unaffected and the reminders owed are simply sent by a later pass. Each pass logs its counts as one structured line.
/// </summary>
public sealed class FollowUpReminderHostedService(IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<FollowUpReminderHostedService> logger) : BackgroundService
{
    private const int MaxBackoffSeconds = 300;
    private const int MaxPassesPerWake = 20;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var poll = configuration.GetValue<int?>("FollowUps:ReminderPollSeconds") ?? 60;
        if (poll <= 0) return;
        var failures = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var started = DateTimeOffset.UtcNow;
                int sent = 0, fallback = 0, undeliverable = 0, deferred = 0;
                for (var i = 0; i < MaxPassesPerWake; i++)
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var result = await scope.ServiceProvider.GetRequiredService<FollowUpReminderService>().ProcessOnceAsync(stoppingToken);
                    sent += result.Sent; fallback += result.Fallback; undeliverable = result.Undeliverable; deferred = result.Deferred;
                    if (result.Total == 0) break;
                }
                failures = 0;
                if (sent + fallback + undeliverable + deferred > 0)
                    logger.LogInformation("followup_reminders_pass outcome=success sent={Sent} fallback={Fallback} undeliverable={Undeliverable} deferred={Deferred} duration_ms={DurationMs}",
                        sent, fallback, undeliverable, deferred, (int)(DateTimeOffset.UtcNow - started).TotalMilliseconds);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                failures++;
                logger.LogWarning("followup_reminders_pass outcome=failure error_class={ErrorClass} consecutive_failures={Failures}", ex.GetType().Name, failures);
            }

            try { await Task.Delay(TimeSpan.FromSeconds(Math.Min(poll * (1 << Math.Min(failures, 4)), MaxBackoffSeconds)), stoppingToken); }
            catch (TaskCanceledException) { /* shutdown */ }
        }
    }
}
