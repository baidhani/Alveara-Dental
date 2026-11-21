using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Alveara.Api.Architecture.Pdf;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N026 S4 against the real, provisioned engine (Test-Data\PdfEngine, from <c>scripts\Install-PdfEngine.ps1 -ForTests</c>): the engine is ready only when its hash, its version, its fonts
/// and a self-test render all check out, and says exactly why not otherwise; a render produces a PDF and leaves nothing behind; a render is killed at its timeout; no more than two run at once; and a
/// template cannot read outside its own folder. No test here is skipped when the engine is missing: they fail, naming the script to run.
/// </summary>
public class PdfEngineTests : IDisposable
{
    private readonly string _scratch = Path.Combine(AlveraTestEnvironment.TempFolder, $"pdf-tests-{Guid.NewGuid():N}");

    public PdfEngineTests()
    {
        Directory.CreateDirectory(_scratch);
        Assert.True(File.Exists(Path.Combine(EngineDir, "typst.exe")),
            $"The PDF engine is not provisioned for tests. Run: powershell -NoProfile -ExecutionPolicy Bypass -File scripts\\Install-PdfEngine.ps1 -ForTests (expected {EngineDir}).");
    }

    public void Dispose()
    {
        if (Directory.Exists(_scratch)) Directory.Delete(_scratch, recursive: true);
    }

    private static string EngineDir => AlveraTestEnvironment.PdfEngineFolder;
    private static string FontDir => Path.Combine(EngineDir, "fonts");
    private string WorkDir => Path.Combine(_scratch, "work");

    private PdfEngine Engine(string? engineDir = null, string? fontDir = null, int timeoutSeconds = 30, PdfEngineManifest? manifest = null, Func<string, string, PdfTemplate>? templates = null) =>
        new(Options.Create(new PdfOptions { EngineDirectory = engineDir ?? EngineDir, FontDirectory = fontDir ?? FontDir, WorkDirectory = WorkDir, TimeoutSeconds = timeoutSeconds }),
            NullLogger<PdfEngine>.Instance, manifest, templates);

    private static PdfTemplate Template(string source) => new("test", "v1", new Dictionary<string, byte[]> { ["main.typ"] = Encoding.UTF8.GetBytes(source) });

    private string CopyEngine(Action<string>? tamper = null)
    {
        var dir = Path.Combine(_scratch, $"engine-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        File.Copy(Path.Combine(EngineDir, "typst.exe"), Path.Combine(dir, "typst.exe"));
        tamper?.Invoke(Path.Combine(dir, "typst.exe"));
        return dir;
    }

    // ---------- readiness ----------

    [Fact]
    public async Task The_provisioned_engine_is_ready_with_the_pinned_bundle()
    {
        using var engine = Engine();
        var readiness = await engine.ReadinessAsync(default, force: true);
        Assert.True(readiness.Ready, $"{readiness.Code} {readiness.ErrorClass} {readiness.Reason}");
        Assert.StartsWith("typst-0.15.1+noto-sans-2.015+vcrt-", readiness.BundleId);
        Assert.DoesNotContain("missing", readiness.BundleId);
    }

    [Fact]
    public async Task A_missing_engine_is_unavailable_and_says_so()
    {
        using var engine = Engine(engineDir: Path.Combine(_scratch, "nowhere"));
        var r = await engine.ReadinessAsync(default, force: true);
        Assert.Equal((false, PdfErrorCodes.EngineUnavailable, "EngineMissing"), (r.Ready, r.Code, r.ErrorClass));
    }

    [Fact]
    public async Task An_engine_that_differs_from_the_approved_one_is_untrusted_and_never_run()
    {
        using var engine = Engine(engineDir: CopyEngine(exe => File.AppendAllText(exe, "x")));
        var r = await engine.ReadinessAsync(default, force: true);
        Assert.Equal((false, PdfErrorCodes.EngineUntrusted, "EngineHashMismatch"), (r.Ready, r.Code, r.ErrorClass));
    }

    [Fact]
    public async Task An_engine_that_reports_another_version_is_untrusted()
    {
        var real = PdfEngineManifest.Load();
        using var engine = Engine(manifest: real with { ExpectedVersionOutput = "typst 9.9.9 (00000000)" });
        var r = await engine.ReadinessAsync(default, force: true);
        Assert.Equal((false, PdfErrorCodes.EngineUntrusted, "EngineVersionMismatch"), (r.Ready, r.Code, r.ErrorClass));
    }

    [Fact]
    public async Task A_font_that_differs_from_the_approved_one_is_untrusted()
    {
        var fonts = Path.Combine(_scratch, "fonts");
        Directory.CreateDirectory(fonts);
        foreach (var f in Directory.GetFiles(FontDir)) File.Copy(f, Path.Combine(fonts, Path.GetFileName(f)));
        File.AppendAllText(Path.Combine(fonts, "NotoSans-Bold.ttf"), "x");
        using var engine = Engine(fontDir: fonts);
        var r = await engine.ReadinessAsync(default, force: true);
        Assert.Equal((false, PdfErrorCodes.EngineUntrusted, "FontMismatch"), (r.Ready, r.Code, r.ErrorClass));
    }

    [Fact]
    public async Task While_the_engine_is_not_ready_a_render_is_refused_with_its_reason()
    {
        using var engine = Engine(engineDir: Path.Combine(_scratch, "nowhere"));
        var e = await Assert.ThrowsAsync<PdfEngineException>(() => engine.RenderAsync("selftest", "v1", "{\"text\":\"x\"}", default));
        Assert.Equal((PdfErrorCodes.EngineUnavailable, 503), (e.Code, e.StatusCode));
    }

    // ---------- rendering ----------

    [Fact]
    public async Task A_render_returns_a_pdf_records_the_bundle_and_leaves_no_folder_behind()
    {
        using var engine = Engine();
        var result = await engine.RenderAsync("selftest", "v1", "{\"text\":\"hello\"}", default);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(result.Pdf, 0, 4));
        Assert.Contains("NotoSans", Encoding.Latin1.GetString(result.Pdf));
        Assert.Equal(engine.BundleId, result.BundleId);
        Assert.Empty(Directory.GetDirectories(WorkDir));
    }

