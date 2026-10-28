using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Alveara.Api.Architecture.Imports;
using Session = Alveara.Api.Tests.SchedulingApiHarness.Session;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-018-C02 slice 2, mapping and the dry-run through the real pipeline (real API, real SQL Server, cookie sessions and CSRF): the suggestion, the mapping save, the dry-run and the row results work for the
/// administrator end to end, with no-store; saving a mapping and running the dry-run need the CSRF token; every one of the four calls is refused to an anonymous caller (401) and to every role without
/// BulkImportData (403); a bad mapping is 400 mapping_invalid with its problems; and an unknown batch, a missing mapping, a missing dry-run, a bad status and a kind not supported yet each have a stable code.
/// </summary>
public class ImportMappingApiTests : IAsyncLifetime
{
    private const string BatchesUrl = "/api/import-batches";
    private readonly TestDatabaseFixture _fixture = new();
    private SchedulingApiHarness _api = null!;
    private Session _admin = null!;
    private readonly Dictionary<string, Session> _others = new();

    private static readonly byte[] Patients = Encoding.UTF8.GetBytes(
        "First Name,Last Name,DOB,Phone,Email,Address,City,State,Zip\r\n" +
        "Ann,Rivera,1950-01-10,555-100-1000,,4 Pine St,Austin,TX,78701\r\n" +
        ",Moss,1962-03-04,555-300-3000,,4 Pine St,Austin,TX,78701\r\n");

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

    private static async Task<Reply> SendAsync(Session session, HttpMethod method, string url, object? body = null, byte[]? raw = null, bool csrf = true)
    {
        var request = new HttpRequestMessage(method, url);
        if (csrf) request.Headers.Add("X-CSRF-Token", session.Csrf);
        if (raw is not null) { request.Content = new ByteArrayContent(raw); request.Content.Headers.ContentType = new("text/csv"); }
        else if (body is not null) request.Content = System.Net.Http.Json.JsonContent.Create(body);
        var response = await session.Client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        return new Reply(response.StatusCode, string.IsNullOrWhiteSpace(text) ? default : JsonDocument.Parse(text).RootElement.Clone(), response);
    }

    private async Task<Guid> UploadAsync(string kind = "Patients") =>
        (await SendAsync(_admin, HttpMethod.Post, $"{BatchesUrl}?sourceKind={kind}&label=Old%20system", raw: Patients)).Body.GetProperty("batch").GetProperty("id").GetGuid();

    private static Dictionary<string, string?> Mapping() => new()
    {
        ["First Name"] = "firstName", ["Last Name"] = "lastName", ["DOB"] = "dateOfBirth", ["Phone"] = "phone", ["Email"] = "email", ["Address"] = "addressLine1", ["City"] = "city", ["State"] = "state", ["Zip"] = "postalCode",
    };

    private async Task<int> PatientCountAsync() { await using var db = _fixture.CreateContext(); return await db.Patients.CountAsync(); }

    [Fact]
    public async Task The_administrator_maps_dry_runs_and_reads_the_row_results_end_to_end_and_no_patient_is_registered()
    {
        var id = await UploadAsync();

        var suggestion = await SendAsync(_admin, HttpMethod.Get, $"{BatchesUrl}/{id}/mapping-suggestion");
        Assert.Equal(HttpStatusCode.OK, suggestion.Status);
        Assert.Equal("no-store", suggestion.Raw.Headers.CacheControl?.ToString());
        Assert.Equal("firstName", suggestion.Body.GetProperty("suggested").GetProperty("First Name").GetString());
        Assert.Equal(JsonValueKind.Null, suggestion.Body.GetProperty("current").ValueKind);
        Assert.Equal(12, suggestion.Body.GetProperty("fields").GetArrayLength());

        var saved = await SendAsync(_admin, HttpMethod.Put, $"{BatchesUrl}/{id}/mapping", new { mapping = Mapping() });
        Assert.Equal(HttpStatusCode.OK, saved.Status);
        Assert.Equal("dateOfBirth", saved.Body.GetProperty("current").GetProperty("DOB").GetString());

        var dryRun = await SendAsync(_admin, HttpMethod.Post, $"{BatchesUrl}/{id}/dry-run");
        Assert.Equal(HttpStatusCode.OK, dryRun.Status);
        Assert.Equal((1, 1, 0, 0), (dryRun.Body.GetProperty("valid").GetInt32(), dryRun.Body.GetProperty("rejected").GetInt32(), dryRun.Body.GetProperty("skipped").GetInt32(), dryRun.Body.GetProperty("unresolved").GetInt32()));
        Assert.Equal("DryRunComplete", dryRun.Body.GetProperty("batch").GetProperty("status").GetString());

        var rows = await SendAsync(_admin, HttpMethod.Get, $"{BatchesUrl}/{id}/rows");
        Assert.Equal(HttpStatusCode.OK, rows.Status);
        Assert.Equal(["Valid", "Rejected"], rows.Body.EnumerateArray().Select(r => r.GetProperty("status").GetString()));
        Assert.Equal("First name is required.", rows.Body[1].GetProperty("errors").GetProperty("firstName").GetString());
        Assert.DoesNotContain("Rivera", rows.Body.GetRawText());                                           // the results never repeat a value from the file
        var rejectedOnly = await SendAsync(_admin, HttpMethod.Get, $"{BatchesUrl}/{id}/rows?status=Rejected&limit=5&after=1");
        Assert.Equal([3], rejectedOnly.Body.EnumerateArray().Select(r => r.GetProperty("rowNumber").GetInt32()));
        Assert.Equal(0, await PatientCountAsync());
    }

