using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Billing;
using Alveara.Api.Architecture.Clinical;
using Alveara.Api.Architecture.Completion;
using Alveara.Api.Architecture.Configuration;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Odontogram;
using Alveara.Api.Architecture.Procedures;
using Alveara.Api.Architecture.Treatment;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-008-C01 test helpers: a provider, a chart finding, a plan whose procedures are in the states a completion needs (completed, accepted only, withdrawn, tooth-level), and the rows of the three new tables
/// written straight to the database, for the tests that go around the service on purpose.
/// </summary>
public abstract class CompletionTestBase : BillingTestBase
{
    protected const string HashA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    protected const string HashB = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    /// <summary>The completion service the way a request builds it: one context, the plan service beside it, and a clock (the real one unless a test needs to say what time it is).</summary>
    protected Task<T> Completion<T>(Func<CompletedProcedureService, Task<T>> action, Alveara.Api.Architecture.Time.IPracticeClock? clock = null) => WithDb(db =>
    {
        var c = clock ?? Clock;
        return action(new CompletedProcedureService(db, c, new TreatmentPlanService(db, c, CatalogFor(db)), new OdontogramService(db, c), new BillingService(db, c)));
    });

    /// <summary>A plan of <paramref name="count"/> accepted procedures (distinct catalog codes, fee 100 each) followed by one still pending; the returned plan carries the current row version.</summary>
    protected async Task<PlanView> AcceptedPlanAsync(int count = 3, Guid? patient = null, int firstCode = 500)
    {
        var diagnosis = await DiagnosisAsync(patient);
        var inputs = new List<PlanItemInput>();
        for (var i = 0; i <= count; i++) inputs.Add(Item(diagnosis, (await ProcedureAsync($"LOCAL-{firstCode + i}", fee: 100m, description: $"Procedure {i + 1}")).Summary.Id));
        var plan = await CreateAsync(inputs, patient: patient);
        for (var i = 0; i < count; i++)
        {
            var item = plan.Items[i].Id;
            plan = await Svc(s => s.DecideItemAsync(plan.Id, item, "Accepted", null, null, plan.RowVersion, S.Actor, default));
        }
        return plan;
    }

    protected static CompleteProceduresRequest RequestFor(PlanView plan, IEnumerable<int> itemIndexes, Guid encounter, Guid provider, string key = "set-1", DateTimeOffset? performed = null, string? version = null) =>
        new(key, version ?? plan.RowVersion, encounter, provider, performed ?? DateTimeOffset.UtcNow, itemIndexes.Select(i => plan.Items[i].Id).ToList());

    protected Task<PlanView> PlanNowAsync(Guid planId) => Svc(s => s.GetAsync(planId, default));

    protected sealed record Snapshot(int States, int Events, int Audit, int Sets, int Procedures, int Charges, int Accounts, int FindingVersions, string Version);

    /// <summary>Everything a completion writes, counted (plan states and history, audit, sets, records, ledger entries and accounts, chart history, and the plan's version), so a refusal can prove it wrote nothing.</summary>
    protected async Task<Snapshot> SnapshotAsync(Guid planId)
    {
        await using var db = Fixture.CreateContext();
        var plan = await PlanNowAsync(planId);
        return new Snapshot(await db.TreatmentPlanItemStates.CountAsync(), await db.TreatmentPlanEvents.CountAsync(), await db.AuditLogEntries.CountAsync(), await db.CompletionSets.CountAsync(),
            await db.CompletedProcedures.CountAsync(), await db.BillingEntries.CountAsync(), await db.BillingAccounts.CountAsync(), await db.ToothFindingVersions.CountAsync(), plan.RowVersion);
    }

    /// <summary>Links a diagnosis to a chart finding, which is how a plan procedure finds its finding.</summary>
    protected Task LinkAsync(Guid diagnosis, Guid finding) => WithDb(db => new DiagnosisService(db, Clock).LinkAsync(diagnosis, DiagnosisLinkTypes.Finding, finding, S.Actor, default));

    protected async Task<string> FindingStateAsync(Guid finding)
    {
        await using var db = Fixture.CreateContext();
        return (await db.ToothFindings.AsNoTracking().SingleAsync(f => f.Id == finding)).State;
    }

    /// <summary>A plan of accepted tooth-level procedures for one diagnosis, one per entry (a catalog code, its fee, the tooth and, for a surface procedure, the surface).</summary>
    protected async Task<PlanView> AcceptedToothPlanAsync(Guid diagnosis, params (string Code, decimal Fee, string Tooth, string? Surface)[] procedures)
    {
        var inputs = new List<PlanItemInput>();
        foreach (var (code, fee, tooth, surface) in procedures)
        {
            var created = await ProcedureAsync(code, surface is null ? ProcedureScopes.Tooth : ProcedureScopes.ToothSurface, ProcedureDentitions.Both, fee, $"Procedure {code}");
            inputs.Add(Item(diagnosis, created.Summary.Id, tooth, surface));
        }
        var plan = await CreateAsync(inputs);
        for (var i = 0; i < procedures.Length; i++)
        {
            var item = plan.Items[i].Id;
            plan = await Svc(s => s.DecideItemAsync(plan.Id, item, "Accepted", null, null, plan.RowVersion, S.Actor, default));
        }
        return plan;
    }

