using Alveara.Api.Architecture.Treatment;

namespace Alveara.Api.Architecture.Completion;

/// <summary>What a procedure's completion did to its charge: <c>created</c> (this completion made it), <c>existing</c> (billing already had the procedure's one charge) or <c>none</c> (a stored fee of zero is not billable).</summary>
public static class ChargeOutcomes
{
    public const string Created = "created";
    public const string Existing = "existing";
    public const string None = "none";
}

public sealed record ChargeEffect(string Outcome, int? EntryNumber, decimal? Amount);

/// <summary>One procedure of a completion set as it was recorded: where it came from, who did it, when, where on the chart and what happened to the chart and the charge.</summary>
public sealed record CompletedProcedureView(
    Guid Id, Guid PlanItemId, int ItemNumber, string ProcedureCode, string ProcedureDescription, string? ToothKey, string? Surface, Guid EncounterId, Guid ProviderProfileId,
    DateTimeOffset PerformedAtUtc, int PlanRevisionNumber, string ChartOutcome, Guid? ToothFindingId, ChargeEffect? Charge);

/// <summary>The answer to completing a set: the plan as it now stands, the set, whether this was a replay of an earlier identical submission, and each procedure as recorded.</summary>
public sealed record CompletionResult(PlanView Plan, Guid SetId, Guid CorrelationId, bool Replayed, IReadOnlyList<CompletedProcedureView> Procedures);

/// <summary>Why one selected procedure cannot be completed now: <c>already_completed</c>, <c>withdrawn</c> or <c>not_accepted</c>.</summary>
public sealed record ItemRefusal(Guid ItemId, int ItemNumber, string Reason);

/// <summary>
/// One or more selected procedures cannot be completed. Nothing was recorded for any of them. The reasons are listed per procedure, and also as one message per procedure under <c>items.{number}</c> so a caller
/// that only reads field errors still sees every one.
/// </summary>
public sealed class ItemsNotCompletableException(IReadOnlyList<ItemRefusal> items) : TreatmentPlanException(
    "items_not_completable", "Some of the selected procedures cannot be completed, so none were. Review the list and try again.", 409,
    items.ToDictionary(i => $"items.{i.ItemNumber}", i => Words(i)))
{
    public IReadOnlyList<ItemRefusal> Items { get; } = items;

    private static string Words(ItemRefusal i) => i.Reason switch
    {
        "already_completed" => $"Procedure {i.ItemNumber} is already completed.",
        "withdrawn" => $"Procedure {i.ItemNumber} was withdrawn from the plan.",
        _ => $"Procedure {i.ItemNumber} has not been accepted.",
    };
}

/// <summary>A selected procedure with more than one chart finding that could be its own: only which procedure, which tooth and surface, and how many matched.</summary>
public sealed record ChartAmbiguity(Guid ItemId, int ItemNumber, string ToothKey, string? Surface, int Matches);

/// <summary>More than one chart finding could be a selected procedure's, so nothing was completed. The chart is never guessed at: unselect the procedure or correct the diagnosis links.</summary>
public sealed class AmbiguousChartFindingException(IReadOnlyList<ChartAmbiguity> items) : TreatmentPlanException(
    "ambiguous_chart_finding", "A chart finding could not be chosen for some of the selected procedures, so none were completed.", 409,
    items.ToDictionary(i => $"items.{i.ItemNumber}", i => $"Procedure {i.ItemNumber} matches {i.Matches} chart findings on tooth {i.ToothKey}{(i.Surface is null ? "" : $" ({i.Surface})")}. Unselect it or correct the diagnosis links, then try again."))
{
    public IReadOnlyList<ChartAmbiguity> Items { get; } = items;
}

/// <summary>What completing one procedure would do now, as the server will do it: whether it can be completed (and why not), what happens to the chart, and whether it will be charged.</summary>
public sealed record ItemReadiness(
    Guid ItemId, int ItemNumber, string ProcedureCode, string ProcedureDescription, string? ToothKey, string? Surface, bool Ready, string? NotReadyReason,
    string ChartEffect, int ChartMatches, bool Billable, decimal Fee, bool ChargeExists);

/// <summary>Readiness of a selection (or of every open procedure), read from the same rules the completion applies, with the plan's version the answer is about.</summary>
public sealed record CompletionReadiness(Guid PlanId, string RowVersion, IReadOnlyList<ItemReadiness> Items, IReadOnlyList<ProviderChoice> Providers, IReadOnlyList<EncounterChoice> Encounters);

/// <summary>An active provider the review screen may offer.</summary>
public sealed record ProviderChoice(Guid Id, string Name, string Specialty);

/// <summary>One of the patient's encounters the review screen may offer: when it was and whether it is still a draft.</summary>
public sealed record EncounterChoice(Guid Id, DateTimeOffset AtUtc, string Status);

/// <summary>One correction to a completed procedure: what it changed (only the fields it corrected), why, and who made it and when.</summary>
public sealed record AmendmentView(int Number, string Reason, Guid? ProviderProfileId, string? ProviderName, DateTimeOffset? PerformedAtUtc, Guid? EncounterId, DateTimeOffset? EncounterAtUtc, string AmendedByName, DateTimeOffset AmendedAtUtc);

/// <summary>
/// A completed procedure as it stands now: where it came from, its current provider, performed time and encounter (the latest correction of each, else the original), the originals, what happened to the chart and the
/// charge, who finalized it and when, and every amendment in order. The original record is never changed; the current values are read from it and its amendments.
/// </summary>
public sealed record CompletedProcedureDetail(
    Guid Id, Guid SetId, Guid PatientId, Guid PlanId, string PlanTitle, Guid PlanItemId, int ItemNumber, int PlanRevisionNumber, string ProcedureCode, string ProcedureDescription, string? ToothKey, string? Surface,
    Guid EncounterId, DateTimeOffset EncounterAtUtc, Guid ProviderProfileId, string ProviderName, DateTimeOffset PerformedAtUtc,
    Guid OriginalEncounterId, Guid OriginalProviderProfileId, DateTimeOffset OriginalPerformedAtUtc,
    string ChartOutcome, Guid? ToothFindingId, ChargeEffect? Charge, string FinalizedByName, DateTimeOffset FinalizedAtUtc, int LatestAmendmentNumber, IReadOnlyList<AmendmentView> Amendments);

/// <summary>The answer to correcting a completed procedure: the procedure as it now stands, the number of the amendment, and whether this was a replay of an earlier identical correction.</summary>
public sealed record AmendmentResult(CompletedProcedureDetail Procedure, int AmendmentNumber, bool Replayed);
