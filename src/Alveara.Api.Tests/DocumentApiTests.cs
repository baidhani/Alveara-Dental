using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Session = Alveara.Api.Tests.SchedulingApiHarness.Session;

namespace Alveara.Api.Tests;

/// <summary>
/// STORY-010 at the HTTP boundary, against a real API, real SQL Server and a real signed-in session per role: who may read, import, edit and configure (and that a change needs a CSRF token), the whole route from an
/// import to a download, the stable error codes and statuses the screen relies on, that every file is served as a download except PDF, PNG and JPEG under a restrictive policy, and that no response names where a file is
/// stored.
/// </summary>
public class DocumentApiTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private SchedulingTestSupport _s = null!;
    private SchedulingApiHarness _api = null!;
    private string _patient = "";
    private static readonly Guid Radiograph = Alveara.Api.Architecture.Documents.DocumentCategorySeeds.Radiograph;
    private static readonly Guid Photo = Alveara.Api.Architecture.Documents.DocumentCategorySeeds.Photo;

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
        _s = new SchedulingTestSupport(_fixture);
        _api = new SchedulingApiHarness(_fixture);
        await _s.ArrangeAsync();
        _patient = (await _s.PatientAsync()).ToString();
    }
    public Task DisposeAsync() { _api.Dispose(); return _fixture.DisposeAsync(); }

    private static byte[] Png(int extra = 100, byte fill = 7) => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, .. Enumerable.Repeat(fill, extra)];
    private static byte[] Pdf() => [.. "%PDF-1.7\n"u8.ToArray(), .. Enumerable.Repeat((byte)'x', 100)];
    private static byte[] Program() { var b = new byte[0x100]; b[0] = (byte)'M'; b[1] = (byte)'Z'; b[0x3C] = 0x80; b[0x80] = (byte)'P'; b[0x81] = (byte)'E'; return b; }
    private static string Str(JsonElement e, string name) => e.GetProperty(name).GetString()!;
    private static string Error(JsonElement e) => Str(e, "error");

    /// <summary>A multipart import the way the screen sends it. Any field left null is left out, so a missing field can be tested.</summary>
    private async Task<(HttpStatusCode Status, JsonElement Body)> UploadAsync(Session by, byte[]? bytes = null, string fileName = "bitewing.png", string? category = "default", string? date = "2026-10-01",
        string? description = "Bitewing, left side.", string? key = "default", string? confirm = null, bool csrf = true, bool omitFile = false, string? patient = null)
    {
        using var form = new MultipartFormDataContent();
        if (!omitFile)
        {
            var file = new ByteArrayContent(bytes ?? Png());
            file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            form.Add(file, "file", fileName);
        }
        if (category is not null) form.Add(new StringContent(category == "default" ? Radiograph.ToString() : category), "categoryId");
        if (date is not null) form.Add(new StringContent(date), "documentDate");
        if (description is not null) form.Add(new StringContent(description), "description");
        if (confirm is not null) form.Add(new StringContent(confirm), "confirmDuplicateOf");
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/patients/{patient ?? _patient}/documents") { Content = form };
        if (csrf) request.Headers.Add("X-CSRF-Token", by.Csrf);
        if (key is not null) request.Headers.Add("Idempotency-Key", key == "default" ? $"k-{Guid.NewGuid():N}" : key);
        var response = await by.Client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        return (response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone());
    }

    private async Task<JsonElement> ImportedAsync(Session by, byte[]? bytes = null, string fileName = "bitewing.png")
    {
        var r = await UploadAsync(by, bytes, fileName);
        Assert.Equal(HttpStatusCode.Created, r.Status);
        return r.Body.GetProperty("document");
    }

    private async Task<(int Documents, int Events, int Audits)> CountsAsync()
    {
        await using var db = _fixture.CreateContext();
        return (await db.Documents.CountAsync(), await db.DocumentEvents.CountAsync(), await db.AuditLogEntries.CountAsync(a => a.EventType.StartsWith("Document") || a.EventType.StartsWith("BlockedExtension")));
    }

    // ---------- who may do what ----------

    [Fact]
    public async Task Anonymous_callers_get_401_from_every_route()
    {
        var anon = _api.Factory.CreateClient();
        var id = Guid.NewGuid();
        foreach (var (method, url) in new[]
        {
            (HttpMethod.Get, $"/api/patients/{_patient}/documents"), (HttpMethod.Post, $"/api/patients/{_patient}/documents"), (HttpMethod.Get, $"/api/documents/{id}"), (HttpMethod.Get, $"/api/documents/{id}/content"),
            (HttpMethod.Put, $"/api/documents/{id}"), (HttpMethod.Get, "/api/document-categories"), (HttpMethod.Post, "/api/document-categories"), (HttpMethod.Put, $"/api/document-categories/{id}"),
            (HttpMethod.Get, "/api/document-settings"), (HttpMethod.Put, "/api/document-settings"), (HttpMethod.Get, "/api/document-settings/blocked-extensions"),
            (HttpMethod.Post, "/api/document-settings/blocked-extensions"), (HttpMethod.Delete, "/api/document-settings/blocked-extensions/swf"),
        })
            Assert.Equal(HttpStatusCode.Unauthorized, (await anon.SendAsync(new HttpRequestMessage(method, url))).StatusCode);
    }

    [Theory]
    [InlineData("Admin", true)] [InlineData("OfficeManager", true)] [InlineData("FrontDesk", true)] [InlineData("Dentist", true)] [InlineData("Hygienist", true)] [InlineData("Assistant", true)] [InlineData("Billing", true)]
    public async Task The_staff_who_work_with_a_patients_file_read_the_library_and_the_category_list(string role, bool allowed)
    {
        var admin = await _api.SessionAsync("Admin");
        var doc = Str(await ImportedAsync(admin), "id");
        var by = await _api.SessionAsync(role);
        var expected = allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden;
        foreach (var url in new[] { $"/api/patients/{_patient}/documents", $"/api/documents/{doc}", $"/api/documents/{doc}/content", "/api/document-categories", "/api/document-settings/blocked-extensions" })
            Assert.Equal(expected, (await by.Client.GetAsync(url)).StatusCode);
    }

    [Theory]
    [InlineData("Admin", true)] [InlineData("OfficeManager", true)]
    [InlineData("FrontDesk", false)] [InlineData("Dentist", false)] [InlineData("Hygienist", false)] [InlineData("Assistant", false)] [InlineData("Billing", false)]
    public async Task Only_the_administrator_and_office_manager_import_and_edit_documents(string role, bool allowed)
    {
        var admin = await _api.SessionAsync("Admin");
        var existing = await ImportedAsync(admin, Png(50));
        var by = await _api.SessionAsync(role);
        var before = await CountsAsync();

        var import = await UploadAsync(by, Png(60));
        var edit = await by.SendAsync(HttpMethod.Put, $"/api/documents/{Str(existing, "id")}", new { categoryId = Photo, documentDate = "2026-10-01", description = "x", rowVersion = Str(existing, "rowVersion") });
        if (allowed)
        {
            Assert.Equal(HttpStatusCode.Created, import.Status);
            Assert.Equal(HttpStatusCode.OK, edit.Status);
        }
        else
        {
            Assert.Equal((HttpStatusCode.Forbidden, HttpStatusCode.Forbidden), (import.Status, edit.Status));
            Assert.Equal(before, await CountsAsync());
        }
    }

    [Theory]
    [InlineData("Admin", true)]
    [InlineData("OfficeManager", false)] [InlineData("FrontDesk", false)] [InlineData("Dentist", false)] [InlineData("Billing", false)]
    public async Task Only_the_administrator_reads_or_changes_the_document_settings(string role, bool allowed)
    {
        var by = await _api.SessionAsync(role);
        var before = await CountsAsync();
        var attempts = new (HttpMethod, string, object?)[]
        {
            (HttpMethod.Get, "/api/document-settings", null), (HttpMethod.Put, "/api/document-settings", new { maxUploadBytes = 2_000_000, rowVersion = "AAAAAAAAB9E=" }),
            (HttpMethod.Post, "/api/document-categories", new { name = "Consent copy" }), (HttpMethod.Post, "/api/document-settings/blocked-extensions", new { extension = "swf" }),
        };
        foreach (var (method, url, body) in attempts)
        {
            var r = await by.SendAsync(method, url, body);
            if (allowed) Assert.NotEqual(HttpStatusCode.Forbidden, r.Status); else Assert.Equal(HttpStatusCode.Forbidden, r.Status);
        }
        if (!allowed) Assert.Equal(before, await CountsAsync());
    }

    [Fact]
    public async Task A_change_without_a_csrf_token_is_refused_and_changes_nothing()
    {
        var admin = await _api.SessionAsync("Admin");
        var doc = await ImportedAsync(admin);
        var before = await CountsAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await UploadAsync(admin, Png(70), csrf: false)).Status);
        foreach (var (method, url, body) in new (HttpMethod, string, object?)[]
        {
            (HttpMethod.Put, $"/api/documents/{Str(doc, "id")}", new { categoryId = Photo, documentDate = "2026-10-01", description = "x", rowVersion = Str(doc, "rowVersion") }),
            (HttpMethod.Post, "/api/document-categories", new { name = "Consent copy" }), (HttpMethod.Put, $"/api/document-categories/{Photo}", new { name = "Pic", rowVersion = "AAAAAAAAB9E=" }),
            (HttpMethod.Put, "/api/document-settings", new { maxUploadBytes = 2_000_000, rowVersion = "AAAAAAAAB9E=" }),
            (HttpMethod.Post, "/api/document-settings/blocked-extensions", new { extension = "swf" }), (HttpMethod.Delete, "/api/document-settings/blocked-extensions/swf", null),
        })
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.SendAsync(method, url, body, csrf: false)).Status);
        Assert.Equal(before, await CountsAsync());
    }

    // ---------- the whole route ----------

    [Fact]
    public async Task A_document_is_imported_listed_read_downloaded_edited_and_its_history_kept()
    {
        var office = await _api.SessionAsync("OfficeManager");
        var bytes = Png(500);
        var r = await UploadAsync(office, bytes, "Ann Lee bitewing.png", description: "  Left side.  ", key: "route-1");
        Assert.Equal(HttpStatusCode.Created, r.Status);
        Assert.False(r.Body.GetProperty("replayed").GetBoolean());
        var doc = r.Body.GetProperty("document");
        Assert.Equal(("Radiograph", "2026-10-01", "Left side.", "png"), (Str(doc, "categoryName"), Str(doc, "documentDate"), Str(doc, "description"), Str(doc, "detectedType")));

        var again = await UploadAsync(office, bytes, "Ann Lee bitewing.png", description: "  Left side.  ", key: "route-1");        // the same request again
        Assert.Equal(HttpStatusCode.OK, again.Status);
        Assert.True(again.Body.GetProperty("replayed").GetBoolean());
        Assert.Equal(Str(doc, "id"), Str(again.Body.GetProperty("document"), "id"));
        Assert.Equal(1, (await CountsAsync()).Documents);

        var list = await office.SendAsync(HttpMethod.Get, $"/api/patients/{_patient}/documents");
        Assert.Equal(HttpStatusCode.OK, list.Status);
        Assert.Equal(Str(doc, "id"), Str(list.Body.GetProperty("entries")[0], "id"));
        Assert.Equal(104_857_600, list.Body.GetProperty("maxUploadBytes").GetInt64());
        Assert.Equal(1, (await office.SendAsync(HttpMethod.Get, $"/api/patients/{_patient}/documents?categoryId={Radiograph}")).Body.GetProperty("entries").GetArrayLength());
        Assert.Equal(0, (await office.SendAsync(HttpMethod.Get, $"/api/patients/{_patient}/documents?categoryId={Photo}")).Body.GetProperty("entries").GetArrayLength());

        var content = await office.Client.GetAsync($"/api/documents/{Str(doc, "id")}/content");
        Assert.Equal(HttpStatusCode.OK, content.StatusCode);
        Assert.Equal(bytes, await content.Content.ReadAsByteArrayAsync());

        var edit = await office.SendAsync(HttpMethod.Put, $"/api/documents/{Str(doc, "id")}", new { categoryId = Photo, documentDate = "2026-09-30", description = "Retaken.", rowVersion = Str(doc, "rowVersion") });
        Assert.Equal(HttpStatusCode.OK, edit.Status);
        Assert.Equal("Photo", Str(edit.Body.GetProperty("document"), "categoryName"));

        var detail = await office.SendAsync(HttpMethod.Get, $"/api/documents/{Str(doc, "id")}");
        Assert.Equal(["Imported", "Opened", "Categorized", "MetadataEdited"], detail.Body.GetProperty("history").EnumerateArray().Select(h => Str(h, "kind")));
    }

    [Fact]
    public async Task No_response_names_where_a_file_is_stored_or_which_blob_it_is()
    {
        var admin = await _api.SessionAsync("Admin");
        var doc = await ImportedAsync(admin);
        var texts = new List<string>
        {
            (await admin.Client.GetStringAsync($"/api/patients/{_patient}/documents")), (await admin.Client.GetStringAsync($"/api/documents/{Str(doc, "id")}")), doc.ToString(),
        };
        await using var db = _fixture.CreateContext();
        var blob = (await db.Documents.AsNoTracking().SingleAsync()).BlobId.ToString();
        foreach (var t in texts)
        {
            Assert.DoesNotContain("blob", t, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(blob, t, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Storage", t);
        }
    }

    // ---------- how a file is served ----------

    [Theory]
    [InlineData("scan.pdf", "pdf", "application/pdf", true)]
    [InlineData("pic.png", "png", "image/png", true)]
    [InlineData("scan.tif", "tiff", "application/octet-stream", false)]
    [InlineData("notes.txt", "other", "application/octet-stream", false)]
    public async Task Every_file_is_a_download_except_pdf_and_png_and_jpeg_which_are_shown_under_a_restrictive_policy(string name, string kind, string contentType, bool inline)
    {
        var admin = await _api.SessionAsync("Admin");
        byte[] bytes = kind switch { "pdf" => Pdf(), "png" => Png(), "tiff" => [(byte)'I', (byte)'I', 0x2A, 0x00, 1, 2, 3, 4], _ => "<script>alert(1)</script>"u8.ToArray() };
        var doc = await ImportedAsync(admin, bytes, name);
        var response = await admin.Client.GetAsync($"/api/documents/{Str(doc, "id")}/content");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
        Assert.Equal(contentType, response.Content.Headers.ContentType!.MediaType);
        var disposition = response.Content.Headers.ContentDisposition!;
        Assert.Equal(inline ? "inline" : "attachment", disposition.DispositionType);
        Assert.Equal(name, disposition.FileNameStar ?? disposition.FileName?.Trim('"'));
        if (inline) Assert.StartsWith("sandbox", response.Headers.GetValues("Content-Security-Policy").Single());
        else Assert.False(response.Headers.Contains("Content-Security-Policy") && !response.Headers.GetValues("Content-Security-Policy").Single().StartsWith("sandbox"));
    }

    [Fact]
    public async Task A_file_can_be_forced_to_download_and_an_unusual_name_is_carried_safely()
    {
        var admin = await _api.SessionAsync("Admin");
        var doc = await ImportedAsync(admin, Png(), "résumé de Zoë.png");
        var response = await admin.Client.GetAsync($"/api/documents/{Str(doc, "id")}/content?download=true");
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal("résumé de Zoë.png", response.Content.Headers.ContentDisposition!.FileNameStar);
        Assert.Equal("image/png", response.Content.Headers.ContentType!.MediaType);
    }

    // ---------- the error codes ----------

    [Fact]
    public async Task Each_refusal_has_its_stable_code_and_status_and_stores_nothing()
    {
        var admin = await _api.SessionAsync("Admin");
        var cases = new (string Name, Func<Task<(HttpStatusCode, JsonElement)>> Act, HttpStatusCode Status, string Code)[]
        {
            ("empty file", () => UploadAsync(admin, []), HttpStatusCode.BadRequest, "file_empty"),
            ("blocked type", () => UploadAsync(admin, Png(), "setup.exe"), HttpStatusCode.BadRequest, "file_type_blocked"),
            ("a program", () => UploadAsync(admin, Program(), "invoice.pdf"), HttpStatusCode.BadRequest, "executable_content"),
            ("wrong type", () => UploadAsync(admin, Png(), "scan.pdf"), HttpStatusCode.BadRequest, "file_type_mismatch"),
            ("bad name", () => UploadAsync(admin, Png(), "a|b.png"), HttpStatusCode.BadRequest, "file_name_invalid"),
            ("no key", () => UploadAsync(admin, Png(), key: null), HttpStatusCode.BadRequest, "idempotency_key_required"),
            ("no file", () => UploadAsync(admin, omitFile: true), HttpStatusCode.BadRequest, "file_required"),
            ("no patient", () => UploadAsync(admin, Png(), patient: Guid.NewGuid().ToString()), HttpStatusCode.NotFound, "patient_not_found"),
        };
        foreach (var (name, act, status, code) in cases)
        {
            var (s, body) = await act();
            Assert.True(status == s && code == Error(body), $"{name}: expected {status} {code}, got {s} {(body.ValueKind == JsonValueKind.Undefined ? "" : body.ToString())}");
            Assert.False(string.IsNullOrWhiteSpace(Str(body, "message")));
        }
        Assert.Equal((0, 0, 0), await CountsAsync());
    }

    [Fact]
    public async Task Wrong_details_are_listed_field_by_field_including_a_date_that_is_not_a_date()
    {
        var admin = await _api.SessionAsync("Admin");
        var wrong = await UploadAsync(admin, Png(), category: Guid.NewGuid().ToString(), date: "2999-01-01", description: new string('d', 501));
        Assert.Equal((HttpStatusCode.BadRequest, "metadata_invalid"), (wrong.Status, Error(wrong.Body)));
        Assert.Equal(["category", "description", "documentDate"], wrong.Body.GetProperty("problems").EnumerateArray().Select(p => Str(p, "field")).Order());

        var garbled = await UploadAsync(admin, Png(), category: "not-a-guid", date: "yesterday");
        Assert.Equal("metadata_invalid", Error(garbled.Body));
        Assert.Equal(["category", "documentDate"], garbled.Body.GetProperty("problems").EnumerateArray().Select(p => Str(p, "field")).Order());
        var missing = await UploadAsync(admin, Png(), category: null, date: null);
        Assert.Equal(["category", "documentDate"], missing.Body.GetProperty("problems").EnumerateArray().Select(p => Str(p, "field")).Order());
        Assert.Equal(0, (await CountsAsync()).Documents);
    }

    [Fact]
    public async Task A_file_over_the_limit_is_refused_with_413_before_it_is_stored()
    {
        var admin = await _api.SessionAsync("Admin");
        var settings = await admin.SendAsync(HttpMethod.Get, "/api/document-settings");
        Assert.Equal(HttpStatusCode.OK, (await admin.SendAsync(HttpMethod.Put, "/api/document-settings", new { maxUploadBytes = 1_048_576, rowVersion = Str(settings.Body, "rowVersion") })).Status);
        var big = new byte[3 * 1_048_576]; Png().CopyTo(big, 0);
        var r = await UploadAsync(admin, big, "big.png");
        Assert.Equal((HttpStatusCode.RequestEntityTooLarge, "file_too_large"), (r.Status, Error(r.Body)));
        Assert.Equal(0, (await CountsAsync()).Documents);
        Assert.Equal(HttpStatusCode.Created, (await UploadAsync(admin, Png(), "small.png")).Status);
    }

    [Fact]
    public async Task A_duplicate_is_warned_about_with_409_and_added_only_with_the_confirmation()
    {
        var admin = await _api.SessionAsync("Admin");
        var first = await ImportedAsync(admin, Png(300), "first.png");
        var warn = await UploadAsync(admin, Png(300), "second.png");
        Assert.Equal((HttpStatusCode.Conflict, "duplicate_document"), (warn.Status, Error(warn.Body)));
        var detail = warn.Body.GetProperty("detail");
        Assert.Equal((Str(first, "id"), "Radiograph"), (Str(detail, "documentId"), Str(detail, "categoryName")));
        Assert.Equal(1, (await CountsAsync()).Documents);

        var wrong = await UploadAsync(admin, Png(300), "second.png", confirm: Guid.NewGuid().ToString());
        Assert.Equal((HttpStatusCode.Conflict, "duplicate_confirmation_invalid"), (wrong.Status, Error(wrong.Body)));
        var garbled = await UploadAsync(admin, Png(300), "second.png", confirm: "nope");
        Assert.Equal("metadata_invalid", Error(garbled.Body));

        var added = await UploadAsync(admin, Png(300), "second.png", confirm: Str(first, "id"));
        Assert.Equal(HttpStatusCode.Created, added.Status);
        Assert.Equal(Str(first, "id"), Str(added.Body.GetProperty("document"), "duplicateOfDocumentId"));
    }

    [Fact]
    public async Task The_same_key_for_a_different_file_is_refused_with_409()
    {
        var admin = await _api.SessionAsync("Admin");
        Assert.Equal(HttpStatusCode.Created, (await UploadAsync(admin, Png(300), key: "same")).Status);
        var r = await UploadAsync(admin, Png(301), key: "same");
        Assert.Equal((HttpStatusCode.Conflict, "idempotency_key_reused"), (r.Status, Error(r.Body)));
    }

    [Fact]
    public async Task Listing_the_library_of_an_unknown_patient_is_404_not_an_empty_list()
    {
        var admin = await _api.SessionAsync("Admin");
        var r = await admin.SendAsync(HttpMethod.Get, $"/api/patients/{Guid.NewGuid()}/documents");
        Assert.Equal((HttpStatusCode.NotFound, "patient_not_found"), (r.Status, Error(r.Body)));
    }

    [Fact]
    public async Task Unknown_documents_are_404_and_a_stale_or_missing_version_is_refused()
    {
        var admin = await _api.SessionAsync("Admin");
        var doc = await ImportedAsync(admin);
        var id = Str(doc, "id");
        Assert.Equal("document_not_found", Error((await admin.SendAsync(HttpMethod.Get, $"/api/documents/{Guid.NewGuid()}")).Body));
        Assert.Equal(HttpStatusCode.NotFound, (await admin.Client.GetAsync($"/api/documents/{Guid.NewGuid()}/content")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.SendAsync(HttpMethod.Put, $"/api/documents/{Guid.NewGuid()}", new { categoryId = Photo, documentDate = "2026-10-01", rowVersion = Str(doc, "rowVersion") })).Status);

        var body = new { categoryId = Photo, documentDate = "2026-10-01", description = "x", rowVersion = Str(doc, "rowVersion") };
        Assert.Equal(HttpStatusCode.OK, (await admin.SendAsync(HttpMethod.Put, $"/api/documents/{id}", body)).Status);
        var stale = await admin.SendAsync(HttpMethod.Put, $"/api/documents/{id}", body with { description = "from the old screen" });
        Assert.Equal((HttpStatusCode.Conflict, "concurrency_conflict"), (stale.Status, Error(stale.Body)));
        var none = await admin.SendAsync(HttpMethod.Put, $"/api/documents/{id}", new { categoryId = Photo, documentDate = "2026-10-01", description = "x" });
        Assert.Equal((HttpStatusCode.BadRequest, "row_version_required"), (none.Status, Error(none.Body)));
    }

    // ---------- settings ----------

    [Fact]
    public async Task The_administrator_manages_categories_the_size_limit_and_the_blocked_types()
    {
        var admin = await _api.SessionAsync("Admin");

        var added = await admin.SendAsync(HttpMethod.Post, "/api/document-categories", new { name = "Consent copy" });
        Assert.Equal(HttpStatusCode.Created, added.Status);
        var cat = added.Body.GetProperty("category");
        Assert.Equal("consent_copy", Str(cat, "code"));
        Assert.Equal("category_exists", Error((await admin.SendAsync(HttpMethod.Post, "/api/document-categories", new { name = "consent COPY" })).Body));
        var renamed = await admin.SendAsync(HttpMethod.Put, $"/api/document-categories/{Str(cat, "id")}", new { name = "Signed consent", isActive = false, rowVersion = Str(cat, "rowVersion") });
        Assert.Equal(HttpStatusCode.OK, renamed.Status);
        Assert.False(renamed.Body.GetProperty("category").GetProperty("isActive").GetBoolean());
        Assert.DoesNotContain((await admin.SendAsync(HttpMethod.Get, "/api/document-categories")).Body.GetProperty("categories").EnumerateArray(), c => Str(c, "id") == Str(cat, "id"));
        Assert.Contains((await admin.SendAsync(HttpMethod.Get, "/api/document-categories?includeInactive=true")).Body.GetProperty("categories").EnumerateArray(), c => Str(c, "id") == Str(cat, "id"));

        var settings = await admin.SendAsync(HttpMethod.Get, "/api/document-settings");
        Assert.Equal((104_857_600L, 1_048_576L, 1_073_741_824L), (settings.Body.GetProperty("maxUploadBytes").GetInt64(), settings.Body.GetProperty("minAllowedBytes").GetInt64(), settings.Body.GetProperty("maxAllowedBytes").GetInt64()));
        var tooSmall = await admin.SendAsync(HttpMethod.Put, "/api/document-settings", new { maxUploadBytes = 1000, rowVersion = Str(settings.Body, "rowVersion") });
        Assert.Equal((HttpStatusCode.BadRequest, "metadata_invalid"), (tooSmall.Status, Error(tooSmall.Body)));
        Assert.Equal(HttpStatusCode.OK, (await admin.SendAsync(HttpMethod.Put, "/api/document-settings", new { maxUploadBytes = 200_000_000, rowVersion = Str(settings.Body, "rowVersion") })).Status);

        var blocked = await admin.SendAsync(HttpMethod.Post, "/api/document-settings/blocked-extensions", new { extension = ".SWF" });
        Assert.Equal(HttpStatusCode.OK, blocked.Status);
        Assert.Equal("swf", Str(blocked.Body.GetProperty("blockedExtension"), "extension"));
        Assert.Equal("file_type_blocked", Error((await UploadAsync(admin, Png(), "movie.swf")).Body));
        Assert.Equal(HttpStatusCode.NoContent, (await admin.SendAsync(HttpMethod.Delete, "/api/document-settings/blocked-extensions/swf")).Status);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.SendAsync(HttpMethod.Delete, "/api/document-settings/blocked-extensions/swf")).Status);
        var locked = await admin.SendAsync(HttpMethod.Delete, "/api/document-settings/blocked-extensions/exe");
        Assert.Equal((HttpStatusCode.Conflict, "built_in_locked"), (locked.Status, Error(locked.Body)));
        var list = await admin.SendAsync(HttpMethod.Get, "/api/document-settings/blocked-extensions");
        Assert.Equal(15, list.Body.GetProperty("blockedExtensions").GetArrayLength());
    }

    [Fact]
    public async Task Nothing_in_a_response_or_a_log_entry_carries_the_file_name_or_description()
    {
        var admin = await _api.SessionAsync("Admin");
        await ImportedAsync(admin, Png(), "ann-okafor-private.png");
        await using var db = _fixture.CreateContext();
        var logs = await db.AuditLogEntries.AsNoTracking().Where(a => a.EventType.StartsWith("Document")).Select(a => a.Details + " " + a.Reason + " " + a.EntityType).ToListAsync();
        Assert.NotEmpty(logs);
        Assert.All(logs, l => { Assert.DoesNotContain("okafor", l, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("Bitewing", l, StringComparison.OrdinalIgnoreCase); });
    }
}
