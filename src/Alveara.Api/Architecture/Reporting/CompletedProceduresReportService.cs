using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Reporting;

public sealed record ProcedureDayRow(string Date, int Procedures);

public sealed record ProcedureProviderRow(Guid ProviderProfileId, string ProviderName, int Procedures, decimal EstimatedFee);

public sealed record ProcedureCodeRow(string CodeSystem, string Code, string Description, int Procedures, decimal EstimatedFee);

/// <summary>
/// The completed-procedures report. The days, the providers and the codes are three cuts of the same procedures, so each adds up to <see cref="Procedures"/>; <see cref="Reconciled"/> says so. <see cref="EstimatedFee"/> is the
/// practice's fee estimate on the plan items, not a ledger figure (the financial reports are the ledger).
/// </summary>
public sealed record CompletedProceduresData(int Procedures, decimal EstimatedFee, IReadOnlyList<ProcedureDayRow> Days, IReadOnlyList<ProcedureProviderRow> Providers, IReadOnlyList<ProcedureCodeRow> Codes, bool Reconciled);

/// <summary>
/// ALV-N006 slice 3a: completed procedures. Source: the completed procedures whose EFFECTIVE performed time falls in the range (an amendment that corrects the time moves the procedure to the right day), by the provider now
/// credited and by catalog code. The provider filter narrows every view; a location filter is refused (<c>filter_not_supported</c>): a completed procedure has no location. Failure modes handled: nothing in the range (<c>NoData</c>),
/// more procedures than the limit (413), an audit entry that cannot be saved (503). Definition version 1.
/// </summary>
public class CompletedProceduresReportService(AlveraDbContext db, IPracticeClock clock, IOptions<ReportOptions> options, ILogger<CompletedProceduresReportService> log)
{
    public const int DefinitionVersion = 1;

    public async Task<ReportResult<CompletedProceduresData>> RunAsync(ReportFilter filter, Guid actor, CancellationToken ct)
    {
        if (filter.LocationId is not null) throw new ReportException("filter_not_supported", "Completed procedures cannot be filtered by location: a completed procedure has none.", 400);
        var queries = new ClinicalQueries(db);
        string consistency;
        CompletedProceduresData data;
        await using (var session = await ReportReadSession.OpenAsync(db, log, ct))
        {
            consistency = session.ReadConsistency;
            var lines = queries.ProcedureLines(filter.StartUtc, filter.EndUtc, filter.ProviderIds.ToArray());
            ReportSupport.GuardSize(await lines.CountAsync(ct), options.Value);
            var rows = await lines.Select(l => new { l.ProviderProfileId, l.PerformedAtUtc, l.CodeSystem, l.Code, l.Description, l.EstimatedFee }).ToListAsync(ct);
            var providerIds = rows.Select(r => r.ProviderProfileId).Concat(filter.ProviderIds).Distinct().ToArray();
            var names = await db.ProviderProfiles.AsNoTracking().Where(p => providerIds.Contains(p.Id)).Select(p => new { p.Id, Name = p.StaffProfile!.DisplayName }).ToDictionaryAsync(p => p.Id, p => p.Name, ct);

            var byDay = rows.GroupBy(r => DateOnly.FromDateTime(clock.ToPracticeLocal(r.PerformedAtUtc).DateTime)).ToDictionary(g => g.Key, g => g.Count());
            var days = new List<ProcedureDayRow>(filter.Days);
            for (var day = filter.From; day <= filter.To; day = day.AddDays(1)) days.Add(new ProcedureDayRow(day.ToString("yyyy-MM-dd"), byDay.GetValueOrDefault(day)));
            var providers = providerIds.Select(id => new ProcedureProviderRow(id, names.GetValueOrDefault(id, "Unknown provider"), rows.Count(r => r.ProviderProfileId == id), rows.Where(r => r.ProviderProfileId == id).Sum(r => r.EstimatedFee)))
                .OrderBy(p => p.ProviderName, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.ProviderProfileId).ToList();
            var codes = rows.GroupBy(r => (r.CodeSystem, r.Code)).Select(g => new ProcedureCodeRow(g.Key.CodeSystem, g.Key.Code, g.First().Description, g.Count(), g.Sum(r => r.EstimatedFee)))
                .OrderBy(c => c.CodeSystem, StringComparer.Ordinal).ThenBy(c => c.Code, StringComparer.Ordinal).ToList();
            var reconciled = days.Sum(d => d.Procedures) == rows.Count && providers.Sum(p => p.Procedures) == rows.Count && codes.Sum(c => c.Procedures) == rows.Count;
            data = new CompletedProceduresData(rows.Count, rows.Sum(r => r.EstimatedFee), days, providers, codes, reconciled);
        }

        await ReportSupport.AuditAsync(db, log, ReportIds.CompletedProcedures, false, filter, data.Procedures, actor, ct);
        return new ReportResult<CompletedProceduresData>(ReportIds.CompletedProcedures, DefinitionVersion, ReportSupport.View(filter), clock.UtcNow, consistency, data.Procedures, data.Procedures == 0, data);
    }
}
