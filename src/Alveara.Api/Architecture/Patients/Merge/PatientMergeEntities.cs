namespace Alveara.Api.Architecture.Patients.Merge;

/// <summary>What a merge decided about one conflict. A field the two records disagree on is either kept as the survivor has it or taken from the source (the survivor's old value is kept in the lineage, so no value is lost); a
/// record that could not move because the survivor already has its one allowed (a billing account, a perio session, a recall plan, a clinical item of the same name) stays with the retired record and stays reachable through the lineage.</summary>
public static class PatientMergeResolutionKinds
{
    public const string Field = "Field";
    public const string Record = "Record";
    public static readonly IReadOnlyList<string> All = [Field, Record];
}

public static class PatientMergeChoices
{
    public const string KeptSurvivor = "KeptSurvivor";
    public const string TookSource = "TookSource";
    public const string LeftWithSource = "LeftWithSource";
    public static readonly IReadOnlyList<string> All = [KeptSurvivor, TookSource, LeftWithSource];
}

/// <summary>
/// STORY-017: the lineage of one merge: the retired patient (<see cref="SourcePatientId"/>, which stays in place, marked merged, and is never deleted), the patient that carries on (<see cref="SurvivorPatientId"/>), who merged
/// them and when, why, and the key that makes a repeated request the same merge and not a second one. Never updated and never deleted (the database refuses both). A patient can be retired once, and a retired patient can never be the survivor of another merge.
/// </summary>
public class PatientMerge
{
    public Guid Id { get; set; }
    public required string MergeKey { get; set; }
    public Guid SourcePatientId { get; set; }
    public Guid SurvivorPatientId { get; set; }
    public Guid MergedByUserId { get; set; }
    public DateTimeOffset MergedAtUtc { get; set; }
    public required string Reason { get; set; }
    /// <summary>True when the merge wrote a ledger of the exact rows it moved (<see cref="PatientMergeMovedRow"/>), which is what makes it possible to undo. Merges made before the ledger existed have none and cannot be undone.</summary>
    public bool HasLedger { get; set; }
}

/// <summary>One row a merge moved: the table and the patient-id column it changed, and the row's key (its primary key as text, as it is after the move). The ledger is what an undo moves back; it is never changed or deleted.</summary>
public class PatientMergeMovedRow
{
    public Guid MergeId { get; set; }
    public required string TableName { get; set; }
    public required string ColumnName { get; set; }
    public required string RowKey { get; set; }
}

/// <summary>
/// ALV-017-C01: a merge that was undone: who, when and why. The merge's lineage stays as it was (it is append-only); this row says it no longer holds. One undo per merge; a record whose merge was undone can be merged away again later.
/// </summary>
public class PatientUnmerge
{
    public Guid Id { get; set; }
    public Guid MergeId { get; set; }
    public required string UnmergeKey { get; set; }
    public Guid UnmergedByUserId { get; set; }
    public DateTimeOffset UnmergedAtUtc { get; set; }
    public required string Reason { get; set; }
    public int RowsMovedBack { get; set; }
}

/// <summary>How many rows of one patient-owned column a merge moved to the survivor (zero is recorded too, so the lineage shows every table was looked at).</summary>
public class PatientMergeMove
{
    public Guid MergeId { get; set; }
    public required string TableName { get; set; }
    public required string ColumnName { get; set; }
    public int RowsMoved { get; set; }
}

/// <summary>One conflict and its answer (see <see cref="PatientMergeResolutionKinds"/> and <see cref="PatientMergeChoices"/>). For a field, <see cref="Subject"/> is the field name and <see cref="ReplacedValue"/> the survivor's value when the source's was taken;
/// for a record, <see cref="Subject"/> is the table and the id of the record that stayed with the retired patient.</summary>
public class PatientMergeResolution
{
    public Guid Id { get; set; }
    public Guid MergeId { get; set; }
    public required string Kind { get; set; }
    public required string Subject { get; set; }
    public required string Choice { get; set; }
    public string? ReplacedValue { get; set; }
}
