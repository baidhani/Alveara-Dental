using System.Text;

namespace Alveara.Api.Architecture.Patients.Portability.Package;

/// <summary>How a patient's folder inside a package is named: by the patient's id alone (the default; nothing about the patient shows in a file listing), or by the patient's name and id.</summary>
public static class FolderNaming
{
    public const string IdOnly = "IdOnly";
    public const string NameAndId = "NameAndId";

    public static bool IsValid(string? mode) => mode is IdOnly or NameAndId;

    /// <summary>The mode itself, or an <see cref="ArgumentException"/> naming the two allowed values (a misspelt setting must fail loudly, never fall back to the more revealing mode).</summary>
    public static string Require(string? mode) =>
        IsValid(mode) ? mode! : throw new ArgumentException($"Folder naming must be '{IdOnly}' or '{NameAndId}'.", nameof(mode));
}

/// <summary>
/// ALV-018-C01: the names of the folders and files inside a package, built so that nothing a user typed can reach outside its folder or break an unzip on Windows. Patient folders are the patient's id, or
/// <c>Last-First_id</c> when names are asked for: the name part keeps letters and digits (accents included, ZIP names are UTF-8), turns spaces and separators into single hyphens, drops everything else and is capped, and
/// the id always stays on the end, so two patients with the same name never share a folder and a folder always maps back to its record. A document's file name is the name it was imported under with any folder part,
/// control or reserved character, device name and trailing dot removed and a length cap that keeps the extension. Pure and deterministic: the same input always gives the same name.
/// </summary>
public static class PackagePaths
{
    private const int MaxNamePartLength = 40;
    private const int MaxFileNameLength = 100;
    private static readonly HashSet<string> DeviceNames = new(["CON", "PRN", "AUX", "NUL", .. Enumerable.Range(1, 9).SelectMany(n => new[] { $"COM{n}", $"LPT{n}" })], StringComparer.OrdinalIgnoreCase);

    public static string PatientFolder(Guid patientId, string? firstName, string? lastName, string mode)
    {
        if (FolderNaming.Require(mode) == FolderNaming.IdOnly) return patientId.ToString("D");
        var name = Slug($"{lastName} {firstName}", MaxNamePartLength);
        return name.Length == 0 ? patientId.ToString("D") : $"{name}_{patientId:D}";
    }

    public static string DocumentFileName(string? original)
    {
        var name = new StringBuilder();
        foreach (var ch in (original ?? "").Replace('\\', '/').Split('/')[^1].Normalize(NormalizationForm.FormC))
            if (!char.IsControl(ch) && "\"<>:*?|".IndexOf(ch) < 0) name.Append(ch);
        var cleaned = name.ToString().Trim().TrimEnd('.', ' ').TrimStart('.');                           // no hidden or relative names, no trailing dot or space (Windows drops them)
        if (cleaned.Length == 0) return "file";
        if (cleaned.Length > MaxFileNameLength)
        {
            var dot = cleaned.LastIndexOf('.');
            var extension = dot >= cleaned.Length - 10 && dot > 0 ? cleaned[dot..] : "";
            cleaned = cleaned[..(MaxFileNameLength - extension.Length)].TrimEnd('.', ' ') + extension;
        }
        return DeviceNames.Contains(cleaned.Split('.')[0]) ? "_" + cleaned : cleaned;
    }

    private static string Slug(string text, int max)
    {
        var slug = new StringBuilder();
        foreach (var ch in text.Normalize(NormalizationForm.FormC))
        {
            if (char.IsLetterOrDigit(ch)) slug.Append(ch);
            else if (char.IsWhiteSpace(ch) || ch is '-' or '_' or '.')
            {
                if (slug.Length > 0 && slug[^1] != '-') slug.Append('-');
            }
            // anything else (quotes, slashes, colons, control characters) is dropped
        }
        var result = slug.ToString().Trim('-');
        return result.Length > max ? result[..max].Trim('-') : result;
    }
}
