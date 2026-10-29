using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Treatment;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Reporting;

/// <summary>One treatment-plan item as of a moment: whether it was withdrawn by then and the latest acceptance, scheduling and completion state recorded by then (no state row means Pending, Unscheduled, NotStarted).</summary>
public sealed class PlanItemRow
{
    public Guid ItemId { get; set; }
    public Guid PlanId { get; set; }
    public Guid PatientId { get; set; }
    public int ItemNumber { get; set; }
    /// <summary>The practice's estimate copied from the catalog when the item was proposed. Not a ledger figure.</summary>
    public decimal Fee { get; set; }
    public bool Withdrawn { get; set; }
    public string Acceptance { get; set; } = "";
    public string Scheduling { get; set; } = "";
    public string Completion { get; set; } = "";
}

/// <summary>A completed procedure with its CURRENT provider and performed time: the latest amendment that names each wins, else the original (the rule the completion screens use).</summary>
public sealed class EffectiveProcedure
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    public Guid PlanItemId { get; set; }
    public Guid ProcedureVersionId { get; set; }
    public Guid ProviderProfileId { get; set; }
    public DateTimeOffset PerformedAtUtc { get; set; }
}

/// <summary>The cells of the treatment-plan status report. Every item is in exactly one of the first four (<see cref="Pending"/>, <see cref="Accepted"/>, <see cref="Declined"/>, <see cref="Withdrawn"/>); the last three cut the accepted ones further.</summary>
public static class PlanItemCells
{
    public const string Pending = "Pending";
    public const string Accepted = "Accepted";
    public const string Declined = "Declined";
    public const string Withdrawn = "Withdrawn";
    public const string AcceptedScheduled = "AcceptedScheduled";
    public const string AcceptedUnscheduled = "AcceptedUnscheduled";
    public const string AcceptedCompleted = "AcceptedCompleted";
    public static readonly IReadOnlyList<string> All = [Pending, Accepted, Declined, Withdrawn, AcceptedScheduled, AcceptedUnscheduled, AcceptedCompleted];

    /// <summary>The one definition of a cell, used by the report (on the grouped rows) and by the drill-down (in the database), so the two can never disagree.</summary>
    public static Expression<Func<PlanItemRow, bool>> Predicate(string cell) => cell switch
    {
        Pending => r => !r.Withdrawn && r.Acceptance == ItemAcceptance.Pending,
        Accepted => r => !r.Withdrawn && r.Acceptance == ItemAcceptance.Accepted,
        Declined => r => !r.Withdrawn && r.Acceptance == ItemAcceptance.Declined,
        Withdrawn => r => r.Withdrawn,
        AcceptedScheduled => r => !r.Withdrawn && r.Acceptance == ItemAcceptance.Accepted && r.Scheduling == ItemScheduling.Scheduled,
        AcceptedUnscheduled => r => !r.Withdrawn && r.Acceptance == ItemAcceptance.Accepted && r.Scheduling == ItemScheduling.Unscheduled,
        AcceptedCompleted => r => !r.Withdrawn && r.Acceptance == ItemAcceptance.Accepted && r.Completion == ItemCompletion.Completed,
        _ => throw new ArgumentOutOfRangeException(nameof(cell)),
    };
}

