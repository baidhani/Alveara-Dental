namespace Alveara.Api.Architecture.Imports;

/// <summary>Limits shared by the file checks, the service and the database (the label and the column names are sized to their columns).</summary>
public static class ImportLimits
{
    public const int LabelMax = 100;
    public const int ColumnNameMax = 100;
    /// <summary>The most problems one rejected file lists; the rest are counted, not listed, so a hopeless file cannot produce an unbounded answer.</summary>
    public const int MaxProblemsListed = 100;
}

/// <summary>
/// ALV-018-C02: the settings of bulk import, bound from the <c>DataImport</c> configuration section. Every value has a safe default, so nothing needs configuring. The limits keep one request from taking the
/// server down: a file over any of them is refused whole, never read part way.
/// </summary>
public sealed class ImportOptions
{
    public const string SectionName = "DataImport";

    /// <summary>The largest file accepted, in bytes (10 MB).</summary>
    public long MaxFileBytes { get; set; } = 10L * 1024 * 1024;

    /// <summary>The most data rows in one file (not counting the header).</summary>
    public int MaxRows { get; set; } = 50_000;

    /// <summary>The most columns in one file.</summary>
    public int MaxColumns { get; set; } = 60;

    /// <summary>The longest value one cell may hold, in characters.</summary>
    public int MaxCellChars { get; set; } = 2_000;

    /// <summary>The most valid rows an all-or-nothing commit may carry: it is one database transaction, so it is bounded. A larger batch must be committed as valid rows only.</summary>
    public int MaxAllOrNothingRows { get; set; } = 10_000;

    /// <summary>How many rows (of the file, valid or not) one commit job covers when only the valid rows are committed: each job is one transaction.</summary>
    public int CommitChunkRows { get; set; } = 500;
}
