using Alveara.Api.Architecture.Billing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N024 (F-197, Q-5bf, F-120, Q-5bb, Q-5ax; briefing revision 2 S6, S8, S9) against real SQL Server, through the service:
/// - naming the party who is already responsible says so (<c>Changed</c> false) and records nothing - both with a recorded party and with nothing recorded yet (the patient is implicitly responsible),
///   and in the second case no account is opened;
/// - responsibility can be recorded before anything is billed: it opens the account with no entry and no document;
/// - adding a payment method answers whether it was created or already existed; an empty name and a repeat that differs only in case are handled;
/// - a refunded payment says why it can no longer be reversed or corrected; other entries carry no such note.
/// </summary>
public class BillingResponsibilityAndMethodAnswersTests : BillingTestBase
{
    private Task<BillingResponsibilityView> Record(Guid responsible, string? version = null, string? reason = null) =>
        Bill(s => s.RecordResponsibilityAsync(Ann, new ResponsibilityInput(responsible, null, reason, version), S.Actor, default));

    [Fact]
    public async Task Naming_the_party_already_responsible_records_nothing_whether_or_not_anything_was_recorded_before()
    {
        var implicitSelf = await Record(Ann);                                                                   // nothing recorded yet: Ann is responsible for herself already
        Assert.Equal((false, "Self", 0), (implicitSelf.Changed, implicitSelf.Source, implicitSelf.History.Count));
        Assert.Equal((0, 0, 0), await BillingCountsAsync());                                                      // no account opened, no row, no audit

        var recorded = await Record(Bo, reason: "Parent pays");
        Assert.True(recorded.Changed);
        var again = await Record(Bo, recorded.RowVersion);
        Assert.Equal((false, 1), (again.Changed, again.History.Count));
        Assert.Equal(1, await WithDb(db => db.BillingResponsibilities.CountAsync()));
    }

    [Fact]
    public async Task Responsibility_can_be_recorded_before_anything_is_billed_and_opens_the_account_with_no_entry_or_document()
    {
        var recorded = await Record(Bo, reason: "Parent pays from the start");
        Assert.Equal((true, Bo, "Recorded"), (recorded.Changed, recorded.ResponsiblePatientId, recorded.Source));
        Assert.NotNull(recorded.RowVersion);
        var (accounts, entries, _) = await BillingCountsAsync();
        Assert.Equal((1, 0), (accounts, entries));
        Assert.Equal(0, await WithDb(db => db.FinancialDocuments.CountAsync()));
    }

    [Fact]
    public async Task Adding_a_payment_method_says_whether_it_was_created_or_already_existed()
    {
        var first = await Bill(s => s.AddPaymentMethodAsync(new PaymentMethodInput("Zelle", "Other"), S.Actor, default));
        Assert.True(first.Created);
        var again = await Bill(s => s.AddPaymentMethodAsync(new PaymentMethodInput("zelle", "Other"), S.Actor, default));           // a repeat that differs only in case is the same method
        Assert.Equal((false, first.Id, "Zelle"), (again.Created, again.Id, again.Name));
        Assert.Null((await Bill(s => s.ListPaymentMethodsAsync(false, default))).First().Created);                                 // a plain read does not say
        var empty = await RefusedBilling(() => Bill(s => s.AddPaymentMethodAsync(new PaymentMethodInput("   ", "Other"), S.Actor, default)));
        Assert.Contains("name", empty.FieldErrors.Keys);
        var builtIn = await Bill(s => s.AddPaymentMethodAsync(new PaymentMethodInput("cash", "Cash"), S.Actor, default));        // the built-in Cash, in lower case
        Assert.Equal((false, true), (builtIn.Created!.Value, builtIn.IsBuiltIn));
    }

    [Fact]
    public async Task A_refunded_payment_says_why_it_cannot_be_reversed_or_corrected()
    {
        var plan = await BillablePlanAsync();
        var charged = await Bill(s => s.RecordChargeAsync(Ann, new ChargeInput(plan.Completed, "c-1"), S.Actor, default));
        var paid = await Bill(s => s.RecordPaymentAsync(Ann, new PaymentInput(30m, "Cash", null, charged.RowVersion, "p-1"), S.Actor, default));
        var plain = await Bill(s => s.GetAccountAsync(Ann, default));
        Assert.All(plain.Standing!, st => Assert.Null(st.CannotChangeBecause));
        await Bill(s => s.RefundPaymentAsync(Ann, new RefundInput(2, 5m, "Cash", "Patient request", paid.RowVersion, "rf-1"), S.Actor, default));
        var after = await Bill(s => s.GetAccountAsync(Ann, default));
        Assert.Contains("refunded", after.Standing!.Single(st => st.EntryNumber == 2).CannotChangeBecause);
        Assert.All(after.Standing!.Where(st => st.EntryNumber != 2), st => Assert.Null(st.CannotChangeBecause));
    }
}
