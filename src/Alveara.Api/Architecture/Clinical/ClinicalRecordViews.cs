namespace Alveara.Api.Architecture.Clinical;

/// <summary>ALV-005-C01: what a reader sees of a patient's longitudinal clinical record. Names are the staff members' display names (attribution), never ids alone.</summary>
public sealed record ItemView(
    Guid Id, string Kind, string Name, string? Detail, string? Reaction, string? Severity, string? Dose, string? Frequency, string Status,
    DateTimeOffset CreatedAtUtc, string? CreatedByName, DateTimeOffset? UpdatedAtUtc, string? UpdatedByName, string RowVersion);

/// <summary>
/// One section of the record. <see cref="Status"/> is derived (see <see cref="ClinicalSectionStatuses"/>); <see cref="ReviewedAtUtc"/>/<see cref="ReviewedByName"/> say who last
/// reviewed the section and when, for any stored review state.
/// </summary>
public sealed record RecordSectionView(string Kind, string Status, IReadOnlyList<ItemView> Items, DateTimeOffset? ReviewedAtUtc, string? ReviewedByName);

public sealed record RecordEventView(string Section, string EventType, string? ActorName, DateTimeOffset OccurredAtUtc, Guid? EncounterId, string? Detail);

public sealed record ClinicalRecordView(Guid PatientId, IReadOnlyList<RecordSectionView> Sections, IReadOnlyList<RecordEventView> Timeline);

/// <summary>One version of an item: its values after a change, who changed it, when, why, and during which encounter.</summary>
public sealed record ItemVersionView(
    int VersionNumber, string ChangeType, string Name, string? Detail, string? Reaction, string? Severity, string? Dose, string? Frequency, string Status,
    string? Reason, Guid? EncounterId, string? ActorName, DateTimeOffset OccurredAtUtc);

public sealed record ItemHistoryView(Guid ItemId, string Kind, IReadOnlyList<ItemVersionView> Versions);

/// <summary>The words and limits for the record's sections, one place so messages, history text and tests agree.</summary>
public static partial class ClinicalRecordRules
{
    public const int ReasonMax = 500;

    /// <summary>
    /// ALV-N022 A1 (Addendum 01): NEW allergy input is canonicalized - trimmed and inner whitespace collapsed to one space, case kept - so the unique live-name index also
    /// catches "Penicillin  V" against "Penicillin V" (case and trailing spaces are already equal under the column's collation). Other kinds are unchanged; stored rows are never rewritten.
    /// </summary>
    public static EntryFields Canonical(string kind, EntryFields f) =>
        kind == EncounterEntryKinds.Allergy && f.Name is not null ? f with { Name = InnerWhitespace().Replace(f.Name.Trim(), " ") } : f;

    [System.Text.RegularExpressions.GeneratedRegex(@"\s+")]
    private static partial System.Text.RegularExpressions.Regex InnerWhitespace();

    public static string RequireStatus(string kind, string? status)
    {
        var allowed = ClinicalItemStatuses.AllowedFor(kind);
        if (status is null || !allowed.Contains(status))
            throw new ClinicalException("validation_failed", "That status does not apply to this kind of item.", 400,
                new Dictionary<string, string> { ["status"] = "Choose " + string.Join(", ", allowed.Take(allowed.Count - 1)) + " or " + allowed[^1] + "." });
        return status;
    }

    public static string? CleanReason(string? reason)
    {
        var r = EncounterRules.Clean(reason);
        if (r is { Length: > ReasonMax })
            throw new ClinicalException("validation_failed", "Some fields need attention.", 400, new Dictionary<string, string> { ["reason"] = $"Keep the reason to {ReasonMax} characters or fewer." });
        return r;
    }

    public static string RequireReviewState(string? state)
    {
        if (state is null || (state != ClinicalReviewStates.NotReviewed && !ClinicalReviewStates.Stored.Contains(state)))
            throw new ClinicalException("validation_failed", "That review state does not exist.", 400,
                new Dictionary<string, string> { ["state"] = "Choose Reviewed, NoneKnown, Unknown or NotReviewed." });
        return state;
    }
}
