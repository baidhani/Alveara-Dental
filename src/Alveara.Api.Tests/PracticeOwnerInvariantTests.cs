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
/// ALV-N030-C01 slice 1 (brief 18.3, 18.3a; review R10): the Practice Owner invariant.
/// S11b - only an enabled Administrator may designate, decided inside the transaction (forced-gap and live races);
/// S26 - the owner's role cannot leave Administrator; S27 - the owner's profile cannot be inactivated (and so never unlinked); S28 - the owner's login cannot be disabled.
/// Every refusal changes nothing and is audited with the rule <c>owner_transfer_required</c> or <c>owner_designation_requires_administrator</c>.
/// </summary>
public class PracticeOwnerInvariantTests : IAsyncLifetime
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

    /// <summary>An account, optionally with an active linked staff profile; returns (account id, staff profile id or empty).</summary>
    private Task<(Guid Account, Guid Staff)> SeedAsync(string username, Role role, AccountKind kind = AccountKind.Staff, bool withStaff = true) => WithDbAsync(async db =>
    {
        var account = new UserAccount
        {
            Id = Guid.NewGuid(), Username = username, PasswordHash = Pbkdf2PasswordHasher.Hash(Password), Role = role, AccountKind = kind, CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        db.UserAccounts.Add(account);
        var staffId = Guid.Empty;
        if (withStaff)
        {
            staffId = Guid.NewGuid();
            db.StaffProfiles.Add(new StaffProfile { Id = staffId, DisplayName = $"Staff {username}", UserAccountId = account.Id, IsActive = true, CreatedAtUtc = DateTimeOffset.UtcNow });
        }
        await db.SaveChangesAsync();
        return (account.Id, staffId);
    });

    private sealed record Session(HttpClient Client, string Csrf)
    {
        public Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, object? body = null)
        {
            var request = new HttpRequestMessage(method, url);
            request.Headers.Add("X-CSRF-Token", Csrf);
            if (body is not null) request.Content = JsonContent.Create(body);
            return Client.SendAsync(request);
        }
    }

    private async Task<Session> SignInAsync(string username)
    {
        var client = _factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { username, password = Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var csrf = (await (await client.GetAsync("/api/auth/csrf-token")).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
        return new Session(client, csrf);
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage r) => await r.Content.ReadFromJsonAsync<JsonElement>();

    /// <summary>Designates through the service (the route's permission is checked elsewhere); returns the error code or null on success.</summary>
    private async Task<string?> DesignateAsync(Guid staffId, Guid actor, Func<Task>? gap = null)
    {
        using var scope = _factory.Services.CreateScope();
        var owners = scope.ServiceProvider.GetRequiredService<PracticeOwnerService>();
        owners.TestHook_AfterPreliminaryActorCheck = gap;
        try { await owners.DesignateInitialAsync(staffId, true, actor, CancellationToken.None); return null; }
        catch (ConfigurationException ex) { return ex.Code; }
    }

    private async Task ChangeRoleAsync(Guid target, Role role, Guid actor)
    {
        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AccountService>().ChangeRoleAsync(target, role, actor);
    }

    private async Task DisableAsync(Guid target, Guid actor)
    {
        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AccountService>().SetAccountEnabledAsync(target, false, actor);
    }

    private Task<int> DesignationsAsync() => WithDbAsync(db => db.PracticeOwnerDesignations.CountAsync());

    private Task<int> RefusalsAsync(string rule) => WithDbAsync(db =>
        db.AuditLogEntries.CountAsync(a => a.EventType == AuditEventTypes.AccountActionRefused && a.Details.Contains(rule)));

    /// <summary>The Deployment Administrator, a second administrator, and an Administrator staff member designated Practice Owner.</summary>
    private async Task<(Guid Deploy, Guid Admin, Guid Owner, Guid OwnerStaff)> WithOwnerAsync()
    {
        var deploy = await SeedAsync("deploy.inv", Role.Admin, AccountKind.DeploymentAdministrator, withStaff: false);
        var admin = await SeedAsync("admin.inv", Role.Admin);
        var owner = await SeedAsync("owner.inv", Role.Admin);
        Assert.Null(await DesignateAsync(owner.Staff, deploy.Account));
        return (deploy.Account, admin.Account, owner.Account, owner.Staff);
    }

    // ---------- S11 / S11b: who may designate, and whom ----------

    [Fact]
    public async Task S11_the_target_must_be_an_administrator_and_the_actor_must_be_an_enabled_administrator()
    {
        var deploy = await SeedAsync("deploy.s11", Role.Admin, AccountKind.DeploymentAdministrator, withStaff: false);
        var dentist = await SeedAsync("dentist.s11", Role.Dentist);
        var manager = await SeedAsync("manager.s11", Role.OfficeManager);
        var adminTarget = await SeedAsync("admin.target.s11", Role.Admin);

        // the target: an otherwise eligible Dentist is refused, nothing written
        Assert.Equal(PracticeOwnerGuard.TargetMustBeAdministrator, await DesignateAsync(dentist.Staff, deploy.Account));
        // the actor: ManageUsers is Administrator-only (PermissionProtection.AdminOnly), so the route already refuses an Office Manager (403);
        // the service refuses it too, whatever the route let through
        var managerSession = await SignInAsync("manager.s11");
        Assert.Equal(HttpStatusCode.Forbidden, (await managerSession.SendAsync(HttpMethod.Post, "/api/practice-owner/initial", new DesignatePracticeOwnerRequest(adminTarget.Staff, true))).StatusCode);
        Assert.Equal(PracticeOwnerGuard.ActorMustBeAdministrator, await DesignateAsync(adminTarget.Staff, manager.Account));
        Assert.Equal(0, await DesignationsAsync());
        Assert.Equal(1, await RefusalsAsync(PracticeOwnerGuard.ActorMustBeAdministrator));

        // an Administrator target succeeds
        Assert.Null(await DesignateAsync(adminTarget.Staff, deploy.Account));
        Assert.Equal(1, await DesignationsAsync());
    }

    [Fact]
    public async Task S11b_an_actor_downgraded_in_the_gap_before_the_transaction_is_refused_inside_it()
    {
        var actor = await SeedAsync("actor.gap", Role.Admin);
        var other = await SeedAsync("other.gap", Role.Admin);
        var target = await SeedAsync("target.gap", Role.Admin);

        // the early check passes; the downgrade commits in the gap; the in-transaction check must refuse
        var error = await DesignateAsync(target.Staff, actor.Account, () => ChangeRoleAsync(actor.Account, Role.Dentist, other.Account));
        Assert.Equal(PracticeOwnerGuard.ActorMustBeAdministrator, error);
        Assert.Equal(0, await DesignationsAsync());
        Assert.Equal(1, await RefusalsAsync(PracticeOwnerGuard.ActorMustBeAdministrator));
    }

    [Fact]
    public async Task S11b_an_actor_disabled_in_the_gap_before_the_transaction_is_refused_inside_it()
    {
        var actor = await SeedAsync("actor.off", Role.Admin);
        var other = await SeedAsync("other.off", Role.Admin);
        var target = await SeedAsync("target.off", Role.Admin);

        var error = await DesignateAsync(target.Staff, actor.Account, () => DisableAsync(actor.Account, other.Account));
        Assert.Equal(PracticeOwnerGuard.ActorMustBeAdministrator, error);
        Assert.Equal(0, await DesignationsAsync());
    }

    /// <summary>One round per case: a fresh database each (the designation trigger refuses deletes, so "never designated" cannot be reset in one database).</summary>
    [Theory]
    [InlineData("role", 1)]
    [InlineData("role", 2)]
    [InlineData("role", 3)]
    [InlineData("disable", 1)]
    [InlineData("disable", 2)]
    [InlineData("disable", 3)]
    public async Task S11b_a_live_race_never_lets_an_actor_that_was_ineligible_at_commit_designate(string change, int round)
    {
        var actor = await SeedAsync($"actor.race.{change}.{round}", Role.Admin);
        var other = await SeedAsync($"other.race.{change}.{round}", Role.Admin);
        var target = await SeedAsync($"target.race.{change}.{round}", Role.Admin);

        var designate = DesignateAsync(target.Staff, actor.Account);
        var compete = change == "role" ? ChangeRoleAsync(actor.Account, Role.Dentist, other.Account) : DisableAsync(actor.Account, other.Account);
        await Task.WhenAll(designate, compete);

        var designated = await WithDbAsync(db => db.PracticeOwnerDesignations.AsNoTracking().SingleOrDefaultAsync());
        if (designated is null)
        {
            Assert.Equal(PracticeOwnerGuard.ActorMustBeAdministrator, await designate);   // refused: the change committed first
            return;
        }
        // committed: the actor's change was serialized after the designation (its audit row is not earlier than the designation)
        Assert.Null(await designate);
        var changedAt = await WithDbAsync(db => db.AuditLogEntries.Where(a => a.TargetUserAccountId == actor.Account
                && (a.EventType == AuditEventTypes.RoleChanged || a.EventType == AuditEventTypes.AccountDisabled))
            .Select(a => a.TimestampUtc).SingleAsync());
        Assert.True(changedAt >= designated.DesignatedAtUtc, $"the actor changed at {changedAt:O}, before the designation at {designated.DesignatedAtUtc:O}");
    }

    // ---------- S26 / S27 / S28: the owner's account and profile are protected until a transfer ----------

    [Fact]
    public async Task S26_the_owners_role_cannot_leave_administrator_but_other_administrators_roles_still_change()
    {
        var (_, admin, owner, _) = await WithOwnerAsync();
        var session = await SignInAsync("admin.inv");

        var refused = await session.SendAsync(HttpMethod.Put, $"/api/auth/{owner}/role", new ChangeRoleRequest("Dentist"));
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        var body = await JsonAsync(refused);
        Assert.Equal(PracticeOwnerGuard.TransferRequired, body.GetProperty("error").GetString());
        Assert.Equal(PracticeOwnerGuard.RoleChangeMessage, body.GetProperty("message").GetString());
        Assert.Equal(Role.Admin, await WithDbAsync(db => db.UserAccounts.Where(u => u.Id == owner).Select(u => u.Role).SingleAsync()));
        Assert.Equal(1, await RefusalsAsync(PracticeOwnerGuard.TransferRequired));

        // another Administrator (not the owner) still changes role as before
        var plain = await SeedAsync("plain.admin", Role.Admin);
        Assert.Equal(HttpStatusCode.OK, (await session.SendAsync(HttpMethod.Put, $"/api/auth/{plain.Account}/role", new ChangeRoleRequest("Dentist"))).StatusCode);
    }

    [Fact]
    public async Task S26_a_degraded_owner_can_be_changed_to_administrator()
    {
        var (_, _, owner, _) = await WithOwnerAsync();
        await WithDbAsync(db => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [UserAccounts] SET [Role] = {(int)Role.Dentist} WHERE [Id] = {owner}"));   // a pre-C01 state
        var session = await SignInAsync("admin.inv");
        Assert.Equal(HttpStatusCode.OK, (await session.SendAsync(HttpMethod.Put, $"/api/auth/{owner}/role", new ChangeRoleRequest("Admin"))).StatusCode);
        Assert.Equal(Role.Admin, await WithDbAsync(db => db.UserAccounts.Where(u => u.Id == owner).Select(u => u.Role).SingleAsync()));
    }

    [Fact]
    public async Task S27_the_owners_profile_cannot_be_inactivated_through_either_path_and_so_is_never_unlinked()
    {
        var (_, _, owner, ownerStaff) = await WithOwnerAsync();
        var session = await SignInAsync("admin.inv");

        var refused = await session.SendAsync(HttpMethod.Put, $"/api/config/staff/{ownerStaff}/active", new SetActiveRequest(false, null));
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        var body = await JsonAsync(refused);
        Assert.Equal(PracticeOwnerGuard.TransferRequired, body.GetProperty("error").GetString());
        Assert.Equal(PracticeOwnerGuard.InactivateMessage, body.GetProperty("message").GetString());
        Assert.Equal(1, await RefusalsAsync(PracticeOwnerGuard.TransferRequired));
        // an ordinary refusal, not an inactivation failure: no failure record (and so no alert)
        Assert.Equal(0, await WithDbAsync(db => db.AuditLogEntries.CountAsync(a => a.EventType == ConfigurationAuditEvents.StaffInactivationFailed)));

        // the profile service on its own refuses too
        using (var scope = _factory.Services.CreateScope())
        {
            var staff = scope.ServiceProvider.GetRequiredService<StaffProviderService>();
            var direct = await Assert.ThrowsAsync<ConfigurationException>(() => staff.SetStaffActiveAsync(ownerStaff, false, null, owner, CancellationToken.None));
            Assert.Equal(PracticeOwnerGuard.TransferRequired, direct.Code);
        }

        // unlink needs an inactive profile, so it is unreachable for the owner
        var unlink = await session.SendAsync(HttpMethod.Put, $"/api/config/staff/{ownerStaff}/unlink-account", new UnlinkStaffAccountRequest("left the practice", null));
        Assert.Equal(HttpStatusCode.Conflict, unlink.StatusCode);
        var profile = await WithDbAsync(db => db.StaffProfiles.AsNoTracking().SingleAsync(s => s.Id == ownerStaff));
        Assert.True(profile.IsActive);
        Assert.Equal(owner, profile.UserAccountId);
        Assert.False(await WithDbAsync(db => db.UserAccounts.Where(u => u.Id == owner).Select(u => u.IsDisabled).SingleAsync()));
    }

    [Fact]
    public async Task S28_the_owners_login_cannot_be_disabled_through_either_disable_path()
    {
        var (_, _, owner, _) = await WithOwnerAsync();
        var session = await SignInAsync("admin.inv");

        var refused = await session.SendAsync(HttpMethod.Put, $"/api/auth/{owner}/enabled", new SetEnabledRequest(false));
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        var body = await JsonAsync(refused);
        Assert.Equal(PracticeOwnerGuard.TransferRequired, body.GetProperty("error").GetString());
        Assert.Equal("Transfer the Practice Owner designation before disabling this account.", body.GetProperty("message").GetString());
        Assert.Equal(1, await RefusalsAsync(PracticeOwnerGuard.TransferRequired));

        // the inactivation's disable step refuses as a backstop, inside a transaction (E4 normally refuses before it is reached)
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AlveraDbContext>();
            var accounts = scope.ServiceProvider.GetRequiredService<AccountService>();
            await using var tx = await db.Database.BeginTransactionAsync();
            await Assert.ThrowsAsync<OwnerTransferRequiredException>(() => accounts.DisableForStaffInactivationAsync(owner, owner, "Owner", CancellationToken.None));
            await tx.RollbackAsync();
        }
        Assert.False(await WithDbAsync(db => db.UserAccounts.Where(u => u.Id == owner).Select(u => u.IsDisabled).SingleAsync()));
    }
}
