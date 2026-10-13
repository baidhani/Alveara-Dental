using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Billing;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Treatment;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-007-C01's contract for ALV-008-C01, tested the way that story will use it: one context and one transaction in which a procedure is completed and its charge is made, committed or rolled back together.
/// Exactly one charge exists per billable completed procedure however often a completion is replayed or raced; the amount is always the plan's stored fee; a procedure that is not completed, is withdrawn or is
/// another patient's is refused with a stable code; the call refuses to run outside a transaction; and a failed save (including its audit entry) leaves neither the charge nor the completion behind.
/// </summary>
public class BillingProcedureChargeContractTests : BillingTestBase
{
    /// <summary>What ALV-008-C01 will do: complete the procedure and charge for it in one transaction on one context.</summary>
    private async Task<ProcedureChargeResult> CompleteAndBillAsync(PlanView plan, Guid itemId, Guid? correlation = null, Guid? patient = null, bool commit = true)
    {
        await using var db = Fixture.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync();
        await LegacyCompletionFixture.CompleteItemAsync(db, plan.Id, itemId, S.Actor);
        IProcedureCharging charging = Billing(db);
        var result = await charging.ChargeCompletedProcedureAsync(new ProcedureChargeRequest(patient ?? Ann, itemId, correlation ?? Guid.NewGuid()), S.Actor, default);
        if (commit) await tx.CommitAsync();
        return result;                                                                                      // leaving without committing rolls everything back
    }

    private async Task<ProcedureChargeResult> ChargeOnlyAsync(Guid itemId, Guid? patient = null)
    {
        await using var db = Fixture.CreateContext();
        await using var tx = await db.Database.BeginTransactionAsync();
        IProcedureCharging charging = Billing(db);
        var result = await charging.ChargeCompletedProcedureAsync(new ProcedureChargeRequest(patient ?? Ann, itemId, Guid.NewGuid()), S.Actor, default);
        await tx.CommitAsync();
        return result;
    }

    private Task<bool> IsCompletedAsync(Guid itemId) => WithDb(db => db.TreatmentPlanItemStates.AnyAsync(s => s.ItemId == itemId && s.Completion == ItemCompletion.Completed));

    [Fact]
    public async Task Completing_a_procedure_and_charging_for_it_in_one_transaction_makes_one_charge_at_the_plans_fee()
    {
        var plan = await BillablePlanAsync();
        var correlation = Guid.NewGuid();
        var result = await CompleteAndBillAsync(plan.Plan, plan.Accepted, correlation);

        Assert.Equal((true, false, 1, 100m, "USD", "LOCAL-200"), (result.Created, result.Reversed, result.EntryNumber, result.Amount, result.Currency, result.ProcedureCode));
        Assert.Equal((Ann, plan.Accepted, correlation), (result.PatientId, result.PlanItemId, result.CorrelationId));
        Assert.True(await IsCompletedAsync(plan.Accepted));
        Assert.Equal((1, 1, 1), await BillingCountsAsync());

        await using var db = Fixture.CreateContext();
        var entry = await db.BillingEntries.AsNoTracking().SingleAsync();
        Assert.Equal((result.ChargeEntryId, plan.Accepted, correlation), (entry.Id, entry.PlanItemId, entry.CorrelationId));
        var audit = await db.AuditLogEntries.AsNoTracking().SingleAsync(a => a.EntityType == nameof(BillingAccount));
        Assert.Equal(correlation, audit.CorrelationId);                                                     // the caller's id ties the completion to the charge and its audit entry
        Assert.Equal(100m, (await Bill(s => s.GetAccountAsync(Ann, default))).BalanceOwed);
    }

    [Fact]
    public async Task The_charge_is_the_fee_the_plan_copied_whatever_the_catalog_says_later()
    {
        var plan = await BillablePlanAsync();
        await Catalog(s => s.ReviseAsync(plan.FirstProcedure.Summary.Id, new Alveara.Api.Architecture.Procedures.ProcedureInput(Alveara.Api.Architecture.Procedures.ProcedureCodeSystems.Local, "LOCAL-100", "Periodontal maintenance", "Periodontic",
            Alveara.Api.Architecture.Procedures.ProcedureScopes.WholeMouth, null, 400m, null, null, null, null), "Annual fee review", plan.FirstProcedure.Summary.RowVersion, S.Actor, default));
        var result = await ChargeOnlyAsync(plan.Completed);
        Assert.Equal(100m, result.Amount);
    }

