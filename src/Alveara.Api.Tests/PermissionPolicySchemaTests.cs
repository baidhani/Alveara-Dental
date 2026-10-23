using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Permissions;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N017, the policy tables on their own: rows written straight to the database, around any service. Policy history is append-only (51204); the policy version only moves forward by one and its row is never
/// deleted (51205); the closed sets that can be closed are (the effect of an override, the kind of a history row, the one state row); persisted role and permission values are deliberately NOT constrained so
/// that wrong data is representable and the engine's refusal to restore access on it can be proven; the five default delegations exist; and the migration rolls back and forward while no policy change has been
/// recorded and refuses to roll back over a history row (51206).
/// </summary>
public class PermissionPolicySchemaTests : SafetyTestBase
{
    private const string BeforePolicy = "20261023035521_AddNotificationCenter";

    private async Task AddAsync(params object[] rows)
    {
        await using var db = Fixture.CreateContext();
        db.AddRange(rows);
        await db.SaveChangesAsync();
    }

    private static async Task<Microsoft.Data.SqlClient.SqlException> Refusal(Func<Task> action)
    {
        var e = await Assert.ThrowsAnyAsync<Exception>(action);
        return Assert.IsType<Microsoft.Data.SqlClient.SqlException>(e is DbUpdateException { InnerException: not null } d ? d.InnerException : e);
    }

    private async Task<Microsoft.Data.SqlClient.SqlException> SqlRefusalAsync(FormattableString sql)
    {
        await using var db = Fixture.CreateContext();
        return await Refusal(() => db.Database.ExecuteSqlAsync(sql));
    }

    private RolePermissionOverride Override(string role = "Dentist", string permission = "ViewBilling", string effect = OverrideEffects.Revoke, string reason = "Test reason") =>
        new() { Id = Guid.NewGuid(), Role = role, Permission = permission, Effect = effect, Reason = reason, ChangedByUserId = S.Actor, ChangedAtUtc = DateTimeOffset.UtcNow };

    private RolePermissionHistory History(long version = 1, string kind = PolicyChangeKinds.Revoked) =>
        new() { Id = Guid.NewGuid(), PolicyVersion = version, Kind = kind, Role = "Dentist", Permission = "ViewBilling", OldEffective = true, NewEffective = false, Reason = "Test reason", ChangedByUserId = S.Actor, ChangedAtUtc = DateTimeOffset.UtcNow };

    [Fact]
    public async Task A_fresh_database_has_policy_version_zero_no_overrides_no_history_and_the_five_default_delegations()
    {
        await using var db = Fixture.CreateContext();
        var state = await db.PermissionPolicyStates.AsNoTracking().SingleAsync();
        Assert.Equal((1, 0L), (state.Id, state.Version));
        Assert.Equal(0, await db.RolePermissionOverrides.CountAsync());
        Assert.Equal(0, await db.RolePermissionHistories.CountAsync());
        var delegations = await db.PermissionDelegations.AsNoTracking().ToListAsync();
        Assert.Equal(5, delegations.Count);
        Assert.All(delegations, d => Assert.Equal(("OfficeManager", "ImportDocuments"), (d.DelegateRole, d.Permission)));
        Assert.Equal(new[] { "Assistant", "Billing", "Dentist", "FrontDesk", "Hygienist" }, delegations.Select(d => d.TargetRole).OrderBy(x => x).ToArray());
        Assert.All(delegations, d => Assert.True(PermissionRegistry.IsActive(d.Permission) && Enum.TryParse<Role>(d.TargetRole, out var t) && PermissionProtection.IsDelegationTarget(t)));
    }

    [Fact]
    public async Task Policy_history_is_append_only()
    {
        await AddAsync(History());
        var update = await SqlRefusalAsync($"UPDATE [RolePermissionHistories] SET [Reason] = N'rewritten'");
        Assert.Equal(51204, update.Number);
        var delete = await SqlRefusalAsync($"DELETE FROM [RolePermissionHistories]");
        Assert.Equal(51204, delete.Number);
        await using var db = Fixture.CreateContext();
        Assert.Equal("Test reason", (await db.RolePermissionHistories.AsNoTracking().SingleAsync()).Reason);
    }

    [Fact]
    public async Task The_policy_version_moves_forward_by_one_only_and_its_row_is_never_deleted()
    {
        await using (var db = Fixture.CreateContext()) Assert.Equal(1, await db.Database.ExecuteSqlAsync($"UPDATE [PermissionPolicyState] SET [Version] = [Version] + 1, [UpdatedAtUtc] = SYSDATETIMEOFFSET() WHERE [Id] = 1"));
        Assert.Equal(51205, (await SqlRefusalAsync($"UPDATE [PermissionPolicyState] SET [Version] = [Version] + 2 WHERE [Id] = 1")).Number);
        Assert.Equal(51205, (await SqlRefusalAsync($"UPDATE [PermissionPolicyState] SET [Version] = [Version] WHERE [Id] = 1")).Number);
        Assert.Equal(51205, (await SqlRefusalAsync($"UPDATE [PermissionPolicyState] SET [Version] = 0 WHERE [Id] = 1")).Number);
        Assert.Equal(51205, (await SqlRefusalAsync($"DELETE FROM [PermissionPolicyState]")).Number);
        await using var check = Fixture.CreateContext();
        Assert.Equal(1L, (await check.PermissionPolicyStates.AsNoTracking().SingleAsync()).Version);
    }

