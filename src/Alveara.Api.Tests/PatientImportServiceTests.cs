using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Patients;
using Alveara.Api.Architecture.Patients.Portability;
using Alveara.Api.Data;

namespace Alveara.Api.Tests;

/// <summary>
/// STORY-018, the import against a real SQL Server: a valid file creates the patient and one log entry with the user and the time and no personal detail; a file with any problem stores nothing, lists every
/// problem and is logged as rejected; the same file imported again creates nothing new; an exact or likely duplicate is refused with the existing record or the candidates and logged; an exported file can be
/// imported as another person and comes back out equal; and when the log entry cannot be saved the patient is not created either (and a rejection that cannot be logged is a failure, not a silent pass).
/// </summary>
public class PatientImportServiceTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private SchedulingTestSupport _s = null!;

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
        _s = new SchedulingTestSupport(_fixture);
        await _s.ArrangeAsync();
        await using var db = _fixture.CreateContext();
        db.UserAccounts.Add(new UserAccount { Id = _s.Actor, Username = $"admin-{Guid.NewGuid():N}", CreatedAtUtc = DateTimeOffset.UtcNow });         // the acting user is a real account
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private async Task<T> WithDb<T>(Func<AlveraDbContext, Task<T>> action) { await using var db = _fixture.CreateContext(); return await action(db); }

    private Task<PatientImportResult> ImportAsync(byte[] bytes) =>
        WithDb(db => new PatientImportService(db, new PatientRegistrationService(db, SchedulingTestSupport.Clock), SchedulingTestSupport.Clock).ImportAsync(bytes, _s.Actor, default));

    private Task<PatientExportFile> ExportAsync(Guid patient) => WithDb(db => new PatientExportService(db, SchedulingTestSupport.Clock).ExportAsync(patient, _s.Actor, default));

    private Task<int> PatientCountAsync() => WithDb(db => db.Patients.CountAsync());

    private Task<List<AuditLogEntry>> LogAsync(string eventType) => WithDb(db => db.AuditLogEntries.AsNoTracking().Where(a => a.EventType == eventType).OrderBy(a => a.TimestampUtc).ToListAsync());

    private static async Task<T> Refused<T>(Func<Task> action) where T : Exception => await Assert.ThrowsAnyAsync<T>(action);

    [Fact]
    public async Task A_valid_file_creates_the_patient_and_one_log_entry_with_the_user_and_the_time_and_no_personal_details()
    {
        var before = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();
        var count = await PatientCountAsync();

        var result = await ImportAsync(PatientImportFiles.Bytes(PatientImportFiles.Valid(id)));

        Assert.True(result.Created);
        Assert.Equal(id, result.ExportId);
        Assert.Equal(count + 1, await PatientCountAsync());
        var patient = await WithDb(db => db.Patients.AsNoTracking().SingleAsync(p => p.Id == result.PatientId));
        Assert.Equal(("Eli", "Moss", "1990-05-17", "TX"), (patient.FirstName, patient.LastName, patient.DateOfBirth.ToString("yyyy-MM-dd"), patient.State));
        Assert.Equal($"import-{id:N}", patient.RegistrationKey);                                         // the key that makes a repeat safe
        Assert.Equal(_s.Actor, patient.CreatedByUserId);

        var entry = Assert.Single(await LogAsync(AuditEventTypes.PatientImported));
        Assert.Equal(_s.Actor, entry.PerformedByUserAccountId);                                          // who
        Assert.InRange(entry.TimestampUtc, before.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(1));  // when
        Assert.Equal(result.PatientId, entry.TargetUserAccountId);
        Assert.Equal(id, entry.CorrelationId);
        Assert.Contains(id.ToString(), entry.Details);
        foreach (var personal in new[] { "Eli", "Moss", "1990-05-17", "(555) 010-3344", "9 Oak Road", "78701" }) Assert.DoesNotContain(personal, entry.Details);
        Assert.Single(await LogAsync(PatientAuditEvents.Registered));                                    // and the registration's own entry, as for any patient
    }

    [Fact]
    public async Task A_file_with_problems_stores_nothing_lists_every_problem_and_is_logged_as_rejected_without_its_values()
    {
        var count = await PatientCountAsync();
        var bad = PatientImportFiles.Changed(f =>
        {
            f["patient"]!.AsObject()["phone"] = "12";
            f["patient"]!.AsObject()["dateOfBirth"] = "tomorrow";
            f["patient"]!.AsObject()["firstName"] = "Zelda-private";
        });

        var rejection = await Refused<PatientImportRejectedException>(() => ImportAsync(bad));

        Assert.Contains(rejection.Problems, p => p.Code == "field_invalid" && p.Path == "patient.phone");
        Assert.Contains(rejection.Problems, p => p.Code == "field_invalid" && p.Path == "patient.dateOfBirth");
        Assert.Equal(count, await PatientCountAsync());                                                  // nothing was created
        Assert.Empty(await LogAsync(AuditEventTypes.PatientImported));
        var entry = Assert.Single(await LogAsync(AuditEventTypes.PatientImportRejected));
        Assert.Equal(_s.Actor, entry.PerformedByUserAccountId);
        Assert.True(entry.TimestampUtc > DateTimeOffset.UtcNow.AddMinutes(-5));
        Assert.Contains("field_invalid", entry.Details);                                                 // the codes
        Assert.DoesNotContain("Zelda-private", entry.Details);                                           // never a value from the file
        Assert.DoesNotContain("tomorrow", entry.Details);
    }

    [Fact]
    public async Task A_file_that_is_not_even_JSON_is_rejected_and_logged_too()
    {
        var rejection = await Refused<PatientImportRejectedException>(() => ImportAsync(PatientImportFiles.Bytes("this is not a patient file")));

        Assert.Equal("not_json", Assert.Single(rejection.Problems).Code);
        var entry = Assert.Single(await LogAsync(AuditEventTypes.PatientImportRejected));
        Assert.Contains("not_json", entry.Details);
    }

    [Fact]
    public async Task Importing_the_same_file_again_returns_the_patient_it_created_and_creates_nothing_new()
    {
        var bytes = PatientImportFiles.Bytes(PatientImportFiles.Valid());
        var first = await ImportAsync(bytes);
        var count = await PatientCountAsync();

        var second = await ImportAsync(bytes);

        Assert.True(first.Created);
        Assert.False(second.Created);
        Assert.Equal(first.PatientId, second.PatientId);
        Assert.Equal(count, await PatientCountAsync());
        var log = await LogAsync(AuditEventTypes.PatientImported);
        Assert.Equal(2, log.Count);                                                                      // the second import is an action too, and is logged
        Assert.Contains("nothing new was created", log[1].Details);
        Assert.Single(await LogAsync(PatientAuditEvents.Registered));                                    // but only one patient was ever registered
    }

    [Fact]
    public async Task A_patient_who_is_already_registered_is_refused_with_a_pointer_to_the_record_and_the_refusal_is_logged()
    {
        var existing = await _s.PatientAsync("Ann", "Rivera");
        var file = PatientImportFiles.AsAnotherPerson((await ExportAsync(existing)).Content);            // the same person, as a file with a new export id
        var count = await PatientCountAsync();

        var refusal = await Refused<PatientException>(() => ImportAsync(file));

        Assert.Equal(("duplicate_patient", 409), (refusal.Code, refusal.StatusCode));
        Assert.Equal(existing, refusal.ExistingPatientId);
        Assert.Equal(count, await PatientCountAsync());
        Assert.Empty(await LogAsync(AuditEventTypes.PatientImported));
        Assert.Contains("duplicate_patient", Assert.Single(await LogAsync(AuditEventTypes.PatientImportRejected)).Details);
    }

    [Fact]
    public async Task A_patient_who_may_be_someone_already_registered_is_refused_with_the_candidates_and_is_not_created()
    {
        var existing = await _s.PatientAsync("Ann", "Rivera");                                           // the file has the same birth date and last name but another first name
        var file = PatientImportFiles.AsAnotherPerson((await ExportAsync(existing)).Content, first: "Zelda");
        var count = await PatientCountAsync();

        var refusal = await Refused<PatientException>(() => ImportAsync(file));

        Assert.Equal(("possible_duplicate", 409), (refusal.Code, refusal.StatusCode));
        Assert.Contains(existing, refusal.Candidates.Select(c => c.Id));
        Assert.Equal(count, await PatientCountAsync());
        Assert.Contains("possible_duplicate", Assert.Single(await LogAsync(AuditEventTypes.PatientImportRejected)).Details);
    }

    [Fact]
    public async Task An_exported_file_imported_as_another_person_comes_back_out_with_the_same_fields()
    {
        var original = await _s.PatientAsync("Ann", "Rivera");
        var exported = (await ExportAsync(original)).Content;

        var result = await ImportAsync(PatientImportFiles.AsAnotherPerson(exported, "Beth", "Stone", "1971-02-03"));
        var again = await ExportAsync(result.PatientId);

        using var a = JsonDocument.Parse(exported);
        using var b = JsonDocument.Parse(again.Content);
        var from = a.RootElement.GetProperty("patient");
        var to = b.RootElement.GetProperty("patient");
        foreach (var member in new[] { "middleName", "sex", "phone", "email", "addressLine1", "addressLine2", "city", "state", "postalCode" })
            Assert.Equal(from.GetProperty(member).ToString(), to.GetProperty(member).ToString());       // what was not changed survived the round trip
        Assert.Equal(("Beth", "Stone", "1971-02-03"), (to.GetProperty("firstName").GetString(), to.GetProperty("lastName").GetString(), to.GetProperty("dateOfBirth").GetString()));
    }

    [Fact]
    public async Task When_the_import_log_entry_cannot_be_saved_the_patient_is_not_created_either_and_the_same_file_imports_once_the_fault_is_gone()
    {
        var bytes = PatientImportFiles.Bytes(PatientImportFiles.Valid());
        var count = await PatientCountAsync();
        await using (var db = _fixture.CreateContext())
            await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER [TR_TestFault] ON [AuditLogEntries] AFTER INSERT AS BEGIN IF EXISTS (SELECT 1 FROM inserted WHERE [EventType] = 'PatientImported') THROW 59030, 'the audit store is down', 1; END");
        try
        {
            await Assert.ThrowsAnyAsync<Exception>(() => ImportAsync(bytes));
            Assert.Equal(count, await PatientCountAsync());                                              // no patient without its log entry
            Assert.Empty(await LogAsync(PatientAuditEvents.Registered));                                 // and the registration's entry went with it
        }
        finally
        {
            await using var db = _fixture.CreateContext();
            await db.Database.ExecuteSqlRawAsync("DROP TRIGGER [TR_TestFault]");
        }

        Assert.True((await ImportAsync(bytes)).Created);                                                 // safe to run again
        Assert.Equal(count + 1, await PatientCountAsync());
    }

    [Fact]
    public async Task When_the_rejection_cannot_be_logged_the_call_fails_instead_of_passing_silently()
    {
        await using (var db = _fixture.CreateContext())
            await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER [TR_TestFault] ON [AuditLogEntries] AFTER INSERT AS BEGIN IF EXISTS (SELECT 1 FROM inserted WHERE [EventType] = 'PatientImportRejected') THROW 59031, 'the audit store is down', 1; END");
        try
        {
            var failure = await Assert.ThrowsAnyAsync<Exception>(() => ImportAsync(PatientImportFiles.Bytes("not json")));
            Assert.IsNotType<PatientImportRejectedException>(failure);                                   // the rejection could not be logged, so it is a failure of the system
            Assert.Empty(await LogAsync(AuditEventTypes.PatientImportRejected));
        }
        finally
        {
            await using var db = _fixture.CreateContext();
            await db.Database.ExecuteSqlRawAsync("DROP TRIGGER [TR_TestFault]");
        }
    }
}