    protected Task<ProviderProfile> ProviderAsync(string name = "Dr. Provider") => WithDb(async db =>
    {
        var people = new StaffProviderService(db, Clock);
        var staff = await people.CreateStaffAsync(name, null, null, S.Actor, default);
        return await people.CreateProviderAsync(staff.Id, "General dentistry", S.Actor, default);
    });

    /// <summary>A finding on the patient's tooth, in the given lifecycle state; returns its id.</summary>
    protected async Task<Guid> FindingAsync(string tooth, string? surface, string state = FindingStates.Planned, Guid? patient = null, string condition = FindingConditions.Caries)
    {
        var who = patient ?? Ann;
        await WithDb(db => new OdontogramService(db, Clock).RecordAsync(who, tooth, surface, condition, state, S.Actor, default));
        await using var check = Fixture.CreateContext();
        return await check.ToothFindings.AsNoTracking().Where(f => f.PatientId == who && f.ToothKey == tooth && f.Surface == surface && f.Condition == condition).OrderByDescending(f => f.CreatedAtUtc).Select(f => f.Id).FirstAsync();
    }

    /// <summary>A one-procedure plan for a tooth and surface (a resin filling on 16, O), accepted and completed; for tests that need a chart finding to match.</summary>
    protected async Task<(PlanView Plan, PlanItemView Item)> CompletedToothPlanAsync(Guid? patient = null, string code = "FILL-1")
    {
        var diagnosis = await DiagnosisAsync(patient);
        var filling = await ProcedureAsync(code, ProcedureScopes.ToothSurface, ProcedureDentitions.Both, 140m, "Resin filling");
        var plan = await CreateAsync([Item(diagnosis, filling.Summary.Id, "16", "O")], patient: patient);
        plan = await Svc(s => s.DecideItemAsync(plan.Id, plan.Items[0].Id, "Accepted", null, null, plan.RowVersion, S.Actor, default));
        plan = await LegacyComplete(plan.Id, plan.Items[0].Id);
        return (plan, plan.Items[0]);
    }

    protected Task<DateTimeOffset> AcceptedAtAsync(Guid itemId) => WithDb(db =>
        db.TreatmentPlanItemStates.AsNoTracking().Where(s => s.ItemId == itemId && s.Acceptance == ItemAcceptance.Accepted).MinAsync(s => s.RecordedAtUtc));

    // ---------- rows ----------

    protected static CompletionSet SetRow(PlanView plan, string key = "set-1", int revision = 1, int count = 1, Guid? patient = null) => new()
    {
        Id = Guid.NewGuid(), PatientId = patient ?? plan.PatientId, PlanId = plan.Id, IdempotencyKey = key, RequestHash = HashA, CorrelationId = Guid.NewGuid(), PlanRevisionNumber = revision,
        ItemCount = count, CreatedAtUtc = DateTimeOffset.UtcNow, CreatedByUserId = Guid.NewGuid(),
    };

    protected static CompletedProcedure ProcedureRow(CompletionSet set, PlanItemView item, Guid encounter, Guid provider, DateTimeOffset? performed = null) => new()
    {
        Id = Guid.NewGuid(), SetId = set.Id, PatientId = set.PatientId, PlanId = set.PlanId, PlanItemId = item.Id, PlanRevisionNumber = set.PlanRevisionNumber, ProcedureVersionId = item.ProcedureVersionId,
        ToothKey = item.ToothKey, Surface = item.Surface, EncounterId = encounter, ProviderProfileId = provider, PerformedAtUtc = performed ?? DateTimeOffset.UtcNow,
        ChartOutcome = ChartOutcomes.NotApplicable, FinalizedAtUtc = DateTimeOffset.UtcNow, FinalizedByUserId = Guid.NewGuid(),
    };

    protected static CompletedProcedureAmendment AmendmentRow(CompletedProcedure procedure, int number, string key, Guid? provider = null, DateTimeOffset? performed = null, Guid? encounter = null) => new()
    {
        Id = Guid.NewGuid(), CompletedProcedureId = procedure.Id, PatientId = procedure.PatientId, AmendmentNumber = number, IdempotencyKey = key, RequestHash = HashB, Reason = "The wrong provider was recorded.",
        ProviderProfileId = provider, PerformedAtUtc = performed, EncounterId = encounter, AmendedAtUtc = DateTimeOffset.UtcNow, AmendedByUserId = Guid.NewGuid(),
    };

    /// <summary>Writes the rows in one save (the model orders them by their foreign keys, so a set goes in before its procedures).</summary>
    protected async Task AddRowsAsync(params object[] rows)
    {
        await using var db = Fixture.CreateContext();
        db.AddRange(rows);
        await db.SaveChangesAsync();
    }

    protected async Task<(int Sets, int Procedures, int Amendments)> CompletionCountsAsync()
    {
        await using var db = Fixture.CreateContext();
        return (await db.CompletionSets.CountAsync(), await db.CompletedProcedures.CountAsync(), await db.CompletedProcedureAmendments.CountAsync());
    }
}
