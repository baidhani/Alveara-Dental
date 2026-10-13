using Alveara.Api.Architecture.Completion;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Odontogram;
using Alveara.Api.Architecture.Treatment;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-008-C01 against real SQL Server: the database refuses, on its own, what the completion rules refuse and what no service rule can see. These tests go around the service on purpose - a script, a future
/// module or a person with database access cannot change or delete a completion set, a completed procedure or an amendment, record a completion whose plan, patient, revision, catalog version, tooth, surface,
/// encounter or chart finding do not belong together, record one for a procedure that is not completed, perform one before its plan existed or before it was accepted, complete the same plan procedure twice,
/// reuse a key, or amend with a gap in the numbers, without a reason, without correcting anything, or into another patient's encounter.
/// </summary>
public class CompletedProcedureSchemaTests : CompletionTestBase
{
    private sealed record Ready(BillablePlan Plan, PlanItemView Item, ProviderProfile Provider);
    private sealed record Written(Ready Ready, CompletionSet Set, CompletedProcedure Procedure);

    /// <summary>A plan whose first procedure is completed, a provider and an encounter: what a completion record needs to refer to.</summary>
    private async Task<Ready> ReadyAsync()
    {
        var plan = await BillablePlanAsync();
        return new Ready(plan, plan.Plan.Items.Single(i => i.Id == plan.Completed), await ProviderAsync());
    }

    private async Task<Written> WrittenAsync()
    {
        var ready = await ReadyAsync();
        var set = SetRow(ready.Plan.Plan, revision: ready.Plan.Plan.CurrentRevision);
        var procedure = ProcedureRow(set, ready.Item, AnnEncounter, ready.Provider.Id);
        await AddRowsAsync(set, procedure);
        return new Written(ready, set, procedure);
    }

    // ---------- the rows that are allowed ----------

    [Fact]
    public async Task A_set_a_completed_procedure_and_amendments_numbered_in_order_are_accepted()
    {
        var w = await WrittenAsync();
        var other = await ProviderAsync("Dr. Other");
        await AddRowsAsync(AmendmentRow(w.Procedure, 1, "amend-1", provider: other.Id));
        await AddRowsAsync(AmendmentRow(w.Procedure, 2, "amend-2", performed: DateTimeOffset.UtcNow, encounter: AnnEncounter));
        Assert.Equal((1, 1, 2), await CompletionCountsAsync());
    }

    [Fact]
    public async Task A_completion_that_moved_a_chart_finding_names_that_finding_on_the_procedures_tooth_and_surface()
    {
        var (plan, item) = await CompletedToothPlanAsync();
        var finding = await FindingAsync("16", "O", FindingStates.Completed);                         // recorded as Completed, so its version 1 is the evidence (ALV-008-C02 R02: a finding is named with the version that shows it Completed)
        var provider = await ProviderAsync();
        var set = SetRow(plan, revision: plan.CurrentRevision);
        var procedure = ProcedureRow(set, item, AnnEncounter, provider.Id);
        procedure.ChartOutcome = ChartOutcomes.AlreadyCompleted;
        procedure.ToothFindingId = finding;
        procedure.ToothFindingVersionNumber = 1;
        await AddRowsAsync(set, procedure);
        Assert.Equal((1, 1, 0), await CompletionCountsAsync());
    }

    // ---------- shape rules (check constraints) ----------

    [Theory]
    [InlineData("keyBlank", "CK_CompletionSets_Key")]
    [InlineData("keyControl", "CK_CompletionSets_Key")]
    [InlineData("hashShort", "CK_CompletionSets_Hash")]
    [InlineData("hashUpper", "CK_CompletionSets_Hash")]
    [InlineData("hashNotHex", "CK_CompletionSets_Hash")]
    [InlineData("countZero", "CK_CompletionSets_Counts")]
    [InlineData("revisionZero", "CK_CompletionSets_Counts")]
    public async Task The_database_refuses_a_completion_set_that_breaks_a_shape_rule(string which, string constraint)
    {
        var ready = await ReadyAsync();
        var set = SetRow(ready.Plan.Plan, revision: ready.Plan.Plan.CurrentRevision);
        switch (which)
        {
            case "keyBlank": set.IdempotencyKey = "   "; break;
            case "keyControl": set.IdempotencyKey = "key\u0001"; break;
            case "hashShort": set.RequestHash = "abc"; break;
            case "hashUpper": set.RequestHash = new string('A', 64); break;
            case "hashNotHex": set.RequestHash = new string('g', 64); break;
            case "countZero": set.ItemCount = 0; break;
            case "revisionZero": set.PlanRevisionNumber = 0; break;
            default: throw new ArgumentOutOfRangeException(nameof(which));
        }
        var e = await SqlRefusal(() => AddRowsAsync(set));
        Assert.Contains(constraint, e.Message);
        Assert.Equal((0, 0, 0), await CompletionCountsAsync());
    }

