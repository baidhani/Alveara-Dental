using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Alveara.Api.Architecture.Patients.Merge;
using Alveara.Api.Architecture.Patients.Portability.Package;
using Alveara.Api.Architecture.Treatment;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N026 S8 (U-022, owner decision 2026-11-22): ALV-N023's appointment-link history joins the merge machinery (migration AddTreatmentPlanItemAppointmentLinksMergeScope). Proven directly in SQL:
/// outside a merge the patient of a link cannot be changed and a link cannot be edited or deleted (51292); inside a merge only PatientId may change and no row may be added or removed (51234); a
/// merged-away record takes no new link (51235); an approved merge moves the link to the survivor with its plan, item and appointment, untouched otherwise, and its undo moves it back. The patient
/// export carries the link (treatment_plans) and the visit checkout outcomes (appointments).
/// </summary>
public class TreatmentPlanItemAppointmentLinkMergeTests : TreatmentPlanTestBase
{
    private static int SqlNumber(Exception e) => (e as SqlException ?? e.InnerException as SqlException)?.Number ?? 0;

    private async Task<int> DirectAsync(string statement, bool inMerge = false)
    {
        await using var db = Fixture.CreateContext();
        var sql = inMerge
            ? "EXEC sp_set_session_context N'alveara.patient_merge', 1; BEGIN TRY " + statement + "; EXEC sp_set_session_context N'alveara.patient_merge', NULL; END TRY " +
              "BEGIN CATCH EXEC sp_set_session_context N'alveara.patient_merge', NULL; THROW; END CATCH"
            : statement;
        try { await db.Database.ExecuteSqlRawAsync(sql); return 0; }
        catch (Exception e) { return SqlNumber(e); }
    }

    /// <summary>An accepted item of <paramref name="patient"/> booked by one of their appointments: returns the link row.</summary>
    private async Task<TreatmentPlanItemAppointmentLink> LinkedAsync(Guid patient)
    {
        var proc = await ProcedureAsync(code: $"LOCAL-{Guid.NewGuid():N}"[..14]);
        var plan = await CreateAsync([Item(await DiagnosisAsync(patient), proc.Summary.Id)], patient: patient);
        plan = await Svc(s => s.DecideItemAsync(plan.Id, plan.Items[0].Id, "Accepted", null, null, plan.RowVersion, S.Actor, default));
        var appointment = await AppointmentAsync(patient);
        await Svc(s => s.LinkAppointmentAsync(plan.Id, plan.Items[0].Id, appointment, S.Actor, default));
        return await RowAsync(plan.Items[0].Id);
    }

    private Task<TreatmentPlanItemAppointmentLink> RowAsync(Guid item) => WithDb(db => db.TreatmentPlanItemAppointmentLinks.AsNoTracking().SingleAsync(l => l.ItemId == item));

    private async Task<Guid> MergeAsync(Guid source, Guid survivor)
    {
        var preview = await WithDb(db => new PatientMergeService(db, Clock).PreviewAsync(source, survivor, default));
        Assert.True(preview.CanMerge, string.Join("; ", preview.Blockers.Select(b => b.Message)));
        var done = await WithDb(db => new PatientMergeService(db, Clock).MergeAsync(S.Actor, new MergeRequest(source, survivor, "Registered twice", preview.SourceRowVersion, preview.SurvivorRowVersion,
            preview.Conflicts.ToDictionary(c => c.Field, _ => "Survivor"), true, true), Guid.NewGuid().ToString("N"), default));
        return done.MergeId;
    }

    [Fact]
    public async Task Outside_a_merge_the_patient_of_a_link_cannot_be_changed_and_a_link_is_never_edited_or_deleted()
    {
        var link = await LinkedAsync(Ann);
        Assert.Equal(51292, await DirectAsync($"UPDATE [TreatmentPlanItemAppointmentLinks] SET [PatientId] = '{Bo}' WHERE [Id] = '{link.Id}'"));
        Assert.Equal(51292, await DirectAsync($"UPDATE [TreatmentPlanItemAppointmentLinks] SET [Reason] = N'x' WHERE [Id] = '{link.Id}'"));
        Assert.Equal(51292, await DirectAsync($"DELETE FROM [TreatmentPlanItemAppointmentLinks] WHERE [Id] = '{link.Id}'"));
        Assert.Equal(Ann, (await RowAsync(link.ItemId)).PatientId);
    }

    [Fact]
    public async Task Inside_a_merge_only_the_patient_id_may_change_and_no_row_may_be_added_or_removed()
    {
        var link = await LinkedAsync(Ann);
        Assert.Equal(51292, await DirectAsync($"DELETE FROM [TreatmentPlanItemAppointmentLinks] WHERE [Id] = '{link.Id}'", inMerge: true));
        Assert.Equal(51234, await DirectAsync($"INSERT INTO [TreatmentPlanItemAppointmentLinks] ([Id],[PlanId],[ItemId],[PatientId],[AppointmentId],[Sequence],[Action],[Reason],[RecordedByUserId],[RecordedAtUtc]) " +
                                              $"VALUES (NEWID(), '{link.PlanId}', '{link.ItemId}', '{Ann}', '{link.AppointmentId}', 2, N'Unlinked', N'in merge', '{S.Actor}', SYSDATETIMEOFFSET())", inMerge: true));
        Assert.Equal(1, await WithDb(db => db.TreatmentPlanItemAppointmentLinks.CountAsync(l => l.ItemId == link.ItemId)));

        // the stand-aside applies ONLY the patient id of the rows named: any other value in the same statement is not written
        Assert.Equal(0, await DirectAsync($"UPDATE [TreatmentPlanItemAppointmentLinks] SET [PatientId] = '{Bo}', [Sequence] = 9 WHERE [Id] = '{link.Id}'", inMerge: true));
        var row = await RowAsync(link.ItemId);
        Assert.Equal((Bo, 1), (row.PatientId, row.Sequence));
    }

