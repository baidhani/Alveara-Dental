using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Billing;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-007-C01 against real SQL Server: the database refuses, on its own, what the new ledger rules refuse and what no service rule can see. These tests go around the service on purpose - a script, a
/// future module or a person with database access cannot undo an entry except by a reversal that is exactly its opposite, reverse the same entry twice or reverse a reversal, reverse a charge payments are still
/// applied to, apply a payment to a charge for more than either holds, edit or delete an allocation or a responsibility, name someone who is not responsible as the payer, or delete or rename a payment method.
/// </summary>
public class BillingLedgerSchemaTests : BillingTestBase
{
    /// <summary>A plan whose first two procedures are completed (fee 100 each), so an account can hold two charges.</summary>
    private async Task<(Guid First, Guid Second)> TwoCompletedAsync()
    {
        var p = await BillablePlanAsync();
        await LegacyComplete(p.Plan.Id, p.Accepted);
        return (p.Completed, p.Accepted);
    }

    /// <summary>An account holding a 100 charge (entry 1) and a 60 payment (entry 2), as rows.</summary>
    private async Task<(BillingAccount Account, BillingEntry Charge, BillingEntry Payment, Guid Second)> ChargedAndPaidAsync()
    {
        var (first, second) = await TwoCompletedAsync();
        var account = await NewAccountAsync();
        var charge = ChargeRow(account, first, 100m, 1, "k-1");
        await AddRowAsync(charge);
        var payment = PaymentRow(account, -60m, 2, "k-2");
        await AddRowAsync(payment);
        return (account, charge, payment, second);
    }

    // ---------- check constraints: entries ----------

    [Theory]
    [InlineData("reversalWithoutTarget", "CK_BillingEntries_Reverses")]
    [InlineData("nonReversalWithTarget", "CK_BillingEntries_Reverses")]
    [InlineData("reversalZero", "CK_BillingEntries_Sign")]
    [InlineData("reversalWithoutReason", "CK_BillingEntries_Reason")]
    [InlineData("reversalBlankReason", "CK_BillingEntries_Reason")]
    [InlineData("adjustmentWithPayer", "CK_BillingEntries_PaymentOnly")]
    [InlineData("adjustmentWithMethodId", "CK_BillingEntries_PaymentOnly")]
    public async Task The_database_refuses_a_ledger_entry_that_breaks_a_shape_rule(string which, string constraint)
    {
        var (account, charge, _, _) = await ChargedAndPaidAsync();
        var entry = which switch
        {
            "reversalWithoutTarget" => ReversalRow(account, charge, 3, "k-3"),
            "nonReversalWithTarget" => AdjustmentRow(account, 5m, 3, "k-3"),
            "reversalZero" => ReversalRow(account, charge, 3, "k-3"),
            "reversalWithoutReason" => ReversalRow(account, charge, 3, "k-3"),
            "reversalBlankReason" => ReversalRow(account, charge, 3, "k-3"),
            "adjustmentWithPayer" => AdjustmentRow(account, 5m, 3, "k-3"),
            "adjustmentWithMethodId" => AdjustmentRow(account, 5m, 3, "k-3"),
            _ => throw new ArgumentOutOfRangeException(nameof(which)),
        };
        var e = await SqlRefusal(() => AddRowAsync(entry, x =>
        {
            switch (which)
            {
                case "reversalWithoutTarget": x.ReversesEntryId = null; break;
                case "nonReversalWithTarget": x.ReversesEntryId = charge.Id; break;
                case "reversalZero": x.Amount = 0m; break;
                case "reversalWithoutReason": x.Reason = null; break;
                case "reversalBlankReason": x.Reason = "   "; break;
                case "adjustmentWithPayer": x.PaidByPatientId = Ann; break;
                case "adjustmentWithMethodId": x.PaymentMethodId = BillingPaymentMethodIds.Cash; break;
            }
        }));
        Assert.Equal(547, e.Number);
        Assert.Contains(constraint, e.Message);
    }

    [Fact]
    public async Task An_entry_is_reversed_at_most_once()
    {
        var (account, _, payment, _) = await ChargedAndPaidAsync();
        await AddRowAsync(ReversalRow(account, payment, 3, "k-3"));
        Assert.Contains((await SqlRefusal(() => AddRowAsync(ReversalRow(account, payment, 4, "k-4")))).Number, new[] { 2601, 2627 });
    }

