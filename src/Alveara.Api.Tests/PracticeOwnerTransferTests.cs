using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Alveara.Api.Architecture.Configuration;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Controllers;
using Alveara.Api.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N030-C01 slice 3 (brief 18.5 and 18.10; review R10): ownership transfer.
/// S31 happy path; S32 who may transfer and the two-factor step-up; S33 destination rules; S34 transfer-and-disable both or neither; S35 concurrency and stale
/// intent; S36 idempotency; S40 ids-only stored audit text with names resolved when the audit log is read.
/// </summary>
public class PracticeOwnerTransferTests : IAsyncLifetime
{
    private const string Password = "Seeded-Password-1";
    private const string Reason = "The previous owner sold the practice.";
    private readonly TestDatabaseFixture _fixture = new();
    private WebApplicationFactory<Program> _factory = null!;

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("ConnectionStrings:Alveara", _fixture.ConnectionString);
            b.UseSetting("AuthAttemptRateLimit:PermitLimit", "500");
        });
    }

    public async Task DisposeAsync() { await _factory.DisposeAsync(); await _fixture.DisposeAsync(); }

    // ---------- helpers ----------

    private async Task<T> WithDbAsync<T>(Func<AlveraDbContext, Task<T>> work)
    {
        using var scope = _factory.Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<AlveraDbContext>());
    }

    private Task<(Guid Account, Guid Staff)> SeedAsync(string username, Role role, AccountKind kind = AccountKind.Staff, bool withStaff = true,
        bool staffActive = true, bool disabled = false) => WithDbAsync(async db =>
    {
        var account = new UserAccount
        {
            Id = Guid.NewGuid(), Username = username, PasswordHash = Pbkdf2PasswordHasher.Hash(Password), Role = role, AccountKind = kind, IsDisabled = disabled,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        db.UserAccounts.Add(account);
        var staffId = Guid.Empty;
        if (withStaff)
        {
            staffId = Guid.NewGuid();
            db.StaffProfiles.Add(new StaffProfile { Id = staffId, DisplayName = $"Name of {username}", UserAccountId = account.Id, IsActive = staffActive, CreatedAtUtc = DateTimeOffset.UtcNow });
        }
        await db.SaveChangesAsync();
        return (account.Id, staffId);
    });

    private sealed record Session(HttpClient Client, string Csrf)
    {
        public Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, object? body = null, string? key = null)
        {
            var request = new HttpRequestMessage(method, url);
            request.Headers.Add("X-CSRF-Token", Csrf);
            if (key is not null) request.Headers.Add("Idempotency-Key", key);
            if (body is not null) request.Content = JsonContent.Create(body);
            return Client.SendAsync(request);
        }
    }

    private async Task<Session> SignInAsync(string username)
    {
        var client = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/login", new { username, password = Password })).StatusCode);
        var csrf = (await (await client.GetAsync("/api/auth/csrf-token")).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
        return new Session(client, csrf);
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage r) => await r.Content.ReadFromJsonAsync<JsonElement>();
    private static async Task<string?> ErrorAsync(HttpResponseMessage r) => (await JsonAsync(r)).GetProperty("error").GetString();

    private static TransferPracticeOwnerRequest Body(Guid expected, Guid newOwner, string? reason = Reason, bool confirm = true, string? password = Password,
        string? code = null, bool disable = false) => new(expected, newOwner, reason, confirm, password, code, disable);

    private Task<Guid> ActiveDesignationIdAsync() => WithDbAsync(db => db.PracticeOwnerDesignations.Where(d => d.EndedAtUtc == null).Select(d => d.Id).SingleAsync());
    private Task<int> ActiveCountAsync() => WithDbAsync(db => db.PracticeOwnerDesignations.CountAsync(d => d.EndedAtUtc == null));

    /// <summary>The Deployment Administrator, a plain Administrator, the owner A (designated) and two Administrator candidates B and C.</summary>
    private async Task<World> WorldAsync()
    {
        var deploy = await SeedAsync("deploy.t", Role.Admin, AccountKind.DeploymentAdministrator, withStaff: false);
        var admin = await SeedAsync("admin.t", Role.Admin);
        var a = await SeedAsync("owner.a", Role.Admin);
        var b = await SeedAsync("cand.b", Role.Admin);
        var c = await SeedAsync("cand.c", Role.Admin);
        using (var scope = _factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<PracticeOwnerService>().DesignateInitialAsync(a.Staff, true, deploy.Account, CancellationToken.None);
        return new World(deploy.Account, admin.Account, a, b, c, await ActiveDesignationIdAsync());
    }

    private sealed record World(Guid Deploy, Guid Admin, (Guid Account, Guid Staff) A, (Guid Account, Guid Staff) B, (Guid Account, Guid Staff) C, Guid Designation);

    private async Task<string> EnableMfaAsync(Guid account)
    {
        using var scope = _factory.Services.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountService>();
        var (secret, codes) = await accounts.BeginMfaEnrollmentAsync(account);
        await accounts.ConfirmMfaEnrollmentAsync(account, Totp.GenerateCodeForTests(Totp.FromBase32(secret)));
        return secret + "|" + codes[0];
    }

    // ---------- S31 ----------

    [Fact]
    public async Task S31_the_deployment_administrator_transfers_ownership_in_one_step_and_the_former_owner_is_signed_out()
    {
        var w = await WorldAsync();
        var formerOwner = await SignInAsync("owner.a");
        Assert.Equal(HttpStatusCode.OK, (await formerOwner.Client.GetAsync("/api/auth/permissions")).StatusCode);
        var deploy = await SignInAsync("deploy.t");

        var current = await JsonAsync(await deploy.Client.GetAsync("/api/practice-owner"));
        Assert.Equal(w.Designation, current.GetProperty("owner").GetProperty("designationId").GetGuid());

        var response = await deploy.SendAsync(HttpMethod.Post, "/api/practice-owner/transfer", Body(w.Designation, w.B.Staff), "transfer-1");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await JsonAsync(response);
        Assert.False(body.GetProperty("formerOwnerDisabled").GetBoolean());

        var rows = await WithDbAsync(db => db.PracticeOwnerDesignations.AsNoTracking().OrderBy(d => d.DesignatedAtUtc).ToListAsync());
        Assert.Equal(2, rows.Count);
        var (old, now) = (rows.Single(r => r.Id == w.Designation), rows.Single(r => r.Id != w.Designation));
        Assert.Equal(w.B.Staff, now.StaffProfileId);
        Assert.Null(now.EndedAtUtc);
        Assert.Equal(PracticeOwnerDesignationMethod.Transfer, now.Method);
        Assert.Equal(w.Deploy, now.DesignatedByAccountId);
        Assert.Equal(now.DesignatedAtUtc, old.EndedAtUtc);

        var audit = await WithDbAsync(db => db.AuditLogEntries.AsNoTracking().SingleAsync(a => a.EventType == AuditEventTypes.PracticeOwnerTransferred));
        Assert.Equal(w.Deploy, audit.PerformedByUserAccountId);
        Assert.Equal(Reason, audit.Reason);
        Assert.Contains($"formerOwnerStaffProfileId={w.A.Staff}", audit.Details);
        Assert.Contains($"newOwnerStaffProfileId={w.B.Staff}", audit.Details);
        foreach (var name in new[] { "owner.a", "cand.b", "Name of", "deploy.t" }) Assert.DoesNotContain(name, audit.Details);

        // the former owner's open session ended; their account is unchanged (still an enabled Administrator)
        Assert.Equal(HttpStatusCode.Unauthorized, (await formerOwner.Client.GetAsync("/api/auth/permissions")).StatusCode);
        var formerAccount = await WithDbAsync(db => db.UserAccounts.AsNoTracking().SingleAsync(u => u.Id == w.A.Account));
        Assert.Equal(Role.Admin, formerAccount.Role);
        Assert.False(formerAccount.IsDisabled);
        // the former owner is now an ordinary Administrator: their role may change again (E3 no longer applies)
        var admin = await SignInAsync("admin.t");
        Assert.Equal(HttpStatusCode.OK, (await admin.SendAsync(HttpMethod.Put, $"/api/auth/{w.A.Account}/role", new ChangeRoleRequest("Dentist"))).StatusCode);
    }

    // ---------- S32 ----------

    [Fact]
    public async Task S32_only_the_deployment_administrator_with_a_valid_step_up_confirmation_and_reason_may_transfer()
    {
        var w = await WorldAsync();
        foreach (var other in new[] { "admin.t", "owner.a" })
        {
            var refused = await (await SignInAsync(other)).SendAsync(HttpMethod.Post, "/api/practice-owner/transfer", Body(w.Designation, w.B.Staff), $"k-{other}");
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
            Assert.Equal("transfer_not_permitted", await ErrorAsync(refused));
        }
        await SeedAsync("dentist.t", Role.Dentist);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await SignInAsync("dentist.t")).SendAsync(HttpMethod.Post, "/api/practice-owner/transfer",
            Body(w.Designation, w.B.Staff), "k-dentist")).StatusCode);

        var deploy = await SignInAsync("deploy.t");
        var wrong = await deploy.SendAsync(HttpMethod.Post, "/api/practice-owner/transfer", Body(w.Designation, w.B.Staff, password: "not-the-password"), "k-wrong");
        Assert.Equal(HttpStatusCode.Forbidden, wrong.StatusCode);
        Assert.Equal("reauthentication_failed", await ErrorAsync(wrong));
        Assert.Equal(1, await WithDbAsync(db => db.AuditLogEntries.CountAsync(a => a.EventType == AuditEventTypes.SensitiveActionStepUpFailed)));
        Assert.Equal(1, await WithDbAsync(db => db.UserAccounts.Where(u => u.Id == w.Deploy).Select(u => u.FailedLoginAttempts).SingleAsync()));

        Assert.Equal("confirmation_required", await ErrorAsync(await deploy.SendAsync(HttpMethod.Post, "/api/practice-owner/transfer", Body(w.Designation, w.B.Staff, confirm: false), "k-c")));
        Assert.Equal("validation_failed", await ErrorAsync(await deploy.SendAsync(HttpMethod.Post, "/api/practice-owner/transfer", Body(w.Designation, w.B.Staff, reason: "short"), "k-r")));
        Assert.Equal("idempotency_key_required", await ErrorAsync(await deploy.SendAsync(HttpMethod.Post, "/api/practice-owner/transfer", Body(w.Designation, w.B.Staff))));

        Assert.Equal(1, await ActiveCountAsync());
        Assert.Equal(w.Designation, await ActiveDesignationIdAsync());
        Assert.Equal(0, await WithDbAsync(db => db.AuditLogEntries.CountAsync(a => a.EventType == AuditEventTypes.PracticeOwnerTransferred)));
        // the Administrator and the owner were refused by the account-kind rule (audited); the Dentist never reached it (no ManageUsers: 403 at the route)
        Assert.Equal(2, await WithDbAsync(db => db.AuditLogEntries.CountAsync(a => a.EventType == AuditEventTypes.AccountActionRefused && a.Details.Contains("transfer_not_permitted"))));
    }

    [Fact]
    public async Task S32_with_mfa_on_the_transfer_needs_a_current_authenticator_code_and_refuses_a_recovery_code()
    {
        var w = await WorldAsync();
        var mfa = (await EnableMfaAsync(w.Deploy)).Split('|');
        var secret = Totp.FromBase32(mfa[0]);
        var deploy = await SignInWithMfaAsync("deploy.t", secret);

        Assert.Equal("mfa_required", await ErrorAsync(await deploy.SendAsync(HttpMethod.Post, "/api/practice-owner/transfer", Body(w.Designation, w.B.Staff), "m-1")));
        var wrongCode = await deploy.SendAsync(HttpMethod.Post, "/api/practice-owner/transfer", Body(w.Designation, w.B.Staff, code: "000000"), "m-2");
        Assert.Equal(HttpStatusCode.Forbidden, wrongCode.StatusCode);
        Assert.Equal("reauthentication_failed", await ErrorAsync(wrongCode));
        Assert.Equal(1, await WithDbAsync(db => db.AuditLogEntries.CountAsync(a => a.EventType == AuditEventTypes.MfaStepUpFailed)));
        Assert.Equal("reauthentication_failed", await ErrorAsync(await deploy.SendAsync(HttpMethod.Post, "/api/practice-owner/transfer", Body(w.Designation, w.B.Staff, code: mfa[1]), "m-3")));
        Assert.Equal(w.Designation, await ActiveDesignationIdAsync());

        var ok = await deploy.SendAsync(HttpMethod.Post, "/api/practice-owner/transfer", Body(w.Designation, w.B.Staff, code: Totp.GenerateCodeForTests(secret)), "m-4");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal(w.B.Staff, await WithDbAsync(db => db.PracticeOwnerDesignations.Where(d => d.EndedAtUtc == null).Select(d => d.StaffProfileId).SingleAsync()));
    }

    private async Task<Session> SignInWithMfaAsync(string username, byte[] secret)
    {
        var client = _factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { username, password = Password });
        var challenge = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("challengeToken").GetString();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/mfa/challenge", new { challengeToken = challenge, code = Totp.GenerateCodeForTests(secret) })).StatusCode);
        var csrf = (await (await client.GetAsync("/api/auth/csrf-token")).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
        return new Session(client, csrf);
    }

    // ---------- S33 ----------

    [Fact]
    public async Task S33_the_destination_must_be_another_active_linked_enabled_administrator_staff_member()
    {
        var deployOnly = await SeedAsync("deploy.t", Role.Admin, AccountKind.DeploymentAdministrator, withStaff: false);
        var deploy = await SignInAsync("deploy.t");
        var b = await SeedAsync("cand.b", Role.Admin);
        // no active owner yet
        Assert.Equal("no_active_owner", await ErrorAsync(await deploy.SendAsync(HttpMethod.Post, "/api/practice-owner/transfer", Body(Guid.NewGuid(), b.Staff), "n-0")));

        var a = await SeedAsync("owner.a", Role.Admin);
        using (var scope = _factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<PracticeOwnerService>().DesignateInitialAsync(a.Staff, true, deployOnly.Account, CancellationToken.None);
        var designation = await ActiveDesignationIdAsync();

        var inactive = await SeedAsync("inactive.c", Role.Admin, staffActive: false);
        var disabled = await SeedAsync("disabled.c", Role.Admin, disabled: true);
        var dentist = await SeedAsync("dentist.c", Role.Dentist);
        var unlinked = await WithDbAsync(async db =>
        {
            var s = new StaffProfile { Id = Guid.NewGuid(), DisplayName = "No Login", IsActive = true, CreatedAtUtc = DateTimeOffset.UtcNow };
            db.StaffProfiles.Add(s);
            await db.SaveChangesAsync();
            return s.Id;
        });
        var deployProfile = await WithDbAsync(async db =>   // a state no route creates: a profile pointing at the Deployment Administrator
        {
            var s = new StaffProfile { Id = Guid.NewGuid(), DisplayName = "Kind Check", IsActive = true, UserAccountId = deployOnly.Account, CreatedAtUtc = DateTimeOffset.UtcNow };
            db.StaffProfiles.Add(s);
            await db.SaveChangesAsync();
            return s.Id;
        });

        Assert.Equal("transfer_same_owner", await ErrorAsync(await deploy.SendAsync(HttpMethod.Post, "/api/practice-owner/transfer", Body(designation, a.Staff), "n-1")));
        var n = 2;
        foreach (var target in new[] { inactive.Staff, disabled.Staff, dentist.Staff, unlinked, deployProfile, Guid.NewGuid() })
            Assert.Equal(PracticeOwnerGuard.TargetMustBeAdministrator, await ErrorAsync(await deploy.SendAsync(HttpMethod.Post, "/api/practice-owner/transfer", Body(designation, target), $"n-{n++}")));

        Assert.Equal(designation, await ActiveDesignationIdAsync());
        Assert.Equal(1, await WithDbAsync(db => db.PracticeOwnerDesignations.CountAsync()));
        Assert.Equal(0, await WithDbAsync(db => db.AuditLogEntries.CountAsync(a => a.EventType == AuditEventTypes.PracticeOwnerTransferred)));
    }

    // ---------- S34 ----------

    [Fact]
    public async Task S34_transfer_and_disable_disables_the_former_owner_in_the_same_transaction_without_changing_their_role()
    {
        var w = await WorldAsync();
        var deploy = await SignInAsync("deploy.t");
        var response = await deploy.SendAsync(HttpMethod.Post, "/api/practice-owner/transfer", Body(w.Designation, w.B.Staff, disable: true), "d-1");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True((await JsonAsync(response)).GetProperty("formerOwnerDisabled").GetBoolean());
        var former = await WithDbAsync(db => db.UserAccounts.AsNoTracking().SingleAsync(u => u.Id == w.A.Account));
        Assert.True(former.IsDisabled);
        Assert.Equal(Role.Admin, former.Role);   // Q8: the emergency option never changes the role
        Assert.Equal(1, await WithDbAsync(db => db.AuditLogEntries.CountAsync(a => a.EventType == AuditEventTypes.AccountDisabled && a.TargetUserAccountId == w.A.Account)));
        Assert.Contains("formerOwnerDisabled=true", await WithDbAsync(db => db.AuditLogEntries.Where(a => a.EventType == AuditEventTypes.PracticeOwnerTransferred).Select(a => a.Details).SingleAsync()));
    }

    [Fact]
    public async Task S34_a_fault_after_the_designation_insert_leaves_the_old_owner_active_enabled_and_signed_in()
    {
        var w = await WorldAsync();
        var formerOwner = await SignInAsync("owner.a");
        var stampBefore = await WithDbAsync(db => db.UserAccounts.Where(u => u.Id == w.A.Account).Select(u => u.SecurityStamp).SingleAsync());

        using (var scope = _factory.Services.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<PracticeOwnerTransferService>();
            service.TestHook_AfterDesignationInsert = () => throw new InvalidOperationException("injected fault after the designation insert");
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.TransferAsync(
                new PracticeOwnerTransferRequest(w.Designation, w.B.Staff, Reason, true, Password, null, DisableFormerOwner: true), "f-1", w.Deploy, CancellationToken.None));
        }

        Assert.Equal(w.Designation, await ActiveDesignationIdAsync());
        Assert.Equal(1, await WithDbAsync(db => db.PracticeOwnerDesignations.CountAsync()));
        var former = await WithDbAsync(db => db.UserAccounts.AsNoTracking().SingleAsync(u => u.Id == w.A.Account));
        Assert.False(former.IsDisabled);
        Assert.Equal(stampBefore, former.SecurityStamp);
        Assert.Equal(HttpStatusCode.OK, (await formerOwner.Client.GetAsync("/api/auth/permissions")).StatusCode);
        Assert.Equal(0, await WithDbAsync(db => db.AuditLogEntries.CountAsync(a => a.EventType == AuditEventTypes.PracticeOwnerTransferred)));
    }

    // ---------- S35 ----------

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task S35_two_transfers_confirmed_against_the_same_owner_commit_once_and_readers_never_see_zero_or_two(int round)
    {
        var w = await WorldAsync();
        var observed = new List<int>();
        using var stop = new CancellationTokenSource();
        var reader = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                await using var db = _fixture.CreateContext();
                observed.Add(await db.PracticeOwnerDesignations.CountAsync(d => d.EndedAtUtc == null));
            }
        });

        async Task<string?> Run(Guid destination, string key)
        {
            using var scope = _factory.Services.CreateScope();
            try
            {
                await scope.ServiceProvider.GetRequiredService<PracticeOwnerTransferService>().TransferAsync(
                    new PracticeOwnerTransferRequest(w.Designation, destination, Reason, true, Password, null, false), key, w.Deploy, CancellationToken.None);
                return null;
            }
            catch (ConfigurationException ex) { return ex.Code; }
        }

        var results = await Task.WhenAll(Run(w.B.Staff, $"r{round}-b"), Run(w.C.Staff, $"r{round}-c"));
        stop.Cancel();
        await reader;

        Assert.Single(results, r => r is null);
        Assert.Single(results, r => r == "owner_changed");
        Assert.Equal(1, await ActiveCountAsync());
        Assert.Equal(2, await WithDbAsync(db => db.PracticeOwnerDesignations.CountAsync()));   // A ended, one new row - never a second transfer
        Assert.Equal(1, await WithDbAsync(db => db.AuditLogEntries.CountAsync(a => a.EventType == AuditEventTypes.PracticeOwnerTransferred)));
        Assert.NotEmpty(observed);
        Assert.All(observed, count => Assert.Equal(1, count));

        // a request whose expected designation is already stale is refused before it changes anything
        Assert.Equal("owner_changed", await Run(w.C.Staff, $"r{round}-late"));
    }

    [Fact]
    public async Task S35_a_transfer_racing_a_role_change_of_its_destination_never_leaves_a_non_administrator_owner()
    {
        var w = await WorldAsync();
        async Task<string?> Transfer()
        {
            using var scope = _factory.Services.CreateScope();
            try
            {
                await scope.ServiceProvider.GetRequiredService<PracticeOwnerTransferService>().TransferAsync(
                    new PracticeOwnerTransferRequest(w.Designation, w.B.Staff, Reason, true, Password, null, false), "race-role", w.Deploy, CancellationToken.None);
                return null;
            }
            catch (ConfigurationException ex) { return ex.Code; }
        }
        async Task<string?> Downgrade()
        {
            using var scope = _factory.Services.CreateScope();
            try { await scope.ServiceProvider.GetRequiredService<AccountService>().ChangeRoleAsync(w.B.Account, Role.Dentist, w.Admin); return null; }
            catch (OwnerTransferRequiredException) { return PracticeOwnerGuard.TransferRequired; }
        }

        var results = await Task.WhenAll(Transfer(), Downgrade());
        Assert.Contains(results, r => r is not null);   // never both committed
        var owner = await WithDbAsync(db => db.PracticeOwnerDesignations.Where(d => d.EndedAtUtc == null).Select(d => d.StaffProfile!.UserAccount!.Role).SingleAsync());
        Assert.Equal(Role.Admin, owner);
    }

    // ---------- S36 ----------

    [Fact]
    public async Task S36_the_same_key_replays_the_same_result_and_a_different_body_with_that_key_is_refused()
    {
        var w = await WorldAsync();
        var deploy = await SignInAsync("deploy.t");
        var first = await JsonAsync(await deploy.SendAsync(HttpMethod.Post, "/api/practice-owner/transfer", Body(w.Designation, w.B.Staff), "same-key"));
        var replay = await deploy.SendAsync(HttpMethod.Post, "/api/practice-owner/transfer", Body(w.Designation, w.B.Staff), "same-key");
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        var again = await JsonAsync(replay);
        Assert.True(again.GetProperty("replayed").GetBoolean());
        Assert.Equal(first.GetProperty("designationId").GetGuid(), again.GetProperty("designationId").GetGuid());
        Assert.Equal(1, await WithDbAsync(db => db.AuditLogEntries.CountAsync(a => a.EventType == AuditEventTypes.PracticeOwnerTransferred)));

        var reused = await deploy.SendAsync(HttpMethod.Post, "/api/practice-owner/transfer", Body(w.Designation, w.C.Staff), "same-key");
        Assert.Equal(HttpStatusCode.Conflict, reused.StatusCode);
        Assert.Equal("idempotency_key_reused", await ErrorAsync(reused));
        Assert.Equal(w.B.Staff, await WithDbAsync(db => db.PracticeOwnerDesignations.Where(d => d.EndedAtUtc == null).Select(d => d.StaffProfileId).SingleAsync()));
    }

    // ---------- S40 ----------

    [Fact]
    public async Task S40_owner_audit_rows_store_ids_only_and_the_audit_log_names_the_people_when_an_authorized_user_reads_it()
    {
        var w = await WorldAsync();
        var deploy = await SignInAsync("deploy.t");
        Assert.Equal(HttpStatusCode.OK, (await deploy.SendAsync(HttpMethod.Post, "/api/practice-owner/transfer", Body(w.Designation, w.B.Staff), "audit-1")).StatusCode);

        var stored = await WithDbAsync(db => db.AuditLogEntries.AsNoTracking()
            .Where(a => a.EventType == AuditEventTypes.PracticeOwnerDesignated || a.EventType == AuditEventTypes.PracticeOwnerTransferred).ToListAsync());
        Assert.Equal(2, stored.Count);
        foreach (var row in stored)
            foreach (var name in new[] { "Name of", "owner.a", "cand.b", "deploy.t" }) Assert.DoesNotContain(name, row.Details);

        var log = (await JsonAsync(await deploy.Client.GetAsync("/api/auth/audit-log?eventType=PracticeOwnerTransferred"))).EnumerateArray().Single();
        Assert.Equal("Name of owner.a", log.GetProperty("formerOwnerName").GetString());
        Assert.Equal("Name of cand.b", log.GetProperty("newOwnerName").GetString());
        Assert.Equal("Name of cand.b", log.GetProperty("subjectName").GetString());
        var designated = (await JsonAsync(await deploy.Client.GetAsync("/api/auth/audit-log?eventType=PracticeOwnerDesignated"))).EnumerateArray().Single();
        Assert.Equal("Name of owner.a", designated.GetProperty("subjectName").GetString());

        await SeedAsync("dentist.log", Role.Dentist);
        Assert.Equal(HttpStatusCode.Forbidden, (await (await SignInAsync("dentist.log")).Client.GetAsync("/api/auth/audit-log")).StatusCode);
    }
}
