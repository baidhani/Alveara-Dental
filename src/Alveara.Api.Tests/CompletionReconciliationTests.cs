using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Billing;
using Alveara.Api.Architecture.Completion;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Odontogram;
using Alveara.Api.Architecture.Treatment;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-008-C02 Gate D, against real SQL Server: the reconciliation report is counts only, and every count is zero after a clean walk (completing, adopting, hand-billing then adopting). Each kind of disagreement is
/// seeded directly (the product cannot make them) and must be counted. The chart evidence is read from the finding's own history at the instant the completion was finalized (review correction C2), so a later,
/// legitimate change to the chart does not turn a good completion into a defect, and a completion whose finding was never Completed at that instant is.
/// </summary>
public class CompletionReconciliationTests : CompletionTestBase
{
    private ProviderProfile _provider = null!;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _provider = await ProviderAsync();
    }

    private Task<CompletionReconciliation> ReportAsync() => Completion(s => s.ReconcileAsync(default));
    private static int[] Numbers(CompletionReconciliation r) => [r.LegacyCompletedNotAdopted, r.CompletedMissingCharge, r.CompletedWithUnexpectedCharge, r.OrphanedCharges, r.ChartMismatch, r.DuplicateCharges];

    private Task<CompletionResult> Adopt(PlanView plan, int item, string key = "adopt-1") =>
        Completion(s => s.AdoptAsync(plan.Id, plan.Items[item].Id, new AdoptCompletionRequest(key, plan.RowVersion, AnnEncounter, _provider.Id, DateTimeOffset.UtcNow.AddSeconds(2), "Completed before procedure records existed"), S.Actor, default));

    private async Task Sql(string sql) { await using var db = Fixture.CreateContext(); await db.Database.ExecuteSqlRawAsync(sql); }

    private async Task<(PlanView Plan, Guid Finding)> LegacyToothAsync(string state = FindingStates.Planned)
    {
        var diagnosis = await DiagnosisAsync();
        var finding = await FindingAsync("16", "O", state);
        await LinkAsync(diagnosis, finding);
        var plan = await AcceptedToothPlanAsync(diagnosis, ("FILL-1", 140m, "16", "O"));
        await LegacyComplete(plan.Id, plan.Items[0].Id);
        return (await PlanNowAsync(plan.Id), finding);
    }

    [Fact]
    public async Task An_empty_system_reports_nothing_and_is_clean()
    {
        var r = await ReportAsync();
        Assert.Equal(new[] { 0, 0, 0, 0, 0, 0 }, Numbers(r));
        Assert.True(r.Clean);
    }

    [Fact]
    public async Task A_legacy_completion_is_counted_until_it_is_adopted_and_then_every_count_is_zero()
    {
        var (plan, finding) = await LegacyToothAsync();
        Assert.Equal(new[] { 1, 0, 0, 0, 0, 0 }, Numbers(await ReportAsync()));                                    // completed the old way: waiting for adoption (it has no charge, and no orphan)

        var p = (await Adopt(plan, 0)).Procedures.Single();
        Assert.Equal((ChartOutcomes.Updated, finding), (p.ChartOutcome, p.ToothFindingId));
        var done = await ReportAsync();
        Assert.Equal(new[] { 0, 0, 0, 0, 0, 0 }, Numbers(done));                                                    // recorded, one charge, odontogram agrees
        Assert.True(done.Clean);
    }

    [Fact]
    public async Task A_hand_billed_legacy_completion_is_not_an_orphan_and_adopting_it_reuses_the_charge()
    {
        var plan = await AcceptedPlanAsync(1);
        await LegacyComplete(plan.Id, plan.Items[0].Id);
        await WithDb(db => new BillingService(db, SchedulingTestSupport.Clock).RecordChargeAsync(Ann, new ChargeInput(plan.Items[0].Id, "c-1"), S.Actor, default));
        Assert.Equal(new[] { 1, 0, 0, 0, 0, 0 }, Numbers(await ReportAsync()));

        await Adopt(await PlanNowAsync(plan.Id), 0);
        Assert.Equal(new[] { 0, 0, 0, 0, 0, 0 }, Numbers(await ReportAsync()));
    }

    [Fact]
    public async Task An_already_completed_finding_is_not_a_mismatch_and_a_later_legitimate_change_to_the_chart_leaves_the_completion_clear()
    {
        var (plan, _) = await LegacyToothAsync(FindingStates.Completed);
        Assert.Equal(ChartOutcomes.AlreadyCompleted, (await Adopt(plan, 0)).Procedures.Single().ChartOutcome);
        Assert.Equal(0, (await ReportAsync()).ChartMismatch);

        var (plan2, finding2) = await LegacyToothAsync2();
        var done = (await Adopt(plan2, 0, "adopt-2")).Procedures.Single();
        Assert.Equal(ChartOutcomes.Updated, done.ChartOutcome);
        await using (var db = Fixture.CreateContext())
        {
            var version = Convert.ToBase64String((await db.ToothFindings.AsNoTracking().SingleAsync(f => f.Id == finding2)).RowVersion);
            await WithDb(d => new OdontogramService(d, SchedulingTestSupport.Clock).WithdrawAsync(finding2, "Entered on the wrong patient", version, S.Actor, default));
        }
        var r = await ReportAsync();
        Assert.Equal(0, r.ChartMismatch);                                                                    // the finding was Completed when the completion was finalized; withdrawing it later is not a defect
        Assert.True(r.Clean);
    }

    private async Task<(PlanView Plan, Guid Finding)> LegacyToothAsync2()
    {
        var diagnosis = await DiagnosisAsync();
        var finding = await FindingAsync("26", "O", FindingStates.Planned);
        await LinkAsync(diagnosis, finding);
        var plan = await AcceptedToothPlanAsync(diagnosis, ("FILL-2", 90m, "26", "O"));
        await LegacyComplete(plan.Id, plan.Items[0].Id);
        return (await PlanNowAsync(plan.Id), finding);
    }

    [Fact]
    public async Task A_completion_whose_finding_was_not_completed_when_it_was_finalized_is_a_chart_mismatch()
    {
        var (plan, finding) = await LegacyToothAsync();                                                       // the finding stays Planned: nothing moved it
        var set = SetRow(plan);
        var row = ProcedureRow(set, plan.Items[0], AnnEncounter, _provider.Id);
        row.ChartOutcome = ChartOutcomes.Updated;
        row.ToothFindingId = finding;
        await AddRowsAsync(set, row);

        var r = await ReportAsync();
        Assert.Equal(1, r.ChartMismatch);
        Assert.False(r.Clean);
    }

    [Fact]
    public async Task A_recorded_completion_with_a_fee_and_no_charge_is_counted_as_missing()
    {
        var plan = await AcceptedPlanAsync(1);
        await LegacyComplete(plan.Id, plan.Items[0].Id);
        var set = SetRow(plan);
        await AddRowsAsync(set, ProcedureRow(set, plan.Items[0], AnnEncounter, _provider.Id));
        Assert.Equal(new[] { 0, 1, 0, 0, 0, 0 }, Numbers(await ReportAsync()));
    }

    [Fact]
    public async Task A_recorded_completion_with_no_fee_and_a_charge_is_counted_as_unexpected()
    {
        var plan = await AcceptedPlanAsync(1);
        await LegacyComplete(plan.Id, plan.Items[0].Id);
        await Adopt(await PlanNowAsync(plan.Id), 0);
        await Sql("ALTER TABLE [TreatmentPlanItems] DISABLE TRIGGER ALL; UPDATE [TreatmentPlanItems] SET [Fee] = 0; ALTER TABLE [TreatmentPlanItems] ENABLE TRIGGER ALL;");
        Assert.Equal(new[] { 0, 0, 1, 0, 0, 0 }, Numbers(await ReportAsync()));
    }

    [Fact]
    public async Task A_charge_for_a_procedure_that_is_neither_recorded_nor_completed_is_an_orphan()
    {
        var plan = await AcceptedPlanAsync(1);
        await LegacyComplete(plan.Id, plan.Items[0].Id);
        await WithDb(db => new BillingService(db, SchedulingTestSupport.Clock).RecordChargeAsync(Ann, new ChargeInput(plan.Items[0].Id, "c-1"), S.Actor, default));
        await Sql("ALTER TABLE [TreatmentPlanItemStates] DISABLE TRIGGER ALL; DELETE FROM [TreatmentPlanItemStates] WHERE [Completion] = 'Completed'; ALTER TABLE [TreatmentPlanItemStates] ENABLE TRIGGER ALL;");
        Assert.Equal(new[] { 0, 0, 0, 1, 0, 0 }, Numbers(await ReportAsync()));
    }

    [Fact]
    public async Task More_than_one_charge_for_a_procedure_is_counted_once_per_procedure()
    {
        var plan = await AcceptedPlanAsync(1);
        await LegacyComplete(plan.Id, plan.Items[0].Id);
        await Adopt(await PlanNowAsync(plan.Id), 0);
        await Sql(@"DECLARE @i sysname = (SELECT TOP 1 i.name FROM sys.indexes i WHERE i.object_id = OBJECT_ID('BillingEntries') AND i.is_unique = 1 AND i.has_filter = 1 AND i.filter_definition LIKE '%PlanItemId%');
EXEC('DROP INDEX [' + @i + '] ON [BillingEntries]');
ALTER TABLE [BillingEntries] DISABLE TRIGGER ALL;
INSERT INTO [BillingEntries] (Id, AccountId, PatientId, EntryNumber, Kind, Amount, PlanItemId, ProcedureCode, ProcedureDescription, IdempotencyKey, RecordedAtUtc, RecordedByUserId)
SELECT NEWID(), AccountId, PatientId, EntryNumber + 1, Kind, Amount, PlanItemId, ProcedureCode, ProcedureDescription, 'dup-1', RecordedAtUtc, RecordedByUserId FROM [BillingEntries];
ALTER TABLE [BillingEntries] ENABLE TRIGGER ALL;");
        Assert.Equal(1, (await ReportAsync()).DuplicateCharges);
    }

    [Fact]
    public async Task The_report_holds_counts_only()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(await ReportAsync(), new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        var names = System.Text.Json.JsonDocument.Parse(json).RootElement.EnumerateObject().Select(p => p.Name).Order().ToArray();
        Assert.Equal(["chartMismatch", "clean", "completedMissingCharge", "completedWithUnexpectedCharge", "duplicateCharges", "generatedAtUtc", "legacyCompletedNotAdopted", "orphanedCharges"], names);
    }

    [Fact]
    public async Task A_legacy_completion_on_a_withdrawn_procedure_is_not_waiting_for_adoption()
    {
        var plan = await AcceptedPlanAsync(1);
        await LegacyComplete(plan.Id, plan.Items[0].Id);
        Assert.Equal(1, (await ReportAsync()).LegacyCompletedNotAdopted);
        await Sql("ALTER TABLE [TreatmentPlanItems] DISABLE TRIGGER ALL; UPDATE [TreatmentPlanItems] SET [WithdrawnAtUtc] = SYSUTCDATETIME(), [WithdrawnByUserId] = NEWID(), [WithdrawnReason] = N'Entered in error'; ALTER TABLE [TreatmentPlanItems] ENABLE TRIGGER ALL;");
        Assert.Equal(0, (await ReportAsync()).LegacyCompletedNotAdopted);
    }
}
