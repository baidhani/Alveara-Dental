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

    // ---------- rows written straight to the database, for the tests that go around the service (ALV-007-C01) ----------

    protected async Task<BillingAccount> NewAccountAsync(Guid? patient = null)
    {
        var account = new BillingAccount { Id = Guid.NewGuid(), PatientId = patient ?? Ann, CreatedAtUtc = DateTimeOffset.UtcNow, CreatedByUserId = S.Actor };
        await using var db = Fixture.CreateContext();
        db.BillingAccounts.Add(account);
        await db.SaveChangesAsync();
        return account;
    }

    protected BillingEntry NewEntry(BillingAccount account, string kind, decimal amount, int number, string key) => new()
    {
        Id = Guid.NewGuid(), AccountId = account.Id, PatientId = account.PatientId, EntryNumber = number, Kind = kind, Amount = amount, IdempotencyKey = key,
        RecordedAtUtc = DateTimeOffset.UtcNow, RecordedByUserId = S.Actor,
    };

    protected BillingEntry ChargeRow(BillingAccount account, Guid planItem, decimal amount, int number, string key)
    {
        var e = NewEntry(account, BillingKinds.Charge, amount, number, key);
        (e.PlanItemId, e.ProcedureCode, e.ProcedureDescription) = (planItem, "LOCAL-100", "Periodontal maintenance");
        return e;
    }

    protected BillingEntry PaymentRow(BillingAccount account, decimal amount, int number, string key)
    {
        var e = NewEntry(account, BillingKinds.Payment, amount, number, key);
        e.PaymentMethod = BillingPaymentMethods.Cash;
        return e;
    }

    protected BillingEntry AdjustmentRow(BillingAccount account, decimal amount, int number, string key)
    {
        var e = NewEntry(account, BillingKinds.Adjustment, amount, number, key);
        e.Reason = "Courtesy adjustment";
        return e;
    }

    protected BillingEntry ReversalRow(BillingAccount account, BillingEntry undone, int number, string key)
    {
        var e = NewEntry(account, BillingKinds.Reversal, -undone.Amount, number, key);
        (e.ReversesEntryId, e.Reason) = (undone.Id, "Entered in error");
        return e;
    }

    protected async Task AddRowAsync(BillingEntry entry, Action<BillingEntry>? tweak = null)
    {
        tweak?.Invoke(entry);
        await using var db = Fixture.CreateContext();
        db.BillingEntries.Add(entry);
        await db.SaveChangesAsync();
    }

    protected BillingAllocation AllocationRow(BillingAccount account, BillingEntry payment, BillingEntry charge, decimal amount, int number, string key = "alloc-1") => new()
    {
        Id = Guid.NewGuid(), AccountId = account.Id, PatientId = account.PatientId, AllocationNumber = number, PaymentEntryId = payment.Id, ChargeEntryId = charge.Id, Amount = amount,
        IdempotencyKey = key, AllocatedAtUtc = DateTimeOffset.UtcNow, AllocatedByUserId = S.Actor,
    };

    protected async Task AddRowAsync(BillingAllocation allocation, Action<BillingAllocation>? tweak = null)
    {
        tweak?.Invoke(allocation);
        await using var db = Fixture.CreateContext();
        db.BillingAllocations.Add(allocation);
        await db.SaveChangesAsync();
    }

    protected BillingResponsibility ResponsibilityRow(BillingAccount account, Guid responsible, int sequence) => new()
    {
        Id = Guid.NewGuid(), AccountId = account.Id, PatientId = account.PatientId, Sequence = sequence, ResponsiblePatientId = responsible, EffectiveFrom = new DateOnly(2030, 1, 1),
        SetAtUtc = DateTimeOffset.UtcNow, SetByUserId = S.Actor,
    };

    protected async Task AddRowAsync(BillingResponsibility row, Action<BillingResponsibility>? tweak = null)
    {
        tweak?.Invoke(row);
        await using var db = Fixture.CreateContext();
        db.BillingResponsibilities.Add(row);
        await db.SaveChangesAsync();
    }

    protected async Task<int> ExecuteAsync(string sql, params object[] args)
    {
        await using var db = Fixture.CreateContext();
        return await db.Database.ExecuteSqlRawAsync(sql, args);
    }

    /// <summary>The SqlException behind a refused write, whether it came from the save or from a statement.</summary>
    protected static async Task<Microsoft.Data.SqlClient.SqlException> SqlRefusal(Func<Task> action)
    {
        var e = await Xunit.Assert.ThrowsAnyAsync<Exception>(action);
        return Xunit.Assert.IsType<Microsoft.Data.SqlClient.SqlException>(e is DbUpdateException { InnerException: not null } d ? d.InnerException : e);
    }

    protected async Task<(int Accounts, int Entries, int Audit)> BillingCountsAsync()
    {
        await using var db = Fixture.CreateContext();
        return (await db.BillingAccounts.CountAsync(), await db.BillingEntries.CountAsync(), await db.AuditLogEntries.CountAsync(a => a.EntityType == nameof(BillingAccount)));
    }
}
