using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Alveara.Api.Architecture.BackgroundWork;
using Alveara.Api.Architecture.Imports;
using Alveara.Api.Architecture.Patients;
using Session = Alveara.Api.Tests.SchedulingApiHarness.Session;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-018-C02 slice 4b, an appointment file through the real pipeline (real API host with its real dependency injection, real SQL Server, cookie sessions and CSRF, the real job runner and the real scheduler): the
/// administrator uploads, maps, dry-runs and commits; the commit answers 202 with the batch Committing and, when the jobs have run, the appointments are on the schedule and the batch is Committed; all-or-nothing is
/// refused with its stable code over the API.
/// </summary>
public class ImportAppointmentApiTests : IAsyncLifetime
{
    private const string BatchesUrl = "/api/import-batches";
    private readonly TestDatabaseFixture _fixture = new();
    private SchedulingTestSupport _s = null!;
    private SchedulingApiHarness _api = null!;
    private Session _admin = null!;

    private static readonly byte[] File = Encoding.UTF8.GetBytes(
        "Patient First,Patient Last,DOB,Provider,Operatory,Type,Start,Duration,Notes\r\n" +
        "Ann,Rivera,1950-01-10,Dr. Rivera,Op 1,Exam,2030-01-14 09:00,,first visit\r\n" +
        "Ann,Rivera,1950-01-10,Dr. Patel,Op 2,Quick check,2030-01-14 09:30,,overlaps the first\r\n");

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
        _s = new SchedulingTestSupport(_fixture);
        _api = new SchedulingApiHarness(_fixture);
        await _s.ArrangeAsync();
        _admin = await _api.SessionAsync("Admin");
        await new PatientRegistrationService(_fixture.CreateContext(), SchedulingTestSupport.Clock)
            .RegisterAsync(new RegisterPatientRequest("Ann", null, "Rivera", "1950-01-10", "Female", "555-100-1000", null, "1 Main St", null, "Austin", "TX", "78701"), "api-test-ann", _admin.UserId, default);
    }

    public Task DisposeAsync() { _api.Dispose(); return _fixture.DisposeAsync(); }

    private sealed record Reply(HttpStatusCode Status, JsonElement Body)
    {
        public string? Error => Body.ValueKind == JsonValueKind.Object && Body.TryGetProperty("error", out var e) ? e.GetString() : null;
    }

    private async Task<Reply> SendAsync(HttpMethod method, string url, object? body = null, byte[]? raw = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("X-CSRF-Token", _admin.Csrf);
        if (raw is not null) { request.Content = new ByteArrayContent(raw); request.Content.Headers.ContentType = new("text/csv"); }
        else if (body is not null) request.Content = System.Net.Http.Json.JsonContent.Create(body);
        var response = await _admin.Client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        return new Reply(response.StatusCode, string.IsNullOrWhiteSpace(text) ? default : JsonDocument.Parse(text).RootElement.Clone());
    }

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

    private async Task<Guid> DryRunBatchAsync()
    {
        var id = (await SendAsync(HttpMethod.Post, $"{BatchesUrl}?sourceKind=Appointments&label=Old%20system", raw: File)).Body.GetProperty("batch").GetProperty("id").GetGuid();
        var suggestion = (await SendAsync(HttpMethod.Get, $"{BatchesUrl}/{id}/mapping-suggestion")).Body.GetProperty("suggested");
        var mapping = suggestion.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString());
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Put, $"{BatchesUrl}/{id}/mapping", new { mapping })).Status);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Post, $"{BatchesUrl}/{id}/dry-run")).Status);
        return id;
    }

    [Fact]
    public async Task The_administrator_uploads_maps_dry_runs_and_commits_appointments_end_to_end_and_the_clashing_row_is_not_booked()
    {
        var id = await DryRunBatchAsync();
        var rows = await SendAsync(HttpMethod.Get, $"{BatchesUrl}/{id}/rows");
        Assert.Equal(["Valid", "Rejected"], rows.Body.EnumerateArray().Select(r => r.GetProperty("status").GetString()));            // the second row clashes with the first for the same patient
        Assert.Equal("patient_double_booked", rows.Body[1].GetProperty("reason").GetString());

        var commit = await SendAsync(HttpMethod.Post, $"{BatchesUrl}/{id}/commit", new { policy = "CommitValidRows", expectedValidRows = 1 });

        Assert.Equal(HttpStatusCode.Accepted, commit.Status);
        Assert.Equal("Committing", commit.Body.GetProperty("status").GetString());
        await RunJobsAsync();

        var batch = await SendAsync(HttpMethod.Get, $"{BatchesUrl}/{id}");
        Assert.Equal(("Committed", "CompletedWithExceptions", 1, 1), (batch.Body.GetProperty("status").GetString(), batch.Body.GetProperty("commitOutcome").GetString(), batch.Body.GetProperty("acceptedRows").GetInt32(), batch.Body.GetProperty("rejectedRows").GetInt32()));
        Assert.Equal(1, await _s.CountAsync());
        var accepted = await SendAsync(HttpMethod.Get, $"{BatchesUrl}/{id}/rows?status=Accepted");
        Assert.NotEqual(Guid.Empty, accepted.Body[0].GetProperty("resultPatientId").GetGuid());                                       // the id of the appointment the row made
    }

    [Fact]
    public async Task All_or_nothing_is_refused_for_appointments_over_the_api_with_its_stable_code()
    {
        var id = await DryRunBatchAsync();

        var refused = await SendAsync(HttpMethod.Post, $"{BatchesUrl}/{id}/commit", new { policy = "AllOrNothing", expectedValidRows = 1 });

        Assert.Equal((HttpStatusCode.Conflict, "policy_not_supported"), (refused.Status, refused.Error));
        Assert.Equal("DryRunComplete", (await SendAsync(HttpMethod.Get, $"{BatchesUrl}/{id}")).Body.GetProperty("status").GetString());
        Assert.Equal(0, await _s.CountAsync());
    }
}
