using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Alveara.Api.Architecture.Configuration;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N030-C01 slice 2 (brief 18.4, the U-046 single call; review R10): "Add staff member" with <c>practiceOwner: true</c> creates the profile, the Administrator
/// login and the designation in ONE transaction. S23 - happy path and the Administrator role rule; S24 - both or neither (fault injection, an owner already
/// designated); S25 - idempotent replay and two different keys racing.
/// </summary>
public class PracticeOwnerCreationTests : IAsyncLifetime
{
    private const string Password = "Seeded-Password-1";
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

    private Task<Guid> SeedDeploymentAdministratorAsync() => WithDbAsync(async db =>
    {
        var account = new UserAccount
        {
            Id = Guid.NewGuid(), Username = "deploy.create", PasswordHash = Pbkdf2PasswordHasher.Hash(Password), Role = Role.Admin,
            AccountKind = AccountKind.DeploymentAdministrator, CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        db.UserAccounts.Add(account);
        await db.SaveChangesAsync();
        return account.Id;
    });

    private async Task<(HttpClient Client, string Csrf)> SignInAsync(string username)
    {
        var client = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/login", new { username, password = Password })).StatusCode);
        var csrf = (await (await client.GetAsync("/api/auth/csrf-token")).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
        return (client, csrf);
    }

    private static Task<HttpResponseMessage> AddAsync((HttpClient Client, string Csrf) s, string name, string username, string role, bool owner, string key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/config/staff-members")
        {
            Content = JsonContent.Create(new { displayName = name, jobTitle = "Dentist and owner", username, role, practiceOwner = owner }),
        };
        request.Headers.Add("X-CSRF-Token", s.Csrf);
        request.Headers.Add("Idempotency-Key", key);
        return s.Client.SendAsync(request);
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage r) => await r.Content.ReadFromJsonAsync<JsonElement>();

    private Task<(int Staff, int Accounts, int Designations)> CountsAsync() => WithDbAsync(async db =>
        (await db.StaffProfiles.CountAsync(), await db.UserAccounts.CountAsync(), await db.PracticeOwnerDesignations.CountAsync()));

    /// <summary>The service called directly (one scope each, so concurrent calls use separate contexts); returns the error code or null.</summary>
    private async Task<string?> CreateOwnerAsync(Guid actor, string username, string key, Func<Task>? beforeDesignation = null)
    {
        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<StaffMemberService>();
        service.TestHook_BeforeDesignation = beforeDesignation;
        try
        {
            await service.CreateAsync($"Dr {username}", null, username, "Admin", key, actor, CancellationToken.None, practiceOwner: true);
            return null;
        }
        catch (ConfigurationException ex) { return ex.Code; }
    }

    // ---------- S23 ----------

    [Fact]
    public async Task S23_one_call_creates_the_profile_the_administrator_login_and_the_designation_together()
    {
        var deploy = await SeedDeploymentAdministratorAsync();
        var session = await SignInAsync("deploy.create");
        var before = await CountsAsync();

        var created = await AddAsync(session, "Dr Maya Owner", "mowner", "Admin", owner: true, key: "staff-member:mowner");
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await JsonAsync(created);
        Assert.True(body.GetProperty("practiceOwner").GetBoolean());
        Assert.False(string.IsNullOrEmpty(body.GetProperty("temporaryPassword").GetString()));
        var staffId = body.GetProperty("staffProfileId").GetGuid();
        var accountId = body.GetProperty("userAccountId").GetGuid();

        Assert.Equal((before.Staff + 1, before.Accounts + 1, 1), await CountsAsync());
        var account = await WithDbAsync(db => db.UserAccounts.AsNoTracking().SingleAsync(u => u.Id == accountId));
        Assert.Equal(Role.Admin, account.Role);
        Assert.Equal(AccountKind.Staff, account.AccountKind);
        Assert.True(account.MustChangePassword);
        Assert.Equal(accountId, await WithDbAsync(db => db.StaffProfiles.Where(s => s.Id == staffId).Select(s => s.UserAccountId).SingleAsync()));
        var designation = await WithDbAsync(db => db.PracticeOwnerDesignations.AsNoTracking().SingleAsync());
        Assert.Equal(staffId, designation.StaffProfileId);
        Assert.Null(designation.EndedAtUtc);
        Assert.Equal(PracticeOwnerDesignationMethod.InitialByDeploymentAdministrator, designation.Method);
        Assert.Equal(deploy, designation.DesignatedByAccountId);
        foreach (var eventType in new[] { AuditEventTypes.StaffMemberCreated, AuditEventTypes.TemporaryPasswordIssued })
            Assert.Equal(1, await WithDbAsync(db => db.AuditLogEntries.CountAsync(a => a.EventType == eventType && a.TargetUserAccountId == accountId)));
        Assert.Equal(1, await WithDbAsync(db => db.AuditLogEntries.CountAsync(a => a.EventType == AuditEventTypes.PracticeOwnerDesignated)));
    }

    [Theory]
    [InlineData("Dentist")]
    [InlineData("OfficeManager")]
    [InlineData("FrontDesk")]
    public async Task S23_any_role_other_than_administrator_is_refused_with_400_and_nothing_is_written(string role)
    {
        await SeedDeploymentAdministratorAsync();
        var session = await SignInAsync("deploy.create");
        var before = await CountsAsync();

        var refused = await AddAsync(session, "Dr Wrong Role", "wrong.role", role, owner: true, key: "staff-member:wrong.role");
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal(PracticeOwnerGuard.TargetMustBeAdministrator, (await JsonAsync(refused)).GetProperty("error").GetString());
        Assert.Equal(before, await CountsAsync());

        // without the owner box the same role is an ordinary addition
        Assert.Equal(HttpStatusCode.Created, (await AddAsync(session, "Dr Wrong Role", "wrong.role", role, owner: false, key: "staff-member:wrong.role.2")).StatusCode);
        Assert.Equal(0, (await CountsAsync()).Designations);
    }

    // ---------- S24 ----------

    [Fact]
    public async Task S24_a_fault_after_the_account_insert_rolls_back_the_profile_the_login_and_the_designation()
    {
        var deploy = await SeedDeploymentAdministratorAsync();
        var before = await CountsAsync();

        using (var scope = _factory.Services.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<StaffMemberService>();
            service.TestHook_BeforeDesignation = () => throw new InvalidOperationException("injected fault after the account insert");
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.CreateAsync("Dr Fault", null, "dr.fault", "Admin", "key-fault", deploy, CancellationToken.None, practiceOwner: true));
        }

        Assert.Equal(before, await CountsAsync());
        Assert.Equal(0, await WithDbAsync(db => db.AuditLogEntries.CountAsync(a => a.EventType == AuditEventTypes.StaffMemberCreated)));
        // the same key afterwards is a clean first attempt, not a replay of a half-made person
        Assert.Null(await CreateOwnerAsync(deploy, "dr.fault", "key-fault"));
        Assert.Equal((before.Staff + 1, before.Accounts + 1, 1), await CountsAsync());
    }

    [Fact]
    public async Task S24_when_an_owner_already_exists_the_call_is_refused_and_creates_nobody()
    {
        var deploy = await SeedDeploymentAdministratorAsync();
        Assert.Null(await CreateOwnerAsync(deploy, "first.owner", "key-first"));
        var before = await CountsAsync();

        var session = await SignInAsync("deploy.create");
        var refused = await AddAsync(session, "Dr Second", "second.owner", "Admin", owner: true, key: "staff-member:second.owner");
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("owner_already_designated", (await JsonAsync(refused)).GetProperty("error").GetString());
        Assert.Equal(before, await CountsAsync());
        Assert.False(await WithDbAsync(db => db.UserAccounts.AnyAsync(u => u.Username == "second.owner")));
    }

    // ---------- S25 ----------

    [Fact]
    public async Task S25_the_same_key_twice_is_one_person_one_designation_and_no_second_password()
    {
        await SeedDeploymentAdministratorAsync();
        var session = await SignInAsync("deploy.create");
        var first = await AddAsync(session, "Dr Replay", "dr.replay", "Admin", owner: true, key: "staff-member:dr.replay");
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var after = await CountsAsync();

        var replay = await AddAsync(session, "Dr Replay", "dr.replay", "Admin", owner: true, key: "staff-member:dr.replay");
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        var body = await JsonAsync(replay);
        Assert.Equal("already_created", body.GetProperty("status").GetString());
        Assert.True(body.GetProperty("practiceOwner").GetBoolean());
        Assert.False(body.TryGetProperty("temporaryPassword", out _));
        Assert.Equal(after, await CountsAsync());
        Assert.Equal(1, await WithDbAsync(db => db.AuditLogEntries.CountAsync(a => a.EventType == AuditEventTypes.PracticeOwnerDesignated)));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task S25_two_different_keys_at_once_make_exactly_one_owner_and_the_loser_creates_nothing(int round)
    {
        var deploy = await SeedDeploymentAdministratorAsync();
        var before = await CountsAsync();

        var a = CreateOwnerAsync(deploy, $"race.a.{round}", $"key-a-{round}");
        var b = CreateOwnerAsync(deploy, $"race.b.{round}", $"key-b-{round}");
        var results = await Task.WhenAll(a, b);

        Assert.Single(results, r => r is null);
        Assert.Single(results, r => r == "owner_already_designated");
        Assert.Equal((before.Staff + 1, before.Accounts + 1, 1), await CountsAsync());
        var loser = results[0] is null ? $"race.b.{round}" : $"race.a.{round}";
        Assert.False(await WithDbAsync(db => db.UserAccounts.AnyAsync(u => u.Username == loser)));
        Assert.False(await WithDbAsync(db => db.StaffProfiles.AnyAsync(s => s.DisplayName == $"Dr {loser}")));
    }
}
