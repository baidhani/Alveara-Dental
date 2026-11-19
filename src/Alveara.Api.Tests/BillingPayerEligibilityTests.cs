using Alveara.Api.Architecture.Billing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N024 (PD-18, OQ-1, F-209, F-205; briefing revision 2 S5) against real SQL Server, through the service:
/// - the payer choices are exactly the patient, the registered guarantor, the members of the recorded household and anyone recorded as responsible (active ones), each with how they are related;
/// - the default is the party responsible for the account now; with nobody else responsible it is the patient;
/// - a payment or a correction naming anyone else is refused <c>payer_not_eligible</c> on the <c>paidBy</c> field and records nothing; a guarantor or household member is accepted and named in the ledger.
/// </summary>
public class BillingPayerEligibilityTests : BillingTestBase
{
    private Task<PayerChoicesView> Choices() => Bill(s => s.GetPayerChoicesAsync(Ann, default));

    private async Task<(Guid Guarantor, Guid Sibling, Guid Stranger, Guid Former)> FamilyAsync()
    {
        var guarantor = await S.PatientAsync("Gia", "Guard");
        var sibling = await S.PatientAsync("Sam", "Sib");
        var stranger = await S.PatientAsync("Stu", "Strange");
        var former = await S.PatientAsync("Fay", "Former");                                                     // an inactive household member is not offered
        var household = Guid.NewGuid();
        await ExecuteAsync("INSERT INTO [Households] ([Id],[CreatedAtUtc]) VALUES ({0}, SYSDATETIMEOFFSET())", household);
        await ExecuteAsync("UPDATE [Patients] SET [GuarantorPatientId] = {0} WHERE [Id] = {1}", guarantor, Ann);
        await ExecuteAsync("UPDATE [Patients] SET [HouseholdId] = {0}, [HouseholdRelationship] = 'Sibling' WHERE [Id] IN ({1}, {2}, {3})", household, Ann, sibling, former);
        await ExecuteAsync("UPDATE [Patients] SET [IsActive] = 0 WHERE [Id] = {0}", former);
        return (guarantor, sibling, stranger, former);
    }

    private async Task<BillingAccountView> ChargedAsync()
    {
        var plan = await BillablePlanAsync();
        return await Bill(s => s.RecordChargeAsync(Ann, new ChargeInput(plan.Completed, "c-1"), S.Actor, default));
    }

    private Task<BillingAccountView> Pay(BillingAccountView account, Guid? payer, string key) =>
        Bill(s => s.RecordPaymentAsync(Ann, new PaymentInput(10m, "Cash", null, account.RowVersion, key, null, payer), S.Actor, default));

    [Fact]
    public async Task The_choices_are_the_patient_the_guarantor_and_the_household_and_the_default_is_the_responsible_party()
    {
        var (guarantor, sibling, stranger, former) = await FamilyAsync();
        var choices = await Choices();
        Assert.Equal([Ann, guarantor, sibling], choices.Payers.Select(p => p.PatientId));                       // the patient first, then by last name (Guard, Sib)
        Assert.Equal("the patient", choices.Payers[0].Relationship);
        Assert.Equal("registered guarantor", choices.Payers.Single(p => p.PatientId == guarantor).Relationship);
        Assert.Equal("household member (sibling)", choices.Payers.Single(p => p.PatientId == sibling).Relationship);
        Assert.DoesNotContain(choices.Payers, p => p.PatientId == stranger || p.PatientId == former);
        Assert.Equal(guarantor, choices.DefaultPayerId);                                                          // the registered guarantor is responsible while nothing is recorded

        var account = await ChargedAsync();
        await Bill(s => s.RecordResponsibilityAsync(Ann, new ResponsibilityInput(stranger, null, "Stu pays", account.RowVersion), S.Actor, default));
        var after = await Choices();
        Assert.Equal((stranger, "recorded as responsible"), (after.DefaultPayerId, after.Payers.Single(p => p.PatientId == stranger).Relationship));
    }

