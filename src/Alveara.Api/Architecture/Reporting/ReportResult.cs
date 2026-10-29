using System.Data.Common;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Scheduling;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Reporting;

/// <summary>The filter as the report reports it back, so a printed or saved report says exactly what it covers.</summary>
public sealed record ReportFilterView(string From, string To, int Days, IReadOnlyList<Guid> ProviderIds, Guid? LocationId, string TimeZone);

/// <summary>
/// The envelope every report comes in. <see cref="DefinitionVersion"/> is the version of the report's metric definitions (it changes whenever what a number means changes, so two runs are never silently compared across
/// definitions); <see cref="ReadConsistency"/> says whether every table was read at one moment (<c>Snapshot</c>) or table by table (<c>TableByTable</c>); <see cref="NoData"/> is true when nothing matched (an honest empty
/// state, never a zero that looks like a measurement).
/// </summary>
public sealed record ReportResult<T>(string ReportId, int DefinitionVersion, ReportFilterView Filter, DateTimeOffset GeneratedAtUtc, string ReadConsistency, int RecordCount, bool NoData, T Data);

public static class ReportIds
{
    public const string Schedule = "schedule";
    public const string PatientFlow = "patient-flow";
    public const string Production = "production";
    public const string Collections = "collections";
    public const string Balances = "balances";
    public const string TreatmentPlans = "treatment-plans";
    public const string CompletedProcedures = "completed-procedures";
}

public static class ReportReadModes
{
    public const string Snapshot = "Snapshot";
    public const string TableByTable = "TableByTable";
}

/// <summary>
/// How a report reads the database: inside one snapshot transaction when the database allows snapshot isolation (so the counts of one report agree with each other even while the practice works), otherwise table by table;
/// the envelope says which. Disposing ends the transaction; a failure there is logged with its class and does not hide the report.
/// </summary>
public sealed class ReportReadSession : IAsyncDisposable
{
    private readonly ILogger _log;
    private IDbContextTransaction? _transaction;

    private ReportReadSession(ILogger log) => _log = log;

    public string ReadConsistency { get; private set; } = ReportReadModes.TableByTable;

    public static async Task<ReportReadSession> OpenAsync(AlveraDbContext db, ILogger log, CancellationToken ct)
    {
        var session = new ReportReadSession(log);
        if (db.Database.CurrentTransaction is not null) return session;     // the caller already holds a transaction: join it, start nothing
        var state = await db.Database.SqlQuery<int>($"SELECT CAST(snapshot_isolation_state AS int) AS [Value] FROM sys.databases WHERE name = DB_NAME()").SingleAsync(ct);
        if (state == 1)
        {
            session._transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Snapshot, ct);
            session.ReadConsistency = ReportReadModes.Snapshot;
        }
        return session;
    }

    public async ValueTask DisposeAsync()
    {
        if (_transaction is null) return;
        try
        {
            await _transaction.DisposeAsync();
        }
        catch (Exception ex) when (ex is DbException or InvalidOperationException)
        {
            _log.LogError("event=report_read_session_dispose_failed outcome=failure error_class={ErrorClass}", ex.GetType().Name);
        }
    }
}

/// <summary>Shared rules of every report: which appointments are in scope, how a run is recorded, and the envelope.</summary>
public static class ReportSupport
{
    /// <summary>The appointments whose start falls in the range, for the chosen booked providers and, when asked, the location of the booked operatory.</summary>
    public static IQueryable<Appointment> Appointments(AlveraDbContext db, ReportFilter filter)
    {
        var query = db.Appointments.AsNoTracking().Where(a => a.StartUtc >= filter.StartUtc && a.StartUtc < filter.EndUtc);
        var providers = filter.ProviderIds.ToArray();
        if (providers.Length > 0) query = query.Where(a => providers.Contains(a.ProviderProfileId));
        if (filter.LocationId is { } location) query = query.Where(a => db.Operatories.Any(o => o.Id == a.OperatoryId && o.LocationId == location));
        return query;
    }

    public static ReportFilterView View(ReportFilter filter) =>
        new(filter.From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), filter.To.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), filter.Days, filter.ProviderIds, filter.LocationId, filter.TimeZoneId);

    /// <summary>Refuses a run that would read more rows than <see cref="ReportOptions.MaxSourceRows"/>: better a clear refusal than a number from a cut-off set.</summary>
    public static void GuardSize(int sourceRows, ReportOptions options)
    {
        if (sourceRows > options.MaxSourceRows)
            throw new ReportException("report_too_large", "That range holds too many records for one report. Choose a shorter range or fewer providers.", 413);
    }

    /// <summary>
    /// Records that a report (or its supporting records) was read: who, which report, how long a range and how many records, never a patient or a value other than ids. If the entry cannot be saved the run is refused
    /// (503 <c>audit_unavailable</c>) and nothing is returned: a report that cannot be accounted for is not delivered.
    /// </summary>
    public static async Task AuditAsync(AlveraDbContext db, ILogger log, string reportId, bool supportingRecords, ReportFilter filter, int records, Guid actor, CancellationToken ct)
    {
        var scope = (filter.ProviderIds.Count > 0 ? $", {filter.ProviderIds.Count} provider filter(s)" : "") + (filter.LocationId is null ? "" : ", a location filter");
        AuditService.Record(db, AuditEventTypes.ReportViewed, "Report", Guid.NewGuid(), actor, $"Report {reportId}{(supportingRecords ? " (supporting records)" : "")}: {filter.Days} day(s), {records} record(s){scope}.");
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is DbUpdateException or DbException)
        {
            log.LogError("event=report_audit_failed outcome=failure error_class={ErrorClass} report={Report}", ex.GetType().Name, reportId);
            throw new ReportException("audit_unavailable", "The report could not be recorded, so it was not produced. Try again.", 503);
        }
    }
}
