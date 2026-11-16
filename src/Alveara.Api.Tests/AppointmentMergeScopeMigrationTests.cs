using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N021 A2, migration M6 (owner decision 2026-11-16): the patient-merge guard on Appointments covers the cancel category M1 added. Inside a merge only the
/// patient id of an appointment may change - a merge-time change of CancelCategory is refused 51234 like any other column. Rolled back to before M6 the old
/// guard is in place (the gap this migration closes is real: the change goes through), and forward again it is refused; M1 itself still rolls back and forward.
/// </summary>
public class AppointmentMergeScopeMigrationTests : SafetyTestBase
{
    private const string BeforeM6 = "20261116040919_AddBlockedTimeCalendarLabel";

    private static async Task<Microsoft.Data.SqlClient.SqlException> Refusal(Func<Task> action)
    {
        var e = await Assert.ThrowsAnyAsync<Exception>(action);
        return Assert.IsType<Microsoft.Data.SqlClient.SqlException>(e is DbUpdateException { InnerException: not null } d ? d.InnerException : e);
    }

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

    /// <summary>Runs one statement inside a patient-merge transaction (the session value the merge service sets).</summary>
    private async Task InMergeAsync(FormattableString sql)
    {
        await using var db = Fixture.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlAsync($"EXEC sp_set_session_context N'alveara.patient_merge', 1");
        await db.Database.ExecuteSqlInterpolatedAsync(sql);
        await tx.CommitAsync();
    }

    /// <summary>A cancelled appointment (category "patient"): a cancel category is only allowed on a cancelled appointment (CK_Appointments_CancelCategory).</summary>
    private async Task<Guid> AppointmentAsync()
    {
        var a = (await S.ScheduleAsync(S.Request(Ann, At(9)))).Appointment;
        return (await S.ManageAsync(m => m.CancelAsync(a.Id, "patient", null, a.RowVersion, S.Actor, default))).Id;
    }

    private async Task<string?> CategoryAsync(Guid id)
    {
        await using var db = Fixture.CreateContext();
        return (await db.Appointments.AsNoTracking().SingleAsync(a => a.Id == id)).CancelCategory;
    }

    [Fact]
    public async Task Inside_a_merge_the_cancel_category_cannot_change_but_the_patient_id_can()
    {
        var id = await AppointmentAsync();
        Assert.Equal(51234, (await Refusal(() => InMergeAsync($"UPDATE [Appointments] SET [CancelCategory] = N'office' WHERE [Id] = {id}"))).Number);
        Assert.Equal("patient", await CategoryAsync(id));

        await InMergeAsync($"UPDATE [Appointments] SET [PatientId] = {Bo} WHERE [Id] = {id}");   // the one change a merge makes
        await using var db = Fixture.CreateContext();
        Assert.Equal(Bo, (await db.Appointments.AsNoTracking().SingleAsync(a => a.Id == id)).PatientId);
    }

    [Fact]
    public async Task Before_M6_the_gap_is_real_and_M6_closes_it_again_after_a_roll_forward()
    {
        var id = await AppointmentAsync();
        await MigrateToAsync(BeforeM6);
        await InMergeAsync($"UPDATE [Appointments] SET [CancelCategory] = N'office' WHERE [Id] = {id}");   // the pre-M6 guard did not list the column
        Assert.Equal("office", await CategoryAsync(id));

        await MigrateUpAsync();
        Assert.Equal(51234, (await Refusal(() => InMergeAsync($"UPDATE [Appointments] SET [CancelCategory] = N'weather' WHERE [Id] = {id}"))).Number);
        Assert.Equal("office", await CategoryAsync(id));
    }

    [Fact]
    public async Task M1_still_rolls_back_and_forward_through_M6()
    {
        string beforeM1;
        await using (var db = Fixture.CreateContext())
        {
            var all = db.Database.GetMigrations().ToList();
            beforeM1 = all[all.FindIndex(m => m.EndsWith("_AddVisitWorkflowSettingsAndCancelCategory", StringComparison.Ordinal)) - 1];
        }
        await MigrateToAsync(beforeM1);   // M6 down re-creates the guard without CancelCategory, so M1 down can drop the column
        await MigrateUpAsync();

        var id = await AppointmentAsync();
        Assert.Equal(51234, (await Refusal(() => InMergeAsync($"UPDATE [Appointments] SET [CancelCategory] = N'office' WHERE [Id] = {id}"))).Number);
    }
}
