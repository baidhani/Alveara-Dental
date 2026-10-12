using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Billing;
using Alveara.Api.Architecture.Treatment;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// STORY-007 against real SQL Server: the database refuses, on its own, what the billing rules refuse and what no service rule can see. These tests go around the service on purpose - a script, a future
/// module or a person with database access cannot delete an account or rewrite financial history, bill a procedure that is not completed, is withdrawn or is another patient's, bill an amount other
/// than the procedure's fee, record an amount with the wrong sign for its kind, or leave an account owing less than nothing.
/// </summary>
public class BillingSchemaTests : BillingTestBase
{
    private static async Task<SqlException> Sql(Func<Task> action)
    {
        var e = await Assert.ThrowsAnyAsync<Exception>(action);
        return Assert.IsType<SqlException>(e is DbUpdateException { InnerException: not null } d ? d.InnerException : e);
    }

    private async Task<BillingAccount> InsertAccountAsync(Guid? patient = null)
    {
        var account = new BillingAccount { Id = Guid.NewGuid(), PatientId = patient ?? Ann, CreatedAtUtc = DateTimeOffset.UtcNow, CreatedByUserId = S.Actor };
        await using var db = Fixture.CreateContext();
        db.BillingAccounts.Add(account);
        await db.SaveChangesAsync();
        return account;
    }

    private BillingEntry Entry(BillingAccount account, string kind, decimal amount, int number = 1, string key = "k-1") => new()
    {
        Id = Guid.NewGuid(), AccountId = account.Id, PatientId = account.PatientId, EntryNumber = number, Kind = kind, Amount = amount, IdempotencyKey = key,
        RecordedAtUtc = DateTimeOffset.UtcNow, RecordedByUserId = S.Actor,
    };

    private BillingEntry Charge(BillingAccount account, Guid planItem, decimal amount, int number = 1, string key = "k-1")
    {
        var e = Entry(account, BillingKinds.Charge, amount, number, key);
        (e.PlanItemId, e.ProcedureCode, e.ProcedureDescription) = (planItem, "LOCAL-100", "Periodontal maintenance");
        return e;
    }

    private BillingEntry Payment(BillingAccount account, decimal amount, int number = 2, string key = "k-2")
    {
        var e = Entry(account, BillingKinds.Payment, amount, number, key);
        e.PaymentMethod = BillingPaymentMethods.Cash;
        return e;
    }

    private BillingEntry Adjustment(BillingAccount account, decimal amount, int number = 3, string key = "k-3")
    {
        var e = Entry(account, BillingKinds.Adjustment, amount, number, key);
        e.Reason = "Courtesy adjustment";
        return e;
    }

    private async Task InsertAsync(BillingEntry entry, Action<BillingEntry>? tweak = null)
    {
        tweak?.Invoke(entry);
        await using var db = Fixture.CreateContext();
        db.BillingEntries.Add(entry);
        await db.SaveChangesAsync();
    }

    private async Task<int> RunAsync(string sql, params object[] args)
    {
        await using var db = Fixture.CreateContext();
        return await db.Database.ExecuteSqlRawAsync(sql, args);
    }

    private async Task<(PlanView Plan, Guid Completed, Guid Accepted, Guid Withdrawn)> PlanAsync()
    {
        var p = await BillablePlanAsync();
        return (p.Plan, p.Completed, p.Accepted, p.Withdrawn);
    }

    // ---------- check constraints ----------