/// <summary>ALV-N006 slice 3: the clinical sources, read only. Plans are those created in the range, seen as of the END of the range; completed procedures are dated by their effective performed time.</summary>
public sealed class ClinicalQueries(AlveraDbContext db)
{
    /// <summary>The items of the plans created in [start, end), as they stood at <paramref name="end"/>. An item added to a plan after that moment is not yet there.</summary>
    public IQueryable<PlanItemRow> PlanItems(DateTimeOffset start, DateTimeOffset end) =>
        from i in db.TreatmentPlanItems.AsNoTracking()
        join p in db.TreatmentPlans.AsNoTracking() on i.PlanId equals p.Id
        where p.CreatedAtUtc >= start && p.CreatedAtUtc < end && i.CreatedAtUtc < end
        let s = db.TreatmentPlanItemStates.Where(x => x.ItemId == i.Id && x.RecordedAtUtc < end).OrderByDescending(x => x.Sequence).FirstOrDefault()
        select new PlanItemRow
        {
            ItemId = i.Id, PlanId = i.PlanId, PatientId = i.PatientId, ItemNumber = i.ItemNumber, Fee = i.Fee,
            Withdrawn = i.WithdrawnAtUtc != null && i.WithdrawnAtUtc < end,
            Acceptance = s == null ? ItemAcceptance.Pending : s.Acceptance,
            Scheduling = s == null ? ItemScheduling.Unscheduled : s.Scheduling,
            Completion = s == null ? ItemCompletion.NotStarted : s.Completion,
        };

    /// <summary>The plans created in [start, end) (the plan itself, with whether it was withdrawn by <paramref name="end"/>).</summary>
    public IQueryable<bool> PlansWithdrawnFlags(DateTimeOffset start, DateTimeOffset end) =>
        db.TreatmentPlans.AsNoTracking().Where(p => p.CreatedAtUtc >= start && p.CreatedAtUtc < end).Select(p => p.WithdrawnAtUtc != null && p.WithdrawnAtUtc < end);

    /// <summary>Every completed procedure with its current provider and performed time.</summary>
    public IQueryable<EffectiveProcedure> Procedures() =>
        db.CompletedProcedures.AsNoTracking().Select(cp => new EffectiveProcedure
        {
            Id = cp.Id, PatientId = cp.PatientId, PlanItemId = cp.PlanItemId, ProcedureVersionId = cp.ProcedureVersionId,
            ProviderProfileId = db.CompletedProcedureAmendments.Where(a => a.CompletedProcedureId == cp.Id && a.ProviderProfileId != null).OrderByDescending(a => a.AmendmentNumber).Select(a => a.ProviderProfileId).FirstOrDefault() ?? cp.ProviderProfileId,
            PerformedAtUtc = db.CompletedProcedureAmendments.Where(a => a.CompletedProcedureId == cp.Id && a.PerformedAtUtc != null).OrderByDescending(a => a.AmendmentNumber).Select(a => a.PerformedAtUtc).FirstOrDefault() ?? cp.PerformedAtUtc,
        });

    /// <summary>The completed procedures performed in [start, end) with the facts the report and its drill-down show: the plan item's fee estimate and the catalog code and description.</summary>
    public IQueryable<ProcedureLine> ProcedureLines(DateTimeOffset start, DateTimeOffset end, Guid[] providers)
    {
        var effective = Procedures().Where(p => p.PerformedAtUtc >= start && p.PerformedAtUtc < end);
        if (providers.Length > 0) effective = effective.Where(p => providers.Contains(p.ProviderProfileId));
        return from p in effective
               join i in db.TreatmentPlanItems.AsNoTracking() on p.PlanItemId equals i.Id
               join v in db.ProcedureVersions.AsNoTracking() on p.ProcedureVersionId equals v.Id
               join d in db.ProcedureDefinitions.AsNoTracking() on v.ProcedureId equals d.Id
               select new ProcedureLine
               {
                   Id = p.Id, PatientId = p.PatientId, PlanItemId = p.PlanItemId, ProviderProfileId = p.ProviderProfileId, PerformedAtUtc = p.PerformedAtUtc,
                   CodeSystem = d.CodeSystem, Code = d.Code, Description = v.Description, EstimatedFee = i.Fee,
               };
    }
}

public sealed class ProcedureLine
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    public Guid PlanItemId { get; set; }
    public Guid ProviderProfileId { get; set; }
    public DateTimeOffset PerformedAtUtc { get; set; }
    public string CodeSystem { get; set; } = "";
    public string Code { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal EstimatedFee { get; set; }
}
