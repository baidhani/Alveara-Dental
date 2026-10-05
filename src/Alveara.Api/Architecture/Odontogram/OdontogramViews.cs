namespace Alveara.Api.Architecture.Odontogram;

/// <summary>An odontogram rule refused a request. Carries a stable machine code (the client branches on it), a message safe to show, the HTTP status, and per-field messages.</summary>
public class OdontogramException(string code, string message, int statusCode, IReadOnlyDictionary<string, string>? fieldErrors = null) : Exception(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
    public IReadOnlyDictionary<string, string> FieldErrors { get; } = fieldErrors ?? new Dictionary<string, string>();
}

/// <summary>A link from a finding to a diagnosis, treatment plan or procedure, by reference.</summary>
public sealed record LinkView(string LinkType, string Reference, string? LinkedByName, DateTimeOffset LinkedAtUtc);

/// <summary>
/// One finding as the chart shows it. <see cref="ToothKey"/> is the stored FDI key; the screen renders it with the numbering system in use. <see cref="Condition"/> is the catalogue code and
/// <see cref="ConditionLabel"/> its label, so the screen needs no list of its own. <see cref="RowVersion"/> is what a writer echoes back so a stale change is refused. Names are staff
/// display names ("Staff member" when the account is gone).
/// </summary>
public sealed record FindingView(
    Guid Id, string ToothKey, string? Surface, string Condition, string ConditionLabel, string ConditionScope, string State, string Status,
    string? RecordedByName, DateTimeOffset RecordedAtUtc, string? UpdatedByName, DateTimeOffset? UpdatedAtUtc, string RowVersion, IReadOnlyList<LinkView> Links);

/// <summary>The active findings of one patient's chart. An empty list means nothing is recorded, not that the mouth is healthy.</summary>
public sealed record ChartView(Guid PatientId, IReadOnlyList<FindingView> Findings);

public sealed record FindingVersionView(
    int VersionNumber, string ChangeType, string ToothKey, string? Surface, string Condition, string State, string Status, string? Reason, string? ActorName, DateTimeOffset OccurredAtUtc);

public sealed record FindingHistoryView(Guid FindingId, IReadOnlyList<FindingVersionView> Versions);

/// <summary>One event in a tooth's timeline: a change to one finding on that tooth, with what the finding said after it, who made it, when and why.</summary>
public sealed record ToothEventView(
    Guid FindingId, string Condition, string ConditionLabel, string? Surface, int VersionNumber, string ChangeType, string State, string Status, string? Reason, string? ActorName, DateTimeOffset OccurredAtUtc);

/// <summary>Everything that has happened to one tooth, oldest first: every finding ever recorded on it (withdrawn ones included) and every change to each. Nothing a later edit does removes an event from it.</summary>
public sealed record ToothHistoryView(Guid PatientId, string ToothKey, IReadOnlyList<ToothEventView> Events);

public sealed record ConditionTypeView(
    Guid Id, string Code, string Label, string Scope, string AppliesTo, string ToothEffect, bool IsActive, string? CreatedByName, DateTimeOffset CreatedAtUtc, string RowVersion);

public sealed record ConditionTypeEventView(int EventNumber, string ChangeType, string? Reason, string? ActorName, DateTimeOffset OccurredAtUtc);
