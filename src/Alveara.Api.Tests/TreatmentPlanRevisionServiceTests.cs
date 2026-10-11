using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Procedures;
using Alveara.Api.Architecture.Treatment;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-015-C01 against real SQL Server: revisions and phases. Every change to a plan's content saves a new revision that copies the previous one with the change applied, and an earlier revision is never touched;
/// an explicit revision lays out the rationale, the patient-facing explanation, the phases and where each procedure sits, needs a reason and the current version, names every wrong field at once and stores nothing
/// when anything is wrong; sending what the plan already says saves nothing; a withdrawn plan cannot be revised.
/// </summary>
public class TreatmentPlanRevisionServiceTests : TreatmentPlanTestBase
{
    private Task<PlanView> ReviseAsync(PlanView plan, ReviseInput input, string? version = null) =>
        Svc(s => s.ReviseAsync(plan.Id, input, version ?? plan.RowVersion, S.Actor, default));

    private static ReviseInput Revise(string? reason = "Phased for healing", string? rationale = null, string? explanation = null, IReadOnlyList<PlanPhaseInput>? phases = null, IReadOnlyList<PlanPlacementInput>? placements = null) =>
        new(reason, rationale, explanation, phases, placements);

    private static PlanPhaseInput Phase(int number, string name = "Phase", string? visit = null, DateOnly? date = null) => new(number, $"{name} {number}", visit, date);

    private async Task<List<TreatmentPlanRevision>> RevisionsAsync(Guid planId)
    {
        await using var db = Fixture.CreateContext();
        return await db.TreatmentPlanRevisions.AsNoTracking().Where(r => r.PlanId == planId).OrderBy(r => r.RevisionNumber).ToListAsync();
    }

    private async Task<Dictionary<Guid, (bool Withdrawn, int? Phase)>> HeldAsync(Guid planId, int revision)
    {
        await using var db = Fixture.CreateContext();
        var id = await db.TreatmentPlanRevisions.Where(r => r.PlanId == planId && r.RevisionNumber == revision).Select(r => r.Id).SingleAsync();
        return await db.TreatmentPlanRevisionItems.AsNoTracking().Where(i => i.RevisionId == id).ToDictionaryAsync(i => i.ItemId, i => (i.IsWithdrawn, i.PhaseNumber));
    }

    // ---------- the four everyday changes each save the next revision ----------

    [Fact]
    public async Task Creating_a_plan_saves_revision_one_and_every_change_after_it_saves_the_next_without_touching_the_earlier_ones()
    {
        var (plan, diagnosis, _) = await SimplePlanAsync(50m);
        Assert.Equal(1, plan.CurrentRevision);
        var first = Assert.Single(await RevisionsAsync(plan.Id));
        Assert.Equal(("Created", 2, "Periodontal plan", 1), (first.ChangeType, first.EventNumber, first.Title, first.RevisionNumber));      // Created and one ItemAdded: revision 1 is the plan as created
        Assert.Equal([plan.Items[0].Id], (await HeldAsync(plan.Id, 1)).Keys.ToArray());

        var filling = await ProcedureAsync("FILL-1", ProcedureScopes.ToothSurface, ProcedureDentitions.Both, 140m, "Resin filling");
        var added = await Svc(s => s.AddItemAsync(plan.Id, Item(diagnosis, filling.Summary.Id, "16", "O", "add-1"), plan.RowVersion, S.Actor, default));
        var renamed = await Svc(s => s.RenameAsync(plan.Id, "Gum health plan", added.RowVersion, S.Actor, default));
        var withdrawn = await Svc(s => s.WithdrawItemAsync(plan.Id, plan.Items[0].Id, "Patient declined", renamed.RowVersion, S.Actor, default));

        Assert.Equal(4, withdrawn.CurrentRevision);
        var revisions = await RevisionsAsync(plan.Id);
        Assert.Equal(["Created", "ItemAdded", "Renamed", "ItemWithdrawn"], revisions.Select(r => r.ChangeType).ToArray());
        Assert.Equal(["Periodontal plan", "Periodontal plan", "Gum health plan", "Gum health plan"], revisions.Select(r => r.Title).ToArray());
        Assert.Equal([null, null, null, "Patient declined"], revisions.Select(r => r.Reason).ToArray());
        Assert.Equal(revisions.Select(r => r.EventNumber).Distinct().Count(), revisions.Count);

        // each revision still says what it said: revision 1 has one live procedure, 2 has two, 4 has the first marked withdrawn
        var added2 = added.Items[1].Id;
        Assert.Single(await HeldAsync(plan.Id, 1));
        Assert.Equal((2, false), ((await HeldAsync(plan.Id, 2)).Count, (await HeldAsync(plan.Id, 2))[plan.Items[0].Id].Withdrawn));
        var last = await HeldAsync(plan.Id, 4);
        Assert.Equal((true, false), (last[plan.Items[0].Id].Withdrawn, last[added2].Withdrawn));

        // every history entry that made a revision names it
        var history = await WithDb(db => db.TreatmentPlanEvents.AsNoTracking().Where(e => e.PlanId == plan.Id).OrderBy(e => e.EventNumber).Select(e => e.RevisionNumber).ToListAsync());
        Assert.Equal([1, 1, 2, 3, 4], history.ToArray());
    }