    // ---------- check constraints and uniqueness: allocations, responsibilities, methods ----------

    [Theory]
    [InlineData("zero", "CK_BillingAllocations_Amount")]
    [InlineData("negative", "CK_BillingAllocations_Amount")]
    [InlineData("overTheCeiling", "CK_BillingAllocations_Amount")]
    [InlineData("numberZero", "CK_BillingAllocations_Number")]
    [InlineData("blankKey", "CK_BillingAllocations_Key")]
    public async Task The_database_refuses_an_allocation_that_breaks_a_shape_rule(string which, string constraint)
    {
        var (account, charge, payment, _) = await ChargedAndPaidAsync();
        var e = await SqlRefusal(() => AddRowAsync(AllocationRow(account, payment, charge, 10m, 1), a =>
        {
            switch (which)
            {
                case "zero": a.Amount = 0m; break;
                case "negative": a.Amount = -1m; break;
                case "overTheCeiling": a.Amount = 1_000_000.01m; break;
                case "numberZero": a.AllocationNumber = 0; break;
                case "blankKey": a.IdempotencyKey = "  "; break;
            }
        }));
        Assert.Equal(547, e.Number);
        Assert.Contains(constraint, e.Message);
    }

    [Fact]
    public async Task An_allocation_number_is_used_once_per_account_but_a_command_key_may_cover_several_allocations()
    {
        var (account, charge, payment, _) = await ChargedAndPaidAsync();
        await AddRowAsync(AllocationRow(account, payment, charge, 10m, 1, "apply-1"));
        await AddRowAsync(AllocationRow(account, payment, charge, 10m, 2, "apply-1"));              // the same command, a second row
        Assert.Contains((await SqlRefusal(() => AddRowAsync(AllocationRow(account, payment, charge, 5m, 2, "apply-2")))).Number, new[] { 2601, 2627 });
    }

    [Theory]
    [InlineData("zeroSequence", "CK_BillingResponsibilities_Sequence")]
    [InlineData("blankReason", "CK_BillingResponsibilities_Reason")]
    public async Task The_database_refuses_a_responsibility_that_breaks_a_shape_rule(string which, string constraint)
    {
        var account = await NewAccountAsync();
        var e = await SqlRefusal(() => AddRowAsync(ResponsibilityRow(account, Ann, 1), r =>
        {
            if (which == "zeroSequence") r.Sequence = 0; else r.Reason = "   ";
        }));
        Assert.Equal(547, e.Number);
        Assert.Contains(constraint, e.Message);
    }

    [Fact]
    public async Task A_responsibility_sequence_is_used_once_per_account()
    {
        var account = await NewAccountAsync();
        await AddRowAsync(ResponsibilityRow(account, Ann, 1));
        Assert.Contains((await SqlRefusal(() => AddRowAsync(ResponsibilityRow(account, Bo, 1)))).Number, new[] { 2601, 2627 });
    }

    [Theory]
    [InlineData("blankName", "CK_BillingPaymentMethods_Name")]
    [InlineData("controlName", "CK_BillingPaymentMethods_Name")]
    [InlineData("badKind", "CK_BillingPaymentMethods_Kind")]
    [InlineData("builtInInactive", "CK_BillingPaymentMethods_BuiltInActive")]
    public async Task The_database_refuses_a_payment_method_that_breaks_a_shape_rule(string which, string constraint)
    {
        var method = new BillingPaymentMethod { Id = Guid.NewGuid(), Name = "Venmo", Kind = BillingPaymentMethods.Other, CreatedAtUtc = DateTimeOffset.UtcNow, CreatedByUserId = S.Actor };
        switch (which)
        {
            case "blankName": method.Name = "  "; break;
            case "controlName": method.Name = "Ve\u0007nmo"; break;
            case "badKind": method.Kind = "Barter"; break;
            case "builtInInactive": (method.IsBuiltIn, method.IsActive) = (true, false); break;
        }
        await using var db = Fixture.CreateContext();
        db.BillingPaymentMethods.Add(method);
        var e = await SqlRefusal(() => db.SaveChangesAsync());
        Assert.Equal(547, e.Number);
        Assert.Contains(constraint, e.Message);
    }

