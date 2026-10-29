using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Reporting;

/// <summary>One follow-up task behind a cell: ids and facts only. No title and no note (they are free text and may name a patient's circumstances).</summary>
public sealed record FollowUpTaskRow(Guid Id, Guid PatientId, string Kind, string Status, string Priority, DateTimeOffset DueAtUtc, DateTimeOffset? ClosedAtUtc, Guid? AssigneeUserId, bool ClosedBySystem);

public sealed record FollowUpCursor(DateTimeOffset AfterDueAtUtc, Guid AfterId);

/// <summary><see cref="AsOfUtc"/> is the moment the judgements were made; <see cref="Total"/> is the number of tasks in the cell that was clicked (a reconciliation); <see cref="Rows"/> is one page.</summary>
public sealed record FollowUpReportPage(string ReportId, ReportFilterView Filter, DateTimeOffset AsOfUtc, int Total, IReadOnlyList<FollowUpTaskRow> Rows, FollowUpCursor? Next);

/// <summary>Which tasks: one kind and/or one outcome (see <see cref="FollowUpOutcomes"/>), the moment to judge at (the report's <c>AsOfUtc</c>, so the cell matches exactly), and the page.</summary>
public sealed record FollowUpDrillCriteria(string? Kind, string? Outcome, DateTimeOffset? AsOfUtc, int? Limit, DateTimeOffset? AfterDueAtUtc, Guid? AfterId);

/// <summary>
/// ALV-N006 slice 4: the follow-up tasks behind a number of the follow-up report. The outcome uses the SAME definition as the report (<see cref="FollowUpOutcomes.Predicate"/>), judged at the same moment when the caller passes
/// the report's <c>AsOfUtc</c>, so the total equals the cell. Pages are keyed on (due time, id). Every call is recorded like a report run. Failure modes handled: an unknown kind or outcome, a bad limit or half a cursor (stable
/// 400 codes), a provider or location filter (<c>filter_not_supported</c>), an audit entry that cannot be saved (503).
/// </summary>
public class FollowUpDrillService(AlveraDbContext db, IPracticeClock clock, IOptions<ReportOptions> options, ILogger<FollowUpDrillService> log)
{
    public async Task<FollowUpReportPage> TasksAsync(ReportFilter filter, FollowUpDrillCriteria criteria, Guid actor, CancellationToken ct)
    {
        var opts = options.Value;
        if (filter.ProviderIds.Count > 0 || filter.LocationId is not null) throw new ReportException("filter_not_supported", "That filter is not available for follow-up.", 400);
        if (criteria.Kind is not null && !FollowUpOutcomes.Kinds.Contains(criteria.Kind)) throw new ReportException("invalid_kind", $"The kind must be one of {string.Join(", ", FollowUpOutcomes.Kinds)}.", 400);
        if (criteria.Outcome is not null && !FollowUpOutcomes.All.Contains(criteria.Outcome)) throw new ReportException("invalid_outcome", $"The outcome must be one of {string.Join(", ", FollowUpOutcomes.All)}.", 400);
        var limit = criteria.Limit ?? opts.DefaultDrillRows;
        if (limit < 1 || limit > opts.MaxDrillRows) throw new ReportException("invalid_limit", $"The page size must be between 1 and {opts.MaxDrillRows}.", 400);
        if ((criteria.AfterDueAtUtc is null) != (criteria.AfterId is null)) throw new ReportException("invalid_cursor", "Give both parts of the page position, or neither.", 400);

        var asOf = criteria.AsOfUtc ?? clock.UtcNow;
        int total;
        List<FollowUpTaskRow> rows;
        await using (await ReportReadSession.OpenAsync(db, log, ct))
        {
            var query = new FollowUpQueries(db).Tasks(filter.StartUtc, filter.EndUtc, asOf);
            if (criteria.Kind is { } kind) query = query.Where(t => t.Kind == kind);
            if (criteria.Outcome is { } outcome) query = query.Where(FollowUpOutcomes.Predicate(outcome));
            total = await query.CountAsync(ct);
            if (criteria.AfterDueAtUtc is { } afterDue && criteria.AfterId is { } afterId)
                query = query.Where(t => t.DueAtUtc > afterDue || (t.DueAtUtc == afterDue && t.Id.CompareTo(afterId) > 0));
            rows = await query.OrderBy(t => t.DueAtUtc).ThenBy(t => t.Id).Take(limit + 1)
                .Select(t => new FollowUpTaskRow(t.Id, t.PatientId, t.Kind, t.Status, t.Priority, t.DueAtUtc, t.ClosedAtUtc, t.AssigneeUserId, t.ClosedBySystem)).ToListAsync(ct);
        }

        var more = rows.Count > limit;
        if (more) rows.RemoveAt(rows.Count - 1);
        await ReportSupport.AuditAsync(db, log, ReportIds.FollowUps, true, filter, rows.Count, actor, ct);
        return new FollowUpReportPage(ReportIds.FollowUps, ReportSupport.View(filter), asOf, total, rows, more ? new FollowUpCursor(rows[^1].DueAtUtc, rows[^1].Id) : null);
    }
}
