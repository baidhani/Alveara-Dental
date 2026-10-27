using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Imports;
using Session = Alveara.Api.Tests.SchedulingApiHarness.Session;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-018-C02 slice 1, bulk import batches through the real pipeline (real API, real SQL Server, cookie sessions and CSRF): the upload, the list and one batch are refused to an anonymous caller (401) and to every
/// role without BulkImportData (403), the upload also needs the CSRF token (400 csrf_token_invalid) and nothing is created by a refused call; the administrator's upload is 201 and no-store, the same file again is 200
/// with the same batch, a refused file is 400 import_file_rejected with its problems and without any value from the file, a file over the size limit is refused the same way, a bad kind or label is 400 with a stable
/// code, an unknown batch is 404, and the list and one batch show counts, hash and columns but no file content.
/// </summary>
public class ImportBatchesApiTests : IAsyncLifetime
{
    private const string BatchesUrl = "/api/import-batches";
    private readonly TestDatabaseFixture _fixture = new();
    private SchedulingApiHarness _api = null!;
    private Session _admin = null!;
    private readonly Dictionary<string, Session> _others = new();

    private static readonly byte[] Sound = Encoding.UTF8.GetBytes("First,Last\r\nAnn,Rivera\r\nBo,Moss\r\n");

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
        var s = new SchedulingTestSupport(_fixture);
        _api = new SchedulingApiHarness(_fixture);
        await s.ArrangeAsync();
        _admin = await _api.SessionAsync("Admin");
        foreach (var role in new[] { "Dentist", "Hygienist", "Assistant", "FrontDesk", "Billing", "OfficeManager" }) _others[role] = await _api.SessionAsync(role);
    }

    public Task DisposeAsync() { _api.Dispose(); return _fixture.DisposeAsync(); }

    private sealed record Reply(HttpStatusCode Status, JsonElement Body, HttpResponseMessage Raw)
    {
        public string? Error => Body.ValueKind == JsonValueKind.Object && Body.TryGetProperty("error", out var e) ? e.GetString() : null;
    }

    private static async Task<Reply> ReadAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return new Reply(response.StatusCode, string.IsNullOrWhiteSpace(text) ? default : JsonDocument.Parse(text).RootElement.Clone(), response);
    }

    private static async Task<Reply> UploadAsync(Session session, byte[] content, string? kind = "Patients", string? label = "Old system export", bool csrf = true)
    {
        var query = new List<string>();
        if (kind is not null) query.Add($"sourceKind={Uri.EscapeDataString(kind)}");
        if (label is not null) query.Add($"label={Uri.EscapeDataString(label)}");
        var request = new HttpRequestMessage(HttpMethod.Post, BatchesUrl + (query.Count > 0 ? "?" + string.Join("&", query) : "")) { Content = new ByteArrayContent(content) };
        request.Content.Headers.ContentType = new("text/csv");
        if (csrf) request.Headers.Add("X-CSRF-Token", session.Csrf);
        return await ReadAsync(await session.Client.SendAsync(request));
    }

    private async Task<int> BatchCountAsync() { await using var db = _fixture.CreateContext(); return await db.ImportBatches.CountAsync(); }

    private async Task<int> AuditCountAsync(string eventType) { await using var db = _fixture.CreateContext(); return await db.AuditLogEntries.CountAsync(a => a.EventType == eventType); }

    [Fact]
    public async Task The_administrator_uploads_a_file_and_the_same_file_again_returns_the_same_batch()
    {
        var first = await UploadAsync(_admin, Sound);

        Assert.Equal(HttpStatusCode.Created, first.Status);
        Assert.Equal("no-store", first.Raw.Headers.CacheControl?.ToString());
        Assert.True(first.Body.GetProperty("created").GetBoolean());
        var batch = first.Body.GetProperty("batch");
        Assert.Equal(("Patients", "Old system export", "Uploaded", "AllOrNothing", 2), (batch.GetProperty("sourceKind").GetString(), batch.GetProperty("sourceLabel").GetString(), batch.GetProperty("status").GetString(), batch.GetProperty("policy").GetString(), batch.GetProperty("rowCount").GetInt32()));
        Assert.Equal(["First", "Last"], batch.GetProperty("columnNames").EnumerateArray().Select(c => c.GetString()));
        Assert.Equal($"{BatchesUrl}/{batch.GetProperty("id").GetGuid()}", first.Raw.Headers.Location?.ToString());

        var again = await UploadAsync(_admin, Sound);

        Assert.Equal(HttpStatusCode.OK, again.Status);
        Assert.False(again.Body.GetProperty("created").GetBoolean());
        Assert.Equal(batch.GetProperty("id").GetGuid(), again.Body.GetProperty("batch").GetProperty("id").GetGuid());
        Assert.Equal(1, await BatchCountAsync());
        Assert.Equal(1, await AuditCountAsync(AuditEventTypes.ImportBatchCreated));
    }

    [Fact]
    public async Task The_list_and_one_batch_show_counts_hash_and_columns_and_an_unknown_batch_is_404()
    {
        var made = (await UploadAsync(_admin, Sound)).Body.GetProperty("batch");
        var id = made.GetProperty("id").GetGuid();

        var list = await ReadAsync(await _admin.Client.GetAsync(BatchesUrl));
        var one = await ReadAsync(await _admin.Client.GetAsync($"{BatchesUrl}/{id}"));
        var missing = await ReadAsync(await _admin.Client.GetAsync($"{BatchesUrl}/{Guid.NewGuid()}"));

        Assert.Equal(HttpStatusCode.OK, list.Status);
        Assert.Equal(id, Assert.Single(list.Body.EnumerateArray()).GetProperty("id").GetGuid());
        Assert.Equal("no-store", list.Raw.Headers.CacheControl?.ToString());
        Assert.Equal((HttpStatusCode.OK, 64), (one.Status, one.Body.GetProperty("fileSha256").GetString()!.Length));
        Assert.DoesNotContain("Rivera", one.Body.GetRawText());                                           // a batch never shows a value from the file
        Assert.Equal((HttpStatusCode.NotFound, "batch_not_found"), (missing.Status, missing.Error));
    }

    [Fact]
    public async Task Every_call_is_refused_to_an_anonymous_caller_and_to_every_role_without_the_permission_and_creates_nothing()
    {
        using var anonymous = _api.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync(BatchesUrl + "?sourceKind=Patients&label=x", new ByteArrayContent(Sound))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(BatchesUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"{BatchesUrl}/{Guid.NewGuid()}")).StatusCode);

        foreach (var (role, session) in _others)
        {
            Assert.True(HttpStatusCode.Forbidden == (await UploadAsync(session, Sound)).Status, $"{role} must not upload");
            Assert.True(HttpStatusCode.Forbidden == (await session.Client.GetAsync(BatchesUrl)).StatusCode, $"{role} must not list");
            Assert.True(HttpStatusCode.Forbidden == (await session.Client.GetAsync($"{BatchesUrl}/{Guid.NewGuid()}")).StatusCode, $"{role} must not read one");
        }

        Assert.Equal(0, await BatchCountAsync());
        Assert.Equal(0, await AuditCountAsync(AuditEventTypes.ImportBatchCreated));
    }

    [Fact]
    public async Task An_upload_without_the_csrf_token_is_refused_and_creates_nothing()
    {
        var refused = await UploadAsync(_admin, Sound, csrf: false);

        Assert.Equal((HttpStatusCode.BadRequest, "csrf_token_invalid"), (refused.Status, refused.Error));
        Assert.Equal(0, await BatchCountAsync());
    }

    [Fact]
    public async Task A_refused_file_is_400_with_its_problems_and_without_any_value_from_the_file_and_nothing_is_stored()
    {
        var bad = Encoding.UTF8.GetBytes("First,Last\r\nZEBRA-VALUE\r\nBo,Moss,extra\r\n");

        var refused = await UploadAsync(_admin, bad);

        Assert.Equal((HttpStatusCode.BadRequest, "import_file_rejected"), (refused.Status, refused.Error));
        Assert.Equal(2, refused.Body.GetProperty("totalProblems").GetInt32());
        Assert.Equal(["row_width", "row_width"], refused.Body.GetProperty("problems").EnumerateArray().Select(p => p.GetProperty("code").GetString()));
        Assert.Equal([2, 3], refused.Body.GetProperty("problems").EnumerateArray().Select(p => p.GetProperty("row").GetInt32()));
        Assert.DoesNotContain("ZEBRA", refused.Body.GetRawText());
        Assert.Equal(0, await BatchCountAsync());
        Assert.Equal(1, await AuditCountAsync(AuditEventTypes.ImportFileRejected));
    }

    [Fact]
    public async Task A_file_over_the_size_limit_is_refused_the_same_way_and_a_file_exactly_at_the_limit_is_not_refused_for_its_size()
    {
        var limit = new ImportOptions().MaxFileBytes;
        var over = new byte[limit + 1];
        Array.Fill(over, (byte)'a');

        var refused = await UploadAsync(_admin, over);

        Assert.Equal((HttpStatusCode.BadRequest, "import_file_rejected"), (refused.Status, refused.Error));
        Assert.Equal("file_too_large", refused.Body.GetProperty("problems")[0].GetProperty("code").GetString());
        Assert.Equal(0, await BatchCountAsync());

        var exact = new byte[limit];
        Array.Fill(exact, (byte)'a');
        var atLimit = await UploadAsync(_admin, exact);                                                // not a table (one header, no rows), but not too large either
        Assert.Equal(HttpStatusCode.BadRequest, atLimit.Status);
        Assert.DoesNotContain("file_too_large", atLimit.Body.GetRawText());
    }

    [Fact]
    public async Task A_bad_kind_or_a_bad_label_is_400_with_a_stable_code_and_creates_nothing()
    {
        foreach (var kind in new string?[] { null, "", "Everything" })
        {
            var refused = await UploadAsync(_admin, Sound, kind);
            Assert.Equal((HttpStatusCode.BadRequest, "invalid_source_kind"), (refused.Status, refused.Error));
        }

        foreach (var label in new string?[] { null, "  ", new string('x', 101) })
        {
            var refused = await UploadAsync(_admin, Sound, label: label);
            Assert.Equal((HttpStatusCode.BadRequest, "invalid_label"), (refused.Status, refused.Error));
        }

        Assert.Equal(0, await BatchCountAsync());
    }
}
