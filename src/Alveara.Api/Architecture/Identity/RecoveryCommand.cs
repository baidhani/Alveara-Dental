namespace Alveara.Api.Architecture.Identity;

/// <summary>
/// ALV-N030-C01 (brief 18.6): the installer's command <c>Alveara.Api recovery issue-token</c>, the counterpart of <see cref="SetupCommand"/>. It issues a one-time
/// recovery token for the Deployment Administrator and writes it, with its expiry and the recovery page address, to the console ONLY (standard output, never
/// through ILogger, so it is in no application log). Exit codes: 0 issued; 2 refused because the installation is not set up yet (use first-run setup);
/// 1 any other failure (the message names the class of failure, never a secret). In dev, scripts/New-DevRecoveryToken.ps1 calls it.
/// </summary>
public static class RecoveryCommand
{
    public const int ExitIssued = 0;
    public const int ExitFailed = 1;
    public const int ExitNotSetUp = 2;

    public static bool Matches(string[] args) =>
        args.Length >= 2 && args[0] == "recovery" && args[1] == "issue-token";

    public static async Task<int> RunAsync(IServiceProvider services, TextWriter output, TextWriter error, string recoveryPageUrl)
    {
        try
        {
            await using var scope = services.CreateAsyncScope();
            var issued = await scope.ServiceProvider.GetRequiredService<DeploymentRecoveryService>().IssueTokenAsync(CancellationToken.None);
            await output.WriteLineAsync("DentalKeep Deployment Administrator recovery token (shown once; type it into the recovery page):");
            await output.WriteLineAsync(issued.Token);
            await output.WriteLineAsync($"Expires at {issued.ExpiresAtUtc:u}. Recovery page: {recoveryPageUrl}");
            await output.WriteLineAsync("Issuing a new token cancels this one.");
            return ExitIssued;
        }
        catch (RecoveryNotAvailableException)
        {
            await error.WriteLineAsync("This installation has not been set up yet; use first-run setup (setup issue-token). No recovery token was issued.");
            return ExitNotSetUp;
        }
        catch (Exception ex)
        {
            await error.WriteLineAsync($"No recovery token was issued ({ex.GetType().Name}). Check that the database is installed and reachable.");
            return ExitFailed;
        }
    }
}