    [Fact]
    public async Task The_four_built_in_methods_exist_and_a_method_name_is_used_once_whatever_its_case()
    {
        await using var db = Fixture.CreateContext();
        var built = await db.BillingPaymentMethods.AsNoTracking().Where(m => m.IsBuiltIn).OrderBy(m => m.Name).ToListAsync();
        Assert.Equal(new[] { "Card", "Cash", "Check", "Other" }, built.Select(m => m.Name).ToArray());
        Assert.All(built, m => Assert.Equal((m.Name, true), (m.Kind, m.IsActive)));

        db.BillingPaymentMethods.Add(new BillingPaymentMethod { Id = Guid.NewGuid(), Name = "cash", Kind = BillingPaymentMethods.Cash, CreatedAtUtc = DateTimeOffset.UtcNow, CreatedByUserId = S.Actor });
        Assert.Contains((await SqlRefusal(() => db.SaveChangesAsync())).Number, new[] { 2601, 2627 });
    }

    // ---------- triggers: reversals ----------

    [Fact]
    public async Task A_reversal_undoes_an_entry_of_the_same_account_and_never_a_reversal()
    {
        var (account, charge, payment, _) = await ChargedAndPaidAsync();
        var bo = await NewAccountAsync(Bo);
        await AddRowAsync(ChargeRow(bo, (await BillablePlanAsync(Bo)).Completed, 100m, 1, "bo-1"));
        var otherPayment = PaymentRow(bo, -50m, 2, "bo-2");
        await AddRowAsync(otherPayment);
        Assert.Equal(51138, (await SqlRefusal(() => AddRowAsync(ReversalRow(account, otherPayment, 3, "k-3")))).Number);     // another account's entry

        var reversal = ReversalRow(account, payment, 3, "k-3");
        await AddRowAsync(reversal);                                                                                            // a payment may be reversed
        Assert.Equal(51139, (await SqlRefusal(() => AddRowAsync(ReversalRow(account, reversal, 4, "k-4")))).Number);          // but a reversal never is
    }

    [Fact]
    public async Task A_reversal_is_exactly_the_opposite_of_the_entry_it_undoes()
    {
        var (account, _, payment, _) = await ChargedAndPaidAsync();
        Assert.Equal(51140, (await SqlRefusal(() => AddRowAsync(ReversalRow(account, payment, 3, "k-3"), r => r.Amount = 59.99m))).Number);
        Assert.Equal(51140, (await SqlRefusal(() => AddRowAsync(ReversalRow(account, payment, 3, "k-3"), r => r.Amount = 61m))).Number);
        await AddRowAsync(ReversalRow(account, payment, 3, "k-3"));
    }

    [Fact]
    public async Task A_reversal_cannot_leave_the_account_owing_less_than_nothing()
    {
        var (account, charge, _, _) = await ChargedAndPaidAsync();       // 100 charged, 60 paid (not applied to the charge)
        Assert.Equal(51126, (await SqlRefusal(() => AddRowAsync(ReversalRow(account, charge, 3, "k-3")))).Number);
    }

    [Fact]
    public async Task A_charge_that_payments_are_applied_to_cannot_be_reversed_until_those_payments_are()
    {
        var (account, charge, payment, second) = await ChargedAndPaidAsync();
        await AddRowAsync(ChargeRow(account, second, 100m, 3, "k-3"));           // a second charge, so reversing the first never takes the balance below zero
        await AddRowAsync(AllocationRow(account, payment, charge, 60m, 1));
        Assert.Equal(51141, (await SqlRefusal(() => AddRowAsync(ReversalRow(account, charge, 4, "k-4")))).Number);

        await AddRowAsync(ReversalRow(account, payment, 4, "k-4"));              // the payment is reversed: its allocation is released
        await AddRowAsync(ReversalRow(account, charge, 5, "k-5"));               // and now the charge can be
        await using var db = Fixture.CreateContext();
        Assert.Equal(100m, await db.BillingEntries.Where(e => e.AccountId == account.Id).SumAsync(e => e.Amount));       // only the second charge is left owing
    }

    // ---------- triggers: allocations ----------

