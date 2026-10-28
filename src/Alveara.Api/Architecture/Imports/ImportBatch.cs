namespace Alveara.Api.Architecture.Imports;

/// <summary>What a batch brings in. The order is the order a migration is loaded in: patients first, then the appointments and balances that refer to them.</summary>
public static class ImportSourceKind
{
    public const string Patients = "Patients";
    public const string Appointments = "Appointments";
    public const string OpeningBalances = "OpeningBalances";
    public static readonly IReadOnlyList<string> All = [Patients, Appointments, OpeningBalances];
}

/// <summary>
/// Where a batch is. <see cref="Uploaded"/> is the first state (the file passed its structural checks and is stored); the later states belong to the following slices of ALV-018-C02 (dry-run, commit) and are
/// declared now so the database check and the guard trigger never need a second change. A batch that is <see cref="Committed"/>, <see cref="Failed"/> or <see cref="Cancelled"/> never changes again.
/// </summary>
public static class ImportBatchStatus
{
    public const string Uploaded = "Uploaded";
    public const string DryRunComplete = "DryRunComplete";
    public const string Committing = "Committing";
    public const string Committed = "Committed";
    public const string Failed = "Failed";
    public const string Cancelled = "Cancelled";
    public static readonly IReadOnlyList<string> All = [Uploaded, DryRunComplete, Committing, Committed, Failed, Cancelled];
    public static readonly IReadOnlyList<string> Terminal = [Committed, Failed, Cancelled];
    public static readonly IReadOnlyList<string> Open = [Uploaded, DryRunComplete, Committing];
}

/// <summary>How a commit ended. <see cref="Completed"/>: every valid row was registered and no row is left over. <see cref="CompletedWithExceptions"/>: the valid rows were registered; some rows were rejected, skipped or left unresolved
/// (the counters say how many). <see cref="Refused"/>: an all-or-nothing commit found that a row was no longer valid at commit time, so nothing was registered and a new dry-run is needed. <see cref="Interrupted"/>: a part of the commit could not be
/// finished (its job failed); the rows already registered stay registered and the commit can be confirmed again.</summary>
public static class ImportCommitOutcome
{
    public const string Completed = "Completed";
    public const string CompletedWithExceptions = "CompletedWithExceptions";
    public const string Refused = "Refused";
    public const string Interrupted = "Interrupted";
    public static readonly IReadOnlyList<string> All = [Completed, CompletedWithExceptions, Refused, Interrupted];
}

/// <summary>What happens to the other rows when some rows of a batch cannot be imported. The operator sees the choice before committing.</summary>
public static class ImportBatchPolicy
{
    /// <summary>Nothing is committed unless every row is accepted: a rejected or unresolved row stops the whole batch. The default.</summary>
    public const string AllOrNothing = "AllOrNothing";
    /// <summary>Rows that pass are committed; the others stay in a visible queue with their reasons.</summary>
    public const string CommitValidRows = "CommitValidRows";
    public static readonly IReadOnlyList<string> All = [AllOrNothing, CommitValidRows];
}

/// <summary>
/// ALV-018-C02: one file brought in for loading (patients, appointments or opening balances). It records who brought the file, when, what kind it is, the operator's own label for the source, the file's hash and
/// size, and how many data rows and columns it has, with the column names. It never holds a value from the file: the file itself is kept in the import file store (not the document store, whose orphan scan
/// would otherwise remove it) and the counters say what happened to its rows. Only one open batch may exist for the same file of the same kind (a filtered unique index), so sending a file twice never makes
/// a second batch. The database refuses to delete a batch, to change what it recorded about its file, and to change a finished batch (errors 51260 to 51262).
/// </summary>
public class ImportBatch
{
    public Guid Id { get; set; }
    public required string SourceKind { get; set; }
    /// <summary>The operator's own name for where the file came from ("Old system export, October"). Free text, at most <see cref="ImportLimits.LabelMax"/> characters, never from the file.</summary>
    public required string SourceLabel { get; set; }
    public required string Status { get; set; }
    public required string Policy { get; set; }
    public Guid FileBlobId { get; set; }
    /// <summary>SHA-256 of the file in lower-case hex.</summary>
    public required string FileSha256 { get; set; }
    public long FileSizeBytes { get; set; }
    /// <summary>Data rows, not counting the header.</summary>
    public int RowCount { get; set; }
    public int ColumnCount { get; set; }
    /// <summary>The header names as a JSON array of strings (names only, no data).</summary>
    public required string ColumnNamesJson { get; set; }
    public int AttemptedRows { get; set; }
    public int AcceptedRows { get; set; }
    public int RejectedRows { get; set; }
    public int SkippedRows { get; set; }
    public int UnresolvedRows { get; set; }
    /// <summary>ALV-018-C02 slice 5a: opening balances only (0 for the other kinds): the total, in dollars, of the rows the last dry-run found valid, so the operator can check it against the old system before committing.</summary>
    public decimal ValidAmount { get; set; }
    /// <summary>ALV-018-C02 slice 5: opening balances only: the total of the balances a commit has loaded so far. It must equal the sum of the ledger entries the batch made.</summary>
    public decimal AcceptedAmount { get; set; }
    /// <summary>ALV-018-C02 slice 2: which column fills which field, as a JSON object of column name to field name; null until the operator has confirmed a mapping.</summary>
    public string? MappingJson { get; set; }
    /// <summary>When the last dry-run finished; null if none has, or if the mapping changed since (the results are then stale and a new dry-run is needed).</summary>
    public DateTimeOffset? DryRunAtUtc { get; set; }
    /// <summary>ALV-018-C02 slice 3: how many times a commit has been confirmed for this batch (0 before the first). Part of every commit job's key, so a later commit after an interruption is a new set of jobs.</summary>
    public int CommitRound { get; set; }
    public DateTimeOffset? CommitStartedAtUtc { get; set; }
    public DateTimeOffset? CommittedAtUtc { get; set; }
    /// <summary>The operator who confirmed the commit; the patients it registers are recorded as created by this person.</summary>
    public Guid? CommitConfirmedByUserId { get; set; }
    /// <summary>One of <see cref="ImportCommitOutcome.All"/>; null until a commit has ended.</summary>
    public string? CommitOutcome { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
