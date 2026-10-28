namespace Alveara.Api.Architecture.Imports;

/// <summary>
/// What a dry-run (and later the commit) decided about one data row. <see cref="Valid"/> would be accepted; <see cref="Rejected"/> has something wrong with its values; <see cref="Unresolved"/> needs a person to decide
/// (a likely duplicate); <see cref="Skipped"/> needs nothing (the person is already registered, or an earlier row of the file is the same person); <see cref="Accepted"/> was committed (the commit slice).
/// </summary>
public static class ImportRowStatus
{
    public const string Valid = "Valid";
    public const string Rejected = "Rejected";
    public const string Unresolved = "Unresolved";
    public const string Skipped = "Skipped";
    public const string Accepted = "Accepted";
    public static readonly IReadOnlyList<string> All = [Valid, Rejected, Unresolved, Skipped, Accepted];
}

/// <summary>Why a row has the status it has. Stable codes the screens map to words.</summary>
public static class ImportRowReason
{
    public const string InvalidFields = "invalid_fields";
    public const string AlreadyRegistered = "already_registered";
    public const string DuplicateInFile = "duplicate_in_file";
    public const string PossibleDuplicate = "possible_duplicate";
}

/// <summary>
/// ALV-018-C02: the outcome for one data row of a batch. It never holds a value from the file: a rejected row carries the front desk's own field messages (fixed texts such as "Date of birth is required"), a
/// skipped or unresolved row carries the ids of the registered patients it matches and, for a repeat within the file, the number of the earlier row (the header is row 1). Rows are never deleted; a dry-run
/// that is run again updates them in place, and the database refuses any change once the batch is finished (errors 51264 to 51266).
/// </summary>
public class ImportBatchRow
{
    public Guid Id { get; set; }
    public Guid BatchId { get; set; }
    /// <summary>2, 3, 4... : the header is row 1.</summary>
    public int RowNumber { get; set; }
    public required string Status { get; set; }
    public string? Reason { get; set; }
    /// <summary>A JSON object of field name to the front desk's message for a rejected row; null otherwise.</summary>
    public string? ErrorsJson { get; set; }
    /// <summary>A JSON array of the ids (at most ten) of the registered patients a skipped or unresolved row matches; null otherwise.</summary>
    public string? MatchedPatientIdsJson { get; set; }
    /// <summary>The earlier row of the same file this row repeats; null otherwise.</summary>
    public int? DuplicateOfRow { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
