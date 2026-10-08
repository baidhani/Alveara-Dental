namespace Alveara.Api.Architecture.Procedures;

/// <summary>
/// ALV-N005: the practice's procedure catalog - one authoritative definition of each procedure that treatment planning, completion and billing all share. A <see cref="ProcedureDefinition"/> is the
/// STABLE IDENTITY (its Id, code system and code never change); everything a person can describe, price or restrict lives in immutable <see cref="ProcedureVersion"/> rows, so a plan or a charge
/// that remembers the version it used reads exactly what it was made with, whatever the catalog says today. A procedure is never deleted: it is inactivated with a reason, and can still be read
/// by everything that referenced it. Every change appends a <see cref="ProcedureEvent"/> (who, when, why).
/// </summary>
public class ProcedureDefinition
{
    public Guid Id { get; set; }
    /// <summary>One of <see cref="ProcedureCodeSystems.All"/>. Fixed once created (a database trigger refuses a change).</summary>
    public required string CodeSystem { get; set; }
    /// <summary>The procedure code inside its code system, upper case. Fixed once created.</summary>
    public required string Code { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>The number of the newest <see cref="ProcedureVersion"/> (1 for a new procedure).</summary>
    public int CurrentVersionNumber { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset? UpdatedAtUtc { get; set; }
    public Guid? UpdatedByUserId { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>
/// One immutable state of a procedure: what it is called, how it is classified, where it applies, what it costs and when that takes effect. A change of any of these (a fee change included)
/// appends the next version; nothing is ever edited in place (a database trigger refuses it), so history cannot be rewritten by a later edit.
/// </summary>
public class ProcedureVersion
{
    public Guid Id { get; set; }
    public Guid ProcedureId { get; set; }
    public int VersionNumber { get; set; }
    public required string Description { get; set; }
    /// <summary>One of <see cref="ProcedureCategories.All"/>.</summary>
    public required string Category { get; set; }
    /// <summary>One of <see cref="ProcedureScopes.All"/>: what the procedure is performed on.</summary>
    public required string Scope { get; set; }
    /// <summary>One of <see cref="ProcedureDentitions.All"/>; only meaningful for tooth-level scopes.</summary>
    public required string Dentition { get; set; }
    /// <summary>The fee in US dollars with two decimal places (the shared <c>Money</c> convention). Zero is allowed (no charge); negative is not.</summary>
    public decimal Fee { get; set; }
    /// <summary>Where the code comes from (for example the name of the licensed code set the practice holds); required for an external system.</summary>
    public string? SourceName { get; set; }
    /// <summary>The edition of that source (for example "2026"); lets a later code-set update be a new version, never a new identity.</summary>
    public string? SourceVersion { get; set; }
    /// <summary>The practice-local date this version starts to apply (may be in the future).</summary>
    public DateOnly EffectiveFrom { get; set; }
    /// <summary>Optional last practice-local date the code may be used for new work (a retired code in its source).</summary>
    public DateOnly? ValidThrough { get; set; }
    public string? Reason { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}

/// <summary>Append-only history of a procedure: created, revised (a new version), inactivated, reactivated, each with who, when and why. A trigger refuses any edit.</summary>
public class ProcedureEvent
{
    public Guid Id { get; set; }
    public Guid ProcedureId { get; set; }
    public int EventNumber { get; set; }
    /// <summary>One of <see cref="ProcedureChanges"/>.</summary>
    public required string ChangeType { get; set; }
    /// <summary>The version a Created or Revised event produced; null for inactivation and reactivation.</summary>
    public int? VersionNumber { get; set; }
    public string? Reason { get; set; }
    public Guid ActorUserId { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
}

public static class ProcedureChanges
{
    public const string Created = "Created";
    public const string Revised = "Revised";
    public const string Inactivated = "Inactivated";
    public const string Reactivated = "Reactivated";
    public static readonly IReadOnlyList<string> All = [Created, Revised, Inactivated, Reactivated];
}

/// <summary>
/// The code systems a procedure can belong to. <b>Local</b> codes are the practice's own and are never CDT-shaped, so a local code cannot be mistaken for a CDT code. <b>CDT</b> marks a code
/// from the externally licensed CDT code set: the system holds the structure (code shape, source, edition) but NO licensed CDT text - the practice enters its own description. <b>External</b>
/// is any other externally sourced code set, and then the source must be named.
/// </summary>
public static class ProcedureCodeSystems
{
    public const string Local = "Local";
    public const string Cdt = "CDT";
    public const string External = "External";
    public static readonly IReadOnlyList<string> All = [Local, Cdt, External];
}

/// <summary>General categories, named here in plain words; they are not the categories of any licensed code set.</summary>
public static class ProcedureCategories
{
    public static readonly IReadOnlyList<string> All =
        ["Diagnostic", "Preventive", "Restorative", "Endodontic", "Periodontic", "Prosthodontic", "OralSurgery", "Orthodontic", "Adjunctive", "Other"];
}

/// <summary>What a procedure is performed on. A tooth-level scope needs a tooth when it is planned; a surface-level scope needs a surface too.</summary>
public static class ProcedureScopes
{
    public const string WholeMouth = "WholeMouth";
    public const string Arch = "Arch";
    public const string Quadrant = "Quadrant";
    public const string Tooth = "Tooth";
    public const string ToothSurface = "ToothSurface";
    public static readonly IReadOnlyList<string> All = [WholeMouth, Arch, Quadrant, Tooth, ToothSurface];
    public static bool IsToothLevel(string scope) => scope is Tooth or ToothSurface;
}

public static class ProcedureDentitions
{
    public const string Permanent = "Permanent";
    public const string Primary = "Primary";
    public const string Both = "Both";
    public static readonly IReadOnlyList<string> All = [Permanent, Primary, Both];
}
