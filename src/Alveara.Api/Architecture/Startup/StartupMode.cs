using Alveara.Api.Architecture.Identity;
using Alveara.Api.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Alveara.Api.Architecture.Startup;

/// <summary>
/// ALV-N030 (brief section 16, PD-38): what this process is for, decided ONCE before the host is built and never changed while it runs.
/// <list type="bullet">
/// <item><see cref="NotInstalled"/>: the database cannot be reached or does not exist. Only the installer can resolve it (ALV-N013).</item>
/// <item><see cref="UpgradeRequired"/>: the database exists but has migrations to apply. The API never migrates; the upgrade tool does (ALV-N013).</item>
/// <item><see cref="Setup"/>: current schema, no Deployment Administrator and no <see cref="BootstrapState"/>: only first-run setup is offered.</item>
/// <item><see cref="Normal"/>: today's behaviour.</item>
/// </list>
/// In every mode but Normal no application background service is registered and every <c>/api</c> route but the few the mode allows answers 503.
/// </summary>
public enum StartupMode { NotInstalled, UpgradeRequired, Setup, Normal }

/// <summary>The decision, held for the life of the process (a singleton).</summary>
public sealed record StartupModeState(StartupMode Mode, int PendingMigrations)
{
    /// <summary>The exit code with which a process asks its supervisor to start it again (after first-run setup completes).</summary>
    public const int RestartRequestedExitCode = 75;

    /// <summary>The value the status route and the 503 bodies use for each mode.</summary>
    public string Code => Mode switch
    {
        StartupMode.NotInstalled => "installation_incomplete",
        StartupMode.UpgradeRequired => "upgrade_required",
        StartupMode.Setup => "setup_required",
        _ => "normal",
    };
}

/// <summary>
/// ALV-N030 (brief 16.2): the start-up readiness check. One read, with an explicit 15 s connection and command timeout, retried up to 3 times with backoff
/// ONLY on connection-level failures (a server that is not answering). A database that does not exist, or a login the server refuses, is a definite answer:
/// <see cref="StartupMode.NotInstalled"/> at once. The check only reads: it never creates a database and never applies a migration.
/// </summary>
public static class StartupModeProbe
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);
    public const int MaxRetries = 3;
    private static readonly TimeSpan[] DefaultBackoff = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4)];

    /// <summary>SQL Server errors that are an answer, not an outage: cannot open the database (4060), login failed (18456), database does not exist (911).</summary>
    private static readonly HashSet<int> DefiniteErrors = [4060, 18456, 911];

    public static async Task<StartupModeState> DetectAsync(string? connectionString, IReadOnlyList<TimeSpan>? backoff = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) return new StartupModeState(StartupMode.NotInstalled, 0);
        var builder = new SqlConnectionStringBuilder(connectionString) { ConnectTimeout = (int)Timeout.TotalSeconds };
        var options = new DbContextOptionsBuilder<AlveraDbContext>()
            .UseSqlServer(builder.ConnectionString, sql => sql.CommandTimeout((int)Timeout.TotalSeconds))
            .Options;
        var delays = backoff ?? DefaultBackoff;

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                // opened explicitly (not CanConnect, which hides why it failed): a missing database and an unreachable server are different answers
                await using (var probe = new SqlConnection(builder.ConnectionString)) await probe.OpenAsync(ct);
                await using var db = new AlveraDbContext(options);
                var pending = (await db.Database.GetPendingMigrationsAsync(ct)).Count();
                if (pending > 0) return new StartupModeState(StartupMode.UpgradeRequired, pending);
                var setUp = await db.UserAccounts.AnyAsync(u => u.AccountKind == AccountKind.DeploymentAdministrator, ct) || await db.BootstrapStates.AnyAsync(ct);
                return new StartupModeState(setUp ? StartupMode.Normal : StartupMode.Setup, 0);
            }
            catch (SqlException ex) when (DefiniteErrors.Contains(ex.Number))
            {
                return new StartupModeState(StartupMode.NotInstalled, 0);
            }
            catch (SqlException) when (attempt < MaxRetries && attempt < delays.Count)
            {
                await Task.Delay(delays[attempt], ct);   // connection-level failure: the server may still be starting
            }
            catch (SqlException)
            {
                return new StartupModeState(StartupMode.NotInstalled, 0);   // retries exhausted: unreachable
            }
        }
    }
}
