using System.Runtime.ExceptionServices;
using Microsoft.Data.SqlClient;

namespace Alveara.Api.Tests;

/// <summary>
/// The bounded, narrowly classified retry of <see cref="TestDatabaseFixture"/> (split from the fixture file in ALV-N019 R04 to keep both files small). Nothing here changed in behaviour:
/// only the pool-acquisition timeout, the deadlock and the database-in-use errors are ever retried, every retry is bounded and logged by class only.
/// </summary>
public partial class TestDatabaseFixture
{
    /// <summary>SQL Server "chosen as the deadlock victim".</summary>
    internal const int DeadlockErrorNumber = 1205;

    /// <summary>
    /// ALV-017-C01 R02: SQL Server "Cannot drop database ... because it is currently in use". The drop batch is ALTER DATABASE ... SET SINGLE_USER WITH ROLLBACK IMMEDIATE followed by DROP DATABASE; a session
    /// that connects in the instant between the two takes the one single-user slot and the DROP fails with this number. Seen once in a full run (R01 Stage 2, run 2).
    /// </summary>
    internal const int DatabaseInUseErrorNumber = 3702;

    /// <summary>The start of the message SqlClient uses for its pool-acquisition timeout (whitespace normalised before comparing).</summary>
    private const string PoolAcquisitionTimeoutPrefix = "Timeout expired. The timeout period elapsed prior to obtaining a connection from the pool.";

    /// <summary>TESTSPEED-P2: true only for SQL Server error 1205 (this session was chosen as a deadlock victim; the whole batch was rolled back and may be run again).</summary>
    internal static bool IsDeadlock(Exception exception) => exception is SqlException { Number: DeadlockErrorNumber };

    /// <summary>
    /// TESTSPEED-P2: true for the <see cref="InvalidOperationException"/> SqlClient throws when a connection cannot be obtained from a pool ("Timeout expired. The timeout period elapsed prior to
    /// obtaining a connection from the pool ..."). Investigated cause: SqlClient 7.0.0's pool-pruning timer shuts down an empty pool while a caller is acquiring a connection from it, and the
    /// caller gets this same message at once ("Pool is shutting down; abandoning wait"). LIMIT: the message is shared with genuine pool exhaustion, so this classifier can also match that; a
    /// bounded retry of a real exhaustion fails again and the first exception is then rethrown, so nothing is hidden.
    /// </summary>
    internal static bool IsPoolAcquisitionTimeout(Exception exception) =>
        exception is InvalidOperationException && string.Join(' ', exception.Message.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).StartsWith(PoolAcquisitionTimeoutPrefix, StringComparison.Ordinal);

    /// <summary>True only for SQL Server error 3702 (see <see cref="DatabaseInUseErrorNumber"/>).</summary>
    internal static bool IsDatabaseInUse(Exception exception) => exception is SqlException { Number: DatabaseInUseErrorNumber };

    /// <summary>
    /// What the drop retries: the deadlock of the Gate 2 root-cause evidence, the pool-acquisition timeout (the drop opens a connection to master first, which can hit the same pool race), and (ALV-017-C01 R02) the
    /// database being in use. For the last, every retry runs the whole ALTER ... ROLLBACK IMMEDIATE again, which removes whoever took the single-user slot; the attempts are bounded by <see cref="MaxDropAttempts"/>
    /// and when they run out the FIRST exception is rethrown unchanged, so a holder that keeps coming back still fails the test (with the holder's safe metadata in the log, see <see cref="LogDatabaseHoldersAsync"/>).
    /// </summary>
    internal static bool IsTransientDropFailure(Exception exception) => IsDeadlock(exception) || IsPoolAcquisitionTimeout(exception) || IsDatabaseInUse(exception);

    /// <summary>
    /// The bounded retry loop: at most <paramref name="maxAttempts"/> attempts in total, waiting <paramref name="delays"/> between them (a zero wait is not slept), <paramref name="beforeRetry"/> (if any)
    /// run after each failed attempt and before the next one, every retry logged (operation, database name, attempt numbers and a safe failure classification only; never a connection string).
    /// When the last attempt also fails with a retryable error, the FIRST (original) exception is rethrown unchanged (same object), so the failure looks exactly as it did before the retry
    /// existed. A failure <paramref name="isRetryable"/> does not accept propagates at once.
    /// </summary>
    internal static async Task RunWithTransientRetryAsync(Func<Task> operation, Func<Exception, bool> isRetryable, string operationName, string database, int maxAttempts, TimeSpan[] delays,
        Func<TimeSpan, Task> delay, TextWriter log, Func<Task>? beforeRetry)
    {
        ExceptionDispatchInfo? original = null;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await operation();
                return;
            }
            catch (Exception ex) when (isRetryable(ex))
            {
                original ??= ExceptionDispatchInfo.Capture(ex);
                var code = ex is SqlException sql ? $"SQL {sql.Number}" : IsPoolAcquisitionTimeout(ex) ? "pool acquisition timeout" : ex.GetType().Name;
                if (attempt >= maxAttempts)
                {
                    log.WriteLine($"[TestDatabaseFixture] {operationName} of {database} hit {code} (attempt {attempt} of {maxAttempts}); giving up after {maxAttempts} attempts.");
                    original.Throw();
                }

                var wait = delays[attempt - 1];
                log.WriteLine($"[TestDatabaseFixture] {operationName} of {database} hit {code} (attempt {attempt} of {maxAttempts}); retrying in {wait.TotalMilliseconds:0} ms.");
                if (beforeRetry is not null) await beforeRetry();
                if (wait > TimeSpan.Zero) await delay(wait);
            }
        }
    }
}