    [Theory]
    [InlineData("updatedWithoutFinding", "CK_CompletedProcedures_Chart")]
    [InlineData("alreadyWithoutFinding", "CK_CompletedProcedures_Chart")]
    [InlineData("notApplicableWithFinding", "CK_CompletedProcedures_Chart")]
    [InlineData("unknownOutcome", "CK_CompletedProcedures_Chart")]
    [InlineData("surfaceWithoutTooth", "CK_CompletedProcedures_Surface")]
    [InlineData("performedTooEarly", "CK_CompletedProcedures_Performed")]
    [InlineData("performedFarFuture", "CK_CompletedProcedures_Performed")]
    [InlineData("revisionZero", "CK_CompletedProcedures_Revision")]
    public async Task The_database_refuses_a_completed_procedure_that_breaks_a_shape_rule(string which, string constraint)
    {
        var ready = await ReadyAsync();
        var set = SetRow(ready.Plan.Plan, revision: ready.Plan.Plan.CurrentRevision);
        var procedure = ProcedureRow(set, ready.Item, AnnEncounter, ready.Provider.Id);
        switch (which)
        {
            case "updatedWithoutFinding": procedure.ChartOutcome = ChartOutcomes.Updated; break;
            case "alreadyWithoutFinding": procedure.ChartOutcome = ChartOutcomes.AlreadyCompleted; break;
            case "notApplicableWithFinding": procedure.ToothFindingId = await FindingAsync("16", "O"); break;
            case "unknownOutcome": procedure.ChartOutcome = "Maybe"; break;
            case "surfaceWithoutTooth": procedure.Surface = "O"; break;
            case "performedTooEarly": procedure.PerformedAtUtc = new DateTimeOffset(1999, 12, 31, 0, 0, 0, TimeSpan.Zero); break;
            case "performedFarFuture": procedure.PerformedAtUtc = new DateTimeOffset(2300, 1, 1, 0, 0, 0, TimeSpan.Zero); break;
            case "revisionZero": procedure.PlanRevisionNumber = 0; break;
            default: throw new ArgumentOutOfRangeException(nameof(which));
        }
        var e = await SqlRefusal(() => AddRowsAsync(set, procedure));
        Assert.Contains(constraint, e.Message);
        Assert.Equal((0, 0, 0), await CompletionCountsAsync());
    }

    [Theory]
    [InlineData("reasonBlank", "CK_CompletedProcedureAmendments_Reason")]
    [InlineData("reasonControl", "CK_CompletedProcedureAmendments_Reason")]
    [InlineData("noCorrection", "CK_CompletedProcedureAmendments_Changes")]
    [InlineData("hashShort", "CK_CompletedProcedureAmendments_Hash")]
    [InlineData("keyBlank", "CK_CompletedProcedureAmendments_Key")]
    [InlineData("numberZero", "CK_CompletedProcedureAmendments_Number")]
    [InlineData("performedTooEarly", "CK_CompletedProcedureAmendments_Performed")]
    public async Task The_database_refuses_an_amendment_that_breaks_a_shape_rule(string which, string constraint)
    {
        var w = await WrittenAsync();
        var amendment = AmendmentRow(w.Procedure, 1, "amend-1", provider: w.Ready.Provider.Id);
        switch (which)
        {
            case "reasonBlank": amendment.Reason = "   "; break;
            case "reasonControl": amendment.Reason = "wrong\u0007 provider"; break;
            case "noCorrection": amendment.ProviderProfileId = null; break;
            case "hashShort": amendment.RequestHash = "abc"; break;
            case "keyBlank": amendment.IdempotencyKey = " "; break;
            case "numberZero": amendment.AmendmentNumber = 0; break;
            case "performedTooEarly": amendment.PerformedAtUtc = new DateTimeOffset(1999, 12, 31, 0, 0, 0, TimeSpan.Zero); break;
            default: throw new ArgumentOutOfRangeException(nameof(which));
        }
        var e = await SqlRefusal(() => AddRowsAsync(amendment));
        Assert.Contains(constraint, e.Message);
        Assert.Equal((1, 1, 0), await CompletionCountsAsync());
    }

    // ---------- append-only ----------

