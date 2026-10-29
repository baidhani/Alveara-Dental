using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Reporting;

/// <summary>Items and the practice's fee estimate for one cell of the report. The fee is what the plan proposed, not what was billed.</summary>
public sealed record PlanCell(string Cell, int Items, decimal EstimatedFee);

/// <summary>
/// The treatment-plan status report for the plans created in the range, as they stood at the END of the range. <see cref="Cells"/> has a row for every cell: Pending, Accepted, Declined and Withdrawn add up to <see cref="Items"/>;
/// AcceptedScheduled and AcceptedUnscheduled add up to Accepted; AcceptedCompleted is the accepted items whose procedure has been done. <see cref="Reconciled"/> is true when those identities hold against independent counts of
/// the items and the plans. Fees are the practice's estimates copied into the plan, never ledger figures (the financial reports are the ledger).
/// </summary>
public sealed record PlanStatusData(int Plans, int PlansProposed, int PlansWithdrawn, int Items, IReadOnlyList<PlanCell> Cells, bool Reconciled);

/// <summary>
/// ALV-N006 slice 3a: treatment-plan status. Source: the plans created in the range, their items and each item's latest recorded state as of the end of the range (an append-only history, so the answer is the same however
/// often it is asked). A provider or location filter is refused (<c>filter_not_supported</c>): a plan belongs to a patient, not to a provider. Failure modes handled: nothing in the range (<c>NoData</c>), more items than the
/// limit (413), an audit entry that cannot be saved (503). Definition version 1.
/// </summary>
public class TreatmentPlanStatusReportService(AlveraDbContext db, IPracticeClock clock, IOptions<ReportOptions> options, ILogger<TreatmentPlanStatusReportService> log)
{
    public const int DefinitionVersion = 1;

    public async Task<ReportResult<PlanStatusData>> RunAsync(ReportFilter filter, Guid actor, CancellationToken ct)
    {
        if (filter.ProviderIds.Count > 0 || filter.LocationId is not null) throw new ReportException("filter_not_supported", "Treatment-plan status cannot be filtered by provider or location: a plan belongs to a patient.", 400);
        var queries = new ClinicalQueries(db);
        string consistency;
        PlanStatusData data;
        await using (var session = await ReportReadSession.OpenAsync(db, log, ct))
        {
            consistency = session.ReadConsistency;
            var items = queries.PlanItems(filter.StartUtc, filter.EndUtc);
            var itemCount = await items.CountAsync(ct);
            ReportSupport.GuardSize(itemCount, options.Value);
            var groups = await items.GroupBy(i => new { i.Withdrawn, i.Acceptance, i.Scheduling, i.Completion }).Select(g => new { g.Key.Withdrawn, g.Key.Acceptance, g.Key.Scheduling, g.Key.Completion, Items = g.Count(), Fee = g.Sum(x => x.Fee) }).ToListAsync(ct);

            var cells = PlanItemCells.All.Select(cell =>
            {
                var matches = PlanItemCells.Predicate(cell).Compile();
                var hit = groups.Where(g => matches(new PlanItemRow { Withdrawn = g.Withdrawn, Acceptance = g.Acceptance, Scheduling = g.Scheduling, Completion = g.Completion })).ToList();
                return new PlanCell(cell, hit.Sum(g => g.Items), hit.Sum(g => g.Fee));
            }).ToList();
            int Of(string cell) => cells.Single(c => c.Cell == cell).Items;

            var flags = await queries.PlansWithdrawnFlags(filter.StartUtc, filter.EndUtc).ToListAsync(ct);
            var withdrawn = flags.Count(f => f);
            var reconciled = Of(PlanItemCells.Pending) + Of(PlanItemCells.Accepted) + Of(PlanItemCells.Declined) + Of(PlanItemCells.Withdrawn) == itemCount
                && Of(PlanItemCells.AcceptedScheduled) + Of(PlanItemCells.AcceptedUnscheduled) == Of(PlanItemCells.Accepted)
                && groups.Sum(g => g.Items) == itemCount;
            data = new PlanStatusData(flags.Count, flags.Count - withdrawn, withdrawn, itemCount, cells, reconciled);
        }

        await ReportSupport.AuditAsync(db, log, ReportIds.TreatmentPlans, false, filter, data.Plans, actor, ct);
        return new ReportResult<PlanStatusData>(ReportIds.TreatmentPlans, DefinitionVersion, ReportSupport.View(filter), clock.UtcNow, consistency, data.Plans, data.Plans == 0, data);
    }
}
