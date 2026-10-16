using Microsoft.Data.SqlClient;

namespace Alveara.Api.Architecture.Concurrency;

/// <summary>
/// ALV-016-C01 R02 (review finding ALV-016-C01-R01-01): recognises SQL Server's "this session was chosen as the deadlock victim" error (1205) wherever it is found in an exception chain. EF Core's default SQL Server
/// execution strategy wraps a transient provider error (a deadlock is one) in an <see cref="InvalidOperationException"/> ("likely due to a transient failure"), so the 1205 is usually two levels down
/// (<c>InvalidOperationException</c> then <c>DbUpdateException</c> then <c>SqlException</c>); a classifier that only looks at the outer type misses it, which is how a deadlock reached a caller as a raw provider exception.
///
/// A deadlock victim is the one transient failure that is always safe to run again: the server has already rolled the victim's whole transaction back, so nothing of the failed attempt remains. Other transient
/// errors (a timeout, a lost connection) are deliberately NOT classified here: after them it cannot be known whether the work was committed.
/// </summary>
public static class DeadlockVictim
{
    public const int SqlErrorNumber = 1205;

    public static bool Is(Exception? exception)
    {
        for (var e = exception; e is not null; e = e.InnerException)
        {
            if (e is SqlException sql && (sql.Number == SqlErrorNumber || sql.Errors.Cast<SqlError>().Any(x => x.Number == SqlErrorNumber))) return true;
            if (e is AggregateException aggregate && aggregate.InnerExceptions.Any(Is)) return true;
        }
        return false;
    }
}

/// <summary>The operation was chosen as a deadlock victim on every attempt the policy allows. Nothing was recorded (every attempt was rolled back by the server); the caller turns this into its documented save failure.</summary>
public sealed class DeadlockRetriesExhaustedException(int attempts, Exception inner)
    : Exception($"The operation was chosen as a deadlock victim on all {attempts} attempts.", inner)
{
    public int Attempts { get; } = attempts;
}

/// <summary>
/// Runs a whole, idempotent operation again when, and only when, it was chosen as a deadlock victim, at most <see cref="MaxAttempts"/> times in all with a short, jittered, growing pause between attempts.
///
/// Why this is safe for the operations it wraps (completion and adoption): the server rolled the victim's transaction back, so no half-recorded set exists; each attempt starts from scratch (it re-reads the plan and
/// re-checks the caller's row version and key), so an attempt that runs after the competing request committed finds that request's result (a replay of its own key, or a <c>concurrency_conflict</c> because the plan's
/// version moved, or the procedures already completed) instead of recording anything twice. Every other exception, including cancellation, passes through untouched on the first attempt.
///
/// After the last attempt the original exception is wrapped in <see cref="DeadlockRetriesExhaustedException"/> for the caller to convert into its documented, safe-to-show failure; it is never swallowed.
/// </summary>
public sealed class DeadlockRetryPolicy(int maxAttempts = DeadlockRetryPolicy.DefaultAttempts, Func<TimeSpan, CancellationToken, Task>? delay = null, Action<int, Exception>? onRetry = null)
{
    public const int DefaultAttempts = 3;
    private const int BaseDelayMilliseconds = 40;
    private const int JitterMilliseconds = 40;

    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? Task.Delay;

    public static DeadlockRetryPolicy Default { get; } = new();

    public int MaxAttempts { get; } = maxAttempts >= 1 ? maxAttempts : throw new ArgumentOutOfRangeException(nameof(maxAttempts), "At least one attempt is needed.");

    /// <summary>The pause before the attempt that follows failed attempt <paramref name="failedAttempt"/> (1 for the first failure): it grows with each failure and carries a little jitter so two victims do not collide again in step.</summary>
    internal static TimeSpan Pause(int failedAttempt) => TimeSpan.FromMilliseconds(BaseDelayMilliseconds * failedAttempt + Random.Shared.Next(JitterMilliseconds + 1));

    public async Task<T> RunAsync<T>(Func<Task<T>> operation, CancellationToken ct, Action<int, Exception>? observe = null)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await operation();
            }
            catch (Exception ex) when (DeadlockVictim.Is(ex))
            {
                if (attempt >= MaxAttempts) throw new DeadlockRetriesExhaustedException(attempt, ex);
                onRetry?.Invoke(attempt, ex);
                observe?.Invoke(attempt, ex);
                await _delay(Pause(attempt), ct);
            }
        }
    }
}
