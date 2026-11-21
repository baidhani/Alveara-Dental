using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Alveara.Api.Architecture.Pdf;

/// <summary>ALV-N026 S4: where the provisioned engine and fonts are, and the render limits. Set by the installer in production (ALV-N013); by Install-PdfEngine.ps1 for dev and tests.</summary>
public sealed class PdfOptions
{
    public const string SectionName = "Pdf";
    /// <summary>The folder holding typst.exe (and nothing else of the engine).</summary>
    public string? EngineDirectory { get; set; }
    /// <summary>The folder holding the pinned fonts; the only fonts a render may use.</summary>
    public string? FontDirectory { get; set; }
    /// <summary>Where per-render folders are made (each deleted after its render). Default: a dk-pdf folder in the system temporary folder.</summary>
    public string? WorkDirectory { get; set; }
    public int TimeoutSeconds { get; set; } = 30;
    public int MaxConcurrentRenders { get; set; } = 2;
    /// <summary>A readiness result older than this is checked again before the next render.</summary>
    public int ReadinessMaxAgeSeconds { get; set; } = 300;
}

/// <summary>The result of the last readiness check: whether PDFs can be made now, and if not the stable reason; the bundle that would make them.</summary>
public sealed record PdfReadiness(bool Ready, string? Code, string? ErrorClass, string? Reason, string BundleId, DateTimeOffset CheckedAtUtc);

public sealed record PdfRenderResult(byte[] Pdf, string BundleId);

/// <summary>
/// ALV-N026 S4: the server-side PDF engine (proposal R05 with its R04 base; isolation as approved).
/// <list type="bullet">
/// <item><b>Readiness</b> (at startup and whenever the last result is older than <see cref="PdfOptions.ReadinessMaxAgeSeconds"/> or was a failure): typst.exe must match the reviewed manifest's hash
/// (<c>pdf_engine_untrusted</c> otherwise), start, exit 0 and print exactly the expected version, and render the built-in self-test with the pinned fonts (<c>pdf_engine_unavailable</c> otherwise).
/// No fallback engine; nothing else in the application depends on it.</item>
/// <item><b>Each render</b>: a fresh folder is the only root (the template's files and <c>input.json</c>); fonts only from <see cref="PdfOptions.FontDirectory"/>; no system or embedded fonts; an empty
/// package folder and every proxy pointed at a closed local address; risky variables removed; arguments as a list, no shell; a timeout, then the whole process tree is killed; one retry for a
/// crash or a timeout only; at most <see cref="PdfOptions.MaxConcurrentRenders"/> at once; the PDF is returned in memory and the folder deleted whatever happens.</item>
/// <item><b>Logs</b> carry the error class, the template id and version and the duration - never the input, the engine's output text or a path.</item>
/// </list>
/// Not handled here: installing the engine or the Microsoft runtime (ALV-N013), operating-system confinement (service account and folder permissions, ALV-N013).
/// </summary>
public sealed class PdfEngine : IDisposable
{
    public const string ClosedProxy = "http://127.0.0.1:9";
    private static readonly string[] RemovedVariables = ["TYPST_FONT_PATHS", "TYPST_ROOT", "TYPST_IGNORE_SYSTEM_FONTS", "TYPST_IGNORE_EMBEDDED_FONTS", "SOURCE_DATE_EPOCH", "NO_PROXY", "no_proxy"];
    private static readonly string[] ProxyVariables = ["HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY", "http_proxy", "https_proxy", "all_proxy"];

    private readonly PdfOptions _options;
    private readonly PdfEngineManifest _manifest;
    private readonly ILogger<PdfEngine> _log;
    private readonly SemaphoreSlim _renders;
    private readonly SemaphoreSlim _readinessGate = new(1, 1);
    private readonly Func<string, string, PdfTemplate> _templates;
    private PdfReadiness? _readiness;

