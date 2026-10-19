using System.Buffers.Binary;
using System.Globalization;

namespace Alveara.Api.Architecture.Documents;

/// <summary>Stable error codes a document import can return (see docs/DOCUMENTS.md).</summary>
public static class DocumentErrorCodes
{
    public const string FileEmpty = "file_empty";
    public const string FileTooLarge = "file_too_large";
    public const string FileTypeBlocked = "file_type_blocked";
    public const string FileTypeMismatch = "file_type_mismatch";
    public const string ExecutableContent = "executable_content";
    public const string FileNameInvalid = "file_name_invalid";
    public const string MetadataInvalid = "metadata_invalid";
}

/// <summary>What the first bytes of a file say it is. Only the types this story needs to recognize.</summary>
public enum DetectedContent { Unknown, Pdf, Png, Jpeg, Tiff, WindowsExecutable }

/// <summary>A normalized original filename (kept as metadata only) and its lower-case final extension without the dot, or an error code.</summary>
public readonly record struct FileNameResult(string? Name, string? Extension, string? Error)
{
    public bool IsOk => Error is null;
}

/// <summary>A normalized extension an admin wants to block, or an error code.</summary>
public readonly record struct ExtensionResult(string? Extension, string? Error)
{
    public bool IsOk => Error is null;
}

/// <summary>What the server stores about a file's type, decided from its content and never from the client.</summary>
public sealed record StoredTypeInfo(string DetectedType, bool Verified, bool InlineSafe, string ContentType);

/// <summary>
/// The pure rules for what may be imported (docs/DOCUMENTS.md, "What happens to a file on import"): filename normalization, the blocked-extension check,
/// content detection, the extension-against-content check, the size rules. No I/O, no database: every rule is testable on its own.
/// </summary>
public static class DocumentFileRules
{
    public const int MaxFileNameLength = 255;
    public const long MinUploadLimitBytes = 1_048_576;
    public const long MaxUploadLimitBytes = 1_073_741_824;
    public const long DefaultMaxUploadBytes = 104_857_600;

    /// <summary>
    /// How many leading bytes content detection needs. A Windows program whose PE header sits beyond this many bytes (very unusual) is not recognized by content;
    /// the extension rule still blocks it when it is named like one. A documented limit, not a silent one.
    /// </summary>
    public const int HeaderBytesToRead = 4096;

    private const int PdfHeaderWindow = 1024;
    private const int MaxExtensionLength = 16;

    private static readonly HashSet<string> BuiltIn = new(StringComparer.OrdinalIgnoreCase)
    {
        "exe", "html", "js",                                                                   // the three the owner named
        "htm", "bat", "cmd", "com", "msi", "scr", "ps1", "vbs", "dll", "jar", "hta", "lnk",    // close relatives: things a person can run or a browser can execute
    };

    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    private static readonly char[] WindowsInvalidNameCharacters = ['<', '>', '"', '|', '?', '*'];

    /// <summary>The built-in blocked extensions: the three the owner named (.exe .html .js) and twelve close relatives. Locked: configuration can add to the list, never remove these.</summary>
    public static IReadOnlySet<string> BuiltInBlockedExtensions => BuiltIn;

    /// <summary>Normalizes a user-supplied filename (path parts, trailing dots and spaces and a ":stream" suffix removed; control, format and Windows-invalid characters and reserved device names refused).</summary>
    public static FileNameResult NormalizeFileName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return Invalid();

        // Control characters (a NUL, a tab, a newline) and format characters (a right-to-left override that makes "exe" display as "pdf", a zero-width space) are never part of a usable name.
        foreach (var c in raw)
            if (char.IsControl(c) || CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.Format) return Invalid();

        // Only the last path part is the name (both separators: a Windows client may send a full path). A folder that came with it is never used for anything.
        var name = raw[(raw.LastIndexOfAny(['/', '\\']) + 1)..];

        // An NTFS alternate-data-stream suffix ("scan.pdf:evil.exe", "file.pdf::$DATA") is not part of the name.
        var colon = name.IndexOf(':');
        if (colon >= 0) name = name[..colon];

        name = name.Trim().TrimEnd('.', ' ');
        if (name.Length == 0 || name.Length > MaxFileNameLength) return Invalid();
        if (name.IndexOfAny(WindowsInvalidNameCharacters) >= 0) return Invalid();

        // "con", "con.pdf" and "COM1.tar.gz" all name a Windows device, whatever follows the first dot.
        var firstDot = name.IndexOf('.');
        var stem = firstDot < 0 ? name : name[..firstDot];
        if (ReservedDeviceNames.Contains(stem.Trim())) return Invalid();

        var lastDot = name.LastIndexOf('.');
        var extension = lastDot < 0 ? string.Empty : name[(lastDot + 1)..].ToLowerInvariant();
        return new FileNameResult(name, extension, null);

