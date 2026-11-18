using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Alveara.Api.Architecture.Treatment;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N023 (owner decision DR-1; briefing revision 2 section 4.2), against real SQL Server and through the real migrator on a clone: migration AddTreatmentPlanItemAppointmentLinks.
/// - Direct persistence (bypassing the service): a link naming another patient's appointment, or rows whose patient does not match the item, are refused (51291); a link row cannot be updated
///   or deleted (51292); the same-patient link is accepted.
/// - Down removes only what Up added while no link exists, and Up restores it; once a link exists Down is refused (51293) and nothing is lost.
/// </summary>
public class TreatmentPlanAppointmentLinkMigrationTests : TreatmentPlanTestBase
{
    private const string Before = "20261117180509_AddTypedClearanceDocumentAndPerioDiagnosisLinks";
    private const string Triggers = "SELECT COUNT(*) AS [Value] FROM sys.triggers WHERE name IN ('TR_TPItemAppointmentLinks_SamePatient','TR_TPItemAppointmentLinks_AppendOnly')";

    private async Task MigrateToAsync(string target)
    {
        await using var db = Fixture.CreateContext();
        await db.GetInfrastructure().GetRequiredService<IMigrator>().MigrateAsync(target);
    }

    private Task<int> CountAsync(string sql) => WithDb(db => db.Database.SqlQuery<int>(System.Runtime.CompilerServices.FormattableStringFactory.Create(sql)).SingleAsync());
    private async Task<int> Number(Func<Task> action) => Assert.IsType<SqlException>((await Assert.ThrowsAnyAsync<Exception>(action)) switch { DbUpdateException d => d.InnerException, var e => e }).Number;

    private async Task<PlanView> PlanAsync(Guid patient)
    {
        var proc = await ProcedureAsync(code: $"LOCAL-{Guid.NewGuid():N}"[..14]);
        return await CreateAsync([Item(await DiagnosisAsync(patient), proc.Summary.Id)], patient: patient);
    }

    private Task InsertAsync(PlanView plan, Guid appointment, Guid patient, int sequence = 1) => WithDb(async db =>
    {
        db.TreatmentPlanItemAppointmentLinks.Add(new TreatmentPlanItemAppointmentLink
        {
            Id = Guid.NewGuid(), PlanId = plan.Id, ItemId = plan.Items[0].Id, PatientId = patient, AppointmentId = appointment, Sequence = sequence, Action = AppointmentLinkActions.Linked,
            RecordedByUserId = S.Actor, RecordedAtUtc = DateTimeOffset.UtcNow,
        });
        return await db.SaveChangesAsync();
    });

    [Fact]
    public async Task A_direct_link_to_another_patients_appointment_is_refused_by_the_database_and_the_same_patients_is_accepted()
    {
        var plan = await PlanAsync(Ann);
        Assert.Equal(51291, await Number(async () => await InsertAsync(plan, await AppointmentAsync(Bo), Ann)));        // Bo's appointment on Ann's item
        Assert.Equal(51291, await Number(async () => await InsertAsync(plan, await AppointmentAsync(Bo), Bo)));         // a row claiming Bo for Ann's item
        await InsertAsync(plan, await AppointmentAsync(Ann), Ann);
        Assert.Equal(1, await WithDb(db => db.TreatmentPlanItemAppointmentLinks.CountAsync(l => l.ItemId == plan.Items[0].Id)));
    }

    [Fact]
    public async Task A_link_row_cannot_be_updated_or_deleted()
    {
        var plan = await PlanAsync(Ann);
        await InsertAsync(plan, await AppointmentAsync(Ann), Ann);
        Assert.Equal(51292, await Number(() => WithDb(db => db.Database.ExecuteSqlRawAsync("UPDATE [TreatmentPlanItemAppointmentLinks] SET [Sequence] = 5"))));
        Assert.Equal(51292, await Number(() => WithDb(db => db.Database.ExecuteSqlRawAsync("DELETE FROM [TreatmentPlanItemAppointmentLinks]"))));
        Assert.Equal(1, await WithDb(db => db.TreatmentPlanItemAppointmentLinks.CountAsync()));
    }

    [Fact]
    public async Task The_migration_rolls_back_and_forward_while_no_link_exists()
    {
        Assert.Equal(2, await CountAsync(Triggers));
        await MigrateToAsync(Before);
        Assert.Equal(0, await CountAsync(Triggers));
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) AS [Value] FROM sys.tables WHERE name = 'TreatmentPlanItemAppointmentLinks'"));
        await using (var db = Fixture.CreateContext()) await db.Database.MigrateAsync();
        await using var check = Fixture.CreateContext();
        Assert.Empty(await check.Database.GetPendingMigrationsAsync());
        Assert.Equal(2, await CountAsync(Triggers));
    }

    [Fact]
    public async Task Rolling_back_is_refused_once_a_link_exists_and_nothing_is_lost()
    {
        var plan = await PlanAsync(Ann);
        await InsertAsync(plan, await AppointmentAsync(Ann), Ann);
        Assert.Equal(51293, await Number(() => MigrateToAsync(Before)));
        Assert.Equal(1, await WithDb(db => db.TreatmentPlanItemAppointmentLinks.CountAsync()));
        Assert.Equal(2, await CountAsync(Triggers));
    }
}
