namespace Alveara.Api.Architecture.Identity;

/// <summary>
/// ALV-N030 (brief section 6): the installer's command <c>Alveara.Api setup issue-token</c>. It issues a one-time first-run setup token against the configured
/// database and writes it, with its expiry and the setup page address, to the console ONLY (standard output, never through ILogger, so it is in no application
/// log). Exit codes: 0 issued; 2 refused because setup was already completed; 1 any other failure (the message names the class of failure, never a secret).
/// The installer (ALV-N013) calls it; in dev, scripts/New-DevAdmin.ps1 does.
/// </summary>
public static class SetupCommand
{
    public const int ExitIssued = 0;
    public const int ExitFailed = 1;
    public const int ExitAlreadyCompleted = 2;

    public static bool Matches(string[] args) =>
        args.Length >= 2 && args[0] == "setup" && args[1] == "issue-token";

    public static async Task<int> RunAsync(IServiceProvider services, TextWriter output, TextWriter error, string setupPageUrl)
    {
        try
        {
            await using var scope = services.CreateAsyncScope();
            var issued = await scope.ServiceProvider.GetRequiredService<DeploymentSetupService>().IssueTokenAsync(CancellationToken.None);
            await output.WriteLineAsync("DentalKeep first-run setup token (shown once; type it into the setup page):");
            await output.WriteLineAsync(issued.Token);
            await output.WriteLineAsync($"Expires at {issued.ExpiresAtUtc:u}. Setup page: {setupPageUrl}");
            await output.WriteLineAsync("Issuing a new token cancels this one.");
            return ExitIssued;
        }
        catch (SetupAlreadyCompletedException)
        {
            await error.WriteLineAsync("Setup has already been completed for this installation; no token was issued.");
            return ExitAlreadyCompleted;
        }
        catch (Exception ex)
        {
            await error.WriteLineAsync($"No setup token was issued ({ex.GetType().Name}). Check that the database is installed and reachable.");
            return ExitFailed;
        }
    }
}
