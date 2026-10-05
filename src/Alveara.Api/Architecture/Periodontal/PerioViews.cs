namespace Alveara.Api.Architecture.Periodontal;

/// <summary>
/// A periodontal rule refused a request. Carries a stable machine code (the client branches on it), a message safe to show, the HTTP status, and - for <c>validation_failed</c> - every
/// problem found, each naming its tooth, site and field so the screen can point at the entry to correct.
/// </summary>
public class PerioException(string code, string message, int statusCode, IReadOnlyList<PerioProblem>? problems = null) : Exception(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
    public IReadOnlyList<PerioProblem> Problems { get; } = problems ?? [];
}

/// <summary>One probed site as a chart shows it. <see cref="AttachmentLossMm"/> is derived (probing depth + recession), never stored.</summary>
public sealed record PerioReadingView(string ToothKey, string Site, int ProbingDepthMm, int RecessionMm, int AttachmentLossMm, bool Bleeding);

/// <summary>One saved chart. Names are the person's display name (never an account id or login).</summary>
public sealed record PerioExamView(Guid Id, Guid PatientId, DateTimeOffset RecordedAtUtc, string RecordedByName, int ReadingCount, IReadOnlyList<PerioReadingView> Readings);

/// <summary>A patient's saved charts, newest first. An empty list means nothing has been charted, never that the gums are healthy.</summary>
public sealed record PerioHistoryView(Guid PatientId, IReadOnlyList<PerioExamView> Exams);