    [Theory]
    [InlineData("badKind", "CK_BillingEntries_Kind")]
    [InlineData("numberZero", "CK_BillingEntries_Number")]
    [InlineData("blankKey", "CK_BillingEntries_Key")]
    [InlineData("overTheCeiling", "CK_BillingEntries_Max")]
    [InlineData("chargeNegative", "CK_BillingEntries_Sign")]
    [InlineData("paymentPositive", "CK_BillingEntries_Sign")]
    [InlineData("adjustmentZero", "CK_BillingEntries_Sign")]
    [InlineData("chargeWithoutProcedure", "CK_BillingEntries_ChargeSource")]
    [InlineData("paymentNamingAProcedure", "CK_BillingEntries_ChargeSource")]
    [InlineData("paymentWithoutMethod", "CK_BillingEntries_Method")]
    [InlineData("paymentBadMethod", "CK_BillingEntries_Method")]
    [InlineData("chargeWithMethod", "CK_BillingEntries_Method")]
    [InlineData("adjustmentWithoutReason", "CK_BillingEntries_Reason")]
    [InlineData("adjustmentBlankReason", "CK_BillingEntries_Reason")]
    [InlineData("chargeWithNote", "CK_BillingEntries_Reason")]
    public async Task The_database_refuses_an_entry_that_breaks_a_shape_rule(string which, string constraint)
    {
        var (_, completed, _, _) = await PlanAsync();
        var account = await InsertAccountAsync();
        var entry = which switch
        {
            "badKind" => Entry(account, "Bogus", 10m),
            "numberZero" => Payment(account, -10m, number: 0),
            "blankKey" => Payment(account, -10m, key: "   "),
            "overTheCeiling" => Adjustment(account, 1_000_000.01m),
            "chargeNegative" => Charge(account, completed, -100m),
            "paymentPositive" => Payment(account, 10m),
            "adjustmentZero" => Adjustment(account, 0m),
            "chargeWithoutProcedure" => Entry(account, BillingKinds.Charge, 100m),
            "paymentNamingAProcedure" => Payment(account, -10m),
            "paymentWithoutMethod" => Entry(account, BillingKinds.Payment, -10m),
            "paymentBadMethod" => Payment(account, -10m),
            "chargeWithMethod" => Charge(account, completed, 100m),
            "adjustmentWithoutReason" => Entry(account, BillingKinds.Adjustment, 10m),
            "adjustmentBlankReason" => Adjustment(account, 10m),
            "chargeWithNote" => Charge(account, completed, 100m),
            _ => throw new ArgumentOutOfRangeException(nameof(which)),
        };
        var e = await Sql(() => InsertAsync(entry, x =>
        {
            switch (which)
            {
                case "paymentNamingAProcedure": (x.PlanItemId, x.ProcedureCode, x.ProcedureDescription) = (completed, "LOCAL-100", "Periodontal maintenance"); break;
                case "paymentBadMethod": x.PaymentMethod = "Barter"; break;
                case "chargeWithMethod": x.PaymentMethod = BillingPaymentMethods.Cash; break;
                case "adjustmentBlankReason": x.Reason = "   "; break;
                case "chargeWithNote": x.Reason = "a note"; break;
            }
        }));
        Assert.Equal(547, e.Number);
        Assert.Contains(constraint, e.Message);
    }

    // ---------- uniqueness ----------

    [Fact]
    public async Task One_account_per_patient()
    {
        await InsertAccountAsync();
        var e = await Sql(() => InsertAccountAsync());
        Assert.Contains(e.Number, new[] { 2601, 2627 });
    }

    [Fact]
    public async Task A_plan_procedure_can_be_charged_once_an_entry_number_and_a_key_are_used_once_per_account()
    {
        var (_, completed, _, _) = await PlanAsync();
        var account = await InsertAccountAsync();
        await InsertAsync(Charge(account, completed, 100m));
        Assert.Contains((await Sql(() => InsertAsync(Charge(account, completed, 100m, number: 2, key: "k-9")))).Number, new[] { 2601, 2627 });   // the same procedure again
        Assert.Contains((await Sql(() => InsertAsync(Adjustment(account, 5m, number: 1, key: "k-8")))).Number, new[] { 2601, 2627 });           // the same entry number
        Assert.Contains((await Sql(() => InsertAsync(Adjustment(account, 5m, number: 2, key: "k-1")))).Number, new[] { 2601, 2627 });           // the same save key
    }

    // ---------- triggers ----------