    [Fact]
    public async Task Allocations_cannot_be_edited_or_deleted()
    {
        var (account, charge, payment, _) = await ChargedAndPaidAsync();
        var allocation = AllocationRow(account, payment, charge, 10m, 1);
        await AddRowAsync(allocation);
        Assert.Equal(51127, (await SqlRefusal(() => ExecuteAsync("UPDATE [BillingAllocations] SET Amount = 1 WHERE Id = {0}", allocation.Id))).Number);
        Assert.Equal(51127, (await SqlRefusal(() => ExecuteAsync("DELETE FROM [BillingAllocations] WHERE Id = {0}", allocation.Id))).Number);
    }

    [Fact]
    public async Task An_allocation_applies_a_payment_to_a_charge_within_one_account()
    {
        var (account, charge, payment, second) = await ChargedAndPaidAsync();
        var adjustment = AdjustmentRow(account, 5m, 3, "k-3");
        await AddRowAsync(adjustment);
        Assert.Equal(51128, (await SqlRefusal(() => AddRowAsync(AllocationRow(account, payment, adjustment, 5m, 1)))).Number);     // not a charge
        Assert.Equal(51128, (await SqlRefusal(() => AddRowAsync(AllocationRow(account, adjustment, charge, 5m, 1)))).Number);      // not a payment

        var bo = await NewAccountAsync(Bo);
        var boCharge = ChargeRow(bo, (await BillablePlanAsync(Bo)).Completed, 100m, 1, "bo-1");
        await AddRowAsync(boCharge);
        Assert.Equal(51129, (await SqlRefusal(() => AddRowAsync(AllocationRow(account, payment, boCharge, 10m, 1)))).Number);       // another account's charge
        Assert.Equal(51129, (await SqlRefusal(() => AddRowAsync(AllocationRow(account, payment, charge, 10m, 1), a => a.PatientId = Bo))).Number);
        await AddRowAsync(AllocationRow(account, payment, charge, 10m, 1));
    }

    [Fact]
    public async Task A_payment_cannot_be_allocated_for_more_than_it_holds_nor_a_charge_paid_for_more_than_it_is()
    {
        var (account, charge, payment, second) = await ChargedAndPaidAsync();                               // charge 100, payment 60
        await AddRowAsync(ChargeRow(account, second, 100m, 3, "k-3"));                                       // a second charge, so a larger payment is possible
        await AddRowAsync(AllocationRow(account, payment, charge, 40m, 1));
        Assert.Equal(51130, (await SqlRefusal(() => AddRowAsync(AllocationRow(account, payment, charge, 20.01m, 2)))).Number);   // 40 + 20.01 > 60
        await AddRowAsync(AllocationRow(account, payment, charge, 20m, 2));                                  // the payment is now fully applied

        var payment2 = PaymentRow(account, -100m, 4, "k-4");
        await AddRowAsync(payment2);
        Assert.Equal(51131, (await SqlRefusal(() => AddRowAsync(AllocationRow(account, payment2, charge, 40.01m, 3)))).Number);   // 60 + 40.01 > 100
        await AddRowAsync(AllocationRow(account, payment2, charge, 40m, 3));
    }

    [Fact]
    public async Task Reversing_a_payment_releases_what_it_was_applied_to_and_a_reversed_entry_cannot_be_allocated()
    {
        var (account, charge, payment, _) = await ChargedAndPaidAsync();
        await AddRowAsync(AllocationRow(account, payment, charge, 60m, 1));
        var reversal = ReversalRow(account, payment, 3, "k-3");
        await AddRowAsync(reversal);

        var payment2 = PaymentRow(account, -100m, 4, "k-4");
        await AddRowAsync(payment2);
        await AddRowAsync(AllocationRow(account, payment2, charge, 100m, 2));                                // the charge can be paid in full again: the released 60 no longer counts

        Assert.Equal(51132, (await SqlRefusal(() => AddRowAsync(AllocationRow(account, payment, charge, 1m, 3)))).Number);       // the reversed payment
    }

    // ---------- triggers: responsibility and payer ----------

