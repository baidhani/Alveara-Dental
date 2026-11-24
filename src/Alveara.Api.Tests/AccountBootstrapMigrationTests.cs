using Alveara.Api.Architecture.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N030 slice 1 (brief section 3; tests S12 and the database half of S11): the migration AddAccountKindSetupTokensAndPracticeOwner through the real migrator, on a clone.
/// Upgrade data step: exactly one Administrator without a staff profile becomes the Deployment Administrator (with one audit row); none, or two or more, convert nothing
/// (R01 CR-3 path 1); an Administrator WITH a profile is never converted; the migration never designates a Practice Owner. The database rules: at most one Deployment
/// Administrator, at most one active Practice Owner designation, and a designation is never deleted or changed except to end it (51311).
/// </summary>
public class AccountBootstrapMigrationTests : SafetyTestBase
{
    private const string Before = "20261122140138_AddTreatmentPlanItemAppointmentLinksMergeScope";

    private async Task MigrateToAsync(string target)
    {
        await using var db = Fixture.CreateContext();
        await db.GetInfrastructure().GetRequiredService<IMigrator>().MigrateAsync(target);
    }

    private async Task MigrateUpAsync()
    {
        await using var db = Fixture.CreateContext();
        await db.Database.MigrateAsync();
    }

    private static async Task<Microsoft.Data.SqlClient.SqlException> Refusal(Func<Task> action)
    {
        var e = await Assert.ThrowsAnyAsync<Exception>(action);
        return Assert.IsType<Microsoft.Data.SqlClient.SqlException>(e is DbUpdateException { InnerException: not null } d ? d.InnerException : e);
    }

