using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Patients;
using Alveara.Api.Architecture.Patients.Merge;
using Alveara.Api.Architecture.Patients.Portability;
using Alveara.Api.Data;

namespace Alveara.Api.Tests;

/// <summary>
/// STORY-018, the export of one patient's record against a real SQL Server. The file holds exactly the documented layout (the format name and version, an export id, the time, where it came from and the
/// registration fields, and nothing else); every export writes one audit entry with the user and the time and no personal detail; an unknown patient and a patient who was merged away are refused with no
/// file and no log entry; and when the audit entry cannot be saved no file is returned at all.
/// </summary>
public class PatientExportServiceTests : IAsyncLifetime
{
    private static readonly string[] TopLevelMembers = ["format", "formatVersion", "exportId", "exportedAtUtc", "source", "patient"];
    private static readonly string[] PatientMembers =
        ["firstName", "middleName", "lastName", "dateOfBirth", "sex", "phone", "email", "addressLine1", "addressLine2", "city", "state", "postalCode"];

    private readonly TestDatabaseFixture _fixture = new();
    private SchedulingTestSupport _s = null!;

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
        _s = new SchedulingTestSupport(_fixture);
        await _s.ArrangeAsync();
        await using var db = _fixture.CreateContext();
        db.UserAccounts.Add(new UserAccount { Id = _s.Actor, Username = $"admin-{Guid.NewGuid():N}", CreatedAtUtc = DateTimeOffset.UtcNow });         // the acting user is a real account (the merge lineage points at it)
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private async Task<T> WithDb<T>(Func<AlveraDbContext, Task<T>> action) { await using var db = _fixture.CreateContext(); return await action(db); }

    private Task<PatientExportFile> ExportAsync(Guid patient) => WithDb(db => new PatientExportService(db, SchedulingTestSupport.Clock).ExportAsync(patient, _s.Actor, default));

    private Task<List<AuditLogEntry>> ExportLogAsync() => WithDb(db => db.AuditLogEntries.AsNoTracking().Where(a => a.EventType == AuditEventTypes.PatientExported).ToListAsync());

    private async Task<PatientException> RefusedAsync(Func<Task> action) => await Assert.ThrowsAsync<PatientException>(action);

    [Fact]
    public async Task The_file_holds_the_documented_layout_and_the_patients_registration_fields_and_nothing_else()
    {
        var id = await _s.PatientAsync("Ann", "Rivera");
        var patient = await WithDb(db => db.Patients.AsNoTracking().SingleAsync(p => p.Id == id));

        var file = await ExportAsync(id);

        using var json = JsonDocument.Parse(file.Content);                                              // it is valid JSON
        var root = json.RootElement;
        Assert.Equal(TopLevelMembers.Order(), root.EnumerateObject().Select(m => m.Name).Order());      // no member more, none less
        Assert.Equal(PatientExportFormat.FormatName, root.GetProperty("format").GetString());
        Assert.Equal(PatientExportFormat.CurrentVersion, root.GetProperty("formatVersion").GetInt32());
        Assert.Equal(file.ExportId, root.GetProperty("exportId").GetGuid());
        Assert.Equal($"alveara-patient-{file.ExportId:N}.json", file.FileName);
        var exportedAt = DateTimeOffset.Parse(root.GetProperty("exportedAtUtc").GetString()!);
        Assert.True(Math.Abs((exportedAt - SchedulingTestSupport.Clock.UtcNow).TotalMinutes) < 5);
        Assert.EndsWith("Z", root.GetProperty("exportedAtUtc").GetString());                            // always UTC, to the second
        Assert.Equal(PatientExportFormat.SystemName, root.GetProperty("source").GetProperty("system").GetString());
        Assert.Equal(id, root.GetProperty("source").GetProperty("sourcePatientId").GetGuid());

        var p = root.GetProperty("patient");
        Assert.Equal(PatientMembers.Order(), p.EnumerateObject().Select(m => m.Name).Order());          // exactly the registration fields; no ids, no links, no clinical or account data
        Assert.Equal(patient.FirstName, p.GetProperty("firstName").GetString());
        Assert.Equal(patient.LastName, p.GetProperty("lastName").GetString());
        Assert.Equal(patient.DateOfBirth.ToString("yyyy-MM-dd"), p.GetProperty("dateOfBirth").GetString());
        Assert.Equal(patient.Phone, p.GetProperty("phone").GetString());
        Assert.Equal(patient.AddressLine1, p.GetProperty("addressLine1").GetString());
        Assert.Equal(patient.City, p.GetProperty("city").GetString());
        Assert.Equal(patient.State, p.GetProperty("state").GetString());
        Assert.Equal(patient.PostalCode, p.GetProperty("postalCode").GetString());
        foreach (var optional in new[] { "middleName", "sex", "email", "addressLine2" })
            Assert.True(p.TryGetProperty(optional, out _), $"{optional} is written even when empty, so a reader can tell no value from a missing member");
    }

