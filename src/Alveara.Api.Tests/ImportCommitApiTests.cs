using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Alveara.Api.Architecture.BackgroundWork;
using Alveara.Api.Architecture.Imports;
using Alveara.Api.Architecture.Patients;
using Alveara.Api.Architecture.Time;
using Session = Alveara.Api.Tests.SchedulingApiHarness.Session;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-018-C02 slice 3, deciding likely duplicates and committing through the real pipeline (real API, real SQL Server, cookie sessions and CSRF, and the real job runner): the administrator maps, dry-runs, decides a
/// likely duplicate and confirms a commit, which is answered 202 with the batch Committing and settles to Committed when the jobs have run, with the patients registered; both new POSTs need the CSRF token and are
/// refused to an anonymous caller (401) and to every role without BulkImportData (403); and the confirmation's refusals have stable codes (a missing or unknown policy, a missing or stale count of valid rows, a batch
/// with problems under all-or-nothing, a decision that is not allowed).
/// </summary>
public class ImportCommitApiTests : IAsyncLifetime
{
    private const string BatchesUrl = "/api/import-batches";
    private readonly TestDatabaseFixture _fixture = new();
    private SchedulingApiHarness _api = null!;
    private Session _admin = null!;
    private readonly Dictionary<string, Session> _others = new();

    private static readonly byte[] Patients = Encoding.UTF8.GetBytes(
        "First Name,Last Name,DOB,Phone,Email,Address,City,State,Zip\r\n" +
        "Ann,Rivera,1950-01-10,555-100-1000,,4 Pine St,Austin,TX,78701\r\n" +
        "Bo,Moss,1962-03-04,555-300-3000,,4 Pine St,Austin,TX,78701\r\n" +
        ",Lee,1980-05-05,555-500-5000,,4 Pine St,Austin,TX,78701\r\n");

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

    private static Dictionary<string, string?> Mapping() => new()
    {
        ["First Name"] = "firstName", ["Last Name"] = "lastName", ["DOB"] = "dateOfBirth", ["Phone"] = "phone", ["Email"] = "email", ["Address"] = "addressLine1", ["City"] = "city", ["State"] = "state", ["Zip"] = "postalCode",
    };

    private async Task<Guid> DryRunBatchAsync(byte[]? file = null)
    {
        var id = (await SendAsync(_admin, HttpMethod.Post, $"{BatchesUrl}?sourceKind=Patients&label=Old%20system", raw: file ?? Patients)).Body.GetProperty("batch").GetProperty("id").GetGuid();
        await SendAsync(_admin, HttpMethod.Put, $"{BatchesUrl}/{id}/mapping", new { mapping = Mapping() });
        await SendAsync(_admin, HttpMethod.Post, $"{BatchesUrl}/{id}/dry-run");
        return id;
    }

    /// <summary>Runs the real job runner against the API's database until the commit jobs are over; the API's own background service may be doing the same, which the runner's atomic claim makes safe.</summary>
    private async Task RunJobsAsync()
    {
        for (var i = 0; i < 60; i++)
        {
            using var scope = _api.Factory.Services.CreateScope();
            var processed = await scope.ServiceProvider.GetRequiredService<BackgroundJobRunner>().ProcessOnceAsync([ImportCommitJobs.JobType], default);
            await using var db = _fixture.CreateContext();
            if (processed == 0 && !await db.BackgroundJobs.AnyAsync(j => j.JobType == ImportCommitJobs.JobType && j.Status == BackgroundJobStatus.Pending)) return;
            await Task.Delay(50);
        }
    }

    private async Task<int> PatientCountAsync() { await using var db = _fixture.CreateContext(); return await db.Patients.CountAsync(); }

    [Fact]
    public async Task The_administrator_decides_and_commits_end_to_end_and_the_batch_settles_to_committed()
    {
        var id = await DryRunBatchAsync();
        var rows = await SendAsync(_admin, HttpMethod.Get, $"{BatchesUrl}/{id}/rows?status=Rejected");
        Assert.Equal([4], rows.Body.EnumerateArray().Select(r => r.GetProperty("rowNumber").GetInt32()));       // the row with no first name is rejected: nothing to decide on it

        var commit = await SendAsync(_admin, HttpMethod.Post, $"{BatchesUrl}/{id}/commit", new { policy = "CommitValidRows", expectedValidRows = 2 });

        Assert.Equal(HttpStatusCode.Accepted, commit.Status);
        Assert.Equal("no-store", commit.Raw.Headers.CacheControl?.ToString());
        Assert.Equal(("Committing", 1), (commit.Body.GetProperty("status").GetString(), commit.Body.GetProperty("commitRound").GetInt32()));
        await RunJobsAsync();

        var batch = await SendAsync(_admin, HttpMethod.Get, $"{BatchesUrl}/{id}");
        Assert.Equal(("Committed", "CompletedWithExceptions", 2, 1), (batch.Body.GetProperty("status").GetString(), batch.Body.GetProperty("commitOutcome").GetString(), batch.Body.GetProperty("acceptedRows").GetInt32(), batch.Body.GetProperty("rejectedRows").GetInt32()));
        Assert.Equal(2, await PatientCountAsync());
        var accepted = await SendAsync(_admin, HttpMethod.Get, $"{BatchesUrl}/{id}/rows?status=Accepted");
        Assert.All(accepted.Body.EnumerateArray(), r => Assert.NotEqual(Guid.Empty, r.GetProperty("resultPatientId").GetGuid()));
        Assert.DoesNotContain("Rivera", accepted.Body.GetRawText());
    }