    [Theory]
    [InlineData("UPDATE [CompletionSets] SET [ItemCount] = 2", 51144)]
    [InlineData("DELETE FROM [CompletionSets]", 51144)]
    [InlineData("UPDATE [CompletedProcedures] SET [ChartOutcome] = 'Updated'", 51145)]
    [InlineData("UPDATE [CompletedProcedures] SET [PerformedAtUtc] = SYSDATETIMEOFFSET()", 51145)]
    [InlineData("DELETE FROM [CompletedProcedures]", 51145)]
    [InlineData("UPDATE [CompletedProcedureAmendments] SET [Reason] = 'Rewritten'", 51146)]
    [InlineData("DELETE FROM [CompletedProcedureAmendments]", 51146)]
    public async Task A_completion_set_a_completed_procedure_and_an_amendment_can_never_be_changed_or_deleted(string statement, int number)
    {
        var w = await WrittenAsync();
        await AddRowsAsync(AmendmentRow(w.Procedure, 1, "amend-1", provider: w.Ready.Provider.Id));
        var e = await SqlRefusal(() => ExecuteAsync(statement));
        Assert.Equal(number, e.Number);
        Assert.Equal((1, 1, 1), await CompletionCountsAsync());
    }

    // ---------- uniqueness ----------

    [Fact]
    public async Task A_plan_procedure_is_completed_once_and_a_key_is_used_once_per_plan()
    {
        var w = await WrittenAsync();

        var again = SetRow(w.Ready.Plan.Plan, key: "set-2", revision: w.Set.PlanRevisionNumber);
        var twice = await SqlRefusal(() => AddRowsAsync(again, ProcedureRow(again, w.Ready.Item, AnnEncounter, w.Ready.Provider.Id)));
        Assert.Contains(twice.Number, new[] { 2601, 2627 });

        var sameKey = SetRow(w.Ready.Plan.Plan, key: "set-1", revision: w.Set.PlanRevisionNumber);
        var reused = await SqlRefusal(() => AddRowsAsync(sameKey));
        Assert.Contains(reused.Number, new[] { 2601, 2627 });
        Assert.Equal((1, 1, 0), await CompletionCountsAsync());
    }

    // ---------- links (triggers) ----------

    [Fact]
    public async Task A_completion_set_must_carry_the_patient_of_its_plan()
    {
        var ready = await ReadyAsync();
        var e = await SqlRefusal(() => AddRowsAsync(SetRow(ready.Plan.Plan, patient: Bo, revision: ready.Plan.Plan.CurrentRevision)));
        Assert.Equal(51156, e.Number);
        Assert.Equal((0, 0, 0), await CompletionCountsAsync());
    }

    [Theory]
    [InlineData("otherPlan", 51147)]
    [InlineData("revisionMismatch", 51147)]
    [InlineData("otherPlanItem", 51147)]
    [InlineData("versionMismatch", 51148)]
    [InlineData("toothMismatch", 51148)]
    [InlineData("surfaceMismatch", 51148)]
    [InlineData("otherPatientsEncounter", 51149)]
    [InlineData("itemNotCompleted", 51150)]
    [InlineData("performedBeforePlan", 51153)]
    [InlineData("performedBeforeAcceptance", 51154)]
    public async Task The_database_refuses_a_completed_procedure_that_does_not_belong_with_its_set_plan_item_encounter_or_time(string which, int number)
    {
        var ready = await ReadyAsync();
        var plan = ready.Plan.Plan;
        var set = SetRow(plan, revision: plan.CurrentRevision);
        var procedure = ProcedureRow(set, ready.Item, AnnEncounter, ready.Provider.Id);
        switch (which)
        {
            case "otherPlan":
            case "otherPlanItem":
            case "versionMismatch":
                var diagnosis = await DiagnosisAsync();
                var elsewhere = await ProcedureAsync("LOCAL-900", description: "Review");
                var second = await CreateAsync([Item(diagnosis, elsewhere.Summary.Id)], title: "Second plan");
                if (which == "otherPlan") procedure.PlanId = second.Id;
                else if (which == "otherPlanItem") procedure.PlanItemId = second.Items[0].Id;
                else procedure.ProcedureVersionId = second.Items[0].ProcedureVersionId;
                break;
            case "revisionMismatch": procedure.PlanRevisionNumber = set.PlanRevisionNumber + 1; break;
            case "toothMismatch": procedure.ToothKey = "17"; break;
            case "surfaceMismatch": procedure.ToothKey = "17"; procedure.Surface = "O"; break;
            case "otherPatientsEncounter": procedure.EncounterId = BoEncounter; break;
            case "itemNotCompleted":
                var open = plan.Items.Single(i => i.Id == ready.Plan.Accepted);                     // accepted but not completed; the row is otherwise consistent with it
                procedure.PlanItemId = open.Id;
                procedure.ProcedureVersionId = open.ProcedureVersionId;
                break;
            case "performedBeforePlan": procedure.PerformedAtUtc = plan.CreatedAtUtc.AddMinutes(-5); break;
            case "performedBeforeAcceptance":
                var accepted = await AcceptedAtAsync(ready.Item.Id);
                Assert.True(accepted.AddTicks(-1) >= plan.CreatedAtUtc, "the test needs the acceptance to come after the plan was created");
                procedure.PerformedAtUtc = accepted.AddTicks(-1);
                break;
            default: throw new ArgumentOutOfRangeException(nameof(which));
        }
        var e = await SqlRefusal(() => AddRowsAsync(set, procedure));
        Assert.Equal(number, e.Number);
        Assert.Equal((0, 0, 0), await CompletionCountsAsync());
    }

