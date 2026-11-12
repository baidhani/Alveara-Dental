namespace Alveara.Api.Architecture.LoginPresentation;

/// <summary>ALV-N020-C01: how the selected background fills the sign-in page. Tile repeats at natural size; Stretch fills exactly and may distort; Fill covers and keeps proportions.</summary>
public static class LoginDisplayModes
{
    public const string Tile = "Tile";
    public const string Stretch = "Stretch";
    public const string Fill = "Fill";
    public static readonly IReadOnlyList<string> All = [Tile, Stretch, Fill];
}

public static class LoginBackgroundKinds
{
    public const string Bundled = "Bundled";
    public const string Added = "Added";
    public static readonly IReadOnlyList<string> All = [Bundled, Added];
}

/// <summary>
/// The one sign-in presentation row: which background (a bundled key or an added image), its display mode, the optional custom footer notice (null = the deployment
/// default) and whether the notice is shown. Optimistic concurrency through <see cref="RowVersion"/>.
/// </summary>
public class LoginPresentationSettings
{
    public static readonly Guid SingletonId = new("00000000-0000-0000-0000-00000000c01a");

    public Guid Id { get; set; } = SingletonId;
    public required string BackgroundKind { get; set; }
    public string? BundledKey { get; set; }
    public Guid? AddedImageId { get; set; }
    public required string DisplayMode { get; set; }
    public string? FooterNoticeText { get; set; }
    public bool ShowFooterNotice { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public Guid? UpdatedByUserAccountId { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>
/// An administrator-added background: only the server-generated, metadata-free derivative is stored (under <see cref="StorageName"/>, a server-chosen name); the
/// original upload and its file name are never kept. Inactivated, never deleted (a database trigger refuses deletes and changes to the stored facts).
/// </summary>
public class LoginBackgroundImage
{
    public Guid Id { get; set; }
    public required string DisplayName { get; set; }
    /// <summary>SHA-256 of the derivative, lower-case hex; unique (duplicate uploads are refused).</summary>
    public required string Sha256 { get; set; }
    public required string ContentType { get; set; }
    public required string Extension { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public long SizeBytes { get; set; }
    public required string StorageName { get; set; }
    public required string SuggestedDisplayMode { get; set; }
    public bool IsActive { get; set; }
    public Guid AddedByUserAccountId { get; set; }
    public DateTimeOffset AddedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
