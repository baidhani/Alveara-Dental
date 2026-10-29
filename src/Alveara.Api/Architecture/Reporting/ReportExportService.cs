using System.Data.Common;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Reporting;

/// <summary>A CSV ready to send: the bytes (UTF-8 with a byte-order mark) and a file name that says what it holds.</summary>
public sealed record ReportFile(byte[] Bytes, string FileName);

/// <summary>
/// ALV-N006 slice 7: hands a report over as a CSV. The caller has ALREADY run the report through its own service, which checked the filter, applied the permission of its controller and recorded the run, so a person can
/// export exactly what they could view and nothing more. This adds the second record: that the report was taken away as a file (who, which report, how long a range, how many lines; never a value). If that record cannot
/// be saved the file is not handed over (503 <c>audit_unavailable</c>): a copy that cannot be accounted for is not made. A file with more than <see cref="ReportOptions.MaxExportRows"/> lines is refused (413
/// <c>export_too_large</c>), never cut short. Reading the same report twice makes two files with the same numbers and two audit entries: an export is a read, so repeating it changes nothing but the log.
/// </summary>
public class ReportExportService(AlveraDbContext db, IOptions<ReportOptions> options, ILogger<ReportExportService> log)
{
    public async Task<ReportFile> CsvAsync<T>(ReportResult<T> report, Guid actor, CancellationToken ct)
    {
        var (text, dataLines) = ReportCsv.Build(report);
        if (dataLines > options.Value.MaxExportRows)
            throw new ReportException("export_too_large", "That report has too many lines for one file. Choose a shorter range.", 413);

        AuditService.Record(db, AuditEventTypes.ReportExported, "Report", Guid.NewGuid(), actor, $"Report {report.ReportId} exported as CSV: {report.Filter.Days} day(s), {dataLines} line(s).");
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is DbUpdateException or DbException)
        {
            log.LogError("event=report_export_audit_failed outcome=failure error_class={ErrorClass} report={Report}", ex.GetType().Name, report.ReportId);
            throw new ReportException("audit_unavailable", "The report could not be recorded, so it was not exported. Try again.", 503);
        }
        return new(Encoding.UTF8.GetBytes(text), $"{report.ReportId}_{report.Filter.From}_to_{report.Filter.To}.csv");
    }
}
