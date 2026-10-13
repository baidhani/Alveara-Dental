using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Procedures;
using Alveara.Api.Architecture.Treatment;
using Alveara.Api.Data;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-008-C02 TEST-ONLY FIXTURE. The STORY-008 routes and service methods that completed a procedure the old, state-only way were retired: nothing in the product can create such a completion any more. A
/// procedure completed that way is still a legitimate persistent state (it exists in the data written before the retirement, and adopting it is what <c>CompletedProcedureService.AdoptAsync</c> is for), and many
/// tests need one as their starting point ("a procedure is completed and not yet billed, so billing bills it by hand"). This is the only way to make one, and it lives in the test project.
///
/// It writes exactly what the retired methods wrote: it calls the same shared helper the authoritative workflow uses to stage a procedure's Completed state row, its <c>ItemCompleted</c> history event and its
/// <c>TreatmentPlanItemCompleted</c> audit entry, and it does not touch the plan's row (the retired methods did not either). No row format is duplicated here, so a legacy completion made by this fixture has the
/// shape the old service produced by construction. It records no provider, encounter, performed time, chart effect or charge, like the original.
/// </summary>
internal static class LegacyCompletionFixture
{
    private static TreatmentPlanService Plans(AlveraDbContext db) =>
        new(db, SchedulingTestSupport.Clock, new ProcedureCatalogService(db, SchedulingTestSupport.Clock, [new FindingLinkProcedureUsageSource(db), new TreatmentPlanProcedureUsageSource(db)]));

    /// <summary>Completes one accepted procedure the old way. Completing an already completed procedure changes nothing, as before. A procedure that is not accepted is a mistake in the test and throws.</summary>
    public static async Task<PlanView> CompleteItemAsync(AlveraDbContext db, Guid planId, Guid itemId, Guid actor)
    {
        var plans = Plans(db);
        var plan = await db.TreatmentPlans.AsNoTracking().SingleAsync(p => p.Id == planId);
        TreatmentPlanService.RequireOpen(plan);
        var item = await db.TreatmentPlanItems.AsNoTracking().SingleAsync(i => i.Id == itemId && i.PlanId == planId);
        var latest = (await plans.StateHistoryAsync(planId, default))[itemId].OrderByDescending(s => s.Sequence).FirstOrDefault();
        if (TreatmentPlanService.CompletionOfState(latest) == ItemCompletion.Completed) return await plans.GetAsync(planId, default);
        if (latest is null || TreatmentPlanService.AcceptanceOfState(latest) != ItemAcceptance.Accepted)
            throw new InvalidOperationException("The legacy completion fixture needs an accepted procedure (accept it first).");

        await plans.StageCompletionsAsync(plan, [item], new Dictionary<Guid, TreatmentPlanItemState> { [itemId] = latest }, actor, SchedulingTestSupport.Clock.UtcNow, default);
        await plans.SaveStagedAsync(planId, default);
        return await plans.GetAsync(planId, default);
    }

    /// <summary>Completes every accepted, not yet completed, non-withdrawn procedure of a plan the old way (the retired complete-accepted behaviour). Returns the plan; nothing to complete changes nothing.</summary>
    public static async Task<PlanView> CompleteAcceptedAsync(AlveraDbContext db, Guid planId, Guid actor)
    {
        var plans = Plans(db);
        var plan = await db.TreatmentPlans.AsNoTracking().SingleAsync(p => p.Id == planId);
        TreatmentPlanService.RequireOpen(plan);
        var live = await db.TreatmentPlanItems.AsNoTracking().Where(i => i.PlanId == planId && i.WithdrawnAtUtc == null).OrderBy(i => i.ItemNumber).ToListAsync();
        var history = await plans.StateHistoryAsync(planId, default);
        var latest = live.Select(i => (Item: i, State: history[i.Id].OrderByDescending(s => s.Sequence).FirstOrDefault())).ToList();
        var open = latest.Where(x => x.State is not null && TreatmentPlanService.AcceptanceOfState(x.State) == ItemAcceptance.Accepted && TreatmentPlanService.CompletionOfState(x.State) != ItemCompletion.Completed).ToList();
        if (open.Count == 0) return await plans.GetAsync(planId, default);

        await plans.StageCompletionsAsync(plan, open.Select(x => x.Item).ToList(), open.ToDictionary(x => x.Item.Id, x => x.State!), actor, SchedulingTestSupport.Clock.UtcNow, default);
        await plans.SaveStagedAsync(planId, default);
        return await plans.GetAsync(planId, default);
    }

    /// <summary>For the tests that drive the API over HTTP: every accepted procedure of a plan completed the old way, on the test database, with the id as the JSON gave it.</summary>
    public static async Task CompleteAcceptedAsync(TestDatabaseFixture fixture, string planId, Guid actor)
    {
        await using var db = fixture.CreateContext();
        await CompleteAcceptedAsync(db, Guid.Parse(planId), actor);
    }

    /// <summary>For the tests that drive the API over HTTP: the same completion, on the test database, with ids as the JSON gave them.</summary>
    public static async Task CompleteItemAsync(TestDatabaseFixture fixture, string planId, string itemId, Guid actor)
    {
        await using var db = fixture.CreateContext();
        await CompleteItemAsync(db, Guid.Parse(planId), Guid.Parse(itemId), actor);
    }
}
