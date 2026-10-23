using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Permissions;
using Alveara.Api.Controllers;
using Alveara.Api.Data;
using Session = Alveara.Api.Tests.SchedulingApiHarness.Session;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N017, two application hosts on one database. A policy change committed by anyone applies to the very next authorization on EVERY host with no waiting, repeated 200 times with interleaved requests; a
/// revert applies the same way; a reader never sees a half-applied change (a writer holding an open transaction is invisible, and a writer toggling in a loop under a stream of readers is only ever seen as one
/// whole policy or the next); a host that can no longer read the store serves its last verified snapshot for at most 60 seconds and then answers 503.
/// </summary>
public class PermissionPolicyTwoHostTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private SchedulingApiHarness _a = null!;
    private SchedulingApiHarness _b = null!;

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
        _a = new SchedulingApiHarness(_fixture);
        _b = new SchedulingApiHarness(_fixture);
    }

    public Task DisposeAsync() { _a.Dispose(); _b.Dispose(); return _fixture.DisposeAsync(); }

    private const string Url = "/api/patients";                                                                          // ViewPatientRecords, held by the dentist by default

    private async Task<string> DentistUsernameAsync()
    {
        var admin = await _a.SessionAsync("Admin");
        var username = $"dentist-{Guid.NewGuid():N}";
        var id = (await (await admin.Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(username, "password"))).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await admin.SendAsync(HttpMethod.Put, $"/api/auth/{id}/role", new ChangeRoleRequest("Dentist"));
        await admin.SendAsync(HttpMethod.Put, $"/api/auth/{id}/enabled", new SetEnabledRequest(true));
        return username;
    }

    private static async Task<Session> LoginAsync(WebApplicationFactory<Program> factory, string username)
    {
        var client = factory.CreateClient();
        Assert.True((await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(username, "password"))).IsSuccessStatusCode);
        var csrf = (await (await client.GetAsync("/api/auth/csrf-token")).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
        return new Session(client, csrf);
    }

    private async Task WriteAsync(Action<AlveraDbContext> change)
    {
        await using var db = _fixture.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlAsync($"UPDATE [PermissionPolicyState] SET [Version] = [Version] + 1, [UpdatedAtUtc] = SYSDATETIMEOFFSET() WHERE [Id] = 1");
        change(db);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    private Task RevokeAsync() => WriteAsync(db => db.RolePermissionOverrides.Add(new RolePermissionOverride
    {
        Id = Guid.NewGuid(), Role = "Dentist", Permission = nameof(Permission.ViewPatientRecords), Effect = OverrideEffects.Revoke, Reason = "controlled setup", ChangedByUserId = db.UserAccounts.Select(u => u.Id).First(), ChangedAtUtc = DateTimeOffset.UtcNow,
    }));

    private Task RevertAsync() => WriteAsync(db => db.RolePermissionOverrides.RemoveRange(db.RolePermissionOverrides.Where(o => o.Role == "Dentist")));

    private static async Task<HttpStatusCode> StatusAsync(Session s) => (await s.SendAsync(HttpMethod.Get, Url)).Status;

    [Fact]
    public async Task A_revoke_and_a_revert_apply_to_the_very_next_request_on_both_hosts_two_hundred_times_with_interleaved_requests()
    {
        var username = await DentistUsernameAsync();
        var onA = await LoginAsync(_a.Factory, username);
        var onB = await LoginAsync(_b.Factory, username);
        Assert.NotEqual(HttpStatusCode.Forbidden, await StatusAsync(onA));
        Assert.NotEqual(HttpStatusCode.Forbidden, await StatusAsync(onB));

        for (var i = 0; i < 200; i++)
        {
            var revoke = i % 2 == 0;
            if (revoke) await RevokeAsync(); else await RevertAsync();
            // interleaved: B before A on even iterations, A before B on odd ones, so neither host is always the first to notice
            var (first, second) = i % 4 < 2 ? (onB, onA) : (onA, onB);
            var s1 = await StatusAsync(first);
            var s2 = await StatusAsync(second);
            if (revoke) Assert.True(s1 == HttpStatusCode.Forbidden && s2 == HttpStatusCode.Forbidden, $"iteration {i}: after a revoke the hosts answered {s1} and {s2}");
            else Assert.True(s1 != HttpStatusCode.Forbidden && s2 != HttpStatusCode.Forbidden, $"iteration {i}: after a revert the hosts answered {s1} and {s2}");
        }
    }

    [Fact]
    public async Task A_grant_to_a_role_that_lacked_the_permission_also_reaches_the_other_host_at_once()
    {
        var admin = await _a.SessionAsync("Admin");
        var username = $"billing-{Guid.NewGuid():N}";
        var id = (await (await admin.Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest(username, "password"))).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await admin.SendAsync(HttpMethod.Put, $"/api/auth/{id}/role", new ChangeRoleRequest("Billing"));
        await admin.SendAsync(HttpMethod.Put, $"/api/auth/{id}/enabled", new SetEnabledRequest(true));
        var onB = await LoginAsync(_b.Factory, username);
        Assert.Equal(HttpStatusCode.Forbidden, (await onB.SendAsync(HttpMethod.Get, "/api/backup/history")).Status);
        await WriteAsync(db => db.RolePermissionOverrides.Add(new RolePermissionOverride
        {
            Id = Guid.NewGuid(), Role = "Billing", Permission = nameof(Permission.ViewBackupStatus), Effect = OverrideEffects.Grant, Reason = "controlled setup", ChangedByUserId = db.UserAccounts.Select(u => u.Id).First(), ChangedAtUtc = DateTimeOffset.UtcNow,
        }));
        Assert.NotEqual(HttpStatusCode.Forbidden, (await onB.SendAsync(HttpMethod.Get, "/api/backup/history")).Status);
    }

    [Fact]
    public async Task A_writer_with_an_open_transaction_is_invisible_to_both_hosts_and_visible_whole_the_moment_it_commits()
    {
        var username = await DentistUsernameAsync();
        var onA = await LoginAsync(_a.Factory, username);
        var onB = await LoginAsync(_b.Factory, username);
        Assert.NotEqual(HttpStatusCode.Forbidden, await StatusAsync(onA));

        await using var writer = _fixture.CreateContext();
        await using var tx = await writer.Database.BeginTransactionAsync();
        await writer.Database.ExecuteSqlAsync($"UPDATE [PermissionPolicyState] SET [Version] = [Version] + 1, [UpdatedAtUtc] = SYSDATETIMEOFFSET() WHERE [Id] = 1");   // the version has moved inside the open transaction...
        writer.RolePermissionOverrides.Add(new RolePermissionOverride { Id = Guid.NewGuid(), Role = "Dentist", Permission = nameof(Permission.ViewPatientRecords), Effect = OverrideEffects.Revoke, Reason = "controlled setup", ChangedByUserId = writer.UserAccounts.Select(u => u.Id).First(), ChangedAtUtc = DateTimeOffset.UtcNow });
        await writer.SaveChangesAsync();                                                                                 // ...and so has the row, but nothing is committed

        Assert.NotEqual(HttpStatusCode.Forbidden, await StatusAsync(onA));                                               // neither the half-written version nor the half-written row is visible
        Assert.NotEqual(HttpStatusCode.Forbidden, await StatusAsync(onB));
        await tx.CommitAsync();
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(onA));                                                  // both together, at once
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(onB));
    }

    [Fact]
    public async Task A_writer_toggling_in_a_loop_under_a_stream_of_readers_is_only_ever_seen_as_a_whole_policy()
    {
        var username = await DentistUsernameAsync();
        var sessions = new[] { await LoginAsync(_a.Factory, username), await LoginAsync(_b.Factory, username) };
        using var stop = new CancellationTokenSource();
        var statuses = new System.Collections.Concurrent.ConcurrentBag<HttpStatusCode>();
        var readers = sessions.SelectMany(s => Enumerable.Range(0, 2).Select(_ => Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested) statuses.Add(await StatusAsync(s));
        }))).ToArray();
        for (var i = 0; i < 30; i++) { if (i % 2 == 0) await RevokeAsync(); else await RevertAsync(); await Task.Delay(20); }   // an administrator changes policy a few times a second at most; the stream here is far faster than that
        await Task.Delay(300);
        stop.Cancel();
        await Task.WhenAll(readers);
        Assert.NotEmpty(statuses);
        Assert.All(statuses, s => Assert.True(s is HttpStatusCode.OK or HttpStatusCode.BadRequest or HttpStatusCode.Forbidden, $"a reader saw {s}"));   // never a 500 or 503 from a half-applied or torn read
        await RevertAsync();
        Assert.NotEqual(HttpStatusCode.Forbidden, await StatusAsync(sessions[0]));
        Assert.NotEqual(HttpStatusCode.Forbidden, await StatusAsync(sessions[1]));
    }

    /// <summary>A data source that reads the real database until told to fail.</summary>
    private sealed class FailableSource(IPolicyDataSource inner) : IPolicyDataSource
    {
        public volatile bool Failing;
        public Task<long> ReadVersionAsync(CancellationToken ct) => Failing ? throw new InvalidOperationException("injected outage") : inner.ReadVersionAsync(ct);
        public Task<PolicyRows> ReadRowsAsync(CancellationToken ct) => Failing ? throw new InvalidOperationException("injected outage") : inner.ReadRowsAsync(ct);
    }

    [Fact]
    public async Task A_host_that_cannot_read_the_store_serves_its_last_verified_snapshot_for_sixty_seconds_then_answers_503_and_recovers()
    {
        var username = await DentistUsernameAsync();
        var time = new ManualTime();
        FailableSource? failable = null;
        var faulty = _b.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<PermissionPolicyStore>();
            services.AddSingleton(sp =>
            {
                failable = new FailableSource(new SqlPolicyDataSource(sp.GetRequiredService<IServiceScopeFactory>()));
                return new PermissionPolicyStore(failable, time, NullLogger<PermissionPolicyStore>.Instance);
            });
        }));
        var onFaulty = await LoginAsync(faulty, username);
        var onA = await LoginAsync(_a.Factory, username);
        Assert.NotEqual(HttpStatusCode.Forbidden, await StatusAsync(onFaulty));                                          // warm: a verified snapshot exists
        failable!.Failing = true;
        await RevokeAsync();                                                                                             // committed while the faulty host cannot see the store

        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(onA));                                                  // a healthy host applies it at once
        time.Advance(TimeSpan.FromSeconds(59));
        Assert.NotEqual(HttpStatusCode.Forbidden, await StatusAsync(onFaulty));                                          // the accepted exposure: the old snapshot, for at most 60 s
        time.Advance(TimeSpan.FromSeconds(2));
        var denied = await onFaulty.SendAsync(HttpMethod.Get, Url);
        Assert.Equal((HttpStatusCode.ServiceUnavailable, "permission_policy_unavailable"), (denied.Status, denied.Body.GetProperty("error").GetString()));   // past the allowance: deny, never the defaults

        failable.Failing = false;
        Assert.Equal(HttpStatusCode.Forbidden, await StatusAsync(onFaulty));                                             // recovered: the revoke is now applied here too
    }
}
