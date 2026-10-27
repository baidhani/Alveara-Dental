using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Patients.Portability.Package;

/// <summary>
/// One line of the export history. No patient name and no value from any record: the patient is an id, the person is a staff display name, the file is a size and a hash. <paramref name="Interrupted"/> is true for a
/// row still Started long after it began (the server stopped, or the request was lost): it is shown as interrupted and the row itself is never rewritten.
/// </summary>
public sealed record ExportHistoryItem(
    Guid ExportId, string Kind, string Scope, Guid? PatientId, bool IncludesDocumentFiles, string RequestedBy, DateTimeOffset RequestedAtUtc, DateTimeOffset? FinishedAtUtc,
    string Result, bool Interrupted, int PatientCount, int FileCount, long SizeBytes, string? Sha256, int ProblemCount);

public sealed record OverviewDomain(string Key, string Title);

public sealed record OverviewExcluded(string Key, string Reason);

/// <summary>Everything the Export screen needs to explain an export before it is started: what a package holds and what it deliberately leaves out, the limits, how many patients and how many bytes of documents there are now, how an all-patients export would read the database, and the practice time zone the screen shows times in.</summary>
public sealed record ExportOverview(
    IReadOnlyList<OverviewDomain> Domains, IReadOnlyList<OverviewExcluded> Excluded, int MaxPatients, long MaxPackageBytes, string DefaultFolderNaming,
    string AllPatientsReadMode, int PatientCount, long DocumentBytes, bool AllPatientsWithinLimits, string PracticeTimeZone);

/// <summary>
/// ALV-018-C01: reads of the export history and the overview for the Export screen. They write nothing and show no patient content. A person who may only export one patient sees only their own exports; a person who
/// holds the bulk export sees everyone's (the caller decides which, see <c>PatientExportsController</c>).
/// </summary>
public class ExportHistoryService(AlveraDbContext db, IPracticeClock clock, IOptions<DataExportOptions> options)
{
    public const int DefaultLimit = 50, MaxLimit = 200;
    private readonly DataExportOptions _options = options.Value;

    public async Task<IReadOnlyList<ExportHistoryItem>> ListAsync(Guid actor, bool seeEveryone, int limit, DateTimeOffset? before, CancellationToken ct)
    {
        var query = db.DataExportHistory.AsNoTracking();
        if (!seeEveryone) query = query.Where(h => h.RequestedByUserId == actor);
        if (before is { } cursor) query = query.Where(h => h.RequestedAtUtc < cursor);
        var rows = await query.OrderByDescending(h => h.RequestedAtUtc).ThenByDescending(h => h.Id).Take(limit)
            .Select(h => new { Row = h, Name = db.StaffProfiles.Where(s => s.UserAccountId == h.RequestedByUserId).Select(s => s.DisplayName).FirstOrDefault() })
            .ToListAsync(ct);
        var staleBefore = clock.UtcNow - TimeSpan.FromMinutes(_options.InterruptedAfterMinutes);
        return rows.Select(r => new ExportHistoryItem(
            r.Row.ExportId, r.Row.Kind, r.Row.Scope, r.Row.SubjectRecordId, r.Row.IncludesDocumentFiles, r.Name ?? "Unknown staff member", r.Row.RequestedAtUtc, r.Row.FinishedAtUtc,
            r.Row.Result, r.Row.Result == DataExportResult.Started && r.Row.RequestedAtUtc < staleBefore, r.Row.PatientCount, r.Row.FileCount, r.Row.SizeBytes, r.Row.Sha256, r.Row.ProblemCount)).ToList();
    }

    public async Task<ExportOverview> OverviewAsync(CancellationToken ct)
    {
        var patients = await db.Patients.AsNoTracking().CountAsync(p => p.MergedAtUtc == null, ct);
        var documentBytes = await db.Documents.AsNoTracking().Where(d => db.Patients.Any(p => p.Id == d.PatientId && p.MergedAtUtc == null)).SumAsync(d => (long?)d.SizeBytes, ct) ?? 0;
        var snapshot = await db.Database.SqlQueryRaw<int>("SELECT CAST(snapshot_isolation_state AS int) AS [Value] FROM sys.databases WHERE name = DB_NAME()").SingleAsync(ct);
        return new ExportOverview(
            ExportDomainCatalog.Domains.Select(d => new OverviewDomain(d.Key, d.Title)).ToList(), ExportDomainCatalog.Excluded.Select(g => new OverviewExcluded(g.Key, g.Reason)).ToList(),
            _options.MaxPatients, _options.MaxPackageBytes, _options.DefaultFolderNaming, snapshot == 1 ? ReadMode.Snapshot : ReadMode.TableByTable,
            patients, documentBytes, patients <= _options.MaxPatients && documentBytes <= _options.MaxPackageBytes, clock.PracticeTimeZone.Id);
    }
}