    [Fact]
    public async Task An_unknown_template_is_refused()
    {
        using var engine = Engine();
        await engine.ReadinessAsync(default, force: true);
        Assert.Equal(PdfErrorCodes.TemplateUnknown, (await Assert.ThrowsAsync<PdfEngineException>(() => engine.RenderAsync("nope", "v1", "{}", default))).Code);
    }

    [Fact]
    public async Task A_template_cannot_read_outside_its_own_folder_and_the_failure_is_not_retried()
    {
        var secret = Path.Combine(_scratch, "secret.txt");
        await File.WriteAllTextAsync(secret, "SECRET");
        using var engine = Engine(templates: (_, _) => Template($"#read(\"{secret.Replace('\\', '/')}\")"));
        await engine.ReadinessAsync(default, force: true);
        var e = await Assert.ThrowsAsync<PdfEngineException>(() => engine.RenderAsync("test", "v1", "{}", default));
        Assert.Equal((PdfErrorCodes.RenderFailed, "TemplateError"), (e.Code, e.ErrorClass));
        Assert.DoesNotContain("SECRET", e.Message);
        Assert.Empty(Directory.GetDirectories(WorkDir));
    }

    [Fact]
    public async Task A_render_that_runs_too_long_is_killed_at_the_timeout_retried_once_and_leaves_nothing_behind()
    {
        // finite but far longer than the timeout (Typst refuses an endless loop by itself, as a template error, so that would never reach the timeout)
        using var engine = Engine(timeoutSeconds: 2, templates: (_, _) => Template("#let n = 0\n#for i in range(20000) { for j in range(20000) { n = n + 1 } }\n#n"));
        await engine.ReadinessAsync(default, force: true);
        var clock = Stopwatch.StartNew();
        var e = await Assert.ThrowsAsync<PdfEngineException>(() => engine.RenderAsync("test", "v1", "{}", default));
        Assert.Equal((PdfErrorCodes.RenderTimeout, "RenderTimeout"), (e.Code, e.ErrorClass));
        Assert.InRange(clock.Elapsed.TotalSeconds, 3.5, 20);                                                     // two attempts of about 2 seconds each
        Assert.Empty(Directory.GetDirectories(WorkDir));
        Assert.Empty(Process.GetProcessesByName("typst").Where(p => { try { return p.MainModule?.FileName?.StartsWith(EngineDir, StringComparison.OrdinalIgnoreCase) == true; } catch (System.ComponentModel.Win32Exception) { return false; } }));
    }

    [Fact]
    public async Task No_more_than_two_renders_run_at_once()
    {
        using var engine = Engine(templates: (_, _) => Template("#let n = 0\n#for i in range(3000000) { n = n + 1 }\n#n"));
        await engine.ReadinessAsync(default, force: true);
        var most = 0;
        using var stop = new CancellationTokenSource();
        var watcher = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                if (Directory.Exists(WorkDir)) most = Math.Max(most, Directory.GetDirectories(WorkDir).Length);
                await Task.Delay(20);
            }
        });
        await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => engine.RenderAsync("test", "v1", "{}", default)));
        stop.Cancel();
        await watcher;
        Assert.InRange(most, 1, 2);
    }

    // ---------- the trusted manifest ----------

    [Fact]
    public void The_embedded_manifest_carries_the_reviewed_pins()
    {
        var m = PdfEngineManifest.Load();
        Assert.Equal(("0.15.1", "typst 0.15.1 (9dfd3a08)", "081217a463adb006f8894b44227fe4b9c9e91fc85f5463d5948e8370db9bb31e", "2.015", "14.44.35211.0"),
            (m.Version, m.ExpectedVersionOutput, m.ExeSha256, m.FontsVersion, m.RuntimeVersion));
        Assert.Equal("478c558ea716033cd60c03438f628dfa75694dcf6b5f6d505a2f05fd2b4f3823", m.Fonts["NotoSans-Regular.ttf"]);
        Assert.Equal("1df075a380fc7cb898acf64c1f7b3b4dd780de3caa860178bf929de35817a913", m.Fonts["NotoSans-Bold.ttf"]);
    }

    [Fact]
    public void Every_approved_template_matches_its_pin_and_imports_no_package()
    {
        foreach (var key in PdfTemplates.LoadPins(typeof(PdfTemplates).Assembly).Keys)
        {
            var parts = key.Split('/');
            Assert.NotEmpty(PdfTemplates.Get(parts[0], parts[1]).Files);
        }
    }
}
