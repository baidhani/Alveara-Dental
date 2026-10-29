using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Reporting;

/// <summary>The counts for a set of tasks. <see cref="Completed"/>, <see cref="Cancelled"/>, <see cref="ClosedBySystem"/>, <see cref="OpenNotYetDue"/> and <see cref="OpenOverdue"/> add up to <see cref="Due"/>; the last two are cuts (see <see cref="FollowUpOutcomes"/>).</summary>
public sealed record FollowUpOutcomeCounts(int Due, int Completed, int Cancelled, int ClosedBySystem, int OpenNotYetDue, int OpenOverdue, int CompletedLate, int OpenUnassigned);

public sealed record FollowUpKindRow(string Kind, FollowUpOutcomeCounts Counts);

/// <summary>
/// The recall and follow-up report for the tasks DUE in the range, judged at <see cref="AsOfUtc"/> (the moment the report was run: an open task is overdue when it was due before that moment, so the same report run later can show a
/// task moving from not yet due to overdue). <see cref="Reconciled"/> is true when the five exclusive outcomes add up to the tasks due, in the totals and in the kinds, against an independent count.
/// </summary>
public sealed record FollowUpData(DateTimeOffset AsOfUtc, FollowUpOutcomeCounts Totals, IReadOnlyList<FollowUpKindRow> Kinds, bool Reconciled);

/// <summary>
/// ALV-N006 slice 4: recall and follow-up. Source: the follow-up tasks (people's tasks and the system's recall, unscheduled-treatment and referral and lab chase tasks) whose due time falls in the range, their final or open
/// status and who closed them. A provider or location filter is refused (<c>filter_not_supported</c>): a task has an owner, not a provider. Failure modes handled: nothing in the range (<c>NoData</c>), more tasks than the
/// limit (413), an audit entry that cannot be saved (503). Definition version 1.
/// </summary>
public class FollowUpReportService(AlveraDbContext db, IPracticeClock clock, IOptions<ReportOptions> options, ILogger<FollowUpReportService> log)
{
    public const int DefinitionVersion = 1;

    public async Task<ReportResult<FollowUpData>> RunAsync(ReportFilter filter, Guid actor, CancellationToken ct, DateTimeOffset? asOfUtc = null)
    {
        if (filter.ProviderIds.Count > 0 || filter.LocationId is not null) throw new ReportException("filter_not_supported", "Follow-up cannot be filtered by provider or location: a task belongs to an owner.", 400);
        var asOf = asOfUtc ?? clock.UtcNow;
        string consistency;
        FollowUpData data;
        await using (var session = await ReportReadSession.OpenAsync(db, log, ct))
        {
            consistency = session.ReadConsistency;
            var tasks = new FollowUpQueries(db).Tasks(filter.StartUtc, filter.EndUtc, asOf);
            var total = await tasks.CountAsync(ct);
            ReportSupport.GuardSize(total, options.Value);
            var groups = await tasks.GroupBy(t => new { t.Kind, t.Status, t.ClosedBySystem, t.Overdue, t.Unassigned, t.Late }).Select(g => new { g.Key.Kind, g.Key.Status, g.Key.ClosedBySystem, g.Key.Overdue, g.Key.Unassigned, g.Key.Late, Count = g.Count() }).ToListAsync(ct);

            FollowUpOutcomeCounts CountsOf(IEnumerable<string> kinds)
            {
                var chosen = kinds.ToHashSet();
                var rows = groups.Where(g => chosen.Contains(g.Kind)).ToList();
                int Of(string outcome)
                {
                    var matches = FollowUpOutcomes.Predicate(outcome).Compile();
                    return rows.Where(g => matches(new TaskRow { Kind = g.Kind, Status = g.Status, ClosedBySystem = g.ClosedBySystem, Overdue = g.Overdue, Unassigned = g.Unassigned, Late = g.Late })).Sum(g => g.Count);
                }
                return new FollowUpOutcomeCounts(rows.Sum(g => g.Count), Of(FollowUpOutcomes.Completed), Of(FollowUpOutcomes.Cancelled), Of(FollowUpOutcomes.ClosedBySystem), Of(FollowUpOutcomes.OpenNotYetDue), Of(FollowUpOutcomes.OpenOverdue), Of(FollowUpOutcomes.CompletedLate), Of(FollowUpOutcomes.OpenUnassigned));
            }
            static int Exclusive(FollowUpOutcomeCounts c) => c.Completed + c.Cancelled + c.ClosedBySystem + c.OpenNotYetDue + c.OpenOverdue;

            var kinds = FollowUpOutcomes.Kinds.Select(k => new FollowUpKindRow(k, CountsOf([k]))).ToList();
            var totals = CountsOf(FollowUpOutcomes.Kinds);
            var reconciled = totals.Due == total && Exclusive(totals) == total && kinds.Sum(k => k.Counts.Due) == total && kinds.All(k => Exclusive(k.Counts) == k.Counts.Due);
            data = new FollowUpData(asOf, totals, kinds, reconciled);
        }

        await ReportSupport.AuditAsync(db, log, ReportIds.FollowUps, false, filter, data.Totals.Due, actor, ct);
        return new ReportResult<FollowUpData>(ReportIds.FollowUps, DefinitionVersion, ReportSupport.View(filter), clock.UtcNow, consistency, data.Totals.Due, data.Totals.Due == 0, data);
    }
}