    [Fact]
    public async Task Saving_the_same_title_adds_no_revision_and_a_stale_everyday_change_is_a_conflict_that_adds_none()
    {
        var (plan, diagnosis, _) = await SimplePlanAsync();
        await Svc(s => s.RenameAsync(plan.Id, plan.Title, plan.RowVersion, S.Actor, default));
        Assert.Single(await RevisionsAsync(plan.Id));
        var other = await ProcedureAsync("LOCAL-200");
        await Svc(s => s.AddItemAsync(plan.Id, Item(diagnosis, other.Summary.Id, key: "a"), plan.RowVersion, S.Actor, default));
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => Svc(s => s.RenameAsync(plan.Id, "Too late", plan.RowVersion, S.Actor, default)));
        Assert.Equal(2, (await RevisionsAsync(plan.Id)).Count);
    }

    // ---------- an explicit revision ----------

    [Fact]
    public async Task A_revision_lays_out_phases_and_places_procedures_and_the_view_shows_it_while_the_earlier_revision_is_kept()
    {
        var (plan, diagnosis, _) = await SimplePlanAsync(50m);
        var second = await ProcedureAsync("LOCAL-200", fee: 90m, description: "Scaling");
        plan = await Svc(s => s.AddItemAsync(plan.Id, Item(diagnosis, second.Summary.Id, key: "a"), plan.RowVersion, S.Actor, default));

        var revised = await ReviseAsync(plan, Revise(
            rationale: "  Deep pockets\r\non the lower left.  ", explanation: "We will clean below the gums first.",
            phases: [Phase(2, "After healing", "Two weeks later", new DateOnly(2030, 6, 1)), Phase(1, "First visit", "Today")],
            placements: [new(plan.Items[0].Id, 1), new(plan.Items[1].Id, 2)]));

        Assert.Equal(3, revised.CurrentRevision);
        Assert.Equal(("Deep pockets\non the lower left.", "We will clean below the gums first."), (revised.ClinicalRationale, revised.PatientExplanation));
        Assert.Equal([(1, "First visit 1", "Today", null), (2, "After healing 2", "Two weeks later", new DateOnly(2030, 6, 1))],
            revised.Phases!.Select(p => (p.PhaseNumber, p.Name, p.PlannedVisit, p.TargetDate)).ToArray());
        Assert.Equal([1, 2], revised.Items.Select(i => i.PhaseNumber).ToArray());
        Assert.Equal(plan.EstimateTotal, revised.EstimateTotal);                                              // the money did not change

        var revisions = await RevisionsAsync(plan.Id);
        Assert.Equal(("Revised", "Phased for healing"), (revisions[2].ChangeType, revisions[2].Reason));
        Assert.All(revisions.Take(2), r => Assert.Null(r.ClinicalRationale));                                  // the earlier versions did not change
        Assert.All((await HeldAsync(plan.Id, 2)).Values, v => Assert.Null(v.Phase));
        var history = await Svc(s => s.HistoryAsync(plan.Id, default));
        Assert.Equal(("Revised", "Phased for healing"), (history[^1].ChangeType, history[^1].Reason));
        Assert.Equal(1, await WithDb(db => db.AuditLogEntries.CountAsync(a => a.EventType == "TreatmentPlanRevised")));
    }

    [Fact]
    public async Task Null_keeps_a_blank_clears_and_sending_what_the_plan_already_says_saves_nothing()
    {
        var (plan, _, _) = await SimplePlanAsync();
        var phases = new[] { Phase(1), Phase(2) };
        plan = await ReviseAsync(plan, Revise(rationale: "Because", explanation: "In words", phases: phases));
        Assert.Equal(2, plan.CurrentRevision);

        var again = await ReviseAsync(plan, Revise(reason: "Same again", rationale: "Because", explanation: "In words", phases: phases.Reverse().ToArray()), version: plan.RowVersion);
        Assert.Equal((2, plan.RowVersion), (again.CurrentRevision, again.RowVersion));                          // the same layout in another order is the same layout
        var kept = await ReviseAsync(plan, Revise(reason: "Only the words", explanation: "Plainer words"));
        Assert.Equal(("Because", "Plainer words", 2), (kept.ClinicalRationale, kept.PatientExplanation, kept.Phases!.Count));
        var cleared = await ReviseAsync(kept, Revise(reason: "Not needed", rationale: "   "));
        Assert.Equal((null, "Plainer words", 4), (cleared.ClinicalRationale, cleared.PatientExplanation, cleared.CurrentRevision));
        Assert.Equal(4, (await RevisionsAsync(plan.Id)).Count);
    }

    [Fact]
    public async Task Removing_a_phase_needs_its_procedures_moved_in_the_same_revision()
    {
        var (plan, _, _) = await SimplePlanAsync();
        plan = await ReviseAsync(plan, Revise(phases: [Phase(1), Phase(2)], placements: [new(plan.Items[0].Id, 2)]));
        var e = await Refused(() => ReviseAsync(plan, Revise(reason: "Merge", phases: [Phase(1)])));
        Assert.Contains("phases", e.FieldErrors.Keys);
        var moved = await ReviseAsync(plan, Revise(reason: "Merge", phases: [Phase(1)], placements: [new(plan.Items[0].Id, 1)]));
        Assert.Equal((1, 3), (moved.Items[0].PhaseNumber, moved.CurrentRevision));
        var none = await ReviseAsync(moved, Revise(reason: "No phases", phases: [], placements: [new(plan.Items[0].Id, null)]));
        Assert.Equal((null, 0), (none.Items[0].PhaseNumber, none.Phases!.Count));
    }

    [Fact]
    public async Task A_procedure_added_with_a_phase_is_placed_there_and_a_phase_the_plan_lacks_is_refused_on_that_field()
    {
        var (plan, diagnosis, _) = await SimplePlanAsync();
        plan = await ReviseAsync(plan, Revise(phases: [Phase(1)]));
        var other = await ProcedureAsync("LOCAL-200");
        var bad = await Refused(() => Svc(s => s.AddItemAsync(plan.Id, Item(diagnosis, other.Summary.Id, key: "a") with { PhaseNumber = 3 }, plan.RowVersion, S.Actor, default)));
        Assert.Equal("This plan has no phase 3.", bad.FieldErrors["phaseNumber"]);
        var ok = await Svc(s => s.AddItemAsync(plan.Id, Item(diagnosis, other.Summary.Id, key: "a") with { PhaseNumber = 1 }, plan.RowVersion, S.Actor, default));
        Assert.Equal([null, 1], ok.Items.Select(i => i.PhaseNumber).ToArray());
    }

    // ---------- comparing two revisions ----------

    [Fact]
    public async Task Two_revisions_are_compared_from_what_was_saved_naming_every_difference_and_the_estimate_each_came_to()
    {
        var (plan, diagnosis, _) = await SimplePlanAsync(50m);                                                            // revision 1: one procedure, $50
        var filling = await ProcedureAsync("FILL-1", ProcedureScopes.ToothSurface, ProcedureDentitions.Both, 140m, "Resin filling");
        plan = await Svc(s => s.AddItemAsync(plan.Id, Item(diagnosis, filling.Summary.Id, "16", "O", "add-1"), plan.RowVersion, S.Actor, default));      // revision 2: + $140
        plan = await ReviseAsync(plan, Revise(rationale: "Deep pockets", phases: [Phase(1, "First"), Phase(2, "Second", "Later", new DateOnly(2030, 6, 1))],
            placements: [new(plan.Items[0].Id, 1), new(plan.Items[1].Id, 2)]));                                            // revision 3
        plan = await ReviseAsync(plan, Revise(reason: "Merge", rationale: " ", phases: [new(1, "First visit", "Today", null)], placements: [new(plan.Items[1].Id, 1)]));          // revision 4: phase 2 gone, phase 1 renamed
        plan = await Svc(s => s.RenameAsync(plan.Id, "Gum health plan", plan.RowVersion, S.Actor, default));              // revision 5
        plan = await Svc(s => s.WithdrawItemAsync(plan.Id, plan.Items[0].Id, "Patient declined", plan.RowVersion, S.Actor, default));   // revision 6: the $50 procedure goes

        var all = await Svc(s => s.RevisionsAsync(plan.Id, default));
        Assert.Equal([(1, 1, 50m, 0), (2, 2, 190m, 0), (3, 2, 190m, 2), (4, 2, 190m, 1), (5, 2, 190m, 1), (6, 1, 140m, 1)], all.Select(r => (r.RevisionNumber, r.ProcedureCount, r.EstimateTotal, r.PhaseCount)).ToArray());

        var wide = await Svc(s => s.CompareAsync(plan.Id, 1, 6, default));
        Assert.Equal(("Periodontal plan", "Gum health plan"), (wide.Title!.From, wide.Title.To));
        Assert.Equal(["FILL-1"], wide.ItemsAdded.Select(i => i.ProcedureCode).ToArray());
        Assert.Equal(["LOCAL-100"], wide.ItemsWithdrawn.Select(i => i.ProcedureCode).ToArray());
        Assert.Equal((50m, 140m, 90m), (wide.EstimateFrom, wide.EstimateTo, wide.EstimateDelta));
        Assert.Equal(TreatmentPlanEstimate.Label, wide.EstimateLabel);

        var layout = await Svc(s => s.CompareAsync(plan.Id, 3, 4, default));
        Assert.Equal(["Second 2"], layout.PhasesRemoved.Select(p => p.Name).ToArray());
        Assert.Equal([("First 1", "First visit")], layout.PhasesChanged.Select(c => (c.From.Name, c.To.Name)).ToArray());
        Assert.Equal(("Deep pockets", null), (layout.ClinicalRationale!.From, layout.ClinicalRationale.To));
        Assert.Equal([(2, 1)], layout.ItemsMoved.Select(m => (m.FromPhase ?? 0, m.ToPhase ?? 0)).ToArray());
        Assert.Empty(layout.ItemsAdded);
        Assert.Equal(0m, layout.EstimateDelta);

        var same = await Svc(s => s.CompareAsync(plan.Id, 4, 4, default));                                                // a revision compared with itself differs in nothing
        Assert.Equal((null, null, 0, 0, 0, 0m), (same.Title, same.ClinicalRationale, same.ItemsAdded.Count + same.ItemsWithdrawn.Count, same.PhasesAdded.Count + same.PhasesRemoved.Count, same.ItemsMoved.Count, same.EstimateDelta));
        Assert.Equal("revision_not_found", (await Refused(() => Svc(s => s.CompareAsync(plan.Id, 1, 99, default)))).Code);

        var one = await Svc(s => s.RevisionAsync(plan.Id, 3, default));                                                   // an earlier revision reads exactly as it was saved
        Assert.Equal(("Deep pockets", 2, 2), (one.ClinicalRationale, one.Phases.Count, one.Items.Count(i => !i.IsWithdrawn)));
    }

    // ---------- refusals ----------

    [Fact]
    public async Task Every_wrong_field_of_a_revision_is_named_at_once_and_nothing_is_stored()
    {
        var (plan, diagnosis, _) = await SimplePlanAsync();
        var otherPlan = await CreateAsync([Item(diagnosis, (await ProcedureAsync("LOCAL-200")).Summary.Id)], title: "Another", patient: Ann);
        var before = (await CountsAsync(), (await RevisionsAsync(plan.Id)).Count);

        var tooMany = Enumerable.Range(1, 13).Select(n => Phase(n)).ToArray();
        var e1 = await Refused(() => ReviseAsync(plan, Revise(reason: "  ", rationale: new string('x', 2001), explanation: "bell\u0007", phases: tooMany)));
        Assert.Equal(["clinicalRationale", "patientExplanation", "phases", "reason"], e1.FieldErrors.Keys.Order().ToArray());

        var e2 = await Refused(() => ReviseAsync(plan, Revise(
            phases: [new(0, "Zero", null, null), new(2, "  ", null, null), new(2, "Twice", null, null), new(3, "Old", "line\nbreak", null), new(4, "Far", null, new DateOnly(1999, 1, 1))],
            placements: [])));
        Assert.Equal(["phases[0].number", "phases[1].name", "phases[2].number", "phases[3].plannedVisit", "phases[4].targetDate"], e2.FieldErrors.Keys.Order().ToArray());

        var e3 = await Refused(() => ReviseAsync(plan, Revise(phases: [Phase(1)], placements: [new(Guid.NewGuid(), 1), new(otherPlan.Items[0].Id, 1), new(plan.Items[0].Id, 7), new(plan.Items[0].Id, 1)])));
        Assert.Equal(["placements[0].itemId", "placements[1].itemId", "placements[2].phaseNumber", "placements[3].itemId"], e3.FieldErrors.Keys.Order().ToArray());

        var e4 = await Refused(() => ReviseAsync(plan, Revise(reason: null, phases: [Phase(1)])));
        Assert.Equal("Say why this plan is being revised.", e4.FieldErrors["reason"]);

        Assert.Equal(before, (await CountsAsync(), (await RevisionsAsync(plan.Id)).Count));
    }

    [Fact]
    public async Task A_stale_revision_is_a_conflict_a_missing_version_is_refused_and_a_withdrawn_plan_cannot_be_revised()
    {
        var (plan, _, _) = await SimplePlanAsync();
        var revised = await ReviseAsync(plan, Revise(rationale: "First"));
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => ReviseAsync(plan, Revise(reason: "Second", rationale: "Second")));
        Assert.Equal("row_version_required", (await Refused(() => ReviseAsync(plan, Revise(rationale: "Third"), version: ""))).Code);
        Assert.Equal("First", (await Svc(s => s.GetAsync(plan.Id, default))).ClinicalRationale);

        var withdrawn = await Svc(s => s.WithdrawAsync(plan.Id, "Entered in error", revised.RowVersion, S.Actor, default));
        Assert.Equal("plan_withdrawn", (await Refused(() => ReviseAsync(withdrawn, Revise(rationale: "Late")))).Code);
        Assert.Equal("plan_not_found", (await Refused(() => Svc(s => s.ReviseAsync(Guid.NewGuid(), Revise(), "AAAAAAAAAAA=", S.Actor, default)))).Code);
    }
}