    /// <param name="templates">Where approved templates come from: the pinned, embedded ones (<see cref="PdfTemplates.Get"/>) unless a test supplies its own. The readiness self-test always uses the embedded one.</param>
    public PdfEngine(IOptions<PdfOptions> options, ILogger<PdfEngine> log, PdfEngineManifest? manifest = null, Func<string, string, PdfTemplate>? templates = null)
    {
        _options = options.Value;
        _templates = templates ?? PdfTemplates.Get;
        _manifest = manifest ?? PdfEngineManifest.Load();
        _log = log;
        _renders = new SemaphoreSlim(Math.Max(1, _options.MaxConcurrentRenders));
    }

    public PdfReadiness? LastReadiness => _readiness;

    /// <summary>The engine, fonts and Microsoft runtime versions a PDF is made with; recorded with every issued document.</summary>
    public string BundleId => $"typst-{_manifest.Version}+noto-sans-{_manifest.FontsVersion}+vcrt-{RuntimeVersion()}";

    private string RuntimeVersion()
    {
        try
        {
            var path = Path.Combine(Environment.SystemDirectory, "vcruntime140.dll");
            return File.Exists(path) ? FileVersionInfo.GetVersionInfo(path).FileVersion ?? "unknown" : "missing";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "unknown";
        }
    }

    /// <summary>Checks the engine now, or returns the last result while it is fresh and was a success.</summary>
    public async Task<PdfReadiness> ReadinessAsync(CancellationToken ct, bool force = false)
    {
        var last = _readiness;
        if (!force && last is { Ready: true } && DateTimeOffset.UtcNow - last.CheckedAtUtc < TimeSpan.FromSeconds(_options.ReadinessMaxAgeSeconds)) return last;
        await _readinessGate.WaitAsync(ct);
        try
        {
            last = _readiness;
            if (!force && last is { Ready: true } && DateTimeOffset.UtcNow - last.CheckedAtUtc < TimeSpan.FromSeconds(_options.ReadinessMaxAgeSeconds)) return last;
            var result = await CheckAsync(ct);
            _readiness = result;
            if (result.Ready) _log.LogInformation("event=pdf_engine_ready outcome=success bundle={Bundle}", result.BundleId);
            else _log.LogError("event=pdf_engine_not_ready outcome=failure code={Code} error_class={ErrorClass} bundle={Bundle}", result.Code, result.ErrorClass, result.BundleId);
            return result;
        }
        finally { _readinessGate.Release(); }
    }

    private async Task<PdfReadiness> CheckAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        PdfReadiness Fail(string code, string errorClass, string reason) => new(false, code, errorClass, reason, BundleId, now);
        var exe = EnginePath();
        if (exe is null || !File.Exists(exe)) return Fail(PdfErrorCodes.EngineUnavailable, "EngineMissing", "The PDF engine is not installed.");
        if (string.IsNullOrWhiteSpace(_options.FontDirectory) || !Directory.Exists(_options.FontDirectory)) return Fail(PdfErrorCodes.EngineUnavailable, "FontsMissing", "The PDF fonts are not installed.");
        foreach (var (name, sha) in _manifest.Fonts)
        {
            var font = Path.Combine(_options.FontDirectory, name);
            if (!File.Exists(font) || !string.Equals(await HashAsync(font, ct), sha, StringComparison.Ordinal))
                return Fail(PdfErrorCodes.EngineUntrusted, "FontMismatch", "A PDF font does not match the approved font.");
        }
        if (!string.Equals(await HashAsync(exe, ct), _manifest.ExeSha256, StringComparison.Ordinal))
            return Fail(PdfErrorCodes.EngineUntrusted, "EngineHashMismatch", "The PDF engine does not match the approved engine.");