    [Fact]
    public async Task A_likely_duplicate_is_decided_over_the_api_and_the_decision_moves_the_row()
    {
        var registration = new PatientRegistrationService(_fixture.CreateContext(), new PracticeClock(TimeZoneInfo.Utc));
        await registration.RegisterAsync(new RegisterPatientRequest("Ann", null, "Rivera", "1950-01-10", "Female", "555-100-1000", null, "1 Main St", null, "Austin", "TX", "78701"), "api-test-ann", _admin.UserId, default);
        var file = Encoding.UTF8.GetBytes("First Name,Last Name,DOB,Phone,Email,Address,City,State,Zip\r\nAnna,Rivera,1950-01-10,555-200-2000,,4 Pine St,Austin,TX,78701\r\n");
        var id = await DryRunBatchAsync(file);

        var queue = await SendAsync(_admin, HttpMethod.Get, $"{BatchesUrl}/{id}/rows?status=Unresolved");
        Assert.Equal(1, queue.Body.GetArrayLength());

        var decided = await SendAsync(_admin, HttpMethod.Post, $"{BatchesUrl}/{id}/rows/2/decision", new { decision = "RegisterAsNew" });

        Assert.Equal(HttpStatusCode.OK, decided.Status);
        Assert.Equal(("Valid", "reviewed_new_person", true), (decided.Body.GetProperty("row").GetProperty("status").GetString(), decided.Body.GetProperty("row").GetProperty("reason").GetString(), decided.Body.GetProperty("row").GetProperty("decided").GetBoolean()));
        Assert.Equal(0, decided.Body.GetProperty("batch").GetProperty("unresolvedRows").GetInt32());
        var bad = await SendAsync(_admin, HttpMethod.Post, $"{BatchesUrl}/{id}/rows/2/decision", new { decision = "Merge" });
        Assert.Equal((HttpStatusCode.BadRequest, "invalid_decision"), (bad.Status, bad.Error));
    }

    [Fact]
    public async Task The_confirmations_refusals_have_stable_codes_and_register_nothing()
    {
        var id = await DryRunBatchAsync();
        string Url() => $"{BatchesUrl}/{id}/commit";

        var noPolicy = await SendAsync(_admin, HttpMethod.Post, Url(), new { expectedValidRows = 2 });
        var badPolicy = await SendAsync(_admin, HttpMethod.Post, Url(), new { policy = "Everything", expectedValidRows = 2 });
        var noCount = await SendAsync(_admin, HttpMethod.Post, Url(), new { policy = "CommitValidRows" });
        var stale = await SendAsync(_admin, HttpMethod.Post, Url(), new { policy = "CommitValidRows", expectedValidRows = 3 });
        var problems = await SendAsync(_admin, HttpMethod.Post, Url(), new { policy = "AllOrNothing", expectedValidRows = 2 });
        var empty = await SendAsync(_admin, HttpMethod.Post, Url());
        var unknown = await SendAsync(_admin, HttpMethod.Post, $"{BatchesUrl}/{Guid.NewGuid()}/commit", new { policy = "CommitValidRows", expectedValidRows = 1 });

        Assert.Equal([(HttpStatusCode.BadRequest, "invalid_policy"), (HttpStatusCode.BadRequest, "invalid_policy"), (HttpStatusCode.BadRequest, "expected_valid_rows_required"), (HttpStatusCode.Conflict, "results_changed"),
            (HttpStatusCode.Conflict, "batch_has_problems"), (HttpStatusCode.BadRequest, "invalid_policy"), (HttpStatusCode.NotFound, "batch_not_found")],
            new[] { noPolicy, badPolicy, noCount, stale, problems, empty, unknown }.Select(r => (r.Status, r.Error!)));
        Assert.Equal(0, await PatientCountAsync());
        Assert.Equal("DryRunComplete", (await SendAsync(_admin, HttpMethod.Get, $"{BatchesUrl}/{id}")).Body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Deciding_and_committing_need_the_csrf_token_and_are_refused_to_anonymous_callers_and_to_every_role_without_the_permission()
    {
        var id = await DryRunBatchAsync();
        var calls = new (string Url, object Body)[]
        {
            ($"{BatchesUrl}/{id}/rows/2/decision", new { decision = "Skip" }), ($"{BatchesUrl}/{id}/commit", new { policy = "CommitValidRows", expectedValidRows = 2 }),
        };

        foreach (var (url, body) in calls)
        {
            var noCsrf = await SendAsync(_admin, HttpMethod.Post, url, body, csrf: false);
            Assert.Equal((HttpStatusCode.BadRequest, "csrf_token_invalid"), (noCsrf.Status, noCsrf.Error));
            using var anonymous = _api.Factory.CreateClient();
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync(url, null)).StatusCode);
            foreach (var (role, session) in _others) Assert.True(HttpStatusCode.Forbidden == (await SendAsync(session, HttpMethod.Post, url, body)).Status, $"{role} must not POST {url}");
        }

        Assert.Equal("DryRunComplete", (await SendAsync(_admin, HttpMethod.Get, $"{BatchesUrl}/{id}")).Body.GetProperty("status").GetString());
        Assert.Equal(0, await PatientCountAsync());
    }
}
