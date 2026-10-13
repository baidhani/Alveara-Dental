using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Alveara.Api.Architecture.Completion;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Odontogram;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Architecture.Treatment;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-008-C02 R02 (review finding ALV-008-C02-R01-01): a completion records the exact chart-history version it wrote or observed, and reconciliation judges it against that version alone, never against a
/// timestamp. Proved here: an adoption that writes a completed version records exactly that version; one that observes an already-completed finding records the observed version; a later legitimate change at
/// the SAME practice-clock instant leaves either clear; the authoritative completion records it too (including two procedures on one finding in one set); missing, altered or non-completed evidence is reported
/// as a mismatch; the database refuses such evidence at insert on its own; and the migration's one-time backfill picks the first active Completed version, which a later change at the same instant cannot displace.
/// </summary>
public class CompletionChartEvidenceTests : CompletionTestBase
{
    private const string Reason = "Completed before procedure records existed";
    private const string BeforeEvidence = "20261013043201_AddCompletionAdoption";
    private ProviderProfile _provider = null!;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _provider = await ProviderAsync();
    }

    private Task<CompletionReconciliation> ReportAsync() => Completion(s => s.ReconcileAsync(default));

    private async Task<(PlanView Plan, Guid Finding)> LegacyToothAsync(string state = FindingStates.Planned)
    {
        var diagnosis = await DiagnosisAsync();
        var finding = await FindingAsync("16", "O", state);
        await LinkAsync(diagnosis, finding);
        var plan = await AcceptedToothPlanAsync(diagnosis, ("FILL-1", 140m, "16", "O"));
        await LegacyComplete(plan.Id, plan.Items[0].Id);
        return (await PlanNowAsync(plan.Id), finding);
    }

    private async Task<CompletedProcedureView> AdoptAsync(PlanView plan, DateTimeOffset instant, IPracticeClock clock)
    {
        var done = await Completion(s => s.AdoptAsync(plan.Id, plan.Items[0].Id, new AdoptCompletionRequest("adopt-1", plan.RowVersion, AnnEncounter, _provider.Id, instant, Reason), S.Actor, default), clock);
        return done.Procedures.Single();
    }

    private async Task<byte[]> FindingRowVersionAsync(Guid finding)
    {
        await using var db = Fixture.CreateContext();
        return (await db.ToothFindings.AsNoTracking().SingleAsync(f => f.Id == finding)).RowVersion;
    }

    private async Task WithdrawAsync(Guid finding, IPracticeClock clock)
    {
        var version = Convert.ToBase64String(await FindingRowVersionAsync(finding));
        await WithDb(d => new OdontogramService(d, clock).WithdrawAsync(finding, "Entered on the wrong patient", version, S.Actor, default));
    }

    private async Task<List<ToothFindingVersion>> VersionsAsync(Guid finding)
    {
        await using var db = Fixture.CreateContext();
        return await db.ToothFindingVersions.AsNoTracking().Where(v => v.FindingId == finding).OrderBy(v => v.VersionNumber).ToListAsync();
    }

    private async Task<CompletedProcedure> RecordAsync(Guid planItem)
    {
        await using var db = Fixture.CreateContext();
        return await db.CompletedProcedures.AsNoTracking().SingleAsync(c => c.PlanItemId == planItem);
    }

    private async Task Sql(string sql) { await using var db = Fixture.CreateContext(); await db.Database.ExecuteSqlRawAsync(sql); }

    // ---------- the four cases of the review ----------

    [Fact]
    public async Task An_adoption_that_writes_a_completed_version_records_exactly_that_version_and_reconciles_clean()
    {
        var (plan, finding) = await LegacyToothAsync();
        var p = await AdoptAsync(plan, DateTimeOffset.UtcNow.AddSeconds(2), SchedulingTestSupport.Clock);

        Assert.Equal((ChartOutcomes.Updated, finding), (p.ChartOutcome, p.ToothFindingId));
        var versions = await VersionsAsync(finding);
        var written = versions.Single(v => v.ChangeType == FindingChangeTypes.StateChanged && v.State == FindingStates.Completed);
        Assert.Equal(written.VersionNumber, (await RecordAsync(plan.Items[0].Id)).ToothFindingVersionNumber);        // the version this adoption wrote, not "the latest" by any measure
        Assert.Equal(versions.Max(v => v.VersionNumber), written.VersionNumber);
        var r = await ReportAsync();
        Assert.Equal(0, r.ChartMismatch);
        Assert.True(r.Clean);
    }

    [Fact]
    public async Task An_adoption_that_observes_an_already_completed_finding_records_the_observed_version_and_a_later_change_at_the_same_instant_leaves_it_clear()
    {
        var (plan, finding) = await LegacyToothAsync(FindingStates.Completed);
        var instant = DateTimeOffset.UtcNow.AddMinutes(1);
        var clock = new TestClock(instant);
        var p = await AdoptAsync(plan, instant, clock);

        Assert.Equal((ChartOutcomes.AlreadyCompleted, finding), (p.ChartOutcome, p.ToothFindingId));
        var observed = (await VersionsAsync(finding)).Single(v => v.ChangeType == FindingChangeTypes.Recorded);                 // recorded as Completed: version 1 was already there
        Assert.Equal(observed.VersionNumber, (await RecordAsync(plan.Items[0].Id)).ToothFindingVersionNumber);        // nothing was written to the chart: it names the version that was already there
        Assert.Equal(0, (await ReportAsync()).ChartMismatch);

        await WithdrawAsync(finding, clock);                                                                      // a legitimate later change, at the very instant of the adoption
        await using (var db = Fixture.CreateContext())
            Assert.Equal(1, await db.ToothFindingVersions.CountAsync(v => v.FindingId == finding && v.OccurredAtUtc == instant && v.ChangeType == FindingChangeTypes.Withdrawn));
        Assert.Equal(0, (await ReportAsync()).ChartMismatch);
    }

    [Fact]
    public async Task A_completion_that_moves_no_finding_records_no_version_and_reconciles_clean()
    {
        var plan = await AcceptedPlanAsync(1);
        await LegacyComplete(plan.Id, plan.Items[0].Id);
        var p = await AdoptAsync(await PlanNowAsync(plan.Id), DateTimeOffset.UtcNow.AddSeconds(2), SchedulingTestSupport.Clock);
        Assert.Equal((ChartOutcomes.NotApplicable, (Guid?)null), (p.ChartOutcome, p.ToothFindingId));
        Assert.Null((await RecordAsync(plan.Items[0].Id)).ToothFindingVersionNumber);
        Assert.True((await ReportAsync()).Clean);
    }

    [Fact]
    public async Task The_authoritative_completion_records_the_version_it_wrote_and_two_procedures_on_one_finding_share_it()
    {
        var diagnosis = await DiagnosisAsync();
        var finding = await FindingAsync("16", "O", FindingStates.Planned);
        await LinkAsync(diagnosis, finding);
        var plan = await AcceptedToothPlanAsync(diagnosis, ("FILL-1", 140m, "16", "O"), ("FILL-2", 90m, "16", "O"));
        var request = RequestFor(plan, [0, 1], AnnEncounter, _provider.Id, "set-1");

        var done = await Completion(s => s.CompleteAsync(plan.Id, request, S.Actor, default));

        Assert.Equal([ChartOutcomes.Updated, ChartOutcomes.AlreadyCompleted], done.Procedures.OrderBy(p => p.ItemNumber).Select(p => p.ChartOutcome));   // the second sees the first's move
        var written = (await VersionsAsync(finding)).Single(v => v.ChangeType == FindingChangeTypes.StateChanged).VersionNumber;
        Assert.Equal([written, written], (await Task.WhenAll(plan.Items.Take(2).Select(i => RecordAsync(i.Id)))).Select(c => c.ToothFindingVersionNumber));
        Assert.True((await ReportAsync()).Clean);
    }

    [Theory]
    [InlineData("missingVersion")]
    [InlineData("nonCompletedVersion")]
    [InlineData("versionDoesNotExist")]
    [InlineData("versionAfterTheCompletion")]
    [InlineData("withdrawnVersion")]
    [InlineData("alteredHistory")]
    public async Task Missing_altered_or_non_completed_chart_evidence_is_reported_as_a_chart_mismatch(string which)
    {
        var (plan, planned) = await LegacyToothAsync();                                                           // version 1 of this finding is Planned
        var completed = await FindingAsync("16", "O", FindingStates.Completed, condition: FindingConditions.Restoration);   // version 1 of this one is Completed (recorded now)
        var set = SetRow(plan, revision: plan.CurrentRevision);
        var row = ProcedureRow(set, plan.Items[0], AnnEncounter, _provider.Id);
        row.ChartOutcome = ChartOutcomes.Updated;
        (row.ToothFindingId, row.ToothFindingVersionNumber) = which switch
        {
            "missingVersion" => (planned, (int?)null),
            "nonCompletedVersion" => (planned, 1),
            "versionDoesNotExist" => (planned, 99),
            "withdrawnVersion" => (completed, 2),                                                                 // version 2 below: still a Completed state, but the finding was withdrawn
            _ => (completed, 1),
        };
        if (which == "withdrawnVersion")
        {
            await WithdrawAsync(completed, SchedulingTestSupport.Clock);
            row.FinalizedAtUtc = DateTimeOffset.UtcNow.AddMinutes(1);                                             // after the withdrawal, so the version is not newer than the completion and only its Withdrawn status is wrong
        }
        if (which == "versionAfterTheCompletion") row.FinalizedAtUtc = DateTimeOffset.UtcNow.AddHours(-1);        // the named version is newer than the completion
        await Sql("ALTER TABLE [CompletedProcedures] DISABLE TRIGGER [TR_CompletedProcedures_ChartEvidence]");   // what the database would refuse at insert, seeded around it
        try { await AddRowsAsync(set, row); }
        finally { await Sql("ALTER TABLE [CompletedProcedures] ENABLE TRIGGER [TR_CompletedProcedures_ChartEvidence]"); }
        if (which == "alteredHistory")
        {
            Assert.Equal(0, (await ReportAsync()).ChartMismatch);                                                // valid while the history is intact
            await Sql("ALTER TABLE [ToothFindingVersions] DISABLE TRIGGER [TR_ToothFindingVersions_Immutable]; UPDATE [ToothFindingVersions] SET [State] = 'Planned' WHERE [FindingId] = '" + completed + "'; ALTER TABLE [ToothFindingVersions] ENABLE TRIGGER [TR_ToothFindingVersions_Immutable];");
        }

        var r = await ReportAsync();
        Assert.Equal(1, r.ChartMismatch);
        Assert.False(r.Clean);
    }

    // ---------- the database on its own ----------

    [Theory]
    [InlineData("missingVersion", 51173)]
    [InlineData("nonCompletedVersion", 51173)]
    [InlineData("versionDoesNotExist", 51173)]
    [InlineData("withdrawnVersion", 51173)]
    [InlineData("versionAfterTheCompletion", 51173)]
    public async Task The_database_refuses_chart_evidence_that_is_missing_not_completed_withdrawn_or_newer_than_the_completion(string which, int number)
    {
        var (plan, planned) = await LegacyToothAsync();
        var set = SetRow(plan, revision: plan.CurrentRevision);
        var row = ProcedureRow(set, plan.Items[0], AnnEncounter, _provider.Id);
        row.ChartOutcome = ChartOutcomes.Updated;
        switch (which)
        {
            case "missingVersion": (row.ToothFindingId, row.ToothFindingVersionNumber) = (planned, null); break;
            case "nonCompletedVersion": (row.ToothFindingId, row.ToothFindingVersionNumber) = (planned, 1); break;
            case "versionDoesNotExist": (row.ToothFindingId, row.ToothFindingVersionNumber) = (planned, 99); break;
            case "withdrawnVersion":
                var done = await FindingAsync("16", "O", FindingStates.Completed, condition: FindingConditions.Restoration);
                await WithdrawAsync(done, SchedulingTestSupport.Clock);                                          // version 2 shows it Completed but Withdrawn
                (row.ToothFindingId, row.ToothFindingVersionNumber) = (done, 2);
                break;
            default:
                var later = await FindingAsync("16", "O", FindingStates.Completed, condition: FindingConditions.Restoration);
                (row.ToothFindingId, row.ToothFindingVersionNumber) = (later, 1);
                row.FinalizedAtUtc = DateTimeOffset.UtcNow.AddHours(-1);
                break;
        }
        var e = await SqlRefusal(() => AddRowsAsync(set, row));
        Assert.Equal(number, e.Number);
        Assert.Equal((0, 0, 0), await CompletionCountsAsync());
    }

    [Theory]
    [InlineData("versionWithoutFinding")]
    [InlineData("versionZero")]
    public async Task The_database_refuses_a_version_number_that_has_no_finding_or_is_not_positive(string which)
    {
        var (plan, _) = await LegacyToothAsync();
        var set = SetRow(plan, revision: plan.CurrentRevision);
        var row = ProcedureRow(set, plan.Items[0], AnnEncounter, _provider.Id);
        if (which == "versionWithoutFinding") row.ToothFindingVersionNumber = 1;
        else
        {
            var done = await FindingAsync("16", "O", FindingStates.Completed, condition: FindingConditions.Restoration);
            row.ChartOutcome = ChartOutcomes.AlreadyCompleted;
            (row.ToothFindingId, row.ToothFindingVersionNumber) = (done, 0);
        }
        var e = await SqlRefusal(() => AddRowsAsync(set, row));
        Assert.Contains("CK_CompletedProcedures_ChartVersion", e.Message);
        Assert.Equal((0, 0, 0), await CompletionCountsAsync());
    }

    [Fact]
    public async Task Evidence_that_names_a_completed_active_version_not_newer_than_the_completion_is_accepted()
    {
        var (plan, _) = await LegacyToothAsync();
        var done = await FindingAsync("16", "O", FindingStates.Completed, condition: FindingConditions.Restoration);
        var set = SetRow(plan, revision: plan.CurrentRevision);
        var row = ProcedureRow(set, plan.Items[0], AnnEncounter, _provider.Id);
        row.ChartOutcome = ChartOutcomes.AlreadyCompleted;
        (row.ToothFindingId, row.ToothFindingVersionNumber) = (done, 1);
        await AddRowsAsync(set, row);
        Assert.Equal((1, 1, 0), await CompletionCountsAsync());
    }

    // ---------- the read of the evidence ----------

    [Fact]
    public async Task A_finding_that_is_not_an_active_completed_state_when_its_version_is_read_is_refused_not_recorded()
    {
        var (_, planned) = await LegacyToothAsync();                                                              // Planned
        var withdrawn = await FindingAsync("16", "O", FindingStates.Completed, condition: FindingConditions.Restoration);
        await WithdrawAsync(withdrawn, SchedulingTestSupport.Clock);
        var completed = await FindingAsync("17", "O", FindingStates.Completed);

        await using var db = Fixture.CreateContext();
        var chart = new OdontogramService(db, SchedulingTestSupport.Clock);
        Assert.Equal(1, (await chart.CompletedVersionsAsync([completed], default))[completed]);
        Assert.Equal("finding_changed", (await Assert.ThrowsAsync<OdontogramException>(() => chart.CompletedVersionsAsync([planned], default))).Code);
        Assert.Equal("finding_changed", (await Assert.ThrowsAsync<OdontogramException>(() => chart.CompletedVersionsAsync([withdrawn], default))).Code);
        Assert.Equal("finding_not_found", (await Assert.ThrowsAsync<OdontogramException>(() => chart.CompletedVersionsAsync([Guid.NewGuid()], default))).Code);
        Assert.Empty(await chart.CompletedVersionsAsync([], default));
    }

    // ---------- the migration ----------

    [Fact]
    public async Task Rolling_back_and_forward_restores_the_evidence_as_the_first_active_completed_version_which_a_same_instant_withdrawal_cannot_displace()
    {
        var (plan, finding) = await LegacyToothAsync();
        var instant = DateTimeOffset.UtcNow.AddMinutes(1);
        var clock = new TestClock(instant);
        await AdoptAsync(plan, instant, clock);
        await WithDb(d => new OdontogramService(d, clock).LinkAsync(finding, LinkTypes.TreatmentPlan, "plan-2026-07", S.Actor, default));   // version 3: still an active Completed state, same instant as version 2
        await WithdrawAsync(finding, clock);                                                                      // version 4: Completed state, Withdrawn status, same instant again
        var stored = (await RecordAsync(plan.Items[0].Id)).ToothFindingVersionNumber;
        var versions = await VersionsAsync(finding);
        Assert.Equal([1, 2, 3, 4], versions.Select(v => v.VersionNumber));
        Assert.Equal([FindingChangeTypes.Recorded, FindingChangeTypes.StateChanged, FindingChangeTypes.Linked, FindingChangeTypes.Withdrawn], versions.Select(v => v.ChangeType));
        Assert.Equal(2, stored);

        await using (var db = Fixture.CreateContext()) await db.GetInfrastructure().GetRequiredService<IMigrator>().MigrateAsync(BeforeEvidence);
        await using (var db = Fixture.CreateContext())
            Assert.Equal(0, await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM sys.columns WHERE object_id = OBJECT_ID('CompletedProcedures') AND name = 'ToothFindingVersionNumber'").SingleAsync());
        await using (var db = Fixture.CreateContext()) await db.Database.MigrateAsync();

        Assert.Equal(2, (await RecordAsync(plan.Items[0].Id)).ToothFindingVersionNumber);                         // the first active Completed version at or before the completion: neither the Linked version 3 nor the withdrawn version 4 shares-and-wins, whatever their instant
        await using (var db = Fixture.CreateContext())
        {
            Assert.Equal(1, await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM sys.triggers WHERE name = 'TR_CompletedProcedures_ChartEvidence'").SingleAsync());
            Assert.Equal(1, await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM sys.triggers WHERE name = 'TR_CompletedProcedures_Immutable' AND is_disabled = 0").SingleAsync());   // switched off for the backfill only
        }
        Assert.True((await ReportAsync()).Clean);
        var e = await SqlRefusal(() => Sql("UPDATE [CompletedProcedures] SET [ToothFindingVersionNumber] = 1"));      // and the record is still immutable
        Assert.Equal(51145, e.Number);
    }

    [Theory]
    [InlineData("findingNeverCompleted")]
    [InlineData("completedOnlyAfterTheCompletion")]
    public async Task A_completion_whose_evidence_cannot_be_derived_in_the_backfill_stays_empty_and_is_reported(string which)
    {
        var (plan, planned) = await LegacyToothAsync();
        var set = SetRow(plan, revision: plan.CurrentRevision);
        var row = ProcedureRow(set, plan.Items[0], AnnEncounter, _provider.Id);
        row.ChartOutcome = ChartOutcomes.Updated;
        if (which == "findingNeverCompleted") (row.ToothFindingId, row.ToothFindingVersionNumber) = (planned, 1);                  // a record from before the column, whose finding never reached Completed
        else
        {
            var later = await FindingAsync("16", "O", FindingStates.Completed, condition: FindingConditions.Restoration);              // its only Completed version is recorded now...
            (row.ToothFindingId, row.ToothFindingVersionNumber) = (later, 1);
            row.FinalizedAtUtc = DateTimeOffset.UtcNow.AddHours(-1);                                                                   // ...an hour after this completion was finalized
        }
        await Sql("ALTER TABLE [CompletedProcedures] DISABLE TRIGGER [TR_CompletedProcedures_ChartEvidence]");
        try { await AddRowsAsync(set, row); }
        finally { await Sql("ALTER TABLE [CompletedProcedures] ENABLE TRIGGER [TR_CompletedProcedures_ChartEvidence]"); }

        await using (var db = Fixture.CreateContext()) await db.GetInfrastructure().GetRequiredService<IMigrator>().MigrateAsync(BeforeEvidence);
        await using (var db = Fixture.CreateContext()) await db.Database.MigrateAsync();

        Assert.Null((await RecordAsync(plan.Items[0].Id)).ToothFindingVersionNumber);                             // no active Completed version at or before the completion exists, so nothing was invented
        Assert.Equal(1, (await ReportAsync()).ChartMismatch);
    }
}
