using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Alveara.Api.Architecture.Pdf;

/// <summary>
/// ALV-N026 S4: the reviewed, version-controlled pins of the PDF engine bundle (<c>build/pdf-engine.manifest.json</c>, embedded in this assembly at build time). The running app trusts these values
/// only - never a checksum file stored beside the artifacts (G4 review rule).
/// </summary>
public sealed record PdfEngineManifest(string Version, string ExpectedVersionOutput, string ExeSha256, string FontsVersion, IReadOnlyDictionary<string, string> Fonts, string RuntimeVersion)
{
    public const string ResourceName = "Alveara.Api.pdf-engine.manifest.json";

    public static PdfEngineManifest Load()
    {
        using var stream = typeof(PdfEngineManifest).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"The embedded PDF engine manifest ({ResourceName}) is missing from the build.");
        using var json = JsonDocument.Parse(stream);
        var root = json.RootElement;
        var engine = root.GetProperty("engine");
        var fonts = root.GetProperty("fonts");
        return new PdfEngineManifest(
            engine.GetProperty("version").GetString()!, engine.GetProperty("expectedVersionOutput").GetString()!, engine.GetProperty("exeSha256").GetString()!,
            fonts.GetProperty("version").GetString()!, fonts.GetProperty("files").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!),
            root.GetProperty("msvcRuntime").GetProperty("version").GetString()!);
    }
}

/// <summary>One approved template version: its files (name to content) as embedded in this assembly, already checked against their pinned hashes.</summary>
public sealed record PdfTemplate(string Id, string Version, IReadOnlyDictionary<string, byte[]> Files);

/// <summary>
/// ALV-N026 S4: the approved templates. Each version's files are embedded in this assembly (so they cannot be changed on the server) and pinned by hash in
/// <c>Pdf/Templates/templates.manifest.json</c>; a file whose content does not match its pin, or any external package import (<c>#import "@...</c>), refuses the template. A caller names a template by
/// id and version only; it never supplies template text.
/// </summary>
public static partial class PdfTemplates
{
    private const string Prefix = "Alveara.Api.PdfTemplates.";

    [GeneratedRegex("#import\\s+\"@")]
    private static partial Regex PackageImport();

    public static PdfTemplate Get(string id, string version)
    {
        var assembly = typeof(PdfTemplates).Assembly;
        var pins = LoadPins(assembly);
        if (!pins.TryGetValue($"{id}/{version}", out var files)) throw new PdfEngineException(PdfErrorCodes.TemplateUnknown, $"No approved template {id} {version}.", "ContractViolation");
        var content = new Dictionary<string, byte[]>();
        foreach (var (name, sha) in files)
        {
            using var stream = assembly.GetManifestResourceStream($"{Prefix}{id}.{version}.{name}")
                ?? throw new PdfEngineException(PdfErrorCodes.TemplateUntrusted, $"Template {id} {version} is missing {name}.", "ContractViolation");
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            var bytes = buffer.ToArray();
            if (!string.Equals(Convert.ToHexStringLower(SHA256.HashData(bytes)), sha, StringComparison.Ordinal))
                throw new PdfEngineException(PdfErrorCodes.TemplateUntrusted, $"Template {id} {version} file {name} does not match its pinned hash.", "ContractViolation");
            if (name.EndsWith(".typ", StringComparison.Ordinal) && PackageImport().IsMatch(Encoding.UTF8.GetString(bytes)))
                throw new PdfEngineException(PdfErrorCodes.TemplateUntrusted, $"Template {id} {version} file {name} imports an external package.", "ContractViolation");
            content[name] = bytes;
        }
        if (!content.ContainsKey("main.typ")) throw new PdfEngineException(PdfErrorCodes.TemplateUntrusted, $"Template {id} {version} has no main.typ.", "ContractViolation");
        return new PdfTemplate(id, version, content);
    }

    /// <summary>The pinned files of every approved template version, keyed "id/version".</summary>
    public static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> LoadPins(Assembly assembly)
    {
        using var stream = assembly.GetManifestResourceStream(Prefix + "templates.manifest.json")
            ?? throw new InvalidOperationException("The embedded PDF template manifest is missing from the build.");
        using var json = JsonDocument.Parse(stream);
        return json.RootElement.GetProperty("templates").EnumerateObject()
            .ToDictionary(t => t.Name, t => (IReadOnlyDictionary<string, string>)t.Value.EnumerateObject().ToDictionary(f => f.Name, f => f.Value.GetString()!));
    }
}

/// <summary>Stable error codes of the PDF engine. All but a template problem mean "PDFs are unavailable right now"; the rest of the application is unaffected.</summary>
public static class PdfErrorCodes
{
    public const string EngineUnavailable = "pdf_engine_unavailable";
    public const string EngineUntrusted = "pdf_engine_untrusted";
    public const string RenderFailed = "pdf_render_failed";
    public const string RenderTimeout = "pdf_render_timeout";
    public const string TemplateUnknown = "pdf_template_unknown";
    public const string TemplateUntrusted = "pdf_template_untrusted";
}

/// <summary>A PDF could not be produced. <see cref="ErrorClass"/> is the stable class logged with it; the message never carries patient data or engine output.</summary>
public sealed class PdfEngineException(string code, string message, string errorClass, int statusCode = 503) : Exception(message)
{
    public string Code { get; } = code;
    public string ErrorClass { get; } = errorClass;
    public int StatusCode { get; } = statusCode;
}