    /// <summary>Adds an Administrator account by raw SQL with the pre-migration column set (the schema is rolled back when this runs), optionally with a staff profile.</summary>
    private async Task<Guid> AdminBeforeMigrationAsync(bool withProfile)
    {
        var id = Guid.NewGuid();
        await using var db = Fixture.CreateContext();
        await db.Database.ExecuteSqlInterpolatedAsync($@"INSERT INTO [UserAccounts] ([Id], [Username], [Role], [FailedLoginAttempts], [SessionTimeoutMinutes], [CreatedAtUtc], [IsDisabled], [SecurityStamp], [MfaEnabled])
VALUES ({id}, {"admin-" + id.ToString("N")}, 6, 0, 30, SYSDATETIMEOFFSET(), 0, NEWID(), 0)");
        if (withProfile)
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO [StaffProfiles] ([Id], [UserAccountId], [DisplayName], [CreatedAtUtc], [IsActive]) VALUES (NEWID(), {id}, {"Admin " + id.ToString("N")}, SYSDATETIMEOFFSET(), 1)");
        return id;
    }

    private async Task<(List<Guid> Deployment, int AuditRows, int Designations)> StateAsync()
    {
        await using var db = Fixture.CreateContext();
        var deployment = await db.UserAccounts.AsNoTracking().Where(u => u.AccountKind == AccountKind.DeploymentAdministrator).Select(u => u.Id).ToListAsync();
        var audit = await db.AuditLogEntries.AsNoTracking().CountAsync(a => a.EventType == AuditEventTypes.DeploymentAdministratorAssigned);
        var designations = await db.PracticeOwnerDesignations.AsNoTracking().CountAsync();
        return (deployment, audit, designations);
    }

    /// <summary>The template's own accounts are not Administrators without a profile; the cases below set up exactly the Administrators they name.</summary>
    private async Task<int> ProfileLessAdminsAsync()
    {
        await using var db = Fixture.CreateContext();
        return await db.UserAccounts.AsNoTracking().CountAsync(u => u.Role == Role.Admin && !db.StaffProfiles.Any(s => s.UserAccountId == u.Id));
    }

    [Fact]
    public async Task Exactly_one_administrator_without_a_staff_profile_becomes_the_Deployment_Administrator_with_one_audit_row()
    {
        await MigrateToAsync(Before);
        Assert.Equal(0, await ProfileLessAdminsAsync());
        var lone = await AdminBeforeMigrationAsync(withProfile: false);
        var staffed = await AdminBeforeMigrationAsync(withProfile: true);
        await MigrateUpAsync();

        var (deployment, audit, designations) = await StateAsync();
        Assert.Equal([lone], deployment);
        Assert.DoesNotContain(staffed, deployment);
        Assert.Equal(1, audit);
        Assert.Equal(0, designations);   // the migration never chooses a Practice Owner
        await using var db = Fixture.CreateContext();
        var row = await db.AuditLogEntries.AsNoTracking().SingleAsync(a => a.EventType == AuditEventTypes.DeploymentAdministratorAssigned);
        Assert.Equal(lone, row.TargetUserAccountId);
        Assert.Null(row.PerformedByUserAccountId);
        Assert.Contains("UpgradeSingleCandidate", row.Details);
        Assert.Equal(Role.Admin, (await db.UserAccounts.AsNoTracking().SingleAsync(u => u.Id == lone)).Role);   // same access: still the Admin role
    }

    [Fact]
    public async Task No_administrator_without_a_staff_profile_converts_nothing()
    {
        await MigrateToAsync(Before);
        await AdminBeforeMigrationAsync(withProfile: true);
        await MigrateUpAsync();
        Assert.Equal((0, 0, 0), await CountsAsync());
    }

    [Fact]
    public async Task Two_administrators_without_a_staff_profile_convert_neither_and_keep_their_access()
    {
        await MigrateToAsync(Before);
        var a = await AdminBeforeMigrationAsync(withProfile: false);
        var b = await AdminBeforeMigrationAsync(withProfile: false);
        await MigrateUpAsync();

        Assert.Equal((0, 0, 0), await CountsAsync());
        await using var db = Fixture.CreateContext();
        var both = await db.UserAccounts.AsNoTracking().Where(u => u.Id == a || u.Id == b).ToListAsync();
        Assert.All(both, u => { Assert.Equal(Role.Admin, u.Role); Assert.False(u.IsDisabled); Assert.Equal(AccountKind.Staff, u.AccountKind); });
    }

    private async Task<(int, int, int)> CountsAsync()
    {
        var (deployment, audit, designations) = await StateAsync();
        return (deployment.Count, audit, designations);
    }

    [Fact]
    public async Task Existing_accounts_get_the_defaults_and_the_migration_rolls_back_and_forward()
    {
        await MigrateToAsync(Before);
        await MigrateUpAsync();
        await using var db = Fixture.CreateContext();
        Assert.All(await db.UserAccounts.AsNoTracking().ToListAsync(), u => { Assert.Equal(AccountKind.Staff, u.AccountKind); Assert.False(u.MustChangePassword); });
    }

    [Fact]
    public async Task The_database_allows_at_most_one_Deployment_Administrator()
    {
        await using var db = Fixture.CreateContext();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [UserAccounts] SET [AccountKind] = 1 WHERE [Id] = {S.Actor}");
        var e = await Refusal(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [UserAccounts] SET [AccountKind] = 1 WHERE [Id] = {Other}"));
        Assert.Equal(2601, e.Number);   // UX_UserAccounts_OneDeploymentAdministrator
    }

    private async Task<Guid> StaffProfileOfAsync(Guid account)
    {
        await using var db = Fixture.CreateContext();
        return await db.StaffProfiles.AsNoTracking().Where(s => s.UserAccountId == account).Select(s => s.Id).SingleAsync();
    }

    private async Task<Guid> DesignateAsync(Guid staffProfileId)
    {
        var id = Guid.NewGuid();
        await using var db = Fixture.CreateContext();
        db.PracticeOwnerDesignations.Add(new PracticeOwnerDesignation
        {
            Id = id, StaffProfileId = staffProfileId, DesignatedAtUtc = DateTimeOffset.UtcNow, DesignatedByAccountId = S.Actor, Method = PracticeOwnerDesignationMethod.UpgradeTransition,
        });
        await db.SaveChangesAsync();
        return id;
    }

    [Fact]
    public async Task Only_one_Practice_Owner_designation_can_be_active()
    {
        await DesignateAsync(await StaffProfileOfAsync(S.Actor));
        var second = await StaffProfileOfAsync(Other);
        Assert.Equal(2601, (await Refusal(() => DesignateAsync(second))).Number);   // UX_PracticeOwnerDesignations_OneActive
        await using var db = Fixture.CreateContext();
        Assert.Equal(547, (await Refusal(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO [PracticeOwnerDesignations] ([Id], [StaffProfileId], [DesignatedAtUtc], [DesignatedByAccountId], [Method], [EndedAtUtc], [Slot]) VALUES (NEWID(), {second}, SYSDATETIMEOFFSET(), {S.Actor}, N'UpgradeTransition', SYSDATETIMEOFFSET(), 2)"))).Number);   // CK_PracticeOwnerDesignations_Slot
    }

    [Fact]
    public async Task A_designation_is_never_deleted_or_changed_except_to_end_it()
    {
        var id = await DesignateAsync(await StaffProfileOfAsync(S.Actor));
        var other = await StaffProfileOfAsync(Other);
        await using var db = Fixture.CreateContext();
        Assert.Equal(51311, (await Refusal(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM [PracticeOwnerDesignations] WHERE [Id] = {id}"))).Number);
        Assert.Equal(51311, (await Refusal(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [PracticeOwnerDesignations] SET [StaffProfileId] = {other} WHERE [Id] = {id}"))).Number);
        Assert.Equal(51311, (await Refusal(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [PracticeOwnerDesignations] SET [Method] = N'InitialByDeploymentAdministrator' WHERE [Id] = {id}"))).Number);

        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [PracticeOwnerDesignations] SET [EndedAtUtc] = SYSDATETIMEOFFSET() WHERE [Id] = {id}");   // ending: the only change allowed
        Assert.NotNull((await db.PracticeOwnerDesignations.AsNoTracking().SingleAsync(d => d.Id == id)).EndedAtUtc);
        Assert.Equal(51311, (await Refusal(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [PracticeOwnerDesignations] SET [EndedAtUtc] = NULL WHERE [Id] = {id}"))).Number);   // an ended one stays ended
        await DesignateAsync(other);   // with none active, a new designation is possible again
    }
}
