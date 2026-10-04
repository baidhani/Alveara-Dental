namespace Alveara.Api.Architecture.Odontogram;

/// <summary>The lifecycle of a tooth finding. Existing stands alone (already in the mouth, no plan); Diagnosed to Planned to Completed is the path of treatment.</summary>
public static class FindingStates
{
    public const string Existing = "Existing";
    public const string Diagnosed = "Diagnosed";
    public const string Planned = "Planned";
    public const string Completed = "Completed";
    public static readonly IReadOnlyList<string> All = [Existing, Diagnosed, Planned, Completed];
}

/// <summary>Whether a finding stands or was withdrawn as a mistake. A withdrawn finding is kept, with who withdrew it and why; it is never deleted.</summary>
public static class FindingStatuses
{
    public const string Active = "Active";
    public const string Withdrawn = "Withdrawn";
}

/// <summary>The fixed starting list of conditions (a later story, ALV-006-C01, moves it to a table). Some apply to one surface, some to the whole tooth.</summary>
public static class FindingConditions
{
    public const string Caries = "Caries";
    public const string Restoration = "Restoration";
    public const string Crown = "Crown";
    public const string Missing = "Missing";
    public const string Implant = "Implant";
    public const string RootCanal = "RootCanal";

    public static readonly IReadOnlyList<string> All = [Caries, Restoration, Crown, Missing, Implant, RootCanal];

    /// <summary>Conditions recorded on one surface; every other condition is about the whole tooth and carries no surface.</summary>
    public static readonly IReadOnlySet<string> SurfaceConditions = new HashSet<string> { Caries, Restoration };

    public static bool NeedsSurface(string condition) => SurfaceConditions.Contains(condition);
}

/// <summary>Surfaces: Mesial, Distal, Lingual on every tooth; Buccal and Occlusal on posterior teeth; Facial and Incisal on anterior ones.</summary>
public static class ToothSurfaces
{
    public static readonly IReadOnlyList<string> Posterior = ["M", "O", "D", "B", "L"];
    public static readonly IReadOnlyList<string> Anterior = ["M", "I", "D", "F", "L"];
    public static readonly IReadOnlyList<string> Any = ["M", "O", "I", "D", "B", "F", "L"];

    public static IReadOnlyList<string> For(string toothKey) => ToothKeys.IsAnterior(toothKey) ? Anterior : Posterior;

    /// <summary>True when the surface exists on that tooth (an Occlusal surface on an incisor, or any surface on an unknown tooth, is not valid).</summary>
    public static bool IsValid(string toothKey, string? surface) => ToothKeys.IsValid(toothKey) && surface is not null && For(toothKey).Contains(surface);
}

/// <summary>
/// STORY-006: one clinical finding on one tooth (and one surface where the condition has one): its condition and where it is in its lifecycle. A finding is never deleted. A
/// wrong entry is withdrawn with a reason; every change appends a <see cref="ToothFindingVersion"/>. A stale edit is refused through <see cref="RowVersion"/>. The tooth is the
/// FDI key (<see cref="ToothKeys"/>); how it is numbered on screen is not stored.
/// </summary>
public class ToothFinding
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    /// <summary>One of the 52 <see cref="ToothKeys.All"/>.</summary>
    public required string ToothKey { get; set; }
    /// <summary>A surface letter (<see cref="ToothSurfaces"/>) when the condition is about one surface; null for a whole-tooth condition.</summary>
    public string? Surface { get; set; }
    /// <summary>One of <see cref="FindingConditions.All"/>.</summary>
    public required string Condition { get; set; }
    /// <summary>One of <see cref="FindingStates.All"/>.</summary>
    public required string State { get; set; }
    /// <summary><see cref="FindingStatuses.Active"/> or <see cref="FindingStatuses.Withdrawn"/>.</summary>
    public required string Status { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset? UpdatedAtUtc { get; set; }
    public Guid? UpdatedByUserId { get; set; }
    /// <summary>Set exactly when Status is Withdrawn (a database check keeps them in step).</summary>
    public DateTimeOffset? WithdrawnAtUtc { get; set; }
    public Guid? WithdrawnByUserId { get; set; }
    public string? WithdrawnReason { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>Append-only history of a <see cref="ToothFinding"/>: a full snapshot after every record, state change and withdrawal with who, when and why. A trigger refuses any edit.</summary>
public class ToothFindingVersion
{
    public Guid Id { get; set; }
    public Guid FindingId { get; set; }
    public Guid PatientId { get; set; }
    public int VersionNumber { get; set; }
    /// <summary>One of <see cref="FindingChangeTypes"/>.</summary>
    public required string ChangeType { get; set; }
    public required string ToothKey { get; set; }
    public string? Surface { get; set; }
    public required string Condition { get; set; }
    public required string State { get; set; }
    public required string Status { get; set; }
    public string? Reason { get; set; }
    public Guid? ActorUserId { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
}

public static class FindingChangeTypes
{
    public const string Recorded = "Recorded";
    public const string StateChanged = "StateChanged";
    public const string Withdrawn = "Withdrawn";
}
