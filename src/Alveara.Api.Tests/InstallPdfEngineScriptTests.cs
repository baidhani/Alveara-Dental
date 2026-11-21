using System.Diagnostics;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N026 S4: <c>scripts\Install-PdfEngine.ps1</c>, run as a real process against a throwaway data root: a repeat run on a correct target changes nothing; a target holding anything else is refused,
/// never overwritten; and a source that does not match the reviewed manifest is refused and leaves no half-made target. The real store is read only by the first test (repeat run).
/// </summary>
public class InstallPdfEngineScriptTests : IDisposable
{
    private readonly string _root = Path.Combine(AlveraTestEnvironment.TempFolder, $"pdf-script-{Guid.NewGuid():N}");

    public InstallPdfEngineScriptTests() => Directory.CreateDirectory(_root);
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }

    /// <summary>The repository root; its folder name contains [ ], which Windows PowerShell reads as a wildcard in a script path.</summary>
    private static string RepoRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private static (int Exit, string Output) Run(string dataRoot)
    {
        var psi = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, WorkingDirectory = RepoRoot };
        // Set-Location -LiteralPath, then the script by its relative path: Windows PowerShell refuses -File for any path under a folder named with [ ]
        var command = $@"Set-Location -LiteralPath '{RepoRoot}'; & .\scripts\Install-PdfEngine.ps1 -ForTests; exit $LASTEXITCODE";
        foreach (var a in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", command }) psi.ArgumentList.Add(a);
        psi.Environment["ALVERA_DATA_ROOT"] = dataRoot;
        using var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEndAsync();
        var error = p.StandardError.ReadToEndAsync();
        Assert.True(p.WaitForExit(120_000), "the script did not finish in two minutes");
        return (p.ExitCode, output.Result + error.Result);
    }

    [Fact]
    public void A_repeat_run_on_the_provisioned_test_engine_changes_nothing()
    {
        var before = Directory.GetFiles(AlveraTestEnvironment.PdfEngineFolder, "*", SearchOption.AllDirectories).Select(f => (f, File.GetLastWriteTimeUtc(f))).ToList();
        var (exit, output) = Run(AlveraTestEnvironment.DataRoot);
        Assert.Equal(0, exit);
        Assert.Contains("Already provisioned and verified", output);
        Assert.Equal(before, Directory.GetFiles(AlveraTestEnvironment.PdfEngineFolder, "*", SearchOption.AllDirectories).Select(f => (f, File.GetLastWriteTimeUtc(f))).ToList());
    }

    [Fact]
    public void A_target_holding_anything_else_is_refused_and_left_as_it_was()
    {
        var target = Path.Combine(_root, "Test-Data", "PdfEngine");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "typst.exe"), "not the engine");
        var (exit, output) = Run(_root);
        Assert.NotEqual(0, exit);
        Assert.Contains("REFUSED", output);
        Assert.Equal("not the engine", File.ReadAllText(Path.Combine(target, "typst.exe")));
    }

    [Fact]
    public void A_source_that_does_not_match_the_manifest_is_refused_and_no_target_is_left()
    {
        var store = Path.Combine(_root, "release-artifacts");
        var runtime = Path.Combine(store, "msvc-runtime", "14.44.35211.0", "x64");
        Directory.CreateDirectory(runtime);
        File.Copy(Path.Combine(AlveraTestEnvironment.DataRoot, "release-artifacts", "msvc-runtime", "14.44.35211.0", "x64", "vc_redist.x64.exe"), Path.Combine(runtime, "vc_redist.x64.exe"));
        var typst = Path.Combine(store, "typst", "0.15.1", "windows-x64");
        Directory.CreateDirectory(typst);
        File.WriteAllText(Path.Combine(typst, "typst-x86_64-pc-windows-msvc.zip"), "a forged zip");
        var (exit, output) = Run(_root);
        Assert.NotEqual(0, exit);
        Assert.Contains("the engine zip does not match the manifest", output);
        Assert.False(Directory.Exists(Path.Combine(_root, "Test-Data", "PdfEngine")));
        Assert.Empty(Directory.Exists(Path.Combine(_root, "Test-Data")) ? Directory.GetDirectories(Path.Combine(_root, "Test-Data"), "PdfEngine.partial-*") : []);
    }
}
