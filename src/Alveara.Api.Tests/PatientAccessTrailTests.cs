using System.Net;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Identity;
using Session = Alveara.Api.Tests.SchedulingApiHarness.Session;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N025 slice 5 (PD-20; owner decisions EXP-2, SES-2, OQ-1) against real SQL Server and the real HTTP pipeline:
/// - a patient's record open is written once per patient per session (the workspace load), not for searches or the history list; another session writes its own;
///   a cookie from before this release (no sid) writes each open, without a row (SES-2); the trail is append-only (51299);
/// - a print request from an in-application Print button writes PatientMaterialPrinted (the request); an unknown material is 400, an unknown patient 404;
/// - each export path writes exactly one event of its own: PatientExported, PatientDataPackageExported, PatientReadableRecordExported (EXP-2: no duplicate row);
/// - the migration's Down is refused (51300) once either table holds a row.
/// Every event is PHI-free: no patient name or contact detail in its details.
/// </summary>
public class PatientAccessTrailTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private SchedulingTestSupport _s = null!;
    private SchedulingApiHarness _api = null!;
    private Session _admin = null!;
    private Guid _patient;

    public Task InitializeAsync() => _fixture.InitializeOwnerAsync(async () =>
    {
        _s = new SchedulingTestSupport(_fixture);
        _api = new SchedulingApiHarness(_fixture);
        await _s.ArrangeAsync();
        _patient = await _s.PatientAsync("Quinn", "Access");
        _admin = await _api.SessionAsync("Admin");
    });

    public Task DisposeAsync() { _api.Dispose(); return _fixture.DisposeAsync(); }

    private async Task<int> EventsAsync(string type) { await using var db = _fixture.CreateContext(); return await db.AuditLogEntries.CountAsync(a => a.EventType == type && a.TargetUserAccountId == _patient); }
    private async Task<int> OpenRowsAsync() { await using var db = _fixture.CreateContext(); return await db.PatientRecordOpens.CountAsync(o => o.PatientId == _patient); }
    private async Task SqlAsync(string sql) { await using var db = _fixture.CreateContext(); await db.Database.ExecuteSqlRawAsync(sql); }

    private static int SqlNumber(Exception e)
    {
        for (Exception? x = e; x is not null; x = x.InnerException) if (x is SqlException s) return s.Number;
        return 0;
    }

    [Fact]
    public async Task A_record_open_is_written_once_per_patient_per_session_and_never_for_searches_or_the_history()
    {
        Assert.Equal(HttpStatusCode.OK, (await _admin.SendAsync(HttpMethod.Get, $"/api/patients/{_patient}")).Status);
        Assert.Equal(HttpStatusCode.OK, (await _admin.SendAsync(HttpMethod.Get, $"/api/patients/{_patient}")).Status);
        await _admin.SendAsync(HttpMethod.Get, "/api/patients?query=Access");
        await _admin.SendAsync(HttpMethod.Get, $"/api/patients/{_patient}/history");
        Assert.Equal((1, 1), (await EventsAsync(PatientAccessEvents.RecordOpened), await OpenRowsAsync()));

        var billing = await _api.SessionAsync("Billing");                                                     // another session (another person)
        await billing.SendAsync(HttpMethod.Get, $"/api/patients/{_patient}");
        Assert.Equal((2, 2), (await EventsAsync(PatientAccessEvents.RecordOpened), await OpenRowsAsync()));

        await using var db = _fixture.CreateContext();
        var e = await db.AuditLogEntries.FirstAsync(a => a.EventType == PatientAccessEvents.RecordOpened && a.TargetUserAccountId == _patient && a.PerformedByUserAccountId == _admin.UserId);   // the admin's own row (two sessions wrote one each)
        Assert.Equal(("Patient", _admin.UserId), (e.EntityType, e.PerformedByUserAccountId));
        foreach (var leak in new[] { "Quinn", "Access" }) Assert.DoesNotContain(leak, e.Details);
    }

    [Fact]
    public async Task SES2_a_cookie_without_a_session_id_writes_each_open_without_a_row()
    {
        await using (var db = _fixture.CreateContext())
        {
            Assert.True(await new PatientAccessRecorder(db).RecordOpenAsync(_patient, _admin.UserId, null, DateTimeOffset.UtcNow, default));
            Assert.True(await new PatientAccessRecorder(db).RecordOpenAsync(_patient, _admin.UserId, null, DateTimeOffset.UtcNow, default));
        }
        Assert.Equal((2, 0), (await EventsAsync(PatientAccessEvents.RecordOpened), await OpenRowsAsync()));
    }

    [Fact]
    public async Task The_record_open_trail_is_append_only()
    {
        await _admin.SendAsync(HttpMethod.Get, $"/api/patients/{_patient}");
        Assert.Equal(51299, SqlNumber(await Assert.ThrowsAnyAsync<Exception>(() => SqlAsync($"UPDATE [PatientRecordOpens] SET [FirstOpenedAtUtc] = SYSDATETIMEOFFSET() WHERE [PatientId] = '{_patient}'"))));
        Assert.Equal(51299, SqlNumber(await Assert.ThrowsAnyAsync<Exception>(() => SqlAsync($"DELETE FROM [PatientRecordOpens] WHERE [PatientId] = '{_patient}'"))));
    }

    [Fact]
    public async Task A_print_request_is_recorded_as_the_request_and_bad_input_is_refused()
    {
        var resource = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await _admin.SendAsync(HttpMethod.Post, $"/api/patients/{_patient}/print-events", new { materialType = "Statement", resourceId = resource })).Status);
        await using (var db = _fixture.CreateContext())
        {
            var e = await db.AuditLogEntries.SingleAsync(a => a.EventType == PatientAccessEvents.MaterialPrinted && a.TargetUserAccountId == _patient);
            Assert.Contains("Statement", e.Details);
            Assert.Contains(resource.ToString(), e.Details);
            Assert.Contains("outcome requested", e.Details);
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await _admin.SendAsync(HttpMethod.Post, $"/api/patients/{_patient}/print-events", new { materialType = "Poster" })).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await _admin.SendAsync(HttpMethod.Post, $"/api/patients/{Guid.NewGuid()}/print-events", new { materialType = "Receipt" })).Status);
        Assert.NotEqual(HttpStatusCode.NoContent, (await _admin.SendAsync(HttpMethod.Post, $"/api/patients/{_patient}/print-events", new { materialType = "Receipt" }, csrf: false)).Status);
        Assert.Equal(1, await EventsAsync(PatientAccessEvents.MaterialPrinted));
    }

    [Fact]
    public async Task EXP2_each_export_path_writes_exactly_one_event_of_its_own()
    {
        Assert.Equal(HttpStatusCode.OK, (await _admin.Client.GetAsync($"/api/patients/{_patient}/export")).StatusCode);
        var readable = new HttpRequestMessage(HttpMethod.Post, $"/api/patient-exports/patients/{_patient}/readable-record");   // an HTML file, not JSON
        readable.Headers.Add("X-CSRF-Token", _admin.Csrf);
        Assert.Equal(HttpStatusCode.OK, (await _admin.Client.SendAsync(readable)).StatusCode);
        var package = new HttpRequestMessage(HttpMethod.Post, $"/api/patient-exports/patients/{_patient}/package");
        package.Headers.Add("X-CSRF-Token", _admin.Csrf);
        Assert.Equal(HttpStatusCode.OK, (await _admin.Client.SendAsync(package)).StatusCode);

        Assert.Equal(1, await EventsAsync(AuditEventTypes.PatientExported));
        Assert.Equal(1, await EventsAsync(AuditEventTypes.PatientReadableRecordExported));
        Assert.Equal(1, await EventsAsync(AuditEventTypes.PatientDataPackageExported));
        Assert.Equal(0, await EventsAsync("PatientRecordExported"));                                          // the conceptual category, never a stored name
    }

    [Fact]
    public async Task The_migrations_down_is_refused_once_access_rows_exist()
    {
        await _admin.SendAsync(HttpMethod.Get, $"/api/patients/{_patient}");                                  // a session row and an open row now exist
        await using var db = _fixture.CreateContext();
        var e = await Assert.ThrowsAnyAsync<Exception>(() => db.GetInfrastructure().GetRequiredService<IMigrator>().MigrateAsync("20261120054453_AddPatientCountry"));
        Assert.Equal(51300, SqlNumber(e));
        Assert.Contains("20261120125553_AddPatientAccessAudit", await db.Database.GetAppliedMigrationsAsync());
    }
}
