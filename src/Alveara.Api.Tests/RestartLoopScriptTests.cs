using System.Diagnostics;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N030 S22 (brief 16.4): the dev restart loop (scripts/RestartLoop.ps1, used by Start-DevApi.ps1) restarts ONLY on exit code 75, stops on any other code,
/// and the script's own exit code is the last one. A stub stands in for the API: it records each start and exits with the code the test scripts for it.
/// </summary>
public class RestartLoopScriptTests
{
    private static readonly string RepoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    /// <summary>Runs the loop with a stub whose N-th start exits with codes[N] (the last code repeats); returns (script exit code, number of starts).</summary>
    private static async Task<(int Exit, int Starts)> RunAsync(int[] codes, int maxRestarts = 10)
    {
        var loop = Path.Combine(RepoRoot, "scripts", "RestartLoop.ps1").Replace("'", "''");
        var list = string.Join(",", codes);
        var script = $@"$ErrorActionPreference = 'Stop'
. ([WildcardPattern]::Escape('{loop}'))
$script:starts = 0
$codes = @({list})
$code = Invoke-RestartOnRequest -MaxRestarts {maxRestarts} -Start {{
    param($attempt)
    $script:starts++
    $codes[[Math]::Min($attempt, $codes.Count - 1)]
}}
Write-Output ""STARTS=$script:starts""
exit $code";
        var psi = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var a in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", script }) psi.ArgumentList.Add(a);
        using var process = Process.Start(psi)!;
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await process.WaitForExitAsync(timeout.Token);
        var line = output.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("STARTS="));
        Assert.True(line is not null, $"no start count; stdout: {output} stderr: {error}");
        return (process.ExitCode, int.Parse(line!["STARTS=".Length..]));
    }

    [Fact]
    public async Task Exit_75_once_then_0_restarts_exactly_once_and_ends_with_0() =>
        Assert.Equal((0, 2), await RunAsync([75, 0]));

    [Fact]
    public async Task Exit_1_is_not_restarted_and_is_passed_on() =>
        Assert.Equal((1, 1), await RunAsync([1]));

    [Fact]
    public async Task A_restart_request_every_time_is_bounded() =>
        Assert.Equal((75, 3), await RunAsync([75], maxRestarts: 2));

    [Fact]
    public void Start_DevApi_runs_the_api_through_the_loop_and_exits_with_its_code()
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot, "scripts", "Start-DevApi.ps1"));
        Assert.Contains("RestartLoop.ps1", text);
        Assert.Contains("Invoke-RestartOnRequest", text);
        Assert.Contains("exit $code", text);
    }
}