        static FileNameResult Invalid() => new(null, null, DocumentErrorCodes.FileNameInvalid);
    }

    /// <summary>The error code when the extension is on the blocked list (compared case-insensitively), otherwise null.</summary>
    public static string? CheckBlocked(string? extension, IReadOnlySet<string> blocked)
    {
        if (string.IsNullOrWhiteSpace(extension)) return null;
        var normalized = extension.Trim().TrimStart('.').ToLowerInvariant();
        return normalized.Length > 0 && blocked.Contains(normalized) ? DocumentErrorCodes.FileTypeBlocked : null;
    }

    /// <summary>Recognizes a file from its first bytes (a Windows program, PDF, PNG, JPEG or TIFF), or Unknown.</summary>
    public static DetectedContent DetectContent(ReadOnlySpan<byte> header)
    {
        if (IsWindowsProgram(header)) return DetectedContent.WindowsExecutable;

        if (header.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) return DetectedContent.Png;
        if (header.StartsWith(new byte[] { 0xFF, 0xD8, 0xFF })) return DetectedContent.Jpeg;
        if (header.StartsWith(new byte[] { (byte)'I', (byte)'I', 0x2A, 0x00 }) || header.StartsWith(new byte[] { (byte)'M', (byte)'M', 0x00, 0x2A })       // TIFF, little and big endian
            || header.StartsWith(new byte[] { (byte)'I', (byte)'I', 0x2B, 0x00 }) || header.StartsWith(new byte[] { (byte)'M', (byte)'M', 0x00, 0x2B }))   // BigTIFF
            return DetectedContent.Tiff;

        // A PDF may carry a little junk before its header; the specification allows the header anywhere in the first 1024 bytes. A version digit must follow ("%PDF-1.7").
        var window = header[..Math.Min(header.Length, PdfHeaderWindow)];
        var at = window.IndexOf("%PDF-"u8);
        if (at >= 0 && at + 5 < window.Length && window[at + 5] is >= (byte)'0' and <= (byte)'9') return DetectedContent.Pdf;

        return DetectedContent.Unknown;
    }

    private static bool IsWindowsProgram(ReadOnlySpan<byte> header)
    {
        // A DOS header ("MZ", 0x40 bytes) whose pointer at 0x3C leads to the "PE\0\0" signature inside what was read. Text that only starts with "MZ" does not have both.
        if (header.Length < 0x40 || header[0] != (byte)'M' || header[1] != (byte)'Z') return false;
        var pointer = BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(0x3C, 4));
        if (pointer < 0x40 || pointer > (uint)(header.Length - 4)) return false;
        return header.Slice((int)pointer, 4).SequenceEqual(new byte[] { (byte)'P', (byte)'E', 0, 0 });
    }

    /// <summary>The error code when the content is a Windows program, or when an extension that claims PDF, PNG, JPEG or TIFF does not match the content; otherwise null.</summary>
    public static string? CheckContent(string? extension, DetectedContent detected)
    {
        if (detected == DetectedContent.WindowsExecutable) return DocumentErrorCodes.ExecutableContent;

        var claimed = (extension ?? string.Empty).Trim().TrimStart('.').ToLowerInvariant() switch
        {
            "pdf" => DetectedContent.Pdf,
            "png" => DetectedContent.Png,
            "jpg" or "jpeg" => DetectedContent.Jpeg,
            "tif" or "tiff" => DetectedContent.Tiff,
            _ => DetectedContent.Unknown,                                  // any other type is accepted as "type not verified"
        };
        return claimed != DetectedContent.Unknown && claimed != detected ? DocumentErrorCodes.FileTypeMismatch : null;
    }

    /// <summary>The type the server stores and how the file may be served, from the content alone.</summary>
    public static StoredTypeInfo Classify(DetectedContent detected) => detected switch
    {
        DetectedContent.Pdf => new StoredTypeInfo("pdf", true, true, "application/pdf"),
        DetectedContent.Png => new StoredTypeInfo("png", true, true, "image/png"),
        DetectedContent.Jpeg => new StoredTypeInfo("jpeg", true, true, "image/jpeg"),
        DetectedContent.Tiff => new StoredTypeInfo("tiff", true, false, "image/tiff"),            // browsers do not display TIFF: always a download
        DetectedContent.Unknown => new StoredTypeInfo("other", false, false, "application/octet-stream"),
        _ => throw new InvalidOperationException("A Windows program is refused before anything is stored, so it has no stored type."),
    };

    /// <summary>The error code for an empty file or a file over the maximum, otherwise null.</summary>
    public static string? CheckSize(long sizeBytes, long maxBytes)
    {
        if (sizeBytes <= 0) return DocumentErrorCodes.FileEmpty;
        return sizeBytes > maxBytes ? DocumentErrorCodes.FileTooLarge : null;
    }

    /// <summary>The error code when a configured maximum upload size is outside 1 MB to 1 GB, otherwise null.</summary>
    public static string? ValidateMaxUploadBytes(long maxBytes) =>
        maxBytes is < MinUploadLimitBytes or > MaxUploadLimitBytes ? DocumentErrorCodes.MetadataInvalid : null;

    /// <summary>Normalizes an extension an admin wants to block (lower case, no leading dot, letters and digits only, 1 to 16 characters).</summary>
    public static ExtensionResult NormalizeBlockedExtensionInput(string? raw)
    {
        var text = (raw ?? string.Empty).Trim().TrimStart('.');
        if (text.Length is 0 or > MaxExtensionLength) return new ExtensionResult(null, DocumentErrorCodes.MetadataInvalid);
        foreach (var c in text)
            if (!(c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9')) return new ExtensionResult(null, DocumentErrorCodes.MetadataInvalid);
        return new ExtensionResult(text.ToLowerInvariant(), null);
    }
}