    [Fact]
    public async Task A_patient_with_nobody_else_pays_for_themselves_by_default()
    {
        var choices = await Choices();
        Assert.Equal((Ann, 1), (choices.DefaultPayerId, choices.Payers.Count));
    }

    [Fact]
    public async Task A_payment_by_the_guarantor_or_a_household_member_is_accepted_and_anyone_else_is_refused_with_nothing_recorded()
    {
        var (guarantor, sibling, stranger, _) = await FamilyAsync();
        var account = await ChargedAsync();
        account = await Pay(account, guarantor, "p-g");
        account = await Pay(account, sibling, "p-s");
        Assert.Equal(["Gia Guard", "Sam Sib"], account.Entries.Where(e => e.Kind == BillingKinds.Payment).Select(e => e.PaidByName));

        var before = await BillingCountsAsync();
        var refused = await RefusedBilling(() => Pay(account, stranger, "p-x"));
        Assert.Equal(("payer_not_eligible", 400), (refused.Code, refused.StatusCode));
        Assert.Contains("paidBy", refused.FieldErrors.Keys);
        Assert.Equal(before, await BillingCountsAsync());

        var correction = await RefusedBilling(() => Bill(s => s.CorrectEntryAsync(Ann, new CorrectionInput(2, "Wrong amount", 12m, "Cash", null, stranger, account.RowVersion, "fix-1"), S.Actor, default)));
        Assert.Equal("payer_not_eligible", correction.Code);
        var corrected = await Bill(s => s.CorrectEntryAsync(Ann, new CorrectionInput(2, "Wrong amount", 12m, "Cash", null, sibling, account.RowVersion, "fix-2"), S.Actor, default));
        Assert.Equal("Sam Sib", corrected.Entries.Last().PaidByName);
    }

    [Fact]
    public async Task R02_an_inactive_guarantor_household_member_or_responsible_party_is_not_offered_and_is_refused_for_a_payment_and_a_correction()
    {
        // ALV-N024 R02 (CR-1, owner decision "active only"): the same set as the database rule 51142
        var (guarantor, sibling, _, _) = await FamilyAsync();
        var responsible = await S.PatientAsync("Rae", "Resp");
        var account = await ChargedAsync();
        account = (await Bill(s => s.GetAccountAsync(Ann, default)));
        await Bill(s => s.RecordResponsibilityAsync(Ann, new ResponsibilityInput(responsible, null, "Rae pays", account.RowVersion), S.Actor, default));
        account = await Bill(s => s.GetAccountAsync(Ann, default));
        account = await Pay(account, responsible, "p-r");                                                       // active responsible party: accepted
        account = await Pay(account, guarantor, "p-g");
        Assert.Equal(["Rae Resp", "Gia Guard"], account.Entries.Where(e => e.Kind == BillingKinds.Payment).Select(e => e.PaidByName));

        await ExecuteAsync("UPDATE [Patients] SET [IsActive] = 0 WHERE [Id] IN ({0}, {1}, {2})", guarantor, sibling, responsible);
        var choices = await Choices();
        Assert.Equal([Ann], choices.Payers.Select(p => p.PatientId));
        Assert.Equal(Ann, choices.DefaultPayerId);                                                            // the responsible party is inactive, so the patient
        var before = await BillingCountsAsync();
        foreach (var who in new[] { guarantor, sibling, responsible })
        {
            Assert.Equal("payer_not_eligible", (await RefusedBilling(() => Pay(account, who, $"x-{who:N}"))).Code);
            Assert.Equal("payer_not_eligible", (await RefusedBilling(() => Bill(s => s.CorrectEntryAsync(Ann, new CorrectionInput(3, "Wrong amount", 12m, "Cash", null, who, account.RowVersion, $"fix-{who:N}"), S.Actor, default)))).Code);
        }
        Assert.Equal(before, await BillingCountsAsync());
    }
}
