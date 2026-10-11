using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Billing;
using Alveara.Api.Architecture.Procedures;
using Alveara.Api.Architecture.Treatment;
using Alveara.Api.Data;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>Shared arrangement for the STORY-007 billing tests: a real SQL Server database, a billing service over it, and a treatment plan with procedures in the states billing cares about.</summary>
public abstract class BillingTestBase : TreatmentPlanTestBase
{
    protected BillingService Billing(AlveraDbContext db) => new(db, Clock);
    protected Task<T> Bill<T>(Func<BillingService, Task<T>> action) => WithDb(db => action(Billing(db)));

    protected static async Task<BillingException> RefusedBilling<T>(Func<Task<T>> action) => await Xunit.Assert.ThrowsAsync<BillingException>(action);

    /// <summary>A plan with three procedures whose fee is 100 each: the first accepted and completed, the second accepted only, the third withdrawn.</summary>
    protected sealed record BillablePlan(PlanView Plan, ProcedureDetailView FirstProcedure, Guid Completed, Guid Accepted, Guid Withdrawn);

    protected async Task<BillablePlan> BillablePlanAsync(Guid? patient = null)
    {
        var diagnosis = await DiagnosisAsync(patient);
        var a = await ProcedureAsync("LOCAL-100", fee: 100m);
        var b = await ProcedureAsync("LOCAL-200", fee: 100m, description: "Scaling");
        var c = await ProcedureAsync("LOCAL-300", fee: 100m, description: "Polish");
        var plan = await CreateAsync([Item(diagnosis, a.Summary.Id), Item(diagnosis, b.Summary.Id), Item(diagnosis, c.Summary.Id)], patient: patient);
        plan = await Svc(s => s.DecideItemAsync(plan.Id, plan.Items[0].Id, "Accepted", null, null, plan.RowVersion, S.Actor, default));
        plan = await Svc(s => s.DecideItemAsync(plan.Id, plan.Items[1].Id, "Accepted", null, null, plan.RowVersion, S.Actor, default));
        plan = await Svc(s => s.CompleteItemAsync(plan.Id, plan.Items[0].Id, S.Actor, default));
        plan = await Svc(s => s.WithdrawItemAsync(plan.Id, plan.Items[2].Id, "Not needed", plan.RowVersion, S.Actor, default));
        return new BillablePlan(plan, a, plan.Items[0].Id, plan.Items[1].Id, plan.Items[2].Id);
    }

    protected static ChargeInput ChargeOf(Guid planItem, string key = "charge-1") => new(planItem, key);
    protected static PaymentInput PaymentOf(decimal amount, string version, string key = "pay-1", string method = "Cash", string? note = null) => new(amount, method, note, version, key);
    protected static AdjustmentInput AdjustmentOf(decimal amount, string version, string key = "adj-1", string reason = "Courtesy adjustment") => new(amount, reason, version, key);

    protected async Task<(int Accounts, int Entries, int Audit)> BillingCountsAsync()
    {
        await using var db = Fixture.CreateContext();
        return (await db.BillingAccounts.CountAsync(), await db.BillingEntries.CountAsync(), await db.AuditLogEntries.CountAsync(a => a.EntityType == nameof(BillingAccount)));
    }
}
