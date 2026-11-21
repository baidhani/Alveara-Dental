using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Documents;
using Xunit;
using Session = Alveara.Api.Tests.SchedulingApiHarness.Session;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N026 (PD-24) at the HTTP boundary, against a real API, real SQL Server and a real signed-in session per role: a Clinical document is read only with ViewClinicalDocumentation, a NonClinical one with
/// ViewDocuments; a document the caller may not read is answered exactly like one that does not exist (left out of the list, 404 everywhere else); importing or moving a document into a category the caller may
/// not read is refused (403); editing and inactivating need ManageDocuments and never bypass the read rule; reclassifying a category needs ManageDocumentSettings and is audited; a category added by a practice is
/// Clinical; the migration mapped the built-in categories as the owner approved; and the duplicate check never discloses a document the importer may not read.
/// </summary>
public class DocumentClassificationAccessApiTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private SchedulingTestSupport _s = null!;
    private SchedulingApiHarness _api = null!;
    private string _patient = "";
    private static readonly Guid Radiograph = DocumentCategorySeeds.Radiograph;          // Clinical
    private static readonly Guid InsuranceCard = DocumentCategorySeeds.InsuranceCard;    // NonClinical
    private static readonly Guid Identification = DocumentCategorySeeds.Identification;  // NonClinical

    public Task InitializeAsync() => _fixture.InitializeOwnerAsync(async () =>
    {
        _s = new SchedulingTestSupport(_fixture);
        _api = new SchedulingApiHarness(_fixture);
        await _s.ArrangeAsync();
        _patient = (await _s.PatientAsync()).ToString();
    });
    public Task DisposeAsync() { _api.Dispose(); return _fixture.DisposeAsync(); }

    private static byte[] Png(int extra = 100, byte fill = 7) => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, .. Enumerable.Repeat(fill, extra)];
    private static string Str(JsonElement e, string name) => e.GetProperty(name).GetString()!;
    private static string Error(JsonElement e) => Str(e, "error");

    private async Task<(HttpStatusCode Status, JsonElement Body)> UploadAsync(Session by, Guid category, byte[] bytes)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "file", "scan.png");
        form.Add(new StringContent(category.ToString()), "categoryId");
        form.Add(new StringContent("2026-10-01"), "documentDate");
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/patients/{_patient}/documents") { Content = form };
        request.Headers.Add("X-CSRF-Token", by.Csrf);
        request.Headers.Add("Idempotency-Key", $"k-{Guid.NewGuid():N}");
        var response = await by.Client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        return (response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone());
    }

    private async Task<JsonElement> ImportedAsync(Session by, Guid category, int extra)
    {
        var r = await UploadAsync(by, category, Png(extra));
        Assert.Equal(HttpStatusCode.Created, r.Status);
        return r.Body.GetProperty("document");
    }

    private async Task<int> DocumentCountAsync() { await using var db = _fixture.CreateContext(); return await db.Documents.CountAsync(); }

    // ---------- reading, role by role ----------

    /// <summary>The roles that hold ViewClinicalDocumentation by default read Clinical documents; the office manager, front desk and billing read only NonClinical ones.</summary>
    [Theory]
    [InlineData("Admin", true)] [InlineData("Dentist", true)] [InlineData("Hygienist", true)] [InlineData("Assistant", true)]
    [InlineData("OfficeManager", false)] [InlineData("FrontDesk", false)] [InlineData("Billing", false)]
    public async Task A_clinical_document_is_read_only_with_clinical_access_and_is_otherwise_answered_as_not_found(string role, bool readsClinical)
    {
        var admin = await _api.SessionAsync("Admin");
        var clinical = Str(await ImportedAsync(admin, Radiograph, 300), "id");
        var nonClinical = Str(await ImportedAsync(admin, InsuranceCard, 301), "id");
        var by = await _api.SessionAsync(role);

        var listed = (await by.SendAsync(HttpMethod.Get, $"/api/patients/{_patient}/documents")).Body.GetProperty("entries").EnumerateArray()
            .Where(e => Str(e, "kind") == LibraryEntry.DocumentKind).Select(e => Str(e, "id")).ToHashSet();
        Assert.Contains(nonClinical, listed);
        Assert.Equal(readsClinical, listed.Contains(clinical));

        var expected = readsClinical ? HttpStatusCode.OK : HttpStatusCode.NotFound;
        foreach (var url in new[] { $"/api/documents/{clinical}", $"/api/documents/{clinical}/content", $"/api/documents/{clinical}/content?download=true", $"/api/documents/{clinical}/thumbnail" })
            Assert.Equal(expected, (await by.Client.GetAsync(url)).StatusCode);
        foreach (var url in new[] { $"/api/documents/{nonClinical}", $"/api/documents/{nonClinical}/content", $"/api/documents/{nonClinical}/thumbnail" })
            Assert.Equal(HttpStatusCode.OK, (await by.Client.GetAsync(url)).StatusCode);

        if (!readsClinical)
        {
            var hidden = await by.SendAsync(HttpMethod.Get, $"/api/documents/{clinical}");
            Assert.Equal("document_not_found", Error(hidden.Body));                                                                  // the same answer as for a document that does not exist
        }
    }

    // ---------- importing ----------

    [Fact]
    public async Task Importing_into_a_category_the_importer_may_not_read_is_refused_and_stores_nothing()
    {
        var office = await _api.SessionAsync("OfficeManager");                                                                          // imports, but has no clinical access
        var before = await DocumentCountAsync();
        var refused = await UploadAsync(office, Radiograph, Png(400));
        Assert.Equal((HttpStatusCode.Forbidden, DocumentAccessErrors.CategoryNotPermittedCode), (refused.Status, Error(refused.Body)));
        Assert.Equal(before, await DocumentCountAsync());
        Assert.Equal(HttpStatusCode.Created, (await UploadAsync(office, InsuranceCard, Png(401))).Status);                                // a NonClinical category is fine

        var dentist = await _api.SessionAsync("Dentist");                                                                                 // U-008: imports, with clinical access
        Assert.Equal(HttpStatusCode.Created, (await UploadAsync(dentist, Radiograph, Png(402))).Status);
    }

    [Fact]
    public async Task The_duplicate_check_never_discloses_a_document_the_importer_may_not_read()
    {
        var admin = await _api.SessionAsync("Admin");
        var bytes = Png(500, 3);
        Assert.Equal(HttpStatusCode.Created, (await UploadAsync(admin, Radiograph, bytes)).Status);                                     // a clinical copy of the file exists

        var office = await _api.SessionAsync("OfficeManager");
        var again = await UploadAsync(office, InsuranceCard, bytes);
        Assert.Equal(HttpStatusCode.Created, again.Status);                                                                             // not 409 duplicate_document: the clinical copy is not disclosed
        Assert.Equal(JsonValueKind.Null, again.Body.GetProperty("document").GetProperty("duplicateOfDocumentId").ValueKind);

        var visible = await UploadAsync(office, InsuranceCard, bytes);                                                                   // a copy the office manager CAN read is reported as usual
        Assert.Equal((HttpStatusCode.Conflict, "duplicate_document"), (visible.Status, Error(visible.Body)));
    }

    // ---------- editing, inactivating, moving ----------

    [Fact]
    public async Task ManageDocuments_never_bypasses_the_read_rule_and_nobody_moves_a_document_into_a_category_they_cannot_read()
    {
        var admin = await _api.SessionAsync("Admin");
        var clinical = await ImportedAsync(admin, Radiograph, 600);
        var card = await ImportedAsync(admin, InsuranceCard, 601);
        var office = await _api.SessionAsync("OfficeManager");                                                                          // holds ManageDocuments, not clinical access

        var edit = await office.SendAsync(HttpMethod.Put, $"/api/documents/{Str(clinical, "id")}", new { categoryId = Radiograph, documentDate = "2026-10-01", description = "x", rowVersion = Str(clinical, "rowVersion") });
        Assert.Equal((HttpStatusCode.NotFound, "document_not_found"), (edit.Status, Error(edit.Body)));
        var inactivate = await office.SendAsync(HttpMethod.Post, $"/api/documents/{Str(clinical, "id")}/inactivate", new { reason = "Wrong patient.", rowVersion = Str(clinical, "rowVersion") });
        Assert.Equal(HttpStatusCode.NotFound, inactivate.Status);

        var move = await office.SendAsync(HttpMethod.Put, $"/api/documents/{Str(card, "id")}", new { categoryId = Radiograph, documentDate = "2026-10-01", rowVersion = Str(card, "rowVersion") });
        Assert.Equal((HttpStatusCode.Forbidden, DocumentAccessErrors.CategoryNotPermittedCode), (move.Status, Error(move.Body)));
        await using (var db = _fixture.CreateContext())
            Assert.Equal(InsuranceCard, (await db.Documents.AsNoTracking().SingleAsync(d => d.Id == Guid.Parse(Str(card, "id")))).CategoryId);   // unchanged

        var allowed = await office.SendAsync(HttpMethod.Put, $"/api/documents/{Str(card, "id")}", new { categoryId = Identification, documentDate = "2026-10-01", rowVersion = Str(card, "rowVersion") });
        Assert.Equal(HttpStatusCode.OK, allowed.Status);                                                                                // NonClinical to NonClinical is fine

        var dentist = await _api.SessionAsync("Dentist");                                                                                 // reads clinical, but has no ManageDocuments
        var denied = await dentist.SendAsync(HttpMethod.Put, $"/api/documents/{Str(clinical, "id")}", new { categoryId = Radiograph, documentDate = "2026-10-01", description = "x", rowVersion = Str(clinical, "rowVersion") });
        Assert.Equal(HttpStatusCode.Forbidden, denied.Status);
    }

    // ---------- categories ----------

    [Fact]
    public async Task The_migration_mapped_the_built_in_categories_as_approved_and_a_practice_added_category_is_clinical()
    {
        await using (var db = _fixture.CreateContext())
        {
            var actual = await db.DocumentCategories.AsNoTracking().Where(c => DocumentCategorySeeds.All.Select(s => s.Id).Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Classification);
            Assert.Equal(DocumentClassifications.Seeds.OrderBy(k => k.Key), actual.OrderBy(k => k.Key));
            Assert.Equal(2, actual.Values.Count(v => v == DocumentClassifications.NonClinical));                                         // only the insurance card and identification
        }

        var admin = await _api.SessionAsync("Admin");
        var added = await admin.SendAsync(HttpMethod.Post, "/api/document-categories", new { name = "Consent copy" });
        Assert.Equal(HttpStatusCode.Created, added.Status);
        Assert.Equal(DocumentClassifications.Clinical, Str(added.Body.GetProperty("category"), "classification"));

        await using var check = _fixture.CreateContext();
        var refused = await Assert.ThrowsAnyAsync<DbUpdateException>(async () =>
        {
            var row = await check.DocumentCategories.SingleAsync(c => c.Id == InsuranceCard);
            row.Classification = "Public";
            await check.SaveChangesAsync();
        });
        Assert.Contains("CK_DocumentCategories_Classification", refused.InnerException?.Message ?? "");                                 // the database refuses any other value
    }

    [Fact]
    public async Task The_category_list_says_which_categories_the_caller_may_read_and_file_into()
    {
        var office = await _api.SessionAsync("OfficeManager");
        var categories = (await office.SendAsync(HttpMethod.Get, "/api/document-categories")).Body.GetProperty("categories").EnumerateArray()
            .ToDictionary(c => Guid.Parse(Str(c, "id")), c => (Classification: Str(c, "classification"), Readable: c.GetProperty("readable").GetBoolean()));
        Assert.Equal((DocumentClassifications.Clinical, false), categories[Radiograph]);
        Assert.Equal((DocumentClassifications.NonClinical, true), categories[InsuranceCard]);

        var dentist = await _api.SessionAsync("Dentist");
        Assert.True((await dentist.SendAsync(HttpMethod.Get, "/api/document-categories")).Body.GetProperty("categories").EnumerateArray()
            .All(c => c.GetProperty("readable").GetBoolean()));
    }

    [Fact]
    public async Task Reclassifying_a_category_needs_ManageDocumentSettings_is_audited_and_takes_effect_at_once()
    {
        var admin = await _api.SessionAsync("Admin");
        var card = Str(await ImportedAsync(admin, InsuranceCard, 700), "id");
        var office = await _api.SessionAsync("OfficeManager");
        Assert.Equal(HttpStatusCode.OK, (await office.Client.GetAsync($"/api/documents/{card}")).StatusCode);

        var category = (await admin.SendAsync(HttpMethod.Get, "/api/document-categories")).Body.GetProperty("categories").EnumerateArray().Single(c => Str(c, "id") == InsuranceCard.ToString());
        var byOffice = await office.SendAsync(HttpMethod.Put, $"/api/document-categories/{InsuranceCard}", new { classification = "Clinical", rowVersion = Str(category, "rowVersion") });
        Assert.Equal(HttpStatusCode.Forbidden, byOffice.Status);

        var invalid = await admin.SendAsync(HttpMethod.Put, $"/api/document-categories/{InsuranceCard}", new { classification = "Secret", rowVersion = Str(category, "rowVersion") });
        Assert.Equal((HttpStatusCode.BadRequest, DocumentErrorCodes.MetadataInvalid), (invalid.Status, Error(invalid.Body)));

        var changed = await admin.SendAsync(HttpMethod.Put, $"/api/document-categories/{InsuranceCard}", new { classification = "Clinical", rowVersion = Str(category, "rowVersion") });
        Assert.Equal(HttpStatusCode.OK, changed.Status);
        Assert.Equal(DocumentClassifications.Clinical, Str(changed.Body.GetProperty("category"), "classification"));
        Assert.Equal(HttpStatusCode.NotFound, (await office.Client.GetAsync($"/api/documents/{card}")).StatusCode);                     // at once: the office manager no longer reads it

        await using var db = _fixture.CreateContext();
        var audit = await db.AuditLogEntries.AsNoTracking().SingleAsync(a => a.EventType == "DocumentCategoryReclassified");
        Assert.Contains("from NonClinical to Clinical", audit.Details);
        Assert.DoesNotContain("scan.png", audit.Details);                                                                                 // no document detail in the log

        var replay = await admin.SendAsync(HttpMethod.Put, $"/api/document-categories/{InsuranceCard}", new { classification = "Clinical", rowVersion = Str(changed.Body.GetProperty("category"), "rowVersion") });
        Assert.Equal(HttpStatusCode.OK, replay.Status);
        Assert.Equal(1, await db.AuditLogEntries.CountAsync(a => a.EventType == "DocumentCategoryReclassified"));                         // the same value again changes and logs nothing
    }
}