    [Fact]
    public async Task There_is_exactly_one_policy_state_row()
    {
        var e = await SqlRefusalAsync($"INSERT INTO [PermissionPolicyState] ([Id], [Version], [UpdatedAtUtc]) VALUES (2, 0, SYSDATETIMEOFFSET())");
        Assert.True(e.Number is 547 or 2627, e.Message);
    }

    [Theory]
    [InlineData("Allow")]
    [InlineData("grant")]
    [InlineData("")]
    public async Task An_override_must_be_a_grant_or_a_revoke(string effect)
    {
        var e = await Refusal(() => AddAsync(Override(effect: effect)));
        Assert.Equal(547, e.Number);
    }

    [Fact]
    public async Task An_override_needs_a_reason_and_there_is_one_per_role_and_permission()
    {
        Assert.Equal(547, (await Refusal(() => AddAsync(Override(reason: "   ")))).Number);
        await AddAsync(Override());
        var duplicate = await Refusal(() => AddAsync(Override(effect: OverrideEffects.Grant)));
        Assert.Equal(2601, duplicate.Number);
    }

    [Fact]
    public async Task A_delegation_is_unique_per_delegate_permission_and_target()
    {
        var dup = new PermissionDelegation { Id = Guid.NewGuid(), DelegateRole = "OfficeManager", Permission = "ImportDocuments", TargetRole = "Dentist", ChangedAtUtc = DateTimeOffset.UtcNow };
        Assert.Equal(2601, (await Refusal(() => AddAsync(dup))).Number);
    }

    [Fact]
    public async Task A_history_row_has_a_known_kind_a_positive_version_and_a_reason()
    {
        Assert.Equal(547, (await Refusal(() => AddAsync(History(kind: "Changed")))).Number);
        Assert.Equal(547, (await Refusal(() => AddAsync(History(version: 0)))).Number);
        var blank = History(); blank.Reason = " ";
        Assert.Equal(547, (await Refusal(() => AddAsync(blank))).Number);
    }

    [Fact]
    public async Task Wrong_role_permission_and_ordinal_looking_values_can_be_stored_so_the_engine_can_be_proven_to_refuse_them()
    {
        await AddAsync(Override(role: "Wizard", permission: "ViewBilling"), Override(role: "Dentist", permission: "NoSuchPermission"), Override(role: "Admin", permission: "ViewBilling"), Override(role: "Dentist", permission: "17"));
        await using var db = Fixture.CreateContext();
        Assert.Equal(4, await db.RolePermissionOverrides.CountAsync());
    }

    [Fact]
    public async Task A_policy_change_can_be_rolled_back_in_one_transaction_so_row_and_version_never_part()
    {
        await using (var db = Fixture.CreateContext())
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            await db.Database.ExecuteSqlAsync($"UPDATE [PermissionPolicyState] SET [Version] = [Version] + 1 WHERE [Id] = 1");
            db.RolePermissionOverrides.Add(Override());
            db.RolePermissionHistories.Add(History());
            await db.SaveChangesAsync();
            await tx.RollbackAsync();
        }
        await using var check = Fixture.CreateContext();
        Assert.Equal((0L, 0, 0), ((await check.PermissionPolicyStates.AsNoTracking().SingleAsync()).Version, await check.RolePermissionOverrides.CountAsync(), await check.RolePermissionHistories.CountAsync()));
    }

    [Fact]
    public async Task The_migration_rolls_back_and_forward_while_no_change_was_recorded_and_refuses_to_roll_back_over_a_history_row()
    {
        await using (var db = Fixture.CreateContext()) await db.GetInfrastructure().GetRequiredService<IMigrator>().MigrateAsync(BeforePolicy);
        await using (var db = Fixture.CreateContext())
            Assert.Equal(0, await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM sys.tables WHERE name IN ('RolePermissionOverrides','RolePermissionHistories','PermissionDelegations','PermissionPolicyState')").SingleAsync());
        await using (var db = Fixture.CreateContext()) await db.Database.MigrateAsync();
        await using (var db = Fixture.CreateContext())
        {
            Assert.Equal(4, await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM sys.tables WHERE name IN ('RolePermissionOverrides','RolePermissionHistories','PermissionDelegations','PermissionPolicyState')").SingleAsync());
            Assert.Equal(2, await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM sys.triggers WHERE name IN ('TR_RolePermissionHistories_Immutable','TR_PermissionPolicyState_Guard')").SingleAsync());
            Assert.Equal((1, 5), (await db.PermissionPolicyStates.CountAsync(), await db.PermissionDelegations.CountAsync()));   // seeded again after the roll forward
        }

        await AddAsync(History());
        var e = await Refusal(async () => { await using var db = Fixture.CreateContext(); await db.GetInfrastructure().GetRequiredService<IMigrator>().MigrateAsync(BeforePolicy); });
        Assert.Equal(51206, e.Number);
        await using var check = Fixture.CreateContext();
        Assert.Equal(1, await check.RolePermissionHistories.CountAsync());
    }
}
