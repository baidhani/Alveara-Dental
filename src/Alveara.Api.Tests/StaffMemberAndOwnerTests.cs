using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Alveara.Api.Architecture.Configuration;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Permissions;
using Alveara.Api.Controllers;
using Alveara.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N030 slice 4 (brief sections 4 to 6; test rows S8, S9, S11, S16 and the S17 replay/recovery part):
/// <list type="bullet">
/// <item>S9 Add staff member: profile, login and link in one transaction (a fault after the profile insert leaves nothing); the 16-character temporary
/// password is shown once and is in no log or audit row; an Idempotency-Key replay answers <c>already_created</c> without it; ManageUsers is required;
/// the role subset rule.</item>
/// <item>S16: no profile-only create, no link edit, no linkable-accounts list; legacy-login only for an active profile without a login.</item>
/// <item>S8: the Deployment Administrator has Admin's permissions, is named "Deployment Administrator" and is absent from the assignee and recipient pickers.</item>
/// <item>S11: the Practice Owner is designated once, only to an active staff member with an enabled staff login, by a ManageUsers holder.</item>
/// </list>
/// </summary>
public class StaffMemberAndOwnerTests : IAsyncLifetime
{
    private const string Password = "Seeded-Password-1";
    private readonly TestDatabaseFixture _fixture = new();
    private readonly CapturingLoggerProvider _logs = new();
    private WebApplicationFactory<Program> _factory = null!;

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("ConnectionStrings:Alveara", _fixture.ConnectionString);
            b.UseSetting("AuthAttemptRateLimit:PermitLimit", "500");
            b.ConfigureLogging(l => l.AddProvider(_logs));
        });
    }

    public async Task DisposeAsync() { await _factory.DisposeAsync(); await _fixture.DisposeAsync(); }

    // ---------- helpers ----------

    private async Task<T> WithDbAsync<T>(Func<AlveraDbContext, Task<T>> work)
    {
        using var scope = _factory.Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<AlveraDbContext>());
    }

    private Task<Guid> SeedAccountAsync(string username, Role role, AccountKind kind = AccountKind.Staff, string? staffName = null, bool staffActive = true) => WithDbAsync(async db =>
    {
        var account = new UserAccount
        {
            Id = Guid.NewGuid(), Username = username, PasswordHash = Pbkdf2PasswordHasher.Hash(Password), Role = role, AccountKind = kind, CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        db.UserAccounts.Add(account);
        if (staffName is not null)
            db.StaffProfiles.Add(new StaffProfile { Id = Guid.NewGuid(), DisplayName = staffName, UserAccountId = account.Id, IsActive = staffActive, CreatedAtUtc = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        return account.Id;
    });

    private Task<Guid> SeedProfileAsync(string name, bool active = true, Guid? accountId = null) => WithDbAsync(async db =>
    {
        var staff = new StaffProfile { Id = Guid.NewGuid(), DisplayName = name, IsActive = active, UserAccountId = accountId, CreatedAtUtc = DateTimeOffset.UtcNow };
        db.StaffProfiles.Add(staff);
        await db.SaveChangesAsync();
        return staff.Id;
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

    private async Task<Session> SignInAsync(string username, string password = Password)
    {
        var client = _factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { username, password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var csrf = (await (await client.GetAsync("/api/auth/csrf-token")).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
        return new Session(client, csrf);
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage r) => await r.Content.ReadFromJsonAsync<JsonElement>();
    private static async Task<string> ErrorAsync(HttpResponseMessage r) => (await JsonAsync(r)).GetProperty("error").GetString()!;

    private static object Member(string name, string username, string role = "FrontDesk") => new AddStaffMemberRequest(name, "Front desk", username, role);

    // ---------- S9: Add staff member ----------

    [Fact]
    public async Task Add_staff_member_creates_profile_login_and_link_once_and_the_first_sign_in_is_forced()
    {
        await SeedAccountAsync("admin.one", Role.Admin, staffName: "Admin One");
        var admin = await SignInAsync("admin.one");

        Assert.Equal("idempotency_key_required", await ErrorAsync(await admin.SendAsync(HttpMethod.Post, "/api/config/staff-members", Member("Pat Lee", "pat.lee"))));

        var created = await admin.SendAsync(HttpMethod.Post, "/api/config/staff-members", Member("Pat Lee", "pat.lee"), key: "add-pat");
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await JsonAsync(created);
        var temporary = body.GetProperty("temporaryPassword").GetString()!;
        Assert.Equal(StaffMemberService.TemporaryPasswordLength, temporary.Length);
        var staffId = body.GetProperty("staffProfileId").GetGuid();
        var accountId = body.GetProperty("userAccountId").GetGuid();

        var (profile, account) = await WithDbAsync(async db => (await db.StaffProfiles.AsNoTracking().SingleAsync(s => s.Id == staffId), await db.UserAccounts.AsNoTracking().SingleAsync(u => u.Id == accountId)));
        Assert.Equal(accountId, profile.UserAccountId);
        Assert.True(profile.IsActive);
        Assert.Equal(Role.FrontDesk, account.Role);
        Assert.Equal(AccountKind.Staff, account.AccountKind);
        Assert.True(account.MustChangePassword);
        Assert.False(account.IsDisabled);

        var audits = await WithDbAsync(db => db.AuditLogEntries.AsNoTracking().Where(a => a.TargetUserAccountId == accountId).ToListAsync());
        Assert.Single(audits, a => a.EventType == AuditEventTypes.StaffMemberCreated);
        Assert.Single(audits, a => a.EventType == AuditEventTypes.TemporaryPasswordIssued);
        Assert.All(await WithDbAsync(db => db.AuditLogEntries.AsNoTracking().Select(a => a.Details).ToListAsync()), d => Assert.DoesNotContain(temporary, d));

        // Replay with the same key: the same rows, "already created", no password; nothing new written.
        var replay = await admin.SendAsync(HttpMethod.Post, "/api/config/staff-members", Member("Pat Lee", "pat.lee"), key: "add-pat");
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        var replayBody = await JsonAsync(replay);
        Assert.Equal("already_created", replayBody.GetProperty("status").GetString());
        Assert.Equal(accountId, replayBody.GetProperty("userAccountId").GetGuid());
        Assert.False(replayBody.TryGetProperty("temporaryPassword", out _));
        Assert.Equal(1, await WithDbAsync(db => db.UserAccounts.CountAsync(u => u.Username == "pat.lee")));
        Assert.Equal(2, await WithDbAsync(db => db.AuditLogEntries.CountAsync(a => a.TargetUserAccountId == accountId)));

        // A new key with the same name or username is a conflict, not a second person.
        Assert.Equal("staff_name_taken", await ErrorAsync(await admin.SendAsync(HttpMethod.Post, "/api/config/staff-members", Member("Pat Lee", "pat.other"), key: "add-pat-2")));
        Assert.Equal("username_taken", await ErrorAsync(await admin.SendAsync(HttpMethod.Post, "/api/config/staff-members", Member("Pat Other", "pat.lee"), key: "add-pat-3")));
        Assert.Equal("role_invalid", await ErrorAsync(await admin.SendAsync(HttpMethod.Post, "/api/config/staff-members", Member("Pat Role", "pat.role", "Unassigned"), key: "add-pat-4")));

        // First sign-in with the temporary password is gated until the person chooses their own.
        var newcomer = await SignInAsync("pat.lee", temporary);
        Assert.True((await JsonAsync(await newcomer.Client.GetAsync("/api/auth/permissions"))).GetProperty("mustChangePassword").GetBoolean());
        Assert.Equal(HttpStatusCode.Forbidden, (await newcomer.Client.GetAsync("/api/patients")).StatusCode);

        Assert.DoesNotContain(_logs.Entries, e => e.Message.Contains(temporary));
    }

    [Fact]
    public async Task Without_ManageUsers_there_is_no_create_and_nothing_is_written()
    {
        await SeedAccountAsync("office.mgr", Role.OfficeManager, staffName: "Office Manager");
        var manager = await SignInAsync("office.mgr");
        var before = await WithDbAsync(db => db.StaffProfiles.CountAsync());
        var refused = await manager.SendAsync(HttpMethod.Post, "/api/config/staff-members", Member("Sam Ray", "sam.ray"), key: "om-1");
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal(before, await WithDbAsync(db => db.StaffProfiles.CountAsync()));
        Assert.Equal(0, await WithDbAsync(db => db.UserAccounts.CountAsync(u => u.Username == "sam.ray")));
        // Unauthenticated callers never reach it.
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().PostAsJsonAsync("/api/config/staff-members", Member("X", "x"))).StatusCode);
    }

    [Fact]
    public async Task The_role_rule_lets_a_non_administrator_create_only_roles_within_their_own_permissions()
    {
        var manager = await SeedAccountAsync("om.rule", Role.OfficeManager, staffName: "OM Rule");
        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<StaffMemberService>();
        var permissions = scope.ServiceProvider.GetRequiredService<IEffectivePermissions>();
        var mine = await permissions.PermissionsForAsync(Role.OfficeManager, default);

        var adminRefused = await Assert.ThrowsAsync<ConfigurationException>(() => service.CreateAsync("Would Be Admin", null, "would.admin", "Admin", "k-admin", manager, default));
        Assert.Equal((403, AccountTargetRules.TargetIsAdmin), (adminRefused.StatusCode, adminRefused.Code));

        var working = Enum.GetValues<Role>().Where(r => r is not Role.Admin and not Role.Unassigned).ToList();
        var allowed = new List<Role>(); var exceeding = new List<Role>();
        foreach (var r in working) ((await permissions.PermissionsForAsync(r, default)).IsSubsetOf(mine) ? allowed : exceeding).Add(r);
        Assert.NotEmpty(allowed);
        foreach (var r in exceeding)
        {
            var ex = await Assert.ThrowsAsync<ConfigurationException>(() => service.CreateAsync($"Too Much {r}", null, $"too.{r}", r.ToString(), $"k-{r}", manager, default));
            Assert.Equal((403, AccountTargetRules.TargetExceedsActor), (ex.StatusCode, ex.Code));
        }
        var ok = await service.CreateAsync("Within Reach", null, "within.reach", allowed[0].ToString(), "k-ok", manager, default);
        Assert.NotNull(ok.TemporaryPassword);
    }

    [Fact]
    public async Task A_fault_after_the_profile_insert_leaves_neither_profile_nor_login()
    {
        var admin = await SeedAccountAsync("admin.fault", Role.Admin, staffName: "Admin Fault");
        using (var scope = _factory.Services.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<StaffMemberService>();
            service.TestHook_AfterProfileInsert = () => throw new InvalidOperationException("injected fault");
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateAsync("Half Made", null, "half.made", "FrontDesk", "k-fault", admin, default));
        }
        Assert.Equal(0, await WithDbAsync(db => db.StaffProfiles.CountAsync(s => s.DisplayName == "Half Made")));
        Assert.Equal(0, await WithDbAsync(db => db.UserAccounts.CountAsync(u => u.Username == "half.made")));
        // The same key then works normally: nothing half-made blocks it.
        using var again = _factory.Services.CreateScope();
        Assert.NotNull((await again.ServiceProvider.GetRequiredService<StaffMemberService>().CreateAsync("Half Made", null, "half.made", "FrontDesk", "k-fault", admin, default)).TemporaryPassword);
    }

    // ---------- S16: no profile-only create, no link edits; legacy-login ----------

    [Fact]
    public async Task Profile_only_create_link_edits_and_the_link_picker_are_gone_and_legacy_rows_still_list()
    {
        await SeedAccountAsync("admin.s16", Role.Admin, staffName: "Admin S16");
        var spareAccount = await SeedAccountAsync("spare.login", Role.FrontDesk);
        var legacy = await SeedProfileAsync("Legacy Lou");
        var admin = await SignInAsync("admin.s16");
        var profiles = await WithDbAsync(db => db.StaffProfiles.CountAsync());

        var create = await admin.SendAsync(HttpMethod.Post, "/api/config/staff", new SaveStaffRequest("Profile Only", null, null, null));
        Assert.Contains(create.StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed });
        Assert.Equal(profiles, await WithDbAsync(db => db.StaffProfiles.CountAsync()));

        // GET of an unmapped API path: 404, or the client's index.html when a client build exists (the SPA fallback) - never the old JSON list.
        var picker = await admin.Client.GetAsync("/api/config/linkable-accounts");
        Assert.True(picker.StatusCode == HttpStatusCode.NotFound || picker.Content.Headers.ContentType?.MediaType == "text/html", $"answered {(int)picker.StatusCode}");

        var row = await JsonAsync(await admin.Client.GetAsync($"/api/config/staff/{legacy}"));
        var version = row.GetProperty("rowVersion").GetString();
        var linkEdit = await admin.SendAsync(HttpMethod.Put, $"/api/config/staff/{legacy}", new SaveStaffRequest("Legacy Lou", null, spareAccount, version));
        Assert.Equal("link_not_editable", await ErrorAsync(linkEdit));
        Assert.Null(await WithDbAsync(db => db.StaffProfiles.Where(s => s.Id == legacy).Select(s => s.UserAccountId).SingleAsync()));
        var plainEdit = await admin.SendAsync(HttpMethod.Put, $"/api/config/staff/{legacy}", new SaveStaffRequest("Legacy Louise", "Hygiene", null, version));
        Assert.Equal(HttpStatusCode.OK, plainEdit.StatusCode);

        // Unlinked profiles and accounts still list and read.
        Assert.Contains("Legacy Louise", await admin.Client.GetStringAsync("/api/config/staff"));
        Assert.Contains("spare.login", await admin.Client.GetStringAsync("/api/auth/users"));
    }

    [Fact]
    public async Task Legacy_login_is_only_for_an_active_profile_without_one_and_is_idempotent()
    {
        await SeedAccountAsync("admin.legacy", Role.Admin, staffName: "Admin Legacy");
        var legacy = await SeedProfileAsync("Legacy Lin");
        var inactive = await SeedProfileAsync("Gone Gail", active: false);
        var hasLogin = await SeedAccountAsync("has.login", Role.FrontDesk, staffName: "Has Login");
        var hasLoginProfile = await WithDbAsync(db => db.StaffProfiles.Where(s => s.UserAccountId == hasLogin).Select(s => s.Id).SingleAsync());
        var admin = await SignInAsync("admin.legacy");

        var created = await admin.SendAsync(HttpMethod.Post, $"/api/config/staff/{legacy}/legacy-login", new LegacyLoginRequest("legacy.lin", "Hygienist"), key: "ll-1");
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await JsonAsync(created);
        var accountId = body.GetProperty("userAccountId").GetGuid();
        Assert.Equal(StaffMemberService.TemporaryPasswordLength, body.GetProperty("temporaryPassword").GetString()!.Length);
        Assert.Equal(accountId, await WithDbAsync(db => db.StaffProfiles.Where(s => s.Id == legacy).Select(s => s.UserAccountId).SingleAsync()));
        Assert.True(await WithDbAsync(db => db.UserAccounts.Where(u => u.Id == accountId).Select(u => u.MustChangePassword).SingleAsync()));

        var replay = await admin.SendAsync(HttpMethod.Post, $"/api/config/staff/{legacy}/legacy-login", new LegacyLoginRequest("legacy.lin", "Hygienist"), key: "ll-1");
        Assert.Equal("already_created", (await JsonAsync(replay)).GetProperty("status").GetString());
        Assert.Equal("staff_has_login", await ErrorAsync(await admin.SendAsync(HttpMethod.Post, $"/api/config/staff/{legacy}/legacy-login", new LegacyLoginRequest("legacy.two", "Hygienist"), key: "ll-2")));
        Assert.Equal("staff_has_login", await ErrorAsync(await admin.SendAsync(HttpMethod.Post, $"/api/config/staff/{hasLoginProfile}/legacy-login", new LegacyLoginRequest("x.y", "FrontDesk"), key: "ll-3")));
        var refusedInactive = await admin.SendAsync(HttpMethod.Post, $"/api/config/staff/{inactive}/legacy-login", new LegacyLoginRequest("gone.gail", "FrontDesk"), key: "ll-4");
        Assert.Equal(HttpStatusCode.Conflict, refusedInactive.StatusCode);
        Assert.Equal("staff_inactive", await ErrorAsync(refusedInactive));
        Assert.Equal(0, await WithDbAsync(db => db.UserAccounts.CountAsync(u => u.Username == "gone.gail" || u.Username == "legacy.two" || u.Username == "x.y")));
    }

    // ---------- S8: the Deployment Administrator ----------

    [Fact]
    public async Task The_deployment_administrator_has_admin_permissions_a_kind_name_and_is_in_no_picker()
    {
        var deployId = await SeedAccountAsync("deploy", Role.Admin, AccountKind.DeploymentAdministrator);
        await SeedAccountAsync("admin.peer", Role.Admin, staffName: "Admin Peer");
        var deploy = await SignInAsync("deploy");
        var peer = await SignInAsync("admin.peer");

        var deployMe = await JsonAsync(await deploy.Client.GetAsync("/api/auth/permissions"));
        var peerMe = await JsonAsync(await peer.Client.GetAsync("/api/auth/permissions"));
        Assert.Equal(peerMe.GetProperty("permissions").ToString(), deployMe.GetProperty("permissions").ToString());
        Assert.Equal("DeploymentAdministrator", deployMe.GetProperty("accountKind").GetString());

        var id = deployId.ToString();
        Assert.DoesNotContain(id, await peer.Client.GetStringAsync("/api/follow-ups/assignees"), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(id, await peer.Client.GetStringAsync("/api/messages/recipients"), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(id, await peer.Client.GetStringAsync("/api/config/staff?includeInactive=true"), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(id, await peer.Client.GetStringAsync("/api/config/providers?includeInactive=true"), StringComparison.OrdinalIgnoreCase);

        // Still visible in Security Administration, labelled by kind.
        var users = await JsonAsync(await peer.Client.GetAsync("/api/auth/users"));
        Assert.Equal("DeploymentAdministrator", users.EnumerateArray().Single(u => u.GetProperty("id").GetGuid() == deployId).GetProperty("accountKind").GetString());

        // Attribution names it by kind, never its username.
        var names = await WithDbAsync(db => Alveara.Api.Architecture.Clinical.ClinicalNames.ResolveAsync(db, [deployId], default));
        Assert.Equal("Deployment Administrator", names[deployId]);
    }

    // ---------- S11: the Practice Owner ----------

    [Fact]
    public async Task The_practice_owner_is_designated_once_to_an_active_staff_member_with_an_enabled_login()
    {
        var deployOwnerId = await SeedAccountAsync("deploy.owner", Role.Admin, AccountKind.DeploymentAdministrator);
        var ownerAccount = await SeedAccountAsync("dr.owner", Role.Dentist, staffName: "Dr Owner");
        var ownerStaff = await WithDbAsync(db => db.StaffProfiles.Where(s => s.UserAccountId == ownerAccount).Select(s => s.Id).SingleAsync());
        var noLogin = await SeedProfileAsync("No Login");
        var inactiveAccount = await SeedAccountAsync("gone.dr", Role.Dentist, staffName: "Gone Dr", staffActive: false);
        var inactiveStaff = await WithDbAsync(db => db.StaffProfiles.Where(s => s.UserAccountId == inactiveAccount).Select(s => s.Id).SingleAsync());
        await SeedAccountAsync("om.owner", Role.OfficeManager, staffName: "OM Owner");
        var deploy = await SignInAsync("deploy.owner");
        var manager = await SignInAsync("om.owner");

        Assert.Equal(HttpStatusCode.Forbidden, (await manager.SendAsync(HttpMethod.Post, "/api/practice-owner/initial", new DesignatePracticeOwnerRequest(ownerStaff, true))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.Client.GetAsync("/api/practice-owner")).StatusCode);
        Assert.Equal(JsonValueKind.Null, (await JsonAsync(await deploy.Client.GetAsync("/api/practice-owner"))).GetProperty("owner").ValueKind);

        Assert.Equal("confirmation_required", await ErrorAsync(await deploy.SendAsync(HttpMethod.Post, "/api/practice-owner/initial", new DesignatePracticeOwnerRequest(ownerStaff, false))));
        // ALV-N030-C01 (brief 18.3 E1): an otherwise eligible staff member whose login is not an Administrator is refused
        Assert.Equal("owner_must_be_administrator", await ErrorAsync(await deploy.SendAsync(HttpMethod.Post, "/api/practice-owner/initial", new DesignatePracticeOwnerRequest(ownerStaff, true))));
        Assert.Equal("owner_target_invalid", await ErrorAsync(await deploy.SendAsync(HttpMethod.Post, "/api/practice-owner/initial", new DesignatePracticeOwnerRequest(noLogin, true))));
        Assert.Equal("owner_target_invalid", await ErrorAsync(await deploy.SendAsync(HttpMethod.Post, "/api/practice-owner/initial", new DesignatePracticeOwnerRequest(inactiveStaff, true))));
        // A Deployment Administrator account can never be the owner, even if a profile pointed at it (a state no route creates).
        // (the database allows one Deployment Administrator, so the profile points at the existing one)
        var kindStaff = await SeedProfileAsync("Kind Check", accountId: deployOwnerId);
        Assert.Equal(HttpStatusCode.Conflict, (await deploy.SendAsync(HttpMethod.Post, "/api/practice-owner/initial", new DesignatePracticeOwnerRequest(kindStaff, true))).StatusCode);
        Assert.Equal(0, await WithDbAsync(db => db.PracticeOwnerDesignations.CountAsync()));
    }

    [Fact]
    public async Task The_first_designation_succeeds_and_any_second_one_is_refused()
    {
        await SeedAccountAsync("deploy.first", Role.Admin, AccountKind.DeploymentAdministrator);
        var ownerAccount = await SeedAccountAsync("dr.first", Role.Admin, staffName: "Dr First");   // ALV-N030-C01: the owner signs in as an Administrator
        var ownerStaff = await WithDbAsync(db => db.StaffProfiles.Where(s => s.UserAccountId == ownerAccount).Select(s => s.Id).SingleAsync());
        var otherAccount = await SeedAccountAsync("dr.second", Role.Dentist, staffName: "Dr Second");
        var otherStaff = await WithDbAsync(db => db.StaffProfiles.Where(s => s.UserAccountId == otherAccount).Select(s => s.Id).SingleAsync());
        await SeedAccountAsync("admin.later", Role.Admin, staffName: "Admin Later");
        var deploy = await SignInAsync("deploy.first");

        var designated = await deploy.SendAsync(HttpMethod.Post, "/api/practice-owner/initial", new DesignatePracticeOwnerRequest(ownerStaff, true));
        Assert.Equal(HttpStatusCode.Created, designated.StatusCode);
        Assert.Equal("InitialByDeploymentAdministrator", (await JsonAsync(designated)).GetProperty("method").GetString());
        Assert.Equal("Dr First", (await JsonAsync(await deploy.Client.GetAsync("/api/practice-owner"))).GetProperty("owner").GetProperty("displayName").GetString());

        var second = await deploy.SendAsync(HttpMethod.Post, "/api/practice-owner/initial", new DesignatePracticeOwnerRequest(otherStaff, true));
        Assert.Equal("owner_already_designated", await ErrorAsync(second));
        var byAdmin = await (await SignInAsync("admin.later")).SendAsync(HttpMethod.Post, "/api/practice-owner/initial", new DesignatePracticeOwnerRequest(otherStaff, true));
        Assert.Equal(HttpStatusCode.Conflict, byAdmin.StatusCode);
        Assert.Equal(1, await WithDbAsync(db => db.PracticeOwnerDesignations.CountAsync()));
        Assert.Equal(1, await WithDbAsync(db => db.AuditLogEntries.CountAsync(a => a.EventType == AuditEventTypes.PracticeOwnerDesignated)));

        var owner = await SignInAsync("dr.first");
        var me = await JsonAsync(await owner.Client.GetAsync("/api/auth/permissions"));
        Assert.True(me.GetProperty("isPracticeOwner").GetBoolean());
        Assert.True(me.GetProperty("practiceOwnerExists").GetBoolean());
        var other = await JsonAsync(await (await SignInAsync("dr.second")).Client.GetAsync("/api/auth/permissions"));
        Assert.False(other.GetProperty("isPracticeOwner").GetBoolean());
        Assert.True(other.GetProperty("practiceOwnerExists").GetBoolean());
    }

    [Fact]
    public async Task On_an_upgraded_install_an_active_administrator_designates_by_the_upgrade_transition()
    {
        await SeedAccountAsync("admin.upgrade", Role.Admin, staffName: "Admin Upgrade");
        var ownerAccount = await SeedAccountAsync("dr.upgrade", Role.Admin, staffName: "Dr Upgrade");   // ALV-N030-C01: the owner signs in as an Administrator
        var ownerStaff = await WithDbAsync(db => db.StaffProfiles.Where(s => s.UserAccountId == ownerAccount).Select(s => s.Id).SingleAsync());
        var admin = await SignInAsync("admin.upgrade");
        var designated = await admin.SendAsync(HttpMethod.Post, "/api/practice-owner/initial", new DesignatePracticeOwnerRequest(ownerStaff, true));
        Assert.Equal(HttpStatusCode.Created, designated.StatusCode);
        Assert.Equal("UpgradeTransition", (await JsonAsync(designated)).GetProperty("method").GetString());
    }

    // ---------- S17: a lost temporary password ----------

    [Fact]
    public async Task A_lost_temporary_password_is_recovered_by_the_administrator_reset_not_by_a_replay()
    {
        await SeedAccountAsync("admin.s17", Role.Admin, staffName: "Admin S17");
        await SeedAccountAsync("om.s17", Role.OfficeManager, staffName: "OM S17");
        var admin = await SignInAsync("admin.s17");
        var created = await JsonAsync(await admin.SendAsync(HttpMethod.Post, "/api/config/staff-members", Member("Lost Temp", "lost.temp"), key: "s17"));
        var accountId = created.GetProperty("userAccountId").GetGuid();
        var temporary = created.GetProperty("temporaryPassword").GetString()!;

        Assert.False((await JsonAsync(await admin.SendAsync(HttpMethod.Post, "/api/config/staff-members", Member("Lost Temp", "lost.temp"), key: "s17"))).TryGetProperty("temporaryPassword", out _));

        var manager = await SignInAsync("om.s17");
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.SendAsync(HttpMethod.Post, $"/api/auth/{accountId}/reset-password", new { })).StatusCode);

        var stampBefore = await WithDbAsync(db => db.UserAccounts.Where(u => u.Id == accountId).Select(u => u.SecurityStamp).SingleAsync());
        var token = (await JsonAsync(await admin.SendAsync(HttpMethod.Post, $"/api/auth/{accountId}/reset-password", new { }))).GetProperty("token").GetString()!;
        Assert.True(await WithDbAsync(db => db.UserAccounts.Where(u => u.Id == accountId).Select(u => u.MustChangePassword).SingleAsync()));   // issuing alone changes nothing
        var complete = await _factory.CreateClient().PostAsJsonAsync("/api/auth/reset-password/complete", new CompleteResetRequest(accountId, token, "Own-Chosen-Password-9"));
        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);

        var after = await WithDbAsync(db => db.UserAccounts.AsNoTracking().SingleAsync(u => u.Id == accountId));
        Assert.False(after.MustChangePassword);
        Assert.NotEqual(stampBefore, after.SecurityStamp);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().PostAsJsonAsync("/api/auth/login", new { username = "lost.temp", password = temporary })).StatusCode);
        var recovered = await SignInAsync("lost.temp", "Own-Chosen-Password-9");
        Assert.NotEqual(HttpStatusCode.Forbidden, (await recovered.Client.GetAsync("/api/patients")).StatusCode);
    }
}
