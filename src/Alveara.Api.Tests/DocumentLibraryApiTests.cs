using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Clinical;
using Xunit;
using Session = Alveara.Api.Tests.SchedulingApiHarness.Session;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-010-C01 at the HTTP boundary, against a real API, real SQL Server and a real signed-in session per role: the clinical context (encounter, tooth or region) on import and edit, and the inactivate and reactivate
/// routes. Only the administrator and office manager may change a document (with a CSRF token); every refusal has its stable code and names its field; an inactive document is hidden from the library unless asked for,
/// and its reason is shown in the detail and never in the log.
/// </summary>
public class DocumentLibraryApiTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private SchedulingTestSupport _s = null!;
    private SchedulingApiHarness _api = null!;
    private Guid _patient;
    private Guid _otherPatient;
    private Guid _encounter;
    private Guid _otherEncounter;
    private static readonly Guid Radiograph = Alveara.Api.Architecture.Documents.DocumentCategorySeeds.Radiograph;

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
        _s = new SchedulingTestSupport(_fixture);
        _api = new SchedulingApiHarness(_fixture);
        await _s.ArrangeAsync();
        _patient = await _s.PatientAsync();
        _otherPatient = await _s.PatientAsync("Bo", "Kim");
        await using var db = _fixture.CreateContext();
        var mine = new Encounter { Id = Guid.NewGuid(), PatientId = _patient, EncounterAtUtc = DateTimeOffset.UtcNow, Status = EncounterStatuses.Draft, CreatedAtUtc = DateTimeOffset.UtcNow };
        var theirs = new Encounter { Id = Guid.NewGuid(), PatientId = _otherPatient, EncounterAtUtc = DateTimeOffset.UtcNow, Status = EncounterStatuses.Draft, CreatedAtUtc = DateTimeOffset.UtcNow };
        db.Encounters.AddRange(mine, theirs);
        await db.SaveChangesAsync();
        (_encounter, _otherEncounter) = (mine.Id, theirs.Id);
    }
    public Task DisposeAsync() { _api.Dispose(); return _fixture.DisposeAsync(); }

    private static byte[] Png(int extra = 100, byte fill = 7) => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, .. Enumerable.Repeat(fill, extra)];
    private static string Str(JsonElement e, string name) => e.GetProperty(name).GetString()!;
    private static string Error(JsonElement e) => Str(e, "error");

    private async Task<(HttpStatusCode Status, JsonElement Body)> UploadAsync(Session by, byte[]? bytes = null, string? encounter = null, string? tooth = null, string? region = null)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes ?? Png());
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        form.Add(file, "file", "bitewing.png");
        form.Add(new StringContent(Radiograph.ToString()), "categoryId");
        form.Add(new StringContent("2026-10-01"), "documentDate");
        if (encounter is not null) form.Add(new StringContent(encounter), "encounterId");
        if (tooth is not null) form.Add(new StringContent(tooth), "toothKey");
        if (region is not null) form.Add(new StringContent(region), "regionKey");
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/patients/{_patient}/documents") { Content = form };
        request.Headers.Add("X-CSRF-Token", by.Csrf);
        request.Headers.Add("Idempotency-Key", $"k-{Guid.NewGuid():N}");
        var response = await by.Client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        return (response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone());
    }

    private async Task<JsonElement> ImportedAsync(Session by, byte[]? bytes = null, string? encounter = null, string? tooth = null, string? region = null)
    {
        var r = await UploadAsync(by, bytes, encounter, tooth, region);
        Assert.Equal(HttpStatusCode.Created, r.Status);
        return r.Body.GetProperty("document");
    }

    private static object EditBody(JsonElement doc, string? encounter = null, string? tooth = null, string? region = null, string? description = null) =>
        new { categoryId = Str(doc, "categoryId"), documentDate = Str(doc, "documentDate"), description = description ?? (doc.GetProperty("description").ValueKind == JsonValueKind.Null ? null : Str(doc, "description")), rowVersion = Str(doc, "rowVersion"), encounterId = encounter, toothKey = tooth, regionKey = region };

    private async Task<(int Documents, int StatusChanges, int Audits)> CountsAsync()
    {
        await using var db = _fixture.CreateContext();
        return (await db.Documents.CountAsync(), await db.DocumentStatusChanges.CountAsync(), await db.AuditLogEntries.CountAsync(a => a.EventType.StartsWith("Document")));
    }

    private Task<(HttpStatusCode Status, JsonElement Body)> ListAsync(Session by, bool includeInactive = false) =>
        by.SendAsync(HttpMethod.Get, $"/api/patients/{_patient}/documents{(includeInactive ? "?includeInactive=true" : "")}");

    // ---------- context on import and edit ----------

    [Fact]
    public async Task A_document_is_imported_with_an_encounter_and_a_tooth_and_read_back_with_them()
    {
        var office = await _api.SessionAsync("OfficeManager");
        var doc = await ImportedAsync(office, encounter: _encounter.ToString(), tooth: "16");
        Assert.Equal((_encounter.ToString(), "16"), (Str(doc, "encounterId"), Str(doc, "toothKey")));
        var detail = await office.SendAsync(HttpMethod.Get, $"/api/documents/{Str(doc, "id")}");
        Assert.Equal((_encounter.ToString(), "16", "Active"), (Str(detail.Body.GetProperty("document"), "encounterId"), Str(detail.Body.GetProperty("document"), "toothKey"), Str(detail.Body.GetProperty("document"), "status")));
        var region = await ImportedAsync(office, Png(300), region: "UpperLeft");
        Assert.Equal("UpperLeft", Str(region, "regionKey"));
    }

    [Fact]
    public async Task A_wrong_tooth_region_or_encounter_is_named_field_by_field_and_nothing_is_stored()
    {
        var office = await _api.SessionAsync("OfficeManager");
        foreach (var (field, tooth, region, encounter) in new (string, string?, string?, string?)[]
        {
            ("toothKey", "99", null, null), ("regionKey", null, "Nose", null), ("regionKey", "16", "Tmj", null), ("encounter", null, null, Guid.NewGuid().ToString()), ("encounter", null, null, _otherEncounter.ToString()),
        })
        {
            var r = await UploadAsync(office, tooth: tooth, region: region, encounter: encounter);
            Assert.Equal((HttpStatusCode.BadRequest, "metadata_invalid", field), (r.Status, Error(r.Body), Str(r.Body.GetProperty("problems")[0], "field")));
        }
        var garbled = await UploadAsync(office, encounter: "not-a-guid");
        Assert.Equal(("metadata_invalid", "encounter"), (Error(garbled.Body), Str(garbled.Body.GetProperty("problems")[0], "field")));
        Assert.Equal(0, (await CountsAsync()).Documents);
    }

    [Fact]
    public async Task An_edit_sets_and_clears_the_context_and_a_wrong_context_is_refused()
    {
        var office = await _api.SessionAsync("OfficeManager");
        var doc = await ImportedAsync(office);
        var set = await office.SendAsync(HttpMethod.Put, $"/api/documents/{Str(doc, "id")}", EditBody(doc, _encounter.ToString(), "26"));
        Assert.Equal(HttpStatusCode.OK, set.Status);
        var after = set.Body.GetProperty("document");
        Assert.Equal((_encounter.ToString(), "26"), (Str(after, "encounterId"), Str(after, "toothKey")));

        var bad = await office.SendAsync(HttpMethod.Put, $"/api/documents/{Str(doc, "id")}", EditBody(after, _otherEncounter.ToString()));
        Assert.Equal((HttpStatusCode.BadRequest, "encounter"), (bad.Status, Str(bad.Body.GetProperty("problems")[0], "field")));

        var cleared = await office.SendAsync(HttpMethod.Put, $"/api/documents/{Str(doc, "id")}", EditBody(after));
        Assert.Equal(JsonValueKind.Null, cleared.Body.GetProperty("document").GetProperty("toothKey").ValueKind);
    }

    // ---------- inactivate and reactivate ----------

    [Theory]
    [InlineData("Admin", true)] [InlineData("OfficeManager", true)]
    [InlineData("FrontDesk", false)] [InlineData("Dentist", false)] [InlineData("Hygienist", false)] [InlineData("Assistant", false)] [InlineData("Billing", false)]
    public async Task Only_the_administrator_and_office_manager_inactivate_and_reactivate(string role, bool allowed)
    {
        var admin = await _api.SessionAsync("Admin");
        var doc = await ImportedAsync(admin);
        var by = await _api.SessionAsync(role);
        var before = await CountsAsync();
        var r = await by.SendAsync(HttpMethod.Post, $"/api/documents/{Str(doc, "id")}/inactivate", new { reason = "Wrong patient.", rowVersion = Str(doc, "rowVersion") });
        var current = allowed ? Str(r.Body.GetProperty("document"), "rowVersion") : Str(doc, "rowVersion");                       // a reactivation echoes the version the inactivation produced
        var back = await by.SendAsync(HttpMethod.Post, $"/api/documents/{Str(doc, "id")}/reactivate", new { rowVersion = current });
        if (allowed) Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (r.Status, back.Status));
        else
        {
            Assert.Equal((HttpStatusCode.Forbidden, HttpStatusCode.Forbidden), (r.Status, back.Status));
            Assert.Equal(before, await CountsAsync());
        }
    }

    [Fact]
    public async Task Anonymous_callers_get_401_and_a_missing_csrf_token_is_refused_for_both_routes()
    {
        var anon = _api.Factory.CreateClient();
        var id = Guid.NewGuid();
        foreach (var url in new[] { $"/api/documents/{id}/inactivate", $"/api/documents/{id}/reactivate" })
            Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsync(url, null)).StatusCode);
        var admin = await _api.SessionAsync("Admin");
        var doc = await ImportedAsync(admin);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.SendAsync(HttpMethod.Post, $"/api/documents/{Str(doc, "id")}/inactivate", new { reason = "Why.", rowVersion = Str(doc, "rowVersion") }, csrf: false)).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.SendAsync(HttpMethod.Post, $"/api/documents/{Str(doc, "id")}/reactivate", new { rowVersion = Str(doc, "rowVersion") }, csrf: false)).Status);
        Assert.Equal((1, 0), ((await CountsAsync()).Documents, (await CountsAsync()).StatusChanges));
    }

    [Fact]
    public async Task An_inactive_document_leaves_the_library_unless_asked_for_and_shows_its_reason_who_and_when_and_the_log_never_carries_the_reason()
    {
        var office = await _api.SessionAsync("OfficeManager");
        var doc = await ImportedAsync(office, Png(200));
        var keep = await ImportedAsync(office, Png(201));
        var inactive = await office.SendAsync(HttpMethod.Post, $"/api/documents/{Str(doc, "id")}/inactivate", new { reason = "Scanned under the wrong patient.", rowVersion = Str(doc, "rowVersion") });
        Assert.Equal(HttpStatusCode.OK, inactive.Status);
        var view = inactive.Body.GetProperty("document");
        Assert.Equal(("Inactive", "Scanned under the wrong patient."), (Str(view, "status"), Str(view, "inactiveReason")));
        Assert.False(string.IsNullOrWhiteSpace(Str(view, "inactivatedByName")));

        Assert.Equal([Str(keep, "id")], (await ListAsync(office)).Body.GetProperty("entries").EnumerateArray().Select(e => Str(e, "id")));
        var all = (await ListAsync(office, includeInactive: true)).Body.GetProperty("entries").EnumerateArray().ToList();
        Assert.Equal(2, all.Count);
        Assert.True(all.Single(e => Str(e, "id") == Str(doc, "id")).GetProperty("isInactive").GetBoolean());

        var detail = (await office.SendAsync(HttpMethod.Get, $"/api/documents/{Str(doc, "id")}")).Body;
        Assert.Equal(["Inactive"], detail.GetProperty("statusChanges").EnumerateArray().Select(c => Str(c, "toStatus")));
        Assert.Equal(HttpStatusCode.OK, (await office.Client.GetAsync($"/api/documents/{Str(doc, "id")}/content")).StatusCode);        // hidden, not removed

        await using var db = _fixture.CreateContext();
        var log = string.Join(" ", await db.AuditLogEntries.AsNoTracking().Where(a => a.EventType.StartsWith("Document")).Select(a => a.Details + " " + a.Reason).ToListAsync());
        Assert.DoesNotContain("wrong patient", log, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Reactivating_returns_the_document_to_the_library_and_keeps_the_old_reason_in_the_status_history()
    {
        var office = await _api.SessionAsync("OfficeManager");
        var doc = await ImportedAsync(office);
        var off = (await office.SendAsync(HttpMethod.Post, $"/api/documents/{Str(doc, "id")}/inactivate", new { reason = "Duplicate scan.", rowVersion = Str(doc, "rowVersion") })).Body.GetProperty("document");
        var on = await office.SendAsync(HttpMethod.Post, $"/api/documents/{Str(doc, "id")}/reactivate", new { rowVersion = Str(off, "rowVersion") });
        Assert.Equal(HttpStatusCode.OK, on.Status);
        Assert.Equal("Active", Str(on.Body.GetProperty("document"), "status"));
        Assert.Single((await ListAsync(office)).Body.GetProperty("entries").EnumerateArray());
        var changes = (await office.SendAsync(HttpMethod.Get, $"/api/documents/{Str(doc, "id")}")).Body.GetProperty("statusChanges").EnumerateArray().ToList();
        Assert.Equal(["Inactive", "Active"], changes.Select(c => Str(c, "toStatus")));
        Assert.Equal("Duplicate scan.", Str(changes[0], "reason"));
    }

    [Fact]
    public async Task Each_refusal_has_its_stable_code_and_status()
    {
        var admin = await _api.SessionAsync("Admin");
        var doc = await ImportedAsync(admin);
        var id = Str(doc, "id");
        var blank = await admin.SendAsync(HttpMethod.Post, $"/api/documents/{id}/inactivate", new { reason = "  ", rowVersion = Str(doc, "rowVersion") });
        Assert.Equal((HttpStatusCode.BadRequest, "metadata_invalid", "reason"), (blank.Status, Error(blank.Body), Str(blank.Body.GetProperty("problems")[0], "field")));
        var noVersion = await admin.SendAsync(HttpMethod.Post, $"/api/documents/{id}/inactivate", new { reason = "Why." });
        Assert.Equal((HttpStatusCode.BadRequest, "row_version_required"), (noVersion.Status, Error(noVersion.Body)));
        var missing = await admin.SendAsync(HttpMethod.Post, $"/api/documents/{Guid.NewGuid()}/inactivate", new { reason = "Why.", rowVersion = Str(doc, "rowVersion") });
        Assert.Equal((HttpStatusCode.NotFound, "document_not_found"), (missing.Status, Error(missing.Body)));
        Assert.Equal(HttpStatusCode.OK, (await admin.SendAsync(HttpMethod.Put, $"/api/documents/{id}", EditBody(doc, description: "First."))).Status);
        var stale = await admin.SendAsync(HttpMethod.Post, $"/api/documents/{id}/inactivate", new { reason = "Why.", rowVersion = Str(doc, "rowVersion") });
        Assert.Equal((HttpStatusCode.Conflict, "concurrency_conflict"), (stale.Status, Error(stale.Body)));
        Assert.Equal(0, (await CountsAsync()).StatusChanges);
    }
}
