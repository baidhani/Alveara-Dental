namespace Alveara.Api.Architecture.Odontogram;

/// <summary>Whether a condition is recorded on one surface or applies to the whole tooth.</summary>
public static class ConditionScopes
{
    public const string Surface = "Surface";
    public const string WholeTooth = "WholeTooth";
    public static readonly IReadOnlyList<string> All = [Surface, WholeTooth];
}

/// <summary>Which dentition a condition can be recorded on: permanent teeth, primary teeth or both.</summary>
public static class ConditionDentitions
{
    public const string Permanent = "Permanent";
    public const string Primary = "Primary";
    public const string Both = "Both";
    public static readonly IReadOnlyList<string> All = [Permanent, Primary, Both];

    public static bool Allows(string appliesTo, bool toothIsPrimary) => appliesTo == Both || (toothIsPrimary ? appliesTo == Primary : appliesTo == Permanent);
}

/// <summary>
/// What a condition does to the tooth's presence. <see cref="Absent"/> means the tooth is not in the mouth (a missing tooth), <see cref="Replacement"/> means a prosthetic stands in its
/// place (an implant), <see cref="None"/> means nothing about presence (caries, restoration...). While a tooth is absent - an Existing or Completed finding whose condition is Absent -
/// only Absent and Replacement conditions can be recorded on it.
/// </summary>
public static class ToothEffects
{
    public const string None = "None";
    public const string Absent = "Absent";
    public const string Replacement = "Replacement";
    public static readonly IReadOnlyList<string> All = [None, Absent, Replacement];
}

/// <summary>The starting catalogue: the six conditions the course odontogram shipped with, now rows that can be joined by others. Codes are what a finding stores and never change.</summary>
public static class FindingConditions
{
    public const string Caries = "Caries";
    public const string Restoration = "Restoration";
    public const string Crown = "Crown";
    public const string Missing = "Missing";
    public const string Implant = "Implant";
    public const string RootCanal = "RootCanal";

    public static readonly IReadOnlyList<string> All = [Caries, Restoration, Crown, Missing, Implant, RootCanal];

    public sealed record Seed(Guid Id, string Code, string Label, string Scope, string AppliesTo, string Effect);

    /// <summary>Fixed ids so every database (and every test) starts from the same six rows.</summary>
    public static readonly IReadOnlyList<Seed> Seeds =
    [
        new(Guid.Parse("c0de0001-0000-4000-8000-000000000001"), Caries, "Caries", ConditionScopes.Surface, ConditionDentitions.Both, ToothEffects.None),
        new(Guid.Parse("c0de0001-0000-4000-8000-000000000002"), Restoration, "Restoration", ConditionScopes.Surface, ConditionDentitions.Both, ToothEffects.None),
        new(Guid.Parse("c0de0001-0000-4000-8000-000000000003"), Crown, "Crown", ConditionScopes.WholeTooth, ConditionDentitions.Both, ToothEffects.None),
        new(Guid.Parse("c0de0001-0000-4000-8000-000000000004"), Missing, "Missing tooth", ConditionScopes.WholeTooth, ConditionDentitions.Both, ToothEffects.Absent),
        new(Guid.Parse("c0de0001-0000-4000-8000-000000000005"), Implant, "Implant", ConditionScopes.WholeTooth, ConditionDentitions.Permanent, ToothEffects.Replacement),
        new(Guid.Parse("c0de0001-0000-4000-8000-000000000006"), RootCanal, "Root canal", ConditionScopes.WholeTooth, ConditionDentitions.Both, ToothEffects.None),
    ];
}

/// <summary>
/// STORY-006 / ALV-006-C01: one kind of finding a practice can record on a tooth. A catalogue row, not code, so a practice can add a condition without a release. The code, label, scope,
/// dentition and tooth effect are fixed once created (a database trigger refuses a change), so a finding recorded last year can never be re-described by a later edit; a condition that
/// is no longer wanted is retired with a reason and can still be read on every finding that used it. Never deleted; every change appends a <see cref="ConditionTypeEvent"/>.
/// </summary>
public class ConditionType
{
    public Guid Id { get; set; }
    /// <summary>What a finding stores: letters and digits starting with a capital, for example "Fracture". Unique, case-sensitively.</summary>
    public required string Code { get; set; }
    public required string Label { get; set; }
    /// <summary>One of <see cref="ConditionScopes.All"/>.</summary>
    public required string Scope { get; set; }
    /// <summary>One of <see cref="ConditionDentitions.All"/>.</summary>
    public required string AppliesTo { get; set; }
    /// <summary>One of <see cref="ToothEffects.All"/>.</summary>
    public required string ToothEffect { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAtUtc { get; set; }
    /// <summary>Null for the six seeded conditions (created by the system, not a person).</summary>
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset? UpdatedAtUtc { get; set; }
    public Guid? UpdatedByUserId { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>Append-only history of a <see cref="ConditionType"/>: created, retired, reactivated, each with who, when and why. A trigger refuses any edit.</summary>
public class ConditionTypeEvent
{
    public Guid Id { get; set; }
    public Guid ConditionTypeId { get; set; }
    public required string Code { get; set; }
    public int EventNumber { get; set; }
    /// <summary>One of <see cref="ConditionTypeChanges"/>.</summary>
    public required string ChangeType { get; set; }
    public string? Reason { get; set; }
    public Guid? ActorUserId { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
}

public static class ConditionTypeChanges
{
    public const string Created = "Created";
    public const string Retired = "Retired";
    public const string Reactivated = "Reactivated";
}

/// <summary>The kinds of record a finding can be linked to. The targets (diagnoses, treatment plans, procedures) are later stories; a link holds the reference, not a copy.</summary>
public static class LinkTypes
{
    public const string Diagnosis = "Diagnosis";
    public const string TreatmentPlan = "TreatmentPlan";
    public const string Procedure = "Procedure";
    public static readonly IReadOnlyList<string> All = [Diagnosis, TreatmentPlan, Procedure];
}

/// <summary>
/// A link from a finding to the diagnosis, treatment plan or completed procedure it relates to, by reference. Append-only (a trigger refuses any edit or delete), unique per finding, type
/// and reference, and recorded in the finding's history so the timeline shows when it was made. The referenced records do not exist yet, so the reference is opaque text.
/// </summary>
public class ToothFindingLink
{
    public Guid Id { get; set; }
    public Guid FindingId { get; set; }
    public Guid PatientId { get; set; }
    /// <summary>One of <see cref="LinkTypes.All"/>.</summary>
    public required string LinkType { get; set; }
    public required string Reference { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public Guid? CreatedByUserId { get; set; }
}
