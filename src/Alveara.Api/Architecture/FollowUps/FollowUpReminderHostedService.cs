using Alveara.Api.Architecture.Time;

namespace Alveara.Api.Architecture.FollowUps;

/// <summary>
/// STORY-009: runs the reminder scan in the background. <c>FollowUps:ReminderPollSeconds</c> sets the interval (default 60); 0 turns the loop off (tests that drive the pass themselves). ALV-009-C01: the scan only ENQUEUES the
/// reminders that are owed (and gives a failed one a fresh set of attempts after a cool-down); the ALV-N002 job runner, which polls on its own, delivers them with its retries, restart recovery and effect receipts. A failure
/// is logged with its error class only (never a task, title or note), the loop waits longer after each consecutive failure (capped) and carries on; the tasks themselves are unaffected and the reminders owed are enqueued
/// by a later pass. Each pass that did anything logs one structured line.
/// </summary>
public sealed class FollowUpReminderHostedService(IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<FollowUpReminderHostedService> logger) : BackgroundService
{
    private const int MaxBackoffSeconds = 300;

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
                await using var scope = scopeFactory.CreateAsyncScope();
                var result = await scope.ServiceProvider.GetRequiredService<FollowUpReminderService>().EnqueueDueAsync(stoppingToken);
                failures = 0;
                if (result.Enqueued + result.Undeliverable + result.Reset > 0)
                    logger.LogInformation("followup_reminders_pass outcome=success enqueued={Enqueued} undeliverable={Undeliverable} reset={Reset} duration_ms={DurationMs}",
                        result.Enqueued, result.Undeliverable, result.Reset, (int)(DateTimeOffset.UtcNow - started).TotalMilliseconds);
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