    [Fact]
    public async Task Who_is_responsible_cannot_be_edited_or_deleted_and_must_be_an_active_patient_of_the_right_account()
    {
        var account = await NewAccountAsync();
        var row = ResponsibilityRow(account, Bo, 1);
        await AddRowAsync(row);
        Assert.Equal(51133, (await SqlRefusal(() => ExecuteAsync("UPDATE [BillingResponsibilities] SET Reason = 'x' WHERE Id = {0}", row.Id))).Number);
        Assert.Equal(51133, (await SqlRefusal(() => ExecuteAsync("DELETE FROM [BillingResponsibilities] WHERE Id = {0}", row.Id))).Number);
        Assert.Equal(51134, (await SqlRefusal(() => AddRowAsync(ResponsibilityRow(account, Bo, 2), r => r.PatientId = Bo))).Number);

        await ExecuteAsync("UPDATE [Patients] SET IsActive = 0 WHERE Id = {0}", Bo);
        Assert.Equal(51135, (await SqlRefusal(() => AddRowAsync(ResponsibilityRow(account, Bo, 2)))).Number);
        await AddRowAsync(ResponsibilityRow(account, Ann, 2));                                               // the patient themselves
    }

    [Fact]
    public async Task A_payment_is_made_by_the_patient_or_by_someone_recorded_as_responsible_for_the_account()
    {
        var account = await NewAccountAsync();
        var (first, _) = await TwoCompletedAsync();
        await AddRowAsync(ChargeRow(account, first, 100m, 1, "k-1"));
        Assert.Equal(51142, (await SqlRefusal(() => AddRowAsync(PaymentRow(account, -10m, 2, "k-2"), p => p.PaidByPatientId = Bo))).Number);     // a stranger
        await AddRowAsync(PaymentRow(account, -10m, 2, "k-2"), p => p.PaidByPatientId = Ann);                                                      // the patient
        await AddRowAsync(ResponsibilityRow(account, Bo, 1));
        await AddRowAsync(PaymentRow(account, -10m, 3, "k-3"), p => p.PaidByPatientId = Bo);                                                        // now Bo is responsible
    }

    // ---------- triggers: payment methods ----------

    [Fact]
    public async Task A_payment_method_is_never_deleted_or_renamed_and_a_payment_uses_an_active_method_of_its_own_kind()
    {
        var venmo = new BillingPaymentMethod { Id = Guid.NewGuid(), Name = "Venmo", Kind = BillingPaymentMethods.Other, CreatedAtUtc = DateTimeOffset.UtcNow, CreatedByUserId = S.Actor };
        await using (var db = Fixture.CreateContext()) { db.BillingPaymentMethods.Add(venmo); await db.SaveChangesAsync(); }
        Assert.Equal(51136, (await SqlRefusal(() => ExecuteAsync("DELETE FROM [BillingPaymentMethods] WHERE Id = {0}", venmo.Id))).Number);
        Assert.Equal(51137, (await SqlRefusal(() => ExecuteAsync("UPDATE [BillingPaymentMethods] SET Name = 'Zelle' WHERE Id = {0}", venmo.Id))).Number);
        Assert.Equal(51137, (await SqlRefusal(() => ExecuteAsync("UPDATE [BillingPaymentMethods] SET Kind = 'Card' WHERE Id = {0}", venmo.Id))).Number);
        Assert.Equal(547, (await SqlRefusal(() => ExecuteAsync("UPDATE [BillingPaymentMethods] SET IsActive = 0 WHERE Id = {0}", BillingPaymentMethodIds.Cash))).Number);   // a built-in stays active

        var account = await NewAccountAsync();
        var (first, _) = await TwoCompletedAsync();
        await AddRowAsync(ChargeRow(account, first, 100m, 1, "k-1"));
        Assert.Equal(51143, (await SqlRefusal(() => AddRowAsync(PaymentRow(account, -10m, 2, "k-2"), p => p.PaymentMethodId = venmo.Id))).Number);    // Venmo is "Other", the payment says Cash
        await AddRowAsync(PaymentRow(account, -10m, 2, "k-2"), p => (p.PaymentMethod, p.PaymentMethodId) = (BillingPaymentMethods.Other, venmo.Id));

        await ExecuteAsync("UPDATE [BillingPaymentMethods] SET IsActive = 0 WHERE Id = {0}", venmo.Id);                                           // a custom method can be inactivated
        Assert.Equal(51143, (await SqlRefusal(() => AddRowAsync(PaymentRow(account, -10m, 3, "k-3"), p => (p.PaymentMethod, p.PaymentMethodId) = (BillingPaymentMethods.Other, venmo.Id)))).Number);
    }
}