    [Fact]
    public async Task The_table_is_registered_and_carries_both_merge_guards_and_a_merged_away_record_takes_no_new_link()
    {
        Assert.Contains(PatientOwnedData.All, c => c.Table == "TreatmentPlanItemAppointmentLinks" && c.Column == "PatientId" && c.Role == PatientDataRole.Owner && !c.ForeignKey);
        await using (var db = Fixture.CreateContext())
        {
            Assert.Contains("/*retired-record-guard*/", await db.Database.SqlQueryRaw<string>("SELECT OBJECT_DEFINITION(OBJECT_ID('TR_TreatmentPlanItemAppointmentLinks_MergeScope')) AS [Value]").SingleAsync());
            Assert.Contains("/*patient-merge*/", await db.Database.SqlQueryRaw<string>("SELECT OBJECT_DEFINITION(OBJECT_ID('TR_TPItemAppointmentLinks_AppendOnly')) AS [Value]").SingleAsync());
        }

        var gone = Ann; var kept = await S.PatientAsync("Cy", "Link");                                       // Ann has an encounter, so her item can carry a diagnosis
        var link = await LinkedAsync(gone);
        await MergeAsync(gone, kept);
        Assert.Equal(51235, await DirectAsync($"INSERT INTO [TreatmentPlanItemAppointmentLinks] ([Id],[PlanId],[ItemId],[PatientId],[AppointmentId],[Sequence],[Action],[Reason],[RecordedByUserId],[RecordedAtUtc]) " +
                                              $"VALUES (NEWID(), '{link.PlanId}', '{link.ItemId}', '{gone}', '{link.AppointmentId}', 2, N'Unlinked', N'after merge', '{S.Actor}', SYSDATETIMEOFFSET())"));
    }

    [Fact]
    public async Task An_approved_merge_moves_the_link_with_its_plan_item_and_appointment_and_its_undo_moves_it_back()
    {
        var link = await LinkedAsync(Ann);
        var mergeId = await MergeAsync(Ann, Bo);

        var moved = await RowAsync(link.ItemId);
        Assert.Equal(Bo, moved.PatientId);
        Assert.Equal((link.Id, link.PlanId, link.AppointmentId, link.Sequence, link.Action, link.RecordedAtUtc), (moved.Id, moved.PlanId, moved.AppointmentId, moved.Sequence, moved.Action, moved.RecordedAtUtc));
        await using (var db = Fixture.CreateContext())
        {
            Assert.Equal(Bo, (await db.TreatmentPlanItems.AsNoTracking().SingleAsync(i => i.Id == link.ItemId)).PatientId);          // still the same patient as its item and appointment (51291's rule)
            Assert.Equal(Bo, (await db.Appointments.AsNoTracking().SingleAsync(a => a.Id == link.AppointmentId)).PatientId);
        }
        Assert.Equal(Bo, (await Svc(s => s.GetAsync(link.PlanId, default))).PatientId);

        var preview = await WithDb(db => new PatientUnmergeService(db, Clock).PreviewAsync(mergeId, default));
        await WithDb(db => new PatientUnmergeService(db, Clock).UnmergeAsync(S.Actor, new UnmergeRequest(mergeId, "They are different people", preview.SourceRowVersion, preview.SurvivorRowVersion, true), "undo-1", default));
        Assert.Equal(Ann, (await RowAsync(link.ItemId)).PatientId);
    }

    [Fact]
    public async Task The_patient_export_carries_the_patients_links_and_checkout_outcomes_and_never_the_record_opens()
    {
        var mine = await LinkedAsync(Ann);
        var theirs = await LinkedAsync(Bo);
        await using var db = Fixture.CreateContext();
        var filters = new ExportTableFilters(db.Model, $"'{Ann}'");

        var where = filters.WhereFor("TreatmentPlanItemAppointmentLinks");
        Assert.NotNull(where);
        var ids = await db.Database.SqlQueryRaw<Guid>($"SELECT [Id] AS [Value] FROM [TreatmentPlanItemAppointmentLinks] WHERE {where}").ToListAsync();
        Assert.Equal([mine.Id], ids);                                                                              // Ann's link, not Bo's
        Assert.DoesNotContain(theirs.Id, ids);
        Assert.NotNull(filters.WhereFor("VisitCheckoutOutcomes"));                                                // reached through the patient's appointments
        Assert.Contains("TreatmentPlanItemAppointmentLinks", ExportDomainCatalog.Domains.Single(d => d.Key == "treatment_plans").Tables);
        Assert.Contains("VisitCheckoutOutcomes", ExportDomainCatalog.Domains.Single(d => d.Key == "appointments").Tables);
        Assert.DoesNotContain("PatientRecordOpens", ExportDomainCatalog.IncludedTables);
        Assert.Contains("PatientRecordOpens", ExportDomainCatalog.Excluded.Single(g => g.Key == "audit_log").Tables);
    }
}
