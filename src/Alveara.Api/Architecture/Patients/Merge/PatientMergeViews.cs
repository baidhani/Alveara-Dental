namespace Alveara.Api.Architecture.Patients.Merge;

/// <summary>A field both records fill in with different values: the administrator chooses which one the surviving record keeps.</summary>
public sealed record MergeFieldConflict(string Field, string? SurvivorValue, string? SourceValue);

/// <summary>A field the surviving record had empty and the retired one filled in: copied across, and recorded.</summary>
public sealed record MergeAutoFill(string Field, string SourceValue);

/// <summary>How many rows of one patient-owned column a merge moves (or moved).</summary>
public sealed record MergeMoveCount(string Table, string Column, int Rows);

/// <summary>A record the survivor already has its one allowed copy of (or the same natural key as): it stays with the retired patient, with everything that depends on it.</summary>
public sealed record MergeLeftBehind(string Table, string RowKey, string Why);

/// <summary>Why a merge cannot be done as asked: stable code and a message a person can act on.</summary>
public sealed record MergeBlocker(string Code, string Message);

public sealed record MergePreview(PatientBrief Source, PatientBrief Survivor, IReadOnlyList<MergeFieldConflict> Conflicts, IReadOnlyList<MergeAutoFill> AutoFills,
    IReadOnlyList<MergeMoveCount> Moves, IReadOnlyList<MergeLeftBehind> LeftBehind, IReadOnlyList<MergeBlocker> Blockers, bool CanMerge, string SourceRowVersion, string SurvivorRowVersion);

/// <summary><see cref="FieldChoices"/> maps each conflicting field to <c>Survivor</c> (keep the surviving record's value) or <c>Source</c> (take the retired record's); every conflict needs an answer.</summary>
public sealed record MergeRequest(Guid? SourcePatientId, Guid? SurvivorPatientId, string? Reason, string? SourceRowVersion, string? SurvivorRowVersion, IReadOnlyDictionary<string, string>? FieldChoices);

/// <summary>One line of the merge log: who merged which record into which, when and why, and how much moved or stayed.</summary>
public sealed record MergeLogEntry(Guid MergeId, PatientBrief Source, PatientBrief Survivor, Guid MergedByUserId, DateTimeOffset MergedAtUtc, string Reason, int RowsMoved, int RecordsLeftBehind);

public sealed record MergeResolutionView(string Kind, string Subject, string Choice, string? ReplacedValue);

public sealed record MergeResult(Guid MergeId, Guid SourcePatientId, Guid SurvivorPatientId, Guid MergedByUserId, DateTimeOffset MergedAtUtc, string Reason,
    IReadOnlyList<MergeMoveCount> Moves, IReadOnlyList<MergeResolutionView> Resolutions, bool Replayed);
