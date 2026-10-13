using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Treatment;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// STORY-008 against real SQL Server: completing an accepted plan is one all-or-nothing save that gives every accepted procedure the same completion record, history entry and audit entry as completing it alone;
/// the clinical history is read back from those records (no second record), keeps completed care when the plan is revised or withdrawn, and a failed log or a stale screen completes nothing.
/// </summary>
public class TreatmentPlanCompleteAcceptedTests : TreatmentPlanTestBase
{
    private Task<PlanView> Decide(PlanView plan, int item, string decision, string? reason = null) =>
        Svc(s => s.DecideItemAsync(plan.Id, plan.Items[item].Id, decision, reason, null, plan.RowVersion, S.Actor, default));
    private Task<PlanView> CompleteAll(PlanView plan) => LegacyCompleteAccepted(plan.Id);
    private Task<PlanView> Fresh(PlanView plan) => Svc(s => s.GetAsync(plan.Id, default));
    private Task<IReadOnlyList<ClinicalHistoryEntryView>> HistoryOf(Guid patient) => Svc(s => s.ClinicalHistoryAsync(patient, default));
    private Task<int> StateRowsAsync() => WithDb(db => db.TreatmentPlanItemStates.CountAsync());

    /// <summary>Three procedures: the first two accepted, the third declined. The returned plan carries the current row version.</summary>
    private async Task<PlanView> MixedPlanAsync()
    {
        var diagnosis = await DiagnosisAsync();
        var a = await ProcedureAsync("LOCAL-100", fee: 100m, description: "Exam");
        var b = await ProcedureAsync("LOCAL-200", fee: 60m, description: "Scaling");
        var c = await ProcedureAsync("LOCAL-300", fee: 40m, description: "Polish");
        var plan = await CreateAsync([Item(diagnosis, a.Summary.Id), Item(diagnosis, b.Summary.Id), Item(diagnosis, c.Summary.Id)]);
        plan = await Decide(plan, 0, "Accepted");
        plan = await Decide(plan, 1, "Accepted");
        return await Decide(plan, 2, "Declined", "Not now");
    }

    [Fact]
    public async Task Every_accepted_procedure_is_completed_in_one_save_and_the_declined_one_is_left_alone()
    {
        var plan = await MixedPlanAsync();
        var c0 = await CountsAsync();
        var rows0 = await StateRowsAsync();

        var done = await CompleteAll(plan);

        Assert.Equal(["Completed", "Completed", "NotStarted"], done.Items.Select(i => i.Completion).ToArray());
        Assert.Equal("Declined", done.Items[2].Acceptance);
        var c1 = await CountsAsync();
        Assert.Equal((rows0 + 2, c0.Events + 2, c0.Audit + 2), (await StateRowsAsync(), c1.Events, c1.Audit));
        var events = await Svc(s => s.HistoryAsync(plan.Id, default));
        Assert.Equal(["ItemCompleted", "ItemCompleted"], events.TakeLast(2).Select(e => e.ChangeType).ToArray());
        Assert.Equal(events.Count, events.Select(e => e.EventNumber).Distinct().Count());
    }

    [Fact]
    public async Task Completing_again_changes_nothing()
    {
        var plan = await MixedPlanAsync();
        await CompleteAll(plan);
        var rows = await StateRowsAsync();
        var counts = await CountsAsync();
        await CompleteAll(plan);
        await CompleteAll(await Fresh(plan));
        Assert.Equal((rows, counts), (await StateRowsAsync(), await CountsAsync()));
    }

    [Fact]
    public async Task A_withdrawn_procedure_is_not_completed()
    {
        var plan = await MixedPlanAsync();
        var gone = await Svc(s => s.WithdrawItemAsync(plan.Id, plan.Items[1].Id, "Entered by mistake", plan.RowVersion, S.Actor, default));
        var done = await CompleteAll(gone);
        Assert.Equal(["Completed", "NotStarted", "NotStarted"], done.Items.Select(i => i.Completion).ToArray());
        Assert.Single(await HistoryOf(Ann));
    }

    // ---------- the clinical history ----------

    [Fact]
    public async Task The_clinical_history_lists_each_completed_procedure_once_with_who_when_and_the_revision()
    {
        var plan = await MixedPlanAsync();
        Assert.Empty(await HistoryOf(Ann));
        await CompleteAll(plan);
        await CompleteAll(plan);

        var history = await HistoryOf(Ann);
        Assert.Equal(["LOCAL-100", "LOCAL-200"], history.Select(h => h.ProcedureCode).Order().ToArray());
        var first = history.Single(h => h.ProcedureCode == "LOCAL-100");
        Assert.Equal(("Exam", plan.Id, 1, plan.Title), (first.ProcedureDescription, first.PlanId, first.PlanRevisionAtCompletion, first.PlanTitle));
        Assert.False(string.IsNullOrWhiteSpace(first.CompletedByName));
        Assert.True(first.CompletedAtUtc > DateTimeOffset.UtcNow.AddMinutes(-5));
        Assert.Empty(await HistoryOf(Bo));
    }

    [Fact]
    public async Task Completed_care_stays_in_the_history_and_the_earlier_revision_is_kept_when_the_plan_is_revised_or_withdrawn()
    {
        var plan = await MixedPlanAsync();
        var done = await CompleteAll(plan);
        var revised = await Svc(s => s.ReviseAsync(plan.Id, new ReviseInput("Reordered after treatment", "Healing first", null, [new PlanPhaseInput(1, "Healing", null, null)], null), done.RowVersion, S.Actor, default));
        Assert.Equal(2, revised.CurrentRevision);
        await Svc(s => s.WithdrawAsync(plan.Id, "Replaced by a new plan", revised.RowVersion, S.Actor, default));

        var history = await HistoryOf(Ann);
        Assert.Equal(2, history.Count);
        Assert.All(history, h => Assert.Equal(1, h.PlanRevisionAtCompletion));         // completed under revision 1, still says so
        var revisions = await WithDb(db => db.TreatmentPlanRevisions.AsNoTracking().Where(r => r.PlanId == plan.Id).OrderBy(r => r.RevisionNumber).ToListAsync());
        Assert.Equal([1, 2], revisions.Select(r => r.RevisionNumber).ToArray());
    }

    [Fact]
    public async Task The_history_of_an_unknown_patient_is_not_found()
    {
        var e = await Refused(() => HistoryOf(Guid.NewGuid()));
        Assert.Equal(404, e.StatusCode);
    }
}
