namespace Alveara.Api.Architecture.Recall;

/// <summary>
/// ALV-009-C01: every <c>FollowUps:WorkPollSeconds</c> seconds (default 300; 0 turns it off, as in tests that drive the passes themselves) looks for patients due a recall and for accepted treatment nobody has scheduled, and
/// enqueues durable background jobs for them (ALV-N002's runner does the work, with its retries, restart recovery and effect receipts). The passes only enqueue: the job keys make a repeat, a restart or a second server harmless.
/// A failure is logged with its error class only, the loop waits longer after each consecutive failure (capped) and carries on; each pass that did anything logs one structured line.
/// </summary>
public sealed class FollowUpWorkHostedService(IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<FollowUpWorkHostedService> logger) : BackgroundService
{
    private const int MaxBackoffSeconds = 1800;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var poll = configuration.GetValue<int?>("FollowUps:WorkPollSeconds") ?? 300;
        if (poll <= 0) return;
        var failures = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var started = DateTimeOffset.UtcNow;
                await using var scope = scopeFactory.CreateAsyncScope();
                var recall = await scope.ServiceProvider.GetRequiredService<RecallScheduler>().EnqueueDueAsync(DateTimeOffset.UtcNow, stoppingToken);
                var treatment = await scope.ServiceProvider.GetRequiredService<UnscheduledTreatmentScheduler>().EnqueueAsync(stoppingToken);
                var cases = await scope.ServiceProvider.GetRequiredService<ExternalCases.ExternalCaseTaskScheduler>().EnqueueAsync(stoppingToken);      // ALV-009-C02: referral and lab chase tasks
                failures = 0;
                if (recall.Enqueued + treatment.Enqueued + treatment.Closing + cases.Enqueued + cases.Closing > 0)
                    logger.LogInformation("followup_work_pass outcome=success recall_due={RecallDue} recall_enqueued={RecallEnqueued} treatment_enqueued={TreatmentEnqueued} treatment_closing={TreatmentClosing} case_tasks_enqueued={CaseEnqueued} case_tasks_closing={CaseClosing} duration_ms={DurationMs}",
                        recall.Due, recall.Enqueued, treatment.Enqueued, treatment.Closing, cases.Enqueued, cases.Closing, (int)(DateTimeOffset.UtcNow - started).TotalMilliseconds);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                failures++;
                logger.LogWarning("followup_work_pass outcome=failure error_class={ErrorClass} consecutive_failures={Failures}", ex.GetType().Name, failures);
            }

            try { await Task.Delay(TimeSpan.FromSeconds(Math.Min(poll * (1 << Math.Min(failures, 4)), MaxBackoffSeconds)), stoppingToken); }
            catch (TaskCanceledException) { /* shutdown */ }
        }
    }
}
