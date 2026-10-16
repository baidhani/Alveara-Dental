using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Treatment;

namespace Alveara.Api.Architecture.Completion;

/// <summary>
/// ALV-016-C01 R02 (review finding ALV-016-C01-R01-01): what happens when SQL Server chooses a completion or an adoption as a deadlock victim. Before this, the exception left the service as a raw provider
/// exception (EF wraps a transient error in an <see cref="InvalidOperationException"/>, so the <c>DbUpdateException</c> handlers never saw it). Now:
///
/// - <b>Retry.</b> The whole operation is run again from the start, at most <see cref="DeadlockRetryPolicy.DefaultAttempts"/> times in all, with a short jittered pause. Each attempt is safe to repeat: the server has
///   rolled the victim's transaction back (nothing of it remains), the change tracker is cleared, and the attempt re-reads the plan and re-checks the caller's row version and key, so one that runs after the competing
///   request committed meets that request's outcome (a replay of its own saved result, a <c>concurrency_conflict</c>, or <c>items_not_completable</c>) rather than recording anything twice. The all-or-nothing invariant
///   of the set is untouched: it is still one transaction per attempt.
/// - <b>Exhaustion.</b> If every attempt is a deadlock victim the caller gets the documented <c>save_failed</c> (503, "the database was busy, so nothing was ..."), logged by error class only.
/// - <b>Everything else</b> (domain refusals, conflicts, cancellation, any other failure) passes through exactly as before, on the first attempt.
/// - <b>Not covered, deliberately:</b> other transient errors (timeouts, lost connections: after them it cannot be known whether the work committed) and the other plan operations (amendment, plan edits).
/// </summary>
public partial class CompletedProcedureService
{
    private async Task<T> WithDeadlockRetryAsync<T>(string operation, string outcome, Func<Task<T>> attempt, CancellationToken ct)
    {
        var policy = deadlockRetry ?? DeadlockRetryPolicy.Default;
        try
        {
            return await policy.RunAsync(attempt, ct, (failedAttempt, _) =>
            {
                db.ChangeTracker.Clear();                                    // nothing of the failed attempt may be carried into the next one
                logger?.LogWarning("completion_deadlock_retry operation={Operation} attempt={Attempt} max_attempts={MaxAttempts} error_class=DeadlockVictim", operation, failedAttempt, policy.MaxAttempts);
            });
        }
        catch (DeadlockRetriesExhaustedException ex)
        {
            db.ChangeTracker.Clear();
            logger?.LogError("completion_deadlock_retries_exhausted operation={Operation} attempts={Attempts} error_class=DeadlockVictim", operation, ex.Attempts);
            throw new TreatmentPlanException("save_failed", $"The {operation} could not be saved because the database was busy, so nothing was {outcome}. Try again; if it keeps failing, contact support.", 503, inner: ex.InnerException);
        }
    }
}