    [Fact]
    public async Task A_bad_mapping_is_400_with_its_problems_and_nothing_is_saved()
    {
        var id = await UploadAsync();
        var bad = Mapping();
        bad["Ghost"] = "city";
        bad.Remove("Zip");

        var refused = await SendAsync(_admin, HttpMethod.Put, $"{BatchesUrl}/{id}/mapping", new { mapping = bad });

        Assert.Equal((HttpStatusCode.BadRequest, "mapping_invalid"), (refused.Status, refused.Error));
        Assert.Contains("unknown_column", refused.Body.GetProperty("problems").EnumerateArray().Select(p => p.GetProperty("code").GetString()));
        Assert.Contains("required_field_unmapped", refused.Body.GetProperty("problems").EnumerateArray().Select(p => p.GetProperty("code").GetString()));
        var empty = await SendAsync(_admin, HttpMethod.Put, $"{BatchesUrl}/{id}/mapping", new { });
        Assert.Equal((HttpStatusCode.BadRequest, "invalid_mapping"), (empty.Status, empty.Error));
        var batch = await SendAsync(_admin, HttpMethod.Get, $"{BatchesUrl}/{id}");
        Assert.Equal("Uploaded", batch.Body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Saving_a_mapping_and_running_the_dry_run_need_the_csrf_token_and_a_refused_call_changes_nothing()
    {
        var id = await UploadAsync();

        var save = await SendAsync(_admin, HttpMethod.Put, $"{BatchesUrl}/{id}/mapping", new { mapping = Mapping() }, csrf: false);
        var run = await SendAsync(_admin, HttpMethod.Post, $"{BatchesUrl}/{id}/dry-run", csrf: false);

        Assert.Equal((HttpStatusCode.BadRequest, "csrf_token_invalid"), (save.Status, save.Error));
        Assert.Equal((HttpStatusCode.BadRequest, "csrf_token_invalid"), (run.Status, run.Error));
        Assert.Equal(JsonValueKind.Null, (await SendAsync(_admin, HttpMethod.Get, $"{BatchesUrl}/{id}/mapping-suggestion")).Body.GetProperty("current").ValueKind);
    }

    [Fact]
    public async Task Every_new_call_is_refused_to_an_anonymous_caller_and_to_every_role_without_the_permission()
    {
        var id = Guid.NewGuid();
        var calls = new (HttpMethod Method, string Url)[]
        {
            (HttpMethod.Get, $"{BatchesUrl}/{id}/mapping-suggestion"), (HttpMethod.Put, $"{BatchesUrl}/{id}/mapping"), (HttpMethod.Post, $"{BatchesUrl}/{id}/dry-run"), (HttpMethod.Get, $"{BatchesUrl}/{id}/rows"),
        };

        using var anonymous = _api.Factory.CreateClient();
        foreach (var (method, url) in calls)
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.SendAsync(new HttpRequestMessage(method, url))).StatusCode);

        foreach (var (role, session) in _others)
            foreach (var (method, url) in calls)
                Assert.True(HttpStatusCode.Forbidden == (await SendAsync(session, method, url, method == HttpMethod.Put ? new { mapping = Mapping() } : null)).Status, $"{role} must not {method} {url}");
    }

    [Fact]
    public async Task The_stable_codes_for_an_unknown_batch_a_missing_mapping_a_missing_dry_run_a_bad_status_and_a_kind_not_supported_yet()
    {
        var unknown = Guid.NewGuid();
        foreach (var (method, suffix) in new[] { (HttpMethod.Get, "mapping-suggestion"), (HttpMethod.Post, "dry-run"), (HttpMethod.Get, "rows") })
        {
            var missing = await SendAsync(_admin, method, $"{BatchesUrl}/{unknown}/{suffix}");
            Assert.Equal((HttpStatusCode.NotFound, "batch_not_found"), (missing.Status, missing.Error));
        }

        var id = await UploadAsync();
        var noMapping = await SendAsync(_admin, HttpMethod.Post, $"{BatchesUrl}/{id}/dry-run");
        var noDryRun = await SendAsync(_admin, HttpMethod.Get, $"{BatchesUrl}/{id}/rows");
        var badStatus = await SendAsync(_admin, HttpMethod.Get, $"{BatchesUrl}/{id}/rows?status=Weird");
        var appointments = await UploadAsync("Appointments");
        var unsupported = await SendAsync(_admin, HttpMethod.Get, $"{BatchesUrl}/{appointments}/mapping-suggestion");

        Assert.Equal((HttpStatusCode.Conflict, "mapping_required"), (noMapping.Status, noMapping.Error));
        Assert.Equal((HttpStatusCode.Conflict, "dry_run_required"), (noDryRun.Status, noDryRun.Error));
        Assert.Equal((HttpStatusCode.BadRequest, "invalid_status"), (badStatus.Status, badStatus.Error));
        Assert.Equal((HttpStatusCode.Conflict, "kind_not_supported_yet"), (unsupported.Status, unsupported.Error));
    }
}