        var version = await RunAsync(exe, ["--version"], null, null, ct);
        if (version.StartFailed) return Fail(PdfErrorCodes.EngineUnavailable, "EngineStartRefused", "The PDF engine could not be started.");
        if (version.TimedOut) return Fail(PdfErrorCodes.EngineUnavailable, "EngineTimeout", "The PDF engine did not answer in time.");
        if (version.ExitCode != 0) return Fail(PdfErrorCodes.EngineUnavailable, "EngineStartFailed", "The PDF engine did not start (is the Microsoft Visual C++ runtime installed?).");
        if (!string.Equals(version.StdOut.Trim(), _manifest.ExpectedVersionOutput, StringComparison.Ordinal))
            return Fail(PdfErrorCodes.EngineUntrusted, "EngineVersionMismatch", "The PDF engine reports an unexpected version.");

        try
        {
            var selfTest = await RenderCoreAsync(PdfTemplates.Get("selftest", "v1"), "{\"text\":\"ready\"}", ct, retry: false);
            if (!Encoding.Latin1.GetString(selfTest).Contains("NotoSans", StringComparison.Ordinal))
                return Fail(PdfErrorCodes.EngineUnavailable, "FontNotUsed", "The PDF engine did not use the approved font.");
        }
        catch (PdfEngineException ex)
        {
            return Fail(PdfErrorCodes.EngineUnavailable, ex.ErrorClass, "The PDF engine could not render its self-test.");
        }
        return new PdfReadiness(true, null, null, null, BundleId, now);
    }

    /// <summary>Renders an approved template with <paramref name="inputJson"/> as its only data. Refuses (503) while the engine is not ready.</summary>
    public async Task<PdfRenderResult> RenderAsync(string templateId, string templateVersion, string inputJson, CancellationToken ct)
    {
        var readiness = await ReadinessAsync(ct);
        if (!readiness.Ready) throw new PdfEngineException(readiness.Code!, "PDFs cannot be produced right now. The rest of the application is unaffected; ask the practice administrator to check System Status.", readiness.ErrorClass!);
        var template = _templates(templateId, templateVersion);
        var started = Stopwatch.GetTimestamp();
        try
        {
            var pdf = await RenderCoreAsync(template, inputJson, ct, retry: true);
            _log.LogInformation("event=pdf_rendered outcome=success template={Template} version={Version} bytes={Bytes} duration_ms={Ms}", templateId, templateVersion, pdf.Length, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            return new PdfRenderResult(pdf, readiness.BundleId);
        }
        catch (PdfEngineException ex)
        {
            _log.LogError("event=pdf_render_failed outcome=failure code={Code} error_class={ErrorClass} template={Template} version={Version} duration_ms={Ms}", ex.Code, ex.ErrorClass, templateId, templateVersion, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            throw;
        }
    }

    private async Task<byte[]> RenderCoreAsync(PdfTemplate template, string inputJson, CancellationToken ct, bool retry)
    {
        await _renders.WaitAsync(ct);
        try
        {
            for (var attempt = 1; ; attempt++)
            {
                var (pdf, transient, failure) = await RenderOnceAsync(template, inputJson, ct);
                if (pdf is not null) return pdf;
                if (!(retry && transient && attempt == 1)) throw failure!;
                _log.LogWarning("event=pdf_render_retry error_class={ErrorClass} template={Template} version={Version}", failure!.ErrorClass, template.Id, template.Version);
            }
        }
        finally { _renders.Release(); }
    }

    /// <summary>One render in its own folder. A crash or a timeout is transient (retried once); a template or input error is not.</summary>
    private async Task<(byte[]? Pdf, bool Transient, PdfEngineException? Failure)> RenderOnceAsync(PdfTemplate template, string inputJson, CancellationToken ct)
    {
        var workRoot = string.IsNullOrWhiteSpace(_options.WorkDirectory) ? Path.Combine(Path.GetTempPath(), "dk-pdf") : _options.WorkDirectory;
        var folder = Path.Combine(workRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            foreach (var (name, bytes) in template.Files) await File.WriteAllBytesAsync(Path.Combine(folder, name), bytes, ct);
            await File.WriteAllTextAsync(Path.Combine(folder, "input.json"), inputJson, new UTF8Encoding(false), ct);
            var packages = Path.Combine(folder, "no-packages");
            Directory.CreateDirectory(packages);
            var output = Path.Combine(folder, "out.pdf");
            var run = await RunAsync(EnginePath()!, ["compile", "--root", folder, "--ignore-system-fonts", "--ignore-embedded-fonts", "--font-path", _options.FontDirectory!,
                "--pdf-standard", "a-2b", Path.Combine(folder, "main.typ"), output], packages, folder, ct);
            if (run.StartFailed) return (null, true, new PdfEngineException(PdfErrorCodes.EngineUnavailable, "The PDF engine could not be started.", "EngineStartRefused"));
            if (run.TimedOut) return (null, true, new PdfEngineException(PdfErrorCodes.RenderTimeout, "The document took too long to produce.", "RenderTimeout"));
            if (run.ExitCode != 0)
            {
                var typstError = run.StdErr.Split('\n').Any(l => l.StartsWith("error", StringComparison.Ordinal));
                return (null, !typstError, new PdfEngineException(PdfErrorCodes.RenderFailed, "The document could not be produced.", typstError ? "TemplateError" : "EngineCrashed", 500));
            }
            if (!File.Exists(output)) return (null, true, new PdfEngineException(PdfErrorCodes.RenderFailed, "The document could not be produced.", "NoOutput", 500));
            return (await File.ReadAllBytesAsync(output, ct), false, null);
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _log.LogError("event=pdf_work_folder_not_deleted outcome=failure error_class={ErrorClass}", ex.GetType().Name);
            }
        }
    }

    private sealed record RunResult(bool StartFailed, bool TimedOut, int ExitCode, string StdOut, string StdErr);

    private async Task<RunResult> RunAsync(string exe, IReadOnlyList<string> arguments, string? packages, string? workingDirectory, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var a in arguments) psi.ArgumentList.Add(a);
        if (workingDirectory is not null) psi.WorkingDirectory = workingDirectory;
        foreach (var v in RemovedVariables) psi.Environment.Remove(v);
        foreach (var v in ProxyVariables) psi.Environment[v] = ClosedProxy;
        if (packages is not null) { psi.Environment["TYPST_PACKAGE_PATH"] = packages; psi.Environment["TYPST_PACKAGE_CACHE_PATH"] = packages; }

        Process process;
        try { process = Process.Start(psi) ?? throw new InvalidOperationException("no process"); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { return new RunResult(true, false, -1, "", ""); }
        using (process)
        {
            var stdout = process.StandardOutput.ReadToEndAsync(ct);
            var stderr = process.StandardError.ReadToEndAsync(ct);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _options.TimeoutSeconds)));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { _log.LogDebug("event=pdf_process_already_ended"); }   // it ended between the timeout and the kill
                return new RunResult(false, true, -1, "", "");
            }
            return new RunResult(false, false, process.ExitCode, await stdout, await stderr);
        }
    }

    private string? EnginePath() => string.IsNullOrWhiteSpace(_options.EngineDirectory) ? null : Path.Combine(_options.EngineDirectory, "typst.exe");

    private static async Task<string> HashAsync(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, ct));
    }

    public void Dispose() { _renders.Dispose(); _readinessGate.Dispose(); }
}

/// <summary>ALV-N026 S4: checks the engine once when the application starts, so System Status and the log say at once whether PDFs can be made. A failure is logged, never fatal.</summary>
public sealed class PdfEngineStartupCheck(PdfEngine engine, ILogger<PdfEngineStartupCheck> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await engine.ReadinessAsync(stoppingToken, force: true); }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }   // shutting down before the check finished
        catch (Exception ex)
        {
            log.LogError("event=pdf_engine_startup_check_failed outcome=failure error_class={ErrorClass}", ex.GetType().Name);
        }
    }
}
