using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Session = Alveara.Api.Tests.SchedulingApiHarness.Session;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-010-C01 at the HTTP boundary, against a real API with its own storage folder, real SQL Server and a real signed-in session per role: only the administrator and office manager may run the storage check or see
/// its report (a change needs a CSRF token), a damaged or missing file is found and named, a damaged file is refused when opened (409) and is served again once a re-check finds it intact, files nothing points to
/// are listed, and the log never carries a file name.
/// </summary>
public class DocumentStorageApiTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private SchedulingTestSupport _s = null!;
    private SchedulingApiHarness _api = null!;
    private Guid _patient;
    private string _storage = "";
    private static readonly Guid Radiograph = Alveara.Api.Architecture.Documents.DocumentCategorySeeds.Radiograph;

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
        _storage = Path.Combine(AlveraTestEnvironment.StorageFolder, $"storage-api-{Guid.NewGuid():N}");
        _s = new SchedulingTestSupport(_fixture);
        _api = new SchedulingApiHarness(_fixture) { StorageRoot = _storage };
        await _s.ArrangeAsync();
        _patient = await _s.PatientAsync();
    }
    public async Task DisposeAsync() { _api.Dispose(); await _fixture.DisposeAsync(); if (_storage.Length > 0) TestStorageCleanup.Delete(_storage); }

    private static byte[] Png(int extra = 100, byte fill = 7) => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, .. Enumerable.Repeat(fill, extra)];
    private static string Str(JsonElement e, string name) => e.GetProperty(name).GetString()!;

    private async Task<(string Id, string Path)> ImportedAsync(Session by, int extra, string name = "private-bitewing.png")
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Png(extra));
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        form.Add(file, "file", name);
        form.Add(new StringContent(Radiograph.ToString()), "categoryId");
        form.Add(new StringContent("2026-10-01"), "documentDate");
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/patients/{_patient}/documents") { Content = form };
        request.Headers.Add("X-CSRF-Token", by.Csrf);
        request.Headers.Add("Idempotency-Key", $"k-{Guid.NewGuid():N}");
        var response = await by.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var id = Str(JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("document"), "id");
        await using var db = _fixture.CreateContext();
        var blob = (await db.Documents.AsNoTracking().SingleAsync(d => d.Id == Guid.Parse(id))).BlobId;
        return (id, Path.Combine(_storage, blob.ToString("N")));
    }

    private string Stray(DateTimeOffset writtenAt)
    {
        Directory.CreateDirectory(_storage);                                                                   // the API creates it on its first request; a test may write a file before that
        var path = Path.Combine(_storage, Guid.NewGuid().ToString("N"));
        File.WriteAllBytes(path, [1, 2, 3]);
        File.SetLastWriteTimeUtc(path, writtenAt.UtcDateTime);
        return Path.GetFileName(path);
    }

    // ---------- who may do what ----------

    [Fact]
    public async Task Anonymous_callers_get_401_from_every_route()
    {
        var anon = _api.Factory.CreateClient();
        var id = Guid.NewGuid();
        foreach (var (method, url) in new[] { (HttpMethod.Get, "/api/document-storage"), (HttpMethod.Post, "/api/document-storage/check"), (HttpMethod.Post, $"/api/document-storage/documents/{id}/check") })
            Assert.Equal(HttpStatusCode.Unauthorized, (await anon.SendAsync(new HttpRequestMessage(method, url))).StatusCode);
    }

    [Theory]
    [InlineData("Admin", true)] [InlineData("OfficeManager", true)]
    [InlineData("FrontDesk", false)] [InlineData("Dentist", false)] [InlineData("Hygienist", false)] [InlineData("Assistant", false)] [InlineData("Billing", false)]
    public async Task Only_the_administrator_and_office_manager_see_the_storage_report_and_run_the_check(string role, bool allowed)
    {
        var admin = await _api.SessionAsync("Admin");
        var (id, _) = await ImportedAsync(admin, 100);
        var by = await _api.SessionAsync(role);
        var expected = allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden;
        Assert.Equal(expected, (await by.SendAsync(HttpMethod.Get, "/api/document-storage")).Status);
        Assert.Equal(expected, (await by.SendAsync(HttpMethod.Post, "/api/document-storage/check")).Status);
        Assert.Equal(expected, (await by.SendAsync(HttpMethod.Post, $"/api/document-storage/documents/{id}/check")).Status);
        await using var db = _fixture.CreateContext();
        if (!allowed) Assert.Equal(0, await db.DocumentIntegrity.CountAsync());
    }

    [Fact]
    public async Task A_check_without_a_csrf_token_is_refused_and_saves_nothing()
    {
        var admin = await _api.SessionAsync("Admin");
        var (id, _) = await ImportedAsync(admin, 100);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.SendAsync(HttpMethod.Post, "/api/document-storage/check", csrf: false)).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.SendAsync(HttpMethod.Post, $"/api/document-storage/documents/{id}/check", csrf: false)).Status);
        await using var db = _fixture.CreateContext();
        Assert.Equal(0, await db.DocumentIntegrity.CountAsync());
    }

    // ---------- the whole route ----------

    [Fact]
    public async Task A_damaged_or_missing_file_is_found_named_refused_when_opened_and_served_again_after_a_re_check_finds_it_intact()
    {
        var office = await _api.SessionAsync("OfficeManager");
        var (ok, _) = await ImportedAsync(office, 100, "fine-file.png");
        var (damaged, damagedPath) = await ImportedAsync(office, 200, "damaged-file.png");
        var (gone, gonePath) = await ImportedAsync(office, 300, "gone-file.png");
        var original = File.ReadAllBytes(damagedPath);
        var flipped = (byte[])original.Clone(); flipped[^1] ^= 0xFF;
        File.WriteAllBytes(damagedPath, flipped);
        File.Delete(gonePath);
        var orphan = Stray(DateTimeOffset.UtcNow.AddHours(-2));

        var run = await office.SendAsync(HttpMethod.Post, "/api/document-storage/check");
        Assert.Equal(HttpStatusCode.OK, run.Status);
        Assert.Equal((3, 1, 1, 1, 0), (run.Body.GetProperty("checked").GetInt32(), run.Body.GetProperty("ok").GetInt32(), run.Body.GetProperty("missing").GetInt32(), run.Body.GetProperty("corrupt").GetInt32(), run.Body.GetProperty("skipped").GetInt32()));
        var problems = run.Body.GetProperty("problems").EnumerateArray().ToList();
        Assert.Equal(new[] { "damaged-file.png", "gone-file.png" }.Order(), problems.Select(p => Str(p, "fileName")).Order());
        Assert.Contains(run.Body.GetProperty("orphans").EnumerateArray(), o => Str(o, "blobId") == Guid.Parse(orphan).ToString());

        var summary = await office.SendAsync(HttpMethod.Get, "/api/document-storage");
        Assert.Equal((3, 1, 1, 1, 0), (summary.Body.GetProperty("documents").GetInt32(), summary.Body.GetProperty("ok").GetInt32(), summary.Body.GetProperty("missing").GetInt32(), summary.Body.GetProperty("corrupt").GetInt32(), summary.Body.GetProperty("neverChecked").GetInt32()));

        var shown = (await office.SendAsync(HttpMethod.Get, $"/api/documents/{damaged}")).Body.GetProperty("document");
        Assert.Equal("Corrupt", Str(shown, "integrityStatus"));
        var refused = await office.Client.GetAsync($"/api/documents/{damaged}/content");
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains("file_corrupt", await refused.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, (await office.Client.GetAsync($"/api/documents/{ok}/content")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await office.Client.GetAsync($"/api/documents/{gone}/content")).StatusCode);

        File.WriteAllBytes(damagedPath, original);
        var recheck = await office.SendAsync(HttpMethod.Post, $"/api/document-storage/documents/{damaged}/check");
        Assert.Equal((HttpStatusCode.OK, "Ok"), (recheck.Status, Str(recheck.Body, "status")));
        Assert.Equal(HttpStatusCode.OK, (await office.Client.GetAsync($"/api/documents/{damaged}/content")).StatusCode);
    }

    // ---------- removing unreferenced files ----------

    private Task<(HttpStatusCode Status, JsonElement Body)> RemoveAsync(Session by, object body, bool csrf = true) => by.SendAsync(HttpMethod.Post, "/api/document-storage/orphans/remove", body, csrf: csrf);

    [Fact]
    public async Task Anonymous_callers_get_401_from_the_removal_route()
    {
        var anon = _api.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsync("/api/document-storage/orphans/remove", null)).StatusCode);
    }

    [Theory]
    [InlineData("Admin", true)] [InlineData("OfficeManager", true)]
    [InlineData("FrontDesk", false)] [InlineData("Dentist", false)] [InlineData("Hygienist", false)] [InlineData("Assistant", false)] [InlineData("Billing", false)]
    public async Task Only_the_administrator_and_office_manager_remove_unreferenced_files(string role, bool allowed)
    {
        var by = await _api.SessionAsync(role);
        var stray = Stray(DateTimeOffset.UtcNow.AddHours(-3));
        var r = await RemoveAsync(by, new { blobIds = new[] { Guid.Parse(stray) } });
        if (allowed) { Assert.Equal(HttpStatusCode.OK, r.Status); Assert.False(File.Exists(Path.Combine(_storage, stray))); }
        else { Assert.Equal(HttpStatusCode.Forbidden, r.Status); Assert.True(File.Exists(Path.Combine(_storage, stray))); }
    }

    [Fact]
    public async Task A_removal_without_a_csrf_token_is_refused_and_removes_nothing()
    {
        var admin = await _api.SessionAsync("Admin");
        var stray = Stray(DateTimeOffset.UtcNow.AddHours(-3));
        Assert.Equal(HttpStatusCode.BadRequest, (await RemoveAsync(admin, new { blobIds = new[] { Guid.Parse(stray) } }, csrf: false)).Status);
        Assert.True(File.Exists(Path.Combine(_storage, stray)));
    }

    [Fact]
    public async Task A_reviewer_lists_the_unreferenced_files_removes_the_ones_they_saw_and_each_outcome_is_reported()
    {
        var office = await _api.SessionAsync("OfficeManager");
        var (_, docPath) = await ImportedAsync(office, 100);
        File.SetLastWriteTimeUtc(docPath, DateTime.UtcNow.AddHours(-9));
        var old1 = Stray(DateTimeOffset.UtcNow.AddHours(-3));
        var old2 = Stray(DateTimeOffset.UtcNow.AddHours(-4));
        var recent = Stray(DateTimeOffset.UtcNow.AddMinutes(-5));

        var summary = await office.SendAsync(HttpMethod.Get, "/api/document-storage");
        var listed = summary.Body.GetProperty("orphans").EnumerateArray().Select(o => Str(o, "blobId")).ToList();
        Assert.Equal(new[] { Guid.Parse(old1).ToString(), Guid.Parse(old2).ToString() }.Order(), listed.Order());

        var r = await RemoveAsync(office, new { blobIds = new[] { Guid.Parse(old1), Guid.Parse(old2), Guid.Parse(recent), Guid.Parse(Path.GetFileName(docPath)) } });
        Assert.Equal(HttpStatusCode.OK, r.Status);
        Assert.Equal(new[] { "Removed", "Removed", "TooNew", "StillReferenced" }, r.Body.GetProperty("results").EnumerateArray().Select(x => Str(x, "outcome")));
        Assert.Equal(2, r.Body.GetProperty("removed").GetInt32());
        Assert.False(File.Exists(Path.Combine(_storage, old1)) || File.Exists(Path.Combine(_storage, old2)));
        Assert.True(File.Exists(Path.Combine(_storage, recent)) && File.Exists(docPath));
        Assert.Empty((await office.SendAsync(HttpMethod.Get, "/api/document-storage")).Body.GetProperty("orphans").EnumerateArray());

        await using var db = _fixture.CreateContext();
        var log = await db.AuditLogEntries.AsNoTracking().Where(a => a.EventType == "DocumentOrphanRemoved").ToListAsync();
        Assert.Equal(2, log.Count);
        Assert.All(log, a => Assert.NotNull(a.PerformedByUserAccountId));
    }

    [Fact]
    public async Task A_removal_that_names_nothing_or_too_many_files_or_something_that_is_not_an_id_is_refused()
    {
        var admin = await _api.SessionAsync("Admin");
        var none = await RemoveAsync(admin, new { blobIds = Array.Empty<Guid>() });
        Assert.Equal((HttpStatusCode.BadRequest, "metadata_invalid", "blobIds"), (none.Status, Str(none.Body, "error"), Str(none.Body.GetProperty("problems")[0], "field")));
        var many = await RemoveAsync(admin, new { blobIds = Enumerable.Range(0, 501).Select(_ => Guid.NewGuid()).ToArray() });
        Assert.Equal("blobIds", Str(many.Body.GetProperty("problems")[0], "field"));
        var absent = await RemoveAsync(admin, new { });
        Assert.Equal(HttpStatusCode.BadRequest, absent.Status);
        var junk = await RemoveAsync(admin, new { blobIds = new[] { "not-an-id" } });
        Assert.Equal(HttpStatusCode.BadRequest, junk.Status);
    }

    [Fact]
    public async Task An_unknown_document_is_404_and_the_log_never_carries_a_file_name()
    {
        var admin = await _api.SessionAsync("Admin");
        var missing = await admin.SendAsync(HttpMethod.Post, $"/api/document-storage/documents/{Guid.NewGuid()}/check");
        Assert.Equal((HttpStatusCode.NotFound, "document_not_found"), (missing.Status, Str(missing.Body, "error")));

        var (id, _) = await ImportedAsync(admin, 100, "ann-okafor-private.png");
        await admin.SendAsync(HttpMethod.Post, $"/api/document-storage/documents/{id}/check");
        await admin.SendAsync(HttpMethod.Post, "/api/document-storage/check");
        await using var db = _fixture.CreateContext();
        var log = string.Join(" ", await db.AuditLogEntries.AsNoTracking().Where(a => a.EventType.StartsWith("DocumentStorage") || a.EventType == "DocumentIntegrityChecked").Select(a => a.Details + " " + a.Reason).ToListAsync());
        Assert.Contains("Storage checked", log);
        Assert.DoesNotContain("okafor", log, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(_patient.ToString(), log, StringComparison.OrdinalIgnoreCase);
    }
}
