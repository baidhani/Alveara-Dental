using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Clinical;
using Alveara.Api.Architecture.Procedures;
using Alveara.Api.Architecture.Treatment;
using Alveara.Api.Data;
using Xunit;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>
/// Shared arrangement for the STORY-015 treatment plan tests: real SQL Server, two patients each with an encounter, and helpers that create diagnoses and catalog procedures through the real services
/// (so the plan tests use exactly what a clinician and a billing manager would have created) and run the plan service the way a request would.
/// </summary>
public abstract class TreatmentPlanTestBase : SafetyTestBase
{
    protected Guid AnnEncounter, BoEncounter;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        AnnEncounter = await EncounterAsync(Ann);
        BoEncounter = await EncounterAsync(Bo);
    }

    protected async Task<Guid> EncounterAsync(Guid patient)
    {
        await using var db = Fixture.CreateContext();
        var e = new Encounter { Id = Guid.NewGuid(), PatientId = patient, EncounterAtUtc = DateTimeOffset.UtcNow, Status = EncounterStatuses.Draft, CreatedAtUtc = DateTimeOffset.UtcNow };
        db.Encounters.Add(e);
        await db.SaveChangesAsync();
        return e.Id;
    }

    protected TreatmentPlanService Plans(AlveraDbContext db) => new(db, Clock, new ProcedureCatalogService(db, Clock, [new FindingLinkProcedureUsageSource(db)]));
    protected Task<T> Svc<T>(Func<TreatmentPlanService, Task<T>> action) => WithDb(db => action(Plans(db)));
    protected Task<T> Catalog<T>(Func<ProcedureCatalogService, Task<T>> action) => WithDb(db => action(new ProcedureCatalogService(db, Clock, [new FindingLinkProcedureUsageSource(db)])));

    /// <summary>A current diagnosis for a patient, recorded through the real diagnosis service.</summary>
    protected async Task<Guid> DiagnosisAsync(Guid? patient = null, string label = "Chronic periodontitis")
    {
        var p = patient ?? Ann;
        var encounter = p == Bo ? BoEncounter : AnnEncounter;
        var d = await WithDb(db => new DiagnosisService(db, Clock).RecordAsync(p, Guid.NewGuid().ToString("N"), new DiagnosisInput(encounter, label, null, null, null), S.Actor, default));
        return d.Id;
    }

    protected Task WithdrawDiagnosisAsync(Guid id) =>
        WithDb(async db =>
        {
            var svc = new DiagnosisService(db, Clock);
            var d = await svc.GetAsync(id, default);
            return await svc.WithdrawAsync(id, d.RowVersion, "Entered in error", S.Actor, default);
        });

    /// <summary>A catalog procedure (local code system), effective today, created through the real catalog service.</summary>
    protected Task<ProcedureDetailView> ProcedureAsync(string code = "LOCAL-100", string scope = ProcedureScopes.WholeMouth, string? dentition = null, decimal fee = 50m, string description = "Periodontal maintenance") =>
        Catalog(s => s.CreateAsync(new ProcedureInput(ProcedureCodeSystems.Local, code, description, "Periodontic", scope, dentition, fee, null, null, null, null), S.Actor, default));

    protected static PlanItemInput Item(Guid diagnosis, Guid procedure, string? tooth = null, string? surface = null, string? key = null) => new(diagnosis, procedure, tooth, surface, key);

    protected Task<PlanView> CreateAsync(IEnumerable<PlanItemInput> items, string title = "Periodontal plan", string? key = null, Guid? patient = null, Guid? actor = null) =>
        Svc(s => s.CreateAsync(patient ?? Ann, new PlanInput(title, key ?? Guid.NewGuid().ToString("N"), items.ToList()), actor ?? S.Actor, default));

    /// <summary>A one-procedure plan with a fresh diagnosis and a fresh whole-mouth procedure, for tests that only need "a plan".</summary>
    protected async Task<(PlanView Plan, Guid Diagnosis, ProcedureDetailView Procedure)> SimplePlanAsync(decimal fee = 50m)
    {
        var diagnosis = await DiagnosisAsync();
        var procedure = await ProcedureAsync(fee: fee);
        var plan = await CreateAsync([Item(diagnosis, procedure.Summary.Id)]);
        return (plan, diagnosis, procedure);
    }

    protected static async Task<TreatmentPlanException> Refused<T>(Func<Task<T>> action) => await Assert.ThrowsAsync<TreatmentPlanException>(action);

    protected async Task<(int Plans, int Items, int Events, int Audit)> CountsAsync()
    {
        await using var db = Fixture.CreateContext();
        return (await db.TreatmentPlans.CountAsync(), await db.TreatmentPlanItems.CountAsync(), await db.TreatmentPlanEvents.CountAsync(), await db.AuditLogEntries.CountAsync(a => a.EntityType == nameof(TreatmentPlan)));
    }
}