    [Theory]
    [InlineData("otherPatientsFinding")]
    [InlineData("otherToothFinding")]
    [InlineData("otherSurfaceFinding")]
    public async Task A_chart_finding_must_be_the_patients_own_on_the_procedures_tooth_and_surface(string which)
    {
        var (plan, item) = await CompletedToothPlanAsync();
        var provider = await ProviderAsync();
        var finding = which switch                                                                  // each recorded as Completed so its version 1 is valid evidence and only the belonging rule is in play
        {
            "otherPatientsFinding" => await FindingAsync("16", "O", FindingStates.Completed, patient: Bo),
            "otherToothFinding" => await FindingAsync("17", "O", FindingStates.Completed),
            _ => await FindingAsync("16", "M", FindingStates.Completed),
        };
        var set = SetRow(plan, revision: plan.CurrentRevision);
        var procedure = ProcedureRow(set, item, AnnEncounter, provider.Id);
        procedure.ChartOutcome = ChartOutcomes.AlreadyCompleted;
        procedure.ToothFindingId = finding;
        procedure.ToothFindingVersionNumber = 1;
        var e = await SqlRefusal(() => AddRowsAsync(set, procedure));
        Assert.Equal(51149, e.Number);
        Assert.Equal((0, 0, 0), await CompletionCountsAsync());
    }

    [Theory]
    [InlineData("gap", 51152)]
    [InlineData("otherPatient", 51151)]
    [InlineData("otherPatientsEncounter", 51151)]
    [InlineData("beforePlan", 51155)]
    [InlineData("beforeAcceptance", 51155)]
    public async Task The_database_refuses_an_amendment_that_is_out_of_sequence_belongs_to_another_patient_or_predates_the_plan(string which, int number)
    {
        var w = await WrittenAsync();
        var amendment = AmendmentRow(w.Procedure, 1, "amend-1", provider: w.Ready.Provider.Id);
        switch (which)
        {
            case "gap": amendment.AmendmentNumber = 2; break;
            case "otherPatient": amendment.PatientId = Bo; break;
            case "otherPatientsEncounter": amendment.EncounterId = BoEncounter; break;
            case "beforePlan": amendment.PerformedAtUtc = w.Ready.Plan.Plan.CreatedAtUtc.AddMinutes(-5); break;
            case "beforeAcceptance":
                var accepted = await AcceptedAtAsync(w.Ready.Item.Id);
                Assert.True(accepted.AddTicks(-1) >= w.Ready.Plan.Plan.CreatedAtUtc, "the test needs the acceptance to come after the plan was created");
                amendment.PerformedAtUtc = accepted.AddTicks(-1);
                break;
            default: throw new ArgumentOutOfRangeException(nameof(which));
        }
        var e = await SqlRefusal(() => AddRowsAsync(amendment));
        Assert.Equal(number, e.Number);
        Assert.Equal((1, 1, 0), await CompletionCountsAsync());
    }

    [Fact]
    public async Task An_amendment_number_and_an_amendment_key_are_each_used_once_per_procedure()
    {
        var w = await WrittenAsync();
        await AddRowsAsync(AmendmentRow(w.Procedure, 1, "amend-1", provider: w.Ready.Provider.Id));

        var sameNumber = await SqlRefusal(() => AddRowsAsync(AmendmentRow(w.Procedure, 1, "amend-2", provider: w.Ready.Provider.Id)));
        Assert.Contains(sameNumber.Number, new[] { 2601, 2627, 51152 });          // a duplicate number is also a gap-rule breach, whichever the database finds first
        var sameKey = await SqlRefusal(() => AddRowsAsync(AmendmentRow(w.Procedure, 2, "amend-1", provider: w.Ready.Provider.Id)));
        Assert.Contains(sameKey.Number, new[] { 2601, 2627 });
        Assert.Equal((1, 1, 1), await CompletionCountsAsync());
    }
}
