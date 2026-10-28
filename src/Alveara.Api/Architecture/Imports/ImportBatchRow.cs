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
    /// <summary>A person looked at the likely matches and decided this row is a different person: it will be registered.</summary>
    public const string ReviewedNewPerson = "reviewed_new_person";
    /// <summary>A person looked at the likely matches and decided this row is the same person as a registered one: it will not be registered.</summary>
    public const string ReviewedSamePerson = "reviewed_same_person";
    /// <summary>Appointments (slice 4a): the appointment is already on the schedule exactly as the row says.</summary>
    public const string AlreadyScheduled = "already_scheduled";
    // Appointments: a row the scheduling rules refuse carries the scheduler's own code as its reason.
    public const string StartInPast = "start_in_past";
    public const string ProviderUnavailable = "provider_unavailable";
    public const string ProviderDoubleBooked = "provider_double_booked";
    public const string OperatoryConflict = "operatory_conflict";
    public const string PatientDoubleBooked = "patient_double_booked";
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
    /// <summary>The patient this row registered (an Accepted row); null otherwise. Deliberately not named PatientId: like the export history, this is a record of what an import did, not patient data, so it is not carried in a patient's export package.</summary>
    public Guid? ResultRecordId { get; set; }
    /// <summary>Who decided a likely duplicate (see <see cref="ImportRowReason.ReviewedNewPerson"/>) and when; null for a row nobody has decided.</summary>
    public Guid? DecisionByUserId { get; set; }
    public DateTimeOffset? DecidedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
