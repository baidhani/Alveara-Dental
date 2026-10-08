namespace Alveara.Api.Architecture.Procedures;

/// <summary>One version of a procedure as a caller reads it. The fee is in US dollars.</summary>
public sealed record ProcedureVersionView(
    Guid VersionId, Guid ProcedureId, int VersionNumber, string Description, string Category, string Scope, string Dentition, decimal Fee, string Currency,
    string? SourceName, string? SourceVersion, DateOnly EffectiveFrom, DateOnly? ValidThrough, string? Reason, string? CreatedByName, DateTimeOffset CreatedAtUtc);

/// <summary>A catalog row: the identity, whether it is usable on the date asked about, and the version in effect then (for a scheduled procedure, its first version).</summary>
public sealed record ProcedureSummaryView(
    Guid Id, string CodeSystem, string Code, bool IsActive, string Status, DateOnly AsOf, int CurrentVersionNumber, ProcedureVersionView Version, string RowVersion, DateTimeOffset CreatedAtUtc);

/// <summary>A procedure with every version it has ever had, oldest first.</summary>
public sealed record ProcedureDetailView(ProcedureSummaryView Summary, IReadOnlyList<ProcedureVersionView> Versions);

public sealed record ProcedureEventView(int EventNumber, string ChangeType, int? VersionNumber, string? Reason, string? ActorName, DateTimeOffset OccurredAtUtc);

/// <summary>How many records elsewhere refer to a procedure, so a person can be warned before inactivating it. Inactivating never touches those records.</summary>
public sealed record ProcedureUsageView(Guid ProcedureId, int Count, IReadOnlyList<ProcedureUsageBySource> BySource);

public sealed record ProcedureUsageBySource(string Source, int Count);

/// <summary>
/// The shape treatment planning, completion and billing copy when they use a procedure: the identity, the exact version and the fee as of the date asked about. Remembering the
/// <see cref="VersionId"/> (or copying these values) is what keeps a plan or a charge from changing when the catalog's fee does.
/// </summary>
public sealed record ProcedureSnapshot(
    Guid ProcedureId, Guid VersionId, int VersionNumber, string CodeSystem, string Code, string Description, string Category, string Scope, string Dentition, decimal Fee, string Currency,
    string? SourceName, string? SourceVersion, DateOnly EffectiveFrom, DateOnly? ValidThrough);