    [Fact]
    public async Task The_format_name_version_and_size_limit_are_the_documented_ones()
    {
        Assert.Equal("alveara.patient-export", PatientExportFormat.FormatName);
        Assert.Equal(1, PatientExportFormat.CurrentVersion);
        Assert.Equal(256 * 1024, PatientExportFormat.MaxFileBytes);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task An_export_writes_one_audit_entry_with_the_user_and_the_time_and_no_personal_details()
    {
        var id = await _s.PatientAsync("Ann", "Rivera");
        var patient = await WithDb(db => db.Patients.AsNoTracking().SingleAsync(p => p.Id == id));
        var before = DateTimeOffset.UtcNow;

        var file = await ExportAsync(id);

        var entry = Assert.Single(await ExportLogAsync());
        Assert.Equal(_s.Actor, entry.PerformedByUserAccountId);                                         // who
        Assert.InRange(entry.TimestampUtc, before.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(1)); // when
        Assert.Equal(nameof(Patient), entry.EntityType);
        Assert.Equal(id, entry.TargetUserAccountId);
        Assert.Equal(file.ExportId, entry.CorrelationId);
        Assert.Contains(file.ExportId.ToString(), entry.Details);
        Assert.Contains(PatientExportFormat.FormatName, entry.Details);
        foreach (var personal in new[] { patient.FirstName, patient.LastName, patient.Phone, patient.AddressLine1, patient.PostalCode, patient.DateOfBirth.ToString("yyyy-MM-dd") })
            Assert.DoesNotContain(personal, entry.Details);                                             // the log names the patient by id, never by value
    }

    [Fact]
    public async Task Exporting_twice_is_two_actions_each_with_its_own_export_id_and_its_own_log_entry()
    {
        var id = await _s.PatientAsync();

        var first = await ExportAsync(id);
        var second = await ExportAsync(id);

        Assert.NotEqual(first.ExportId, second.ExportId);
        Assert.Equal(2, (await ExportLogAsync()).Count);
        Assert.Equal(2, (await ExportLogAsync()).Select(e => e.CorrelationId).Distinct().Count());
    }

    [Fact]
    public async Task An_unknown_patient_and_a_patient_merged_away_are_refused_with_no_file_and_no_log_entry()
    {
        var unknown = await RefusedAsync(() => ExportAsync(Guid.NewGuid()));
        Assert.Equal(("patient_not_found", 404), (unknown.Code, unknown.StatusCode));

        var retired = await _s.PatientAsync("Cy", "Kim");
        var survivor = await _s.PatientAsync("Dee", "Kim");
        var preview = await WithDb(db => new PatientMergeService(db, SchedulingTestSupport.Clock).PreviewAsync(retired, survivor, default));
        await WithDb(db => new PatientMergeService(db, SchedulingTestSupport.Clock).MergeAsync(_s.Actor, new MergeRequest(retired, survivor, "Registered twice", preview.SourceRowVersion,
            preview.SurvivorRowVersion, preview.Conflicts.ToDictionary(c => c.Field, _ => "Source"), true, true), "merge-for-export", default));

        var merged = await RefusedAsync(() => ExportAsync(retired));
        Assert.Equal(("patient_merged", 409), (merged.Code, merged.StatusCode));
        Assert.Empty(await ExportLogAsync());                                                           // a refused export is not an export
        Assert.NotNull(await ExportAsync(survivor));                                                    // the record it carries on as can be exported
    }

    [Fact]
    public async Task When_the_audit_entry_cannot_be_saved_no_file_is_returned_and_the_same_export_works_once_the_fault_is_gone()
    {
        var id = await _s.PatientAsync();
        await using (var db = _fixture.CreateContext())
            await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER [TR_TestFault] ON [AuditLogEntries] AFTER INSERT AS BEGIN IF EXISTS (SELECT 1 FROM inserted WHERE [EventType] = 'PatientExported') THROW 59020, 'the audit store is down', 1; END");
        try
        {
            PatientExportFile? file = null;
            await Assert.ThrowsAnyAsync<Exception>(async () => file = await ExportAsync(id));
            Assert.Null(file);                                                                          // nothing left the system
            Assert.Empty(await ExportLogAsync());
        }
        finally
        {
            await using var db = _fixture.CreateContext();
            await db.Database.ExecuteSqlRawAsync("DROP TRIGGER [TR_TestFault]");
        }

        Assert.NotNull(await ExportAsync(id));                                                          // safe to run again
        Assert.Single(await ExportLogAsync());
    }
}
