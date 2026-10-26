using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Data;
using Session = Alveara.Api.Tests.SchedulingApiHarness.Session;

namespace Alveara.Api.Tests;

/// <summary>
/// STORY-018, the import through the real pipeline (real API, real SQL Server, cookie sessions): the administrator and the office manager may import and every other working role is 403 (an anonymous call 401,
/// a call without the CSRF token refused), a valid file is 201 and a repeat of it 200 with nothing new created, an incorrect file is 400 with every problem listed and nothing stored, a patient already
/// registered is 409 with a pointer to the record, an oversized body is refused, and every import action (success, repeat, rejected, refused) is logged with the signed-in user and the time.
/// </summary>
public class PatientImportApiTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private SchedulingTestSupport _s = null!;
    private SchedulingApiHarness _api = null!;
    private Guid _existing;
    private Session _admin = null!;
    private readonly Dictionary<string, Session> _others = new();

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
        _s = new SchedulingTestSupport(_fixture);
        _api = new SchedulingApiHarness(_fixture);
        await _s.ArrangeAsync();
        _existing = await _s.PatientAsync("Ann", "Rivera");
        _admin = await _api.SessionAsync("Admin");
        foreach (var role in new[] { "Dentist", "Hygienist", "Assistant", "FrontDesk", "Billing", "OfficeManager" }) _others[role] = await _api.SessionAsync(role);
    }

    public Task DisposeAsync() { _api.Dispose(); return _fixture.DisposeAsync(); }

    private async Task<(HttpStatusCode Status, JsonElement Body)> ImportAsync(Session session, byte[] bytes, bool csrf = true)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/patients/import") { Content = new ByteArrayContent(bytes) };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        if (csrf) request.Headers.Add("X-CSRF-Token", session.Csrf);
        var response = await session.Client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        return (response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone());
    }

    private async Task<int> PatientCountAsync() { await using var db = _fixture.CreateContext(); return await db.Patients.CountAsync(); }

    private async Task<List<AuditLogEntry>> ImportLogAsync()
    {
        await using var db = _fixture.CreateContext();
        return await db.AuditLogEntries.AsNoTracking().Where(a => a.EventType == AuditEventTypes.PatientImported || a.EventType == AuditEventTypes.PatientImportRejected).OrderBy(a => a.TimestampUtc).ToListAsync();
    }

    private static byte[] ValidFile() => PatientImportFiles.Bytes(PatientImportFiles.Valid());

    [Fact]
    public async Task The_administrator_and_the_office_manager_import_a_valid_file_and_a_repeat_creates_nothing_new()
    {
        var count = await PatientCountAsync();
        var file = ValidFile();

        var first = await ImportAsync(_admin, file);
        Assert.Equal(HttpStatusCode.Created, first.Status);
        Assert.True(first.Body.GetProperty("created").GetBoolean());
        Assert.Equal(count + 1, await PatientCountAsync());
        var read = await _admin.SendAsync(HttpMethod.Get, $"/api/patients/{first.Body.GetProperty("patientId").GetGuid()}");
        Assert.Equal("Moss", read.Body.GetProperty("lastName").GetString());                              // the patient really exists and can be opened

        var repeat = await ImportAsync(_others["OfficeManager"], file);                                    // the same file, by the other role that may import
        Assert.Equal(HttpStatusCode.OK, repeat.Status);
        Assert.False(repeat.Body.GetProperty("created").GetBoolean());
        Assert.Equal(first.Body.GetProperty("patientId").GetGuid(), repeat.Body.GetProperty("patientId").GetGuid());
        Assert.Equal(count + 1, await PatientCountAsync());
    }

    [Fact]
    public async Task Every_other_role_is_refused_an_anonymous_caller_sees_nothing_a_missing_csrf_token_is_refused_and_nothing_is_created_or_logged()
    {
        var count = await PatientCountAsync();
        foreach (var role in new[] { "Dentist", "Hygienist", "Assistant", "FrontDesk", "Billing" })
            Assert.True(HttpStatusCode.Forbidden == (await ImportAsync(_others[role], ValidFile())).Status, $"{role} must not import");
        using var anonymous = _api.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync("/api/patients/import", new ByteArrayContent(ValidFile()))).StatusCode);
        var noToken = await ImportAsync(_admin, ValidFile(), csrf: false);
        Assert.False((int)noToken.Status is >= 200 and < 300, "a call without the CSRF token must not succeed");

        Assert.Equal(count, await PatientCountAsync());
        Assert.Empty(await ImportLogAsync());                                                              // refused before the import began: not an import action
    }

    [Fact]
    public async Task An_incorrect_file_is_400_with_every_problem_listed_and_a_prompt_to_correct_it_and_nothing_is_stored()
    {
        var count = await PatientCountAsync();
        var bad = PatientImportFiles.Changed(f =>
        {
            f["formatVersion"] = 7;
            f["patient"]!.AsObject()["phone"] = "12";
            f["patient"]!.AsObject().Remove("city");
        });

        var response = await ImportAsync(_admin, bad);

        Assert.Equal(HttpStatusCode.BadRequest, response.Status);
        Assert.Equal("import_rejected", response.Body.GetProperty("error").GetString());
        Assert.Contains("Correct", response.Body.GetProperty("message").GetString());                     // the prompt to correct the file
        var problems = response.Body.GetProperty("problems").EnumerateArray().Select(p => (p.GetProperty("code").GetString(), p.GetProperty("path").GetString(), p.GetProperty("message").GetString())).ToList();
        Assert.Contains(problems, p => p.Item1 == "unsupported_version" && p.Item2 == "formatVersion");
        Assert.Contains(problems, p => p.Item1 == "missing_member" && p.Item2 == "patient.city");
        Assert.All(problems, p => Assert.False(string.IsNullOrWhiteSpace(p.Item3)));
        Assert.Equal(count, await PatientCountAsync());
        Assert.Equal(AuditEventTypes.PatientImportRejected, Assert.Single(await ImportLogAsync()).EventType);
    }

    [Fact]
    public async Task A_file_that_is_not_JSON_and_a_body_over_the_size_limit_are_both_rejected()
    {
        var notJson = await ImportAsync(_admin, PatientImportFiles.Bytes("hello"));
        Assert.Equal((HttpStatusCode.BadRequest, "not_json"), (notJson.Status, notJson.Body.GetProperty("problems")[0].GetProperty("code").GetString()));

        var oversized = await ImportAsync(_admin, new byte[400 * 1024]);
        Assert.Equal((HttpStatusCode.BadRequest, "file_too_large"), (oversized.Status, oversized.Body.GetProperty("problems")[0].GetProperty("code").GetString()));
        Assert.Equal(2, (await ImportLogAsync()).Count);
    }

    [Fact]
    public async Task A_patient_already_registered_is_409_with_a_pointer_to_the_record_and_is_not_created_again()
    {
        var exported = await _admin.Client.GetByteArrayAsync($"/api/patients/{_existing}/export");
        var count = await PatientCountAsync();

        var response = await ImportAsync(_others["OfficeManager"], PatientImportFiles.AsAnotherPerson(exported));

        Assert.Equal((HttpStatusCode.Conflict, "duplicate_patient"), (response.Status, response.Body.GetProperty("error").GetString()));
        Assert.Equal(_existing, response.Body.GetProperty("existingPatientId").GetGuid());
        Assert.Equal(count, await PatientCountAsync());
    }

    [Fact]
    public async Task An_exported_file_imports_as_another_person_through_the_API()
    {
        var exported = await _admin.Client.GetByteArrayAsync($"/api/patients/{_existing}/export");

        var response = await ImportAsync(_admin, PatientImportFiles.AsAnotherPerson(exported, "Beth", "Stone", "1971-02-03"));

        Assert.Equal(HttpStatusCode.Created, response.Status);
        var read = await _admin.SendAsync(HttpMethod.Get, $"/api/patients/{response.Body.GetProperty("patientId").GetGuid()}");
        Assert.Equal(("Beth", "Stone", "1971-02-03"), (read.Body.GetProperty("firstName").GetString(), read.Body.GetProperty("lastName").GetString(), read.Body.GetProperty("dateOfBirth").GetString()));
    }

    [Fact]
    public async Task Every_import_action_is_logged_with_the_signed_in_user_and_the_time()
    {
        var before = DateTimeOffset.UtcNow.AddMinutes(-1);
        var file = ValidFile();
        var exported = await _admin.Client.GetByteArrayAsync($"/api/patients/{_existing}/export");
        await ImportAsync(_admin, file);                                                                   // created
        await ImportAsync(_others["OfficeManager"], file);                                                 // a repeat
        await ImportAsync(_admin, PatientImportFiles.Bytes("hello"));                                      // rejected
        await ImportAsync(_others["OfficeManager"], PatientImportFiles.AsAnotherPerson(exported));         // refused as a duplicate

        var log = await ImportLogAsync();
        Assert.Equal(4, log.Count);
        Assert.Equal([AuditEventTypes.PatientImported, AuditEventTypes.PatientImported, AuditEventTypes.PatientImportRejected, AuditEventTypes.PatientImportRejected], log.Select(e => e.EventType));
        Assert.Equal([_admin.UserId, _others["OfficeManager"].UserId, _admin.UserId, _others["OfficeManager"].UserId], log.Select(e => e.PerformedByUserAccountId!.Value));
        Assert.All(log, e => Assert.InRange(e.TimestampUtc, before, DateTimeOffset.UtcNow.AddMinutes(1)));
    }
}