    [Fact]
    public async Task A_rolled_back_completion_leaves_no_charge_no_audit_entry_and_no_completion()
    {
        var plan = await BillablePlanAsync();
        var result = await CompleteAndBillAsync(plan.Plan, plan.Accepted, commit: false);
        Assert.True(result.Created);                                                                        // it looked fine to the caller...
        Assert.False(await IsCompletedAsync(plan.Accepted));                                                // ...but the caller rolled back, so neither exists
        Assert.Equal((0, 0, 0), await BillingCountsAsync());
        Assert.Equal(new[] { plan.Completed }, (await Bill(s => s.ListBillableAsync(Ann, default))).Select(b => b.PlanItemId).ToArray());   // the rolled-back procedure is not completed, so it is not billable
    }

    [Fact]
    public async Task Replaying_a_completion_never_makes_a_second_charge()
    {
        var plan = await BillablePlanAsync();
        var first = await CompleteAndBillAsync(plan.Plan, plan.Accepted);
        var second = await CompleteAndBillAsync(plan.Plan, plan.Accepted);                                  // the same completion again, even with a different correlation id
        Assert.Equal((true, false), (first.Created, second.Created));
        Assert.Equal((first.ChargeEntryId, first.EntryNumber, first.CorrelationId), (second.ChargeEntryId, second.EntryNumber, second.CorrelationId));   // the existing charge, with the correlation it was made under
        Assert.Equal((1, 1, 1), await BillingCountsAsync());

        await using var db = Fixture.CreateContext();                                                       // and twice in one transaction
        await using var tx = await db.Database.BeginTransactionAsync();
        var a = await Billing(db).ChargeCompletedProcedureAsync(new ProcedureChargeRequest(Ann, plan.Completed, Guid.NewGuid()), S.Actor, default);
        var b = await Billing(db).ChargeCompletedProcedureAsync(new ProcedureChargeRequest(Ann, plan.Completed, Guid.NewGuid()), S.Actor, default);
        await tx.CommitAsync();
        Assert.Equal((true, false, a.ChargeEntryId), (a.Created, b.Created, b.ChargeEntryId));
        Assert.Equal((1, 2, 2), await BillingCountsAsync());
    }

    [Fact]
    public async Task A_procedure_that_cannot_be_billed_is_refused_with_a_stable_code_and_leaves_nothing_behind()
    {
        var plan = await BillablePlanAsync();
        var bo = await BillablePlanAsync(Bo);
        async Task<(string Code, int Status)> Code(Func<Task<ProcedureChargeResult>> call)
        {
            var e = await RefusedBilling(call);
            return (e.Code, e.StatusCode);
        }
        Assert.Equal(("not_completed", 409), await Code(() => ChargeOnlyAsync(plan.Accepted)));
        Assert.Equal(("item_withdrawn", 409), await Code(() => ChargeOnlyAsync(plan.Withdrawn)));
        Assert.Equal(("plan_item_not_found", 404), await Code(() => ChargeOnlyAsync(bo.Completed)));           // another patient's looks like an unknown one
        Assert.Equal(("plan_item_not_found", 404), await Code(() => ChargeOnlyAsync(Guid.NewGuid())));
        Assert.Equal("patient_not_found", (await RefusedBilling(() => ChargeOnlyAsync(plan.Completed, Guid.NewGuid()))).Code);
        Assert.Equal((0, 0, 0), await BillingCountsAsync());
    }

    [Fact]
    public async Task It_refuses_to_run_outside_a_transaction_because_the_charge_and_the_completion_must_stand_or_fall_together()
    {
        var plan = await BillablePlanAsync();
        await using var db = Fixture.CreateContext();
        var e = await Assert.ThrowsAsync<InvalidOperationException>(() => Billing(db).ChargeCompletedProcedureAsync(new ProcedureChargeRequest(Ann, plan.Completed, Guid.NewGuid()), S.Actor, default));
        Assert.Contains("inside the transaction", e.Message);
        Assert.Equal((0, 0, 0), await BillingCountsAsync());
    }

