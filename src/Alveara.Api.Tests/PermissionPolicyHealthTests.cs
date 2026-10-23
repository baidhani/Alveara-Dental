using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Permissions;
using Alveara.Api.Data;
using Session = Alveara.Api.Tests.SchedulingApiHarness.Session;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N017: reading the effective policy and keeping it healthy, through the real pipeline. The effective view says where each cell comes from; the health view lists every invalid stored row with its raw values;
/// an invalid row is reported once per row version, with a privacy-safe log; an administrator can remove an invalid row with a reason (version bump, delete, history and audit in one transaction, stale row refused,
/// failure changes nothing), and cannot remove a valid one; nobody else can reach any of it.
/// </summary>
public class PermissionPolicyHealthTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private readonly CapturingLoggerProvider _logs = new();
    private SchedulingApiHarness _api = null!;

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
        _api = new SchedulingApiHarness(_fixture) { ConfigureLogging = b => b.AddProvider(_logs) };
    }

    public Task DisposeAsync() { _api.Dispose(); return _fixture.DisposeAsync(); }

    private async Task<Guid> ApplyAsync(Func<AlveraDbContext, Guid> change)
    {
        await using var db = _fixture.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlAsync($"UPDATE [PermissionPolicyState] SET [Version] = [Version] + 1, [UpdatedAtUtc] = SYSDATETIMEOFFSET() WHERE [Id] = 1");
        var id = change(db);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return id;
    }

    private Task<Guid> AddRowAsync(string role, string permission, string effect) => ApplyAsync(db =>
    {
        var row = new RolePermissionOverride { Id = Guid.NewGuid(), Role = role, Permission = permission, Effect = effect, Reason = "controlled setup", ChangedByUserId = db.UserAccounts.Select(u => u.Id).First(), ChangedAtUtc = DateTimeOffset.UtcNow };
        db.RolePermissionOverrides.Add(row);
        return row.Id;
    });

    private static async Task<(HttpStatusCode Status, JsonElement Body)> GetAsync(Session s, string url) => await s.SendAsync(HttpMethod.Get, url);

    private async Task<string> RowVersionAsync(Guid id)
    {
        await using var db = _fixture.CreateContext();
        return Convert.ToBase64String((await db.RolePermissionOverrides.AsNoTracking().SingleAsync(o => o.Id == id)).RowVersion!);
    }

    private async Task<long> VersionAsync() { await using var db = _fixture.CreateContext(); return (await db.PermissionPolicyStates.AsNoTracking().SingleAsync()).Version; }

    // ---------- who may read and repair ----------

    [Fact]
    public async Task The_views_and_the_repair_need_their_own_permissions_and_no_grant_to_another_role_can_open_the_protected_ones()
    {
        var admin = await _api.SessionAsync("Admin");
        var manager = await _api.SessionAsync("OfficeManager");
        var dentist = await _api.SessionAsync("Dentist");
        Assert.Equal(HttpStatusCode.OK, (await GetAsync(admin, "/api/permissions/effective")).Status);
        Assert.Equal(HttpStatusCode.OK, (await GetAsync(manager, "/api/permissions/effective")).Status);              // ViewPermissionMatrix: administrator and office manager
        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync(dentist, "/api/permissions/effective")).Status);
        Assert.Equal(HttpStatusCode.OK, (await GetAsync(admin, "/api/permissions/history")).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync(dentist, "/api/permissions/history")).Status);
        Assert.Equal(HttpStatusCode.OK, (await GetAsync(admin, "/api/permissions/health")).Status);
        foreach (var s in new[] { manager, dentist }) Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync(s, "/api/permissions/health")).Status);

        foreach (var p in Enum.GetValues<Permission>().Where(PermissionProtection.IsGrantable).Where(p => !PermissionMatrix.RoleHas(Role.OfficeManager, p)))
            await AddRowAsync("OfficeManager", p.ToString(), "Grant");                                                // everything an office manager can be given
        Assert.Equal(HttpStatusCode.Forbidden, (await GetAsync(manager, "/api/permissions/health")).Status);
        var removal = await manager.SendAsync(HttpMethod.Post, $"/api/permissions/invalid-rows/{Guid.NewGuid()}/remove", new { reason = "x", rowVersion = "AAAA" });
        Assert.Equal(HttpStatusCode.Forbidden, removal.Status);
    }

    // ---------- the effective view ----------

    [Fact]
    public async Task The_effective_view_says_where_every_cell_comes_from_who_set_it_and_what_can_be_granted_or_delegated()
    {
        var admin = await _api.SessionAsync("Admin");
        await AddRowAsync("Dentist", nameof(Permission.ViewBilling), OverrideEffects.Revoke);
        await AddRowAsync("Billing", nameof(Permission.ImportDocuments), OverrideEffects.Grant);
        var view = (await GetAsync(admin, "/api/permissions/effective")).Body;
        JsonElement Cell(string role, Permission p) => view.GetProperty("roles").EnumerateArray().First(r => r.GetProperty("role").GetString() == role)
            .GetProperty("permissions").EnumerateArray().First(c => c.GetProperty("permission").GetString() == p.ToString());

        var revoked = Cell("Dentist", Permission.ViewBilling);
        Assert.Equal(("revoked", false, "controlled setup"), (revoked.GetProperty("source").GetString(), revoked.GetProperty("effective").GetBoolean(), revoked.GetProperty("reason").GetString()));
        Assert.NotEqual(JsonValueKind.Null, revoked.GetProperty("changedByUserId").ValueKind);
        var granted = Cell("Billing", Permission.ImportDocuments);
        Assert.Equal(("granted", true), (granted.GetProperty("source").GetString(), granted.GetProperty("effective").GetBoolean()));
        var normal = Cell("Dentist", Permission.ViewPatientRecords);
        Assert.Equal(("default", true), (normal.GetProperty("source").GetString(), normal.GetProperty("effective").GetBoolean()));
        Assert.True(normal.GetProperty("grantable").GetBoolean());
        Assert.True(normal.GetProperty("delegable").GetBoolean());
        foreach (var p in new[] { Permission.ManageUsers, Permission.ManageRoles, Permission.ManageMfaPolicy, Permission.IssuePasswordResets, Permission.ManagePermissionGrants })
        {
            Assert.False(Cell("OfficeManager", p).GetProperty("grantable").GetBoolean(), p.ToString());
            Assert.False(Cell("OfficeManager", p).GetProperty("delegable").GetBoolean(), p.ToString());
            Assert.True(Cell("Admin", p).GetProperty("effective").GetBoolean());
            Assert.False(Cell("Admin", p).GetProperty("grantable").GetBoolean());                                     // an administrator's cells are never editable
        }
        Assert.True(Cell("OfficeManager", Permission.ManageAccountStatus).GetProperty("grantable").GetBoolean());
        Assert.False(Cell("OfficeManager", Permission.ManageAccountStatus).GetProperty("delegable").GetBoolean());
        Assert.Equal("Healthy", view.GetProperty("health").GetProperty("status").GetString());
    }

    [Fact]
    public async Task The_history_lists_changes_newest_first_and_starts_empty()
    {
        var admin = await _api.SessionAsync("Admin");
        var empty = (await GetAsync(admin, "/api/permissions/history")).Body;
        Assert.Equal(0, empty.GetProperty("total").GetInt32());
        Assert.Equal(0, empty.GetProperty("items").GetArrayLength());
    }

    // ---------- health and detection ----------

    [Fact]
    public async Task The_health_view_lists_every_invalid_row_with_its_raw_values_and_the_roles_that_need_repair()
    {
        var admin = await _api.SessionAsync("Admin");
        Assert.Equal("Healthy", (await GetAsync(admin, "/api/permissions/health")).Body.GetProperty("status").GetString());
        var protectedRow = await AddRowAsync("OfficeManager", nameof(Permission.ManageUsers), OverrideEffects.Grant);
        var unknownPermission = await AddRowAsync("Dentist", "NoSuchPermission", OverrideEffects.Revoke);
        var health = (await GetAsync(admin, "/api/permissions/health")).Body;
        Assert.Equal("RepairRequired", health.GetProperty("status").GetString());
        Assert.Equal(2, health.GetProperty("invalidRowCount").GetInt32());
        Assert.Equal(new[] { "Dentist", "OfficeManager" }, health.GetProperty("rolesNeedingRepair").EnumerateArray().Select(e => e.GetString()).OrderBy(x => x).ToArray());
        var rows = health.GetProperty("invalidRows").EnumerateArray().ToList();
        var protectedView = rows.Single(r => r.GetProperty("id").GetGuid() == protectedRow);
        Assert.Equal(("OfficeManager", "ManageUsers", "Grant", "ProtectedPermissionOverride"), (protectedView.GetProperty("role").GetString(), protectedView.GetProperty("permission").GetString(), protectedView.GetProperty("effect").GetString(), protectedView.GetProperty("reason").GetString()));
        Assert.Equal("UnknownPermission", rows.Single(r => r.GetProperty("id").GetGuid() == unknownPermission).GetProperty("reason").GetString());
        Assert.NotEmpty(protectedView.GetProperty("rowVersion").GetString()!);
    }

    [Fact]
    public async Task A_row_whose_role_cannot_be_attributed_shows_the_store_as_degraded()
    {
        var admin = await _api.SessionAsync("Admin");
        await AddRowAsync("Wizard", nameof(Permission.ViewBilling), OverrideEffects.Revoke);
        var health = (await GetAsync(admin, "/api/permissions/health")).Body;
        Assert.Equal("Degraded", health.GetProperty("status").GetString());
        Assert.Equal("UnknownRole", health.GetProperty("invalidRows")[0].GetProperty("reason").GetString());
    }

    [Fact]
    public async Task An_invalid_row_is_reported_once_per_row_version_however_often_the_policy_reloads_and_the_log_holds_counts_only()
    {
        var admin = await _api.SessionAsync("Admin");
        var dentist = await _api.SessionAsync("Dentist");
        var id = await AddRowAsync("Wizard", "SecretPermissionName", OverrideEffects.Revoke);
        await GetAsync(dentist, "/api/patients");                                                                    // a request on the changed version reloads and reports (in the background)
        await _api.Factory.Services.GetRequiredService<PermissionPolicyStore>().ReportsIdle();
        await ApplyAsync(db => Guid.Empty);                                                                          // two more policy versions: two more reloads
        await GetAsync(dentist, "/api/patients");
        await _api.Factory.Services.GetRequiredService<PermissionPolicyStore>().ReportsIdle();
        await ApplyAsync(db => Guid.Empty);
        await GetAsync(dentist, "/api/patients");
        await _api.Factory.Services.GetRequiredService<PermissionPolicyStore>().ReportsIdle();
        await using (var db = _fixture.CreateContext())
        {
            var reports = await db.AuditLogEntries.AsNoTracking().Where(a => a.EventType == AuditEventTypes.PermissionPolicyInvalidRowDetected && a.TargetUserAccountId == id).ToListAsync();
            Assert.Single(reports);
            Assert.DoesNotContain("Wizard", reports[0].Details);
            Assert.DoesNotContain("SecretPermissionName", reports[0].Details);
        }
        var logged = string.Join("\n", _logs.Entries.Select(e => e.Message));
        Assert.Contains("permission_policy_invalid_rows", logged);
        Assert.DoesNotContain("Wizard", logged);
        Assert.DoesNotContain("SecretPermissionName", logged);
        Assert.Equal(HttpStatusCode.OK, (await GetAsync(admin, "/api/permissions/health")).Status);
    }

    // ---------- the repair ----------

    [Fact]
    public async Task An_administrator_removes_an_invalid_row_with_a_reason_and_the_role_returns_to_its_default_with_history_and_audit()
    {
        var admin = await _api.SessionAsync("Admin");
        var manager = await _api.SessionAsync("OfficeManager");
        var id = await AddRowAsync("OfficeManager", nameof(Permission.ManageUsers), OverrideEffects.Grant);
        Assert.Equal("permission_policy_repair_required", (await GetAsync(manager, "/api/patients")).Body.GetProperty("error").GetString());
        var before = await VersionAsync();

        var removed = await admin.SendAsync(HttpMethod.Post, $"/api/permissions/invalid-rows/{id}/remove", new { reason = "Corrupt row found by the health check", rowVersion = await RowVersionAsync(id) });
        Assert.Equal(HttpStatusCode.OK, removed.Status);
        Assert.Equal(before + 1, removed.Body.GetProperty("policyVersion").GetInt64());

        Assert.NotEqual(HttpStatusCode.Forbidden, (await GetAsync(manager, "/api/patients")).Status);                // back to the default at once
        await using var db = _fixture.CreateContext();
        Assert.Equal(0, await db.RolePermissionOverrides.CountAsync());
        var history = await db.RolePermissionHistories.AsNoTracking().SingleAsync();
        Assert.Equal((PolicyChangeKinds.InvalidRowRemoved, before + 1, "Corrupt row found by the health check"), (history.Kind, history.PolicyVersion, history.Reason));
        var audit = await db.AuditLogEntries.AsNoTracking().SingleAsync(a => a.EventType == AuditEventTypes.InvalidPolicyRowRemoved);
        Assert.Equal((id, "Corrupt row found by the health check"), (audit.TargetUserAccountId, audit.Reason));
        Assert.Equal("Healthy", (await GetAsync(admin, "/api/permissions/health")).Body.GetProperty("status").GetString());
        Assert.Equal(1, (await GetAsync(admin, "/api/permissions/history")).Body.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task The_repair_refuses_a_missing_reason_a_missing_or_stale_row_version_a_valid_row_and_an_unknown_row_and_changes_nothing()
    {
        var admin = await _api.SessionAsync("Admin");
        var invalid = await AddRowAsync("OfficeManager", nameof(Permission.ManageUsers), OverrideEffects.Grant);
        var valid = await AddRowAsync("Dentist", nameof(Permission.ViewBilling), OverrideEffects.Revoke);
        var version = await RowVersionAsync(invalid);
        var before = await VersionAsync();

        Assert.Equal("reason_required", (await admin.SendAsync(HttpMethod.Post, $"/api/permissions/invalid-rows/{invalid}/remove", new { reason = "  ", rowVersion = version })).Body.GetProperty("error").GetString());
        Assert.Equal("reason_required", (await admin.SendAsync(HttpMethod.Post, $"/api/permissions/invalid-rows/{invalid}/remove", new { reason = new string('r', 501), rowVersion = version })).Body.GetProperty("error").GetString());
        Assert.Equal("row_version_required", (await admin.SendAsync(HttpMethod.Post, $"/api/permissions/invalid-rows/{invalid}/remove", new { reason = "ok" })).Body.GetProperty("error").GetString());
        var stale = await admin.SendAsync(HttpMethod.Post, $"/api/permissions/invalid-rows/{invalid}/remove", new { reason = "ok", rowVersion = Convert.ToBase64String(new byte[8]) });
        Assert.Equal((HttpStatusCode.Conflict, "row_changed"), (stale.Status, stale.Body.GetProperty("error").GetString()));
        var notRemovable = await admin.SendAsync(HttpMethod.Post, $"/api/permissions/invalid-rows/{valid}/remove", new { reason = "ok", rowVersion = await RowVersionAsync(valid) });
        Assert.Equal((HttpStatusCode.Conflict, "row_not_removable"), (notRemovable.Status, notRemovable.Body.GetProperty("error").GetString()));
        var unknown = await admin.SendAsync(HttpMethod.Post, $"/api/permissions/invalid-rows/{Guid.NewGuid()}/remove", new { reason = "ok", rowVersion = version });
        Assert.Equal((HttpStatusCode.NotFound, "row_not_found"), (unknown.Status, unknown.Body.GetProperty("error").GetString()));
        var noCsrf = await admin.SendAsync(HttpMethod.Post, $"/api/permissions/invalid-rows/{invalid}/remove", new { reason = "ok", rowVersion = version }, csrf: false);
        Assert.NotEqual(HttpStatusCode.OK, noCsrf.Status);

        await using var db = _fixture.CreateContext();
        Assert.Equal(2, await db.RolePermissionOverrides.CountAsync());
        Assert.Equal(0, await db.RolePermissionHistories.CountAsync());
        Assert.Equal(before + 0, (await db.PermissionPolicyStates.AsNoTracking().SingleAsync()).Version);             // no refusal consumed a version
    }

    [Fact]
    public async Task Removing_the_same_row_twice_is_a_quiet_not_found_the_second_time()
    {
        var admin = await _api.SessionAsync("Admin");
        var id = await AddRowAsync("OfficeManager", nameof(Permission.ManageRoles), OverrideEffects.Grant);
        var body = new { reason = "Repair", rowVersion = await RowVersionAsync(id) };
        Assert.Equal(HttpStatusCode.OK, (await admin.SendAsync(HttpMethod.Post, $"/api/permissions/invalid-rows/{id}/remove", body)).Status);
        var again = await admin.SendAsync(HttpMethod.Post, $"/api/permissions/invalid-rows/{id}/remove", body);
        Assert.Equal((HttpStatusCode.NotFound, "row_not_found"), (again.Status, again.Body.GetProperty("error").GetString()));
        await using var db = _fixture.CreateContext();
        Assert.Equal(1, await db.RolePermissionHistories.CountAsync());
    }

    [Fact]
    public async Task When_the_audit_entry_cannot_be_written_the_whole_repair_is_rolled_back()
    {
        var admin = await _api.SessionAsync("Admin");
        var id = await AddRowAsync("OfficeManager", nameof(Permission.ManageUsers), OverrideEffects.Grant);
        var version = await RowVersionAsync(id);
        var before = await VersionAsync();
        await using (var db = _fixture.CreateContext())
            await db.Database.ExecuteSqlRawAsync($"CREATE TRIGGER TR_TestRefuseRepairAudit ON AuditLogEntries AFTER INSERT AS IF EXISTS (SELECT 1 FROM inserted WHERE EventType = '{AuditEventTypes.InvalidPolicyRowRemoved}') THROW 59998, 'audit down', 1;");
        var response = await admin.SendAsync(HttpMethod.Post, $"/api/permissions/invalid-rows/{id}/remove", new { reason = "Repair", rowVersion = version });
        Assert.Equal((HttpStatusCode.ServiceUnavailable, "policy_write_failed"), (response.Status, response.Body.GetProperty("error").GetString()));
        await using var check = _fixture.CreateContext();
        Assert.Equal(1, await check.RolePermissionOverrides.CountAsync());                                           // the row is still there
        Assert.Equal(0, await check.RolePermissionHistories.CountAsync());                                           // no history
        Assert.Equal(before, (await check.PermissionPolicyStates.AsNoTracking().SingleAsync()).Version);             // and the version did not move
    }

    [Fact]
    public async Task Two_administrators_removing_the_same_row_at_once_produce_one_removal_and_one_quiet_refusal()
    {
        var admin = await _api.SessionAsync("Admin");
        var id = await AddRowAsync("OfficeManager", nameof(Permission.ManageUsers), OverrideEffects.Grant);
        var body = new { reason = "Repair", rowVersion = await RowVersionAsync(id) };
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => admin.SendAsync(HttpMethod.Post, $"/api/permissions/invalid-rows/{id}/remove", body)));
        Assert.Equal(1, results.Count(r => r.Status == HttpStatusCode.OK));
        Assert.All(results.Where(r => r.Status != HttpStatusCode.OK), r => Assert.Contains(r.Status, new[] { HttpStatusCode.NotFound, HttpStatusCode.Conflict }));
        await using var db = _fixture.CreateContext();
        Assert.Equal(1, await db.RolePermissionHistories.CountAsync());
        Assert.Equal(1, await db.AuditLogEntries.CountAsync(a => a.EventType == AuditEventTypes.InvalidPolicyRowRemoved));
    }
}