    [Fact]
    public async Task An_account_cannot_be_deleted_and_its_patient_cannot_change()
    {
        var account = await InsertAccountAsync();
        Assert.Equal(51118, (await Sql(() => RunAsync("DELETE FROM [BillingAccounts] WHERE Id = {0}", account.Id))).Number);
        Assert.Equal(51119, (await Sql(() => RunAsync("UPDATE [BillingAccounts] SET PatientId = {0} WHERE Id = {1}", Bo, account.Id))).Number);
    }

    [Fact]
    public async Task Financial_history_cannot_be_edited_or_deleted()
    {
        var (_, completed, _, _) = await PlanAsync();
        var account = await InsertAccountAsync();
        var charge = Charge(account, completed, 100m);
        await InsertAsync(charge);
        Assert.Equal(51120, (await Sql(() => RunAsync("UPDATE [BillingEntries] SET Amount = 1 WHERE Id = {0}", charge.Id))).Number);
        Assert.Equal(51120, (await Sql(() => RunAsync("DELETE FROM [BillingEntries] WHERE Id = {0}", charge.Id))).Number);
        await using var db = Fixture.CreateContext();
        Assert.Equal(100m, (await db.BillingEntries.SingleAsync()).Amount);
    }

    [Fact]
    public async Task An_entry_must_carry_the_same_patient_as_its_account()
    {
        var account = await InsertAccountAsync();
        Assert.Equal(51121, (await Sql(() => InsertAsync(Adjustment(account, 10m, number: 1), x => x.PatientId = Bo))).Number);
    }

    [Fact]
    public async Task A_charge_must_bill_a_completed_procedure_of_the_same_patient_that_is_not_withdrawn()
    {
        var (_, completed, accepted, withdrawn) = await PlanAsync();
        var ann = await InsertAccountAsync();
        var bo = await InsertAccountAsync(Bo);
        Assert.Equal(51122, (await Sql(() => InsertAsync(Charge(bo, completed, 100m)))).Number);                         // another patient's procedure
        Assert.Equal(51123, (await Sql(() => InsertAsync(Charge(ann, withdrawn, 100m)))).Number);                        // withdrawn
        Assert.Equal(51124, (await Sql(() => InsertAsync(Charge(ann, accepted, 100m)))).Number);                         // accepted but not completed
        await InsertAsync(Charge(ann, completed, 100m));                                                                   // and the right one is accepted
    }

    [Fact]
    public async Task A_charge_is_for_the_fee_the_procedure_was_proposed_at_and_no_other_amount()
    {
        var (_, completed, _, _) = await PlanAsync();
        var account = await InsertAccountAsync();
        Assert.Equal(51125, (await Sql(() => InsertAsync(Charge(account, completed, 99.99m)))).Number);
        Assert.Equal(51125, (await Sql(() => InsertAsync(Charge(account, completed, 200m)))).Number);
        await InsertAsync(Charge(account, completed, 100m));
    }

    [Fact]
    public async Task An_entry_cannot_leave_the_account_with_a_negative_balance()
    {
        var (_, completed, _, _) = await PlanAsync();
        var account = await InsertAccountAsync();
        Assert.Equal(51126, (await Sql(() => InsertAsync(Payment(account, -10m, number: 1)))).Number);                     // nothing owed yet
        await InsertAsync(Charge(account, completed, 100m, number: 1, key: "k-1"));
        Assert.Equal(51126, (await Sql(() => InsertAsync(Payment(account, -100.01m, number: 2, key: "k-2")))).Number);    // more than is owed
        Assert.Equal(51126, (await Sql(() => InsertAsync(Adjustment(account, -100.01m, number: 2, key: "k-3")))).Number);
        await InsertAsync(Payment(account, -40m, number: 2, key: "k-4"));
        await InsertAsync(Adjustment(account, -60m, number: 3, key: "k-5"));                                               // exactly clears it
        await using var db = Fixture.CreateContext();
        Assert.Equal(0m, await db.BillingEntries.Where(e => e.AccountId == account.Id).SumAsync(e => e.Amount));
    }
}
