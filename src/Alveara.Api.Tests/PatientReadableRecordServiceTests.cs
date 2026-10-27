using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Patients;
using Alveara.Api.Architecture.Patients.Merge;
using Alveara.Api.Architecture.Patients.Portability.Package;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-018-C01, the stand-alone readable record against a real SQL Server and a real chart: the page is the patient's record as the package would carry it but says it is on its own (documents listed by name, the
/// files only in the full package); the history row is kind ReadableRecord for one patient with no document files and carries the size and the hash of the page; the audit entry is the readable-record event with
/// no patient detail and no package event is written; an unknown patient is 404 and a merged-away patient 409 with nothing recorded; when the audit entry cannot be saved nothing is returned, the row is Failed and
/// the failure is logged; and doing it twice makes two separate exports.
/// </summary>
public class PatientReadableRecordServiceTests : MergeChartTestBase
{
    private Task<ReadableRecordFile> ExportAsync(Guid patient)
    {
        var services = new ServiceCollection().AddScoped(_ => Fixture.CreateContext()).BuildServiceProvider();
        var db = Fixture.CreateContext();
        var recorder = new ExportFailureRecorder(services.GetRequiredService<IServiceScopeFactory>(), Clock, NullLogger<ExportFailureRecorder>.Instance);
        return new PatientReadableRecordService(db, new ReadableRecordLoader(db, Clock), Clock, recorder).ExportAsync(patient, S.Actor, default);
    }

    [Fact]
    public async Task The_page_is_the_patients_record_and_says_it_stands_alone_with_documents_listed_but_not_included()
    {
        await GiveRetiringChartAsync(); await GiveSurvivingChartAsync();
        var ann = await WithDb(db => db.Patients.AsNoTracking().SingleAsync(p => p.Id == Ann));
        var bo = await WithDb(db => db.Patients.AsNoTracking().SingleAsync(p => p.Id == Bo));

        var file = await ExportAsync(Ann);

        var page = Encoding.UTF8.GetString(file.Content);
        Assert.Contains($"<h1>{ann.FirstName} {ann.LastName}</h1>", page);
        Assert.Contains("Treatment plans", page);
        Assert.Contains("pano.png", page);                                                                          // the document is listed by name
        Assert.Contains("complete export package, not here", page);                                                 // and the page says its file is not here
        Assert.DoesNotContain("<em>documents</em> folder", page);
        Assert.DoesNotContain($"{bo.FirstName} {bo.LastName}", page);
        Assert.Contains(file.ExportId.ToString(), page);
        Assert.Matches(@"^alveara-readable-record-[0-9a-f]{32}\.html$", file.FileName);
    }

    [Fact]
    public async Task The_history_row_and_the_audit_entry_record_a_readable_record_for_one_patient_with_no_detail()
    {
        await GiveRetiringChartAsync();
        var firstName = await WithDb(db => db.Patients.AsNoTracking().Where(p => p.Id == Ann).Select(p => p.FirstName).SingleAsync());

        var file = await ExportAsync(Ann);

        var row = await WithDb(db => db.DataExportHistory.AsNoTracking().SingleAsync(h => h.ExportId == file.ExportId));
        Assert.Equal((DataExportKind.ReadableRecord, DataExportScope.OnePatient, Ann, false, S.Actor, DataExportResult.Complete), (row.Kind, row.Scope, row.SubjectRecordId, row.IncludesDocumentFiles, row.RequestedByUserId, row.Result));
        Assert.Equal((1, 1, file.SizeBytes, 0), (row.PatientCount, row.FileCount, row.SizeBytes, row.ProblemCount));
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(file.Content)), row.Sha256);
        Assert.Equal(file.Sha256, row.Sha256);
        var entry = await WithDb(db => db.AuditLogEntries.AsNoTracking().SingleAsync(a => a.EventType == AuditEventTypes.PatientReadableRecordExported));
        Assert.Equal((S.Actor, Ann, file.ExportId), (entry.PerformedByUserAccountId, entry.TargetUserAccountId, entry.CorrelationId));
        Assert.DoesNotContain(firstName, entry.Details);
        Assert.Equal(0, await WithDb(db => db.AuditLogEntries.CountAsync(a => a.EventType == AuditEventTypes.PatientDataPackageExported)));      // not mistaken for a package
    }

    [Fact]
    public async Task An_unknown_and_a_merged_away_patient_are_refused_with_nothing_recorded()
    {
        var unknown = await Assert.ThrowsAsync<PatientException>(() => ExportAsync(Guid.NewGuid()));
        var preview = await WithDb(db => new PatientMergeService(db, Clock).PreviewAsync(Bo, Ann, default));
        await WithDb(db => new PatientMergeService(db, Clock).MergeAsync(S.Actor, new MergeRequest(Bo, Ann, "Registered twice", preview.SourceRowVersion, preview.SurvivorRowVersion, preview.Conflicts.ToDictionary(c => c.Field, _ => "Survivor"), true, true), Guid.NewGuid().ToString("N"), default));
        var merged = await Assert.ThrowsAsync<PatientException>(() => ExportAsync(Bo));

        Assert.Equal(("patient_not_found", 404, "patient_merged", 409), (unknown.Code, unknown.StatusCode, merged.Code, merged.StatusCode));
        Assert.Equal(0, await WithDb(db => db.DataExportHistory.CountAsync()));
    }

    [Fact]
    public async Task When_the_audit_entry_cannot_be_saved_nothing_is_returned_the_row_is_failed_and_the_failure_is_logged()
    {
        await GiveRetiringChartAsync();
        await WithDb(db => db.Database.ExecuteSqlRawAsync("CREATE TRIGGER [TR_TestFault] ON [AuditLogEntries] AFTER INSERT AS BEGIN IF EXISTS (SELECT 1 FROM inserted WHERE [EventType] = 'PatientReadableRecordExported') THROW 59033, 'the audit store is down', 1; END"));
        try
        {
            await Assert.ThrowsAnyAsync<Exception>(() => ExportAsync(Ann));

            var row = await WithDb(db => db.DataExportHistory.AsNoTracking().SingleAsync());
            Assert.Equal((DataExportKind.ReadableRecord, DataExportResult.Failed), (row.Kind, row.Result));
            Assert.NotNull(row.FinishedAtUtc);
            Assert.Null(row.Sha256);                                                                                // no file was recorded as delivered
            Assert.Equal(0, await WithDb(db => db.AuditLogEntries.CountAsync(a => a.EventType == AuditEventTypes.PatientReadableRecordExported)));
            Assert.Equal(1, await WithDb(db => db.AuditLogEntries.CountAsync(a => a.EventType == AuditEventTypes.DataExportFailed)));
        }
        finally
        {
            await WithDb(db => db.Database.ExecuteSqlRawAsync("DROP TRIGGER [TR_TestFault]"));
        }
    }

    [Fact]
    public async Task Doing_it_twice_makes_two_separate_exports_each_with_its_own_row()
    {
        await GiveRetiringChartAsync();

        var first = await ExportAsync(Ann);
        var second = await ExportAsync(Ann);

        Assert.NotEqual(first.ExportId, second.ExportId);
        Assert.Equal(2, await WithDb(db => db.DataExportHistory.CountAsync(h => h.Kind == DataExportKind.ReadableRecord && h.SubjectRecordId == Ann)));
        Assert.Equal(2, await WithDb(db => db.AuditLogEntries.CountAsync(a => a.EventType == AuditEventTypes.PatientReadableRecordExported)));
    }
}
