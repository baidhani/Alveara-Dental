namespace Alveara.Api.Architecture.Odontogram;

/// <summary>An odontogram rule refused a request. Carries a stable machine code (the client branches on it), a message safe to show, the HTTP status, and per-field messages.</summary>
public class OdontogramException(string code, string message, int statusCode, IReadOnlyDictionary<string, string>? fieldErrors = null) : Exception(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
    public IReadOnlyDictionary<string, string> FieldErrors { get; } = fieldErrors ?? new Dictionary<string, string>();
}

/// <summary>
/// One finding as the chart shows it. <see cref="ToothKey"/> is the stored FDI key; the screen renders it with <see cref="ToothNumbering"/>. <see cref="RowVersion"/> is what a writer
/// echoes back so a stale change is refused. Names are staff display names ("Staff member" when the account is gone).
/// </summary>
public sealed record FindingView(
    Guid Id, string ToothKey, string? Surface, string Condition, string State, string Status,
    string? RecordedByName, DateTimeOffset RecordedAtUtc, string? UpdatedByName, DateTimeOffset? UpdatedAtUtc, string RowVersion);

/// <summary>The active findings of one patient's chart. An empty list means nothing is recorded, not that the mouth is healthy.</summary>
public sealed record ChartView(Guid PatientId, IReadOnlyList<FindingView> Findings);

public sealed record FindingVersionView(
    int VersionNumber, string ChangeType, string ToothKey, string? Surface, string Condition, string State, string Status, string? Reason, string? ActorName, DateTimeOffset OccurredAtUtc);

public sealed record FindingHistoryView(Guid FindingId, IReadOnlyList<FindingVersionView> Versions);
