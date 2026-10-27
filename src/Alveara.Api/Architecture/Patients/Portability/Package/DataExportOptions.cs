namespace Alveara.Api.Architecture.Patients.Portability.Package;

/// <summary>
/// ALV-018-C01: the settings of the export package, bound from the <c>DataExport</c> configuration section. Every value has a safe default, so nothing needs configuring. The limits keep one request from taking the
/// server down: an all-patients export is refused when it is over either limit (it never silently stops short), see <c>PatientDataPackageService</c>.
/// </summary>
public sealed class DataExportOptions
{
    public const string SectionName = "DataExport";

    /// <summary>The most patients one export may carry.</summary>
    public int MaxPatients { get; set; } = 5_000;

    /// <summary>The most bytes one package may hold (2 GB).</summary>
    public long MaxPackageBytes { get; set; } = 2L * 1024 * 1024 * 1024;

    /// <summary>Where the package is assembled while it is built. Files here hold patient data, so each is deleted when it is closed, on success and on failure; empty means the system temporary folder.</summary>
    public string? TempDirectory { get; set; }

    /// <summary>How long one database read of a table may take, in seconds. No read waits without a limit.</summary>
    public int ReadTimeoutSeconds { get; set; } = 120;

    /// <summary>The practice's default for naming each patient's folder in a package: <c>IdOnly</c> (the default, nothing about the patient shows in a file listing) or <c>NameAndId</c>. An export may choose the other one for itself.</summary>
    public string DefaultFolderNaming { get; set; } = FolderNaming.IdOnly;

    public string ResolveTempDirectory() => string.IsNullOrWhiteSpace(TempDirectory) ? Path.GetTempPath() : TempDirectory;

    /// <summary>A new temporary file in <see cref="ResolveTempDirectory"/> that is deleted when it is closed, so patient data is never left on disk.</summary>
    public FileStream OpenTempFile(Guid exportId, string name) =>
        new(Path.Combine(ResolveTempDirectory(), $"alveara-export-{exportId:N}-{name}-{Guid.NewGuid():N}.tmp"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920,
            FileOptions.DeleteOnClose | FileOptions.Asynchronous | FileOptions.SequentialScan);
}