    [Fact]
    public async Task A_reversed_charge_is_still_the_procedures_one_charge_and_is_not_made_again()
    {
        var plan = await BillablePlanAsync();
        var charged = await ChargeOnlyAsync(plan.Completed);
        var account = await Bill(s => s.GetAccountAsync(Ann, default));
        await Bill(s => s.ReverseEntryAsync(Ann, new ReversalInput(charged.EntryNumber, "Billed in error", account.RowVersion, "r-1"), S.Actor, default));

        var again = await ChargeOnlyAsync(plan.Completed);
        Assert.Equal((false, true, charged.ChargeEntryId), (again.Created, again.Reversed, again.ChargeEntryId));
        Assert.Equal((2, 0m), ((await Bill(s => s.GetAccountAsync(Ann, default))).Entries.Count, (await Bill(s => s.GetAccountAsync(Ann, default))).BalanceOwed));
    }

    [Fact]
    public async Task Two_completions_racing_for_the_same_procedure_end_with_exactly_one_charge()
    {
        var plan = await BillablePlanAsync();
        async Task<(ProcedureChargeResult? Result, Exception? Failure)> Attempt()
        {
            try { return (await CompleteAndBillAsync(plan.Plan, plan.Accepted), null); }
            catch (Exception e) { return (null, e); }
        }
        var outcomes = await Task.WhenAll(Attempt(), Attempt(), Attempt());

        Assert.All(outcomes.Where(o => o.Failure is not null), o => Assert.True(o.Failure is ConcurrencyConflictException or DbUpdateException or TreatmentPlanException, o.Failure!.ToString()));   // a loser rolls back and may retry
        Assert.Equal(1, outcomes.Count(o => o.Result?.Created == true));
        Assert.Equal((1, 1, 1), await BillingCountsAsync());

        var retry = await CompleteAndBillAsync(plan.Plan, plan.Accepted);                                    // the retry a loser makes receives the existing charge
        Assert.False(retry.Created);
        Assert.Equal((1, 1, 1), await BillingCountsAsync());
    }

    [Fact]
    public async Task A_failed_audit_write_leaves_neither_the_charge_nor_the_completion_behind()
    {
        var plan = await BillablePlanAsync();
        await ExecuteAsync(@"CREATE TRIGGER [TR_Test_RefuseBillingAudit] ON [AuditLogEntries] INSTEAD OF INSERT AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted WHERE EntityType = 'BillingAccount') THROW 59030, 'audit unavailable', 1;
    INSERT INTO [AuditLogEntries] SELECT * FROM inserted;
END");
        var e = await RefusedBilling(() => CompleteAndBillAsync(plan.Plan, plan.Accepted));
        Assert.Equal(("save_failed", 503), (e.Code, e.StatusCode));
        Assert.False(await IsCompletedAsync(plan.Accepted));                                                 // the caller's transaction rolled back with it
        Assert.Equal((0, 0, 0), await BillingCountsAsync());

        await ExecuteAsync("DROP TRIGGER [TR_Test_RefuseBillingAudit]");
        var result = await CompleteAndBillAsync(plan.Plan, plan.Accepted);                                   // and the same completion works once the log is back
        Assert.True(result.Created);
        Assert.True(await IsCompletedAsync(plan.Accepted));
    }

    [Fact]
    public async Task A_charge_made_through_the_contract_and_one_made_by_billing_are_the_same_charge_and_the_procedure_is_not_offered_again()
    {
        var plan = await BillablePlanAsync();
        var viaContract = await ChargeOnlyAsync(plan.Completed);
        Assert.DoesNotContain(await Bill(s => s.ListBillableAsync(Ann, default)), b => b.PlanItemId == plan.Completed);
        var byBilling = await RefusedBilling(() => Bill(s => s.RecordChargeAsync(Ann, new ChargeInput(plan.Completed, "c-1"), S.Actor, default)));
        Assert.Equal("already_billed", byBilling.Code);                                                       // billing cannot make a second one

        Assert.Equal("not_completed", (await RefusedBilling(() => Bill(s => s.RecordChargeAsync(Ann, new ChargeInput(plan.Accepted, "c-2"), S.Actor, default)))).Code);     // not completed: refused as before
        Assert.Single((await Bill(s => s.GetAccountAsync(Ann, default))).Entries);
        Assert.Equal(viaContract.ChargeEntryId, await WithDb(db => db.BillingEntries.Where(e => e.PlanItemId == plan.Completed).Select(e => e.Id).SingleAsync()));
    }
}
