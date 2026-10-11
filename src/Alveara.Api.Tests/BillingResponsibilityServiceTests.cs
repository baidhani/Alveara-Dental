using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Billing;
using Alveara.Api.Architecture.Concurrency;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-007-C01 against real SQL Server: who is responsible for an account is recorded by billing, apart from the patient's registered guarantor (it follows that guarantor only until billing records otherwise, and
/// neither ever rewrites the other); a guarantor sees everything they are responsible for with each account's own ledger balance; payments can be recorded as made by the responsible party; and the practice's payment
/// methods are configured by billing (named methods mapped to the four kinds, inactivated rather than deleted, the built-ins always available). Every change is audited and a failed audit stores nothing.
/// </summary>
public class BillingResponsibilityServiceTests : BillingTestBase
{
    private Task<BillingResponsibilityView> Responsibility(Guid? patient = null) => Bill(s => s.GetResponsibilityAsync(patient ?? Ann, default));
    private Task<BillingResponsibilityView> Record(Guid patient, Guid? responsible, string? version = null, DateOnly? from = null, string? reason = null) =>
        Bill(s => s.RecordResponsibilityAsync(patient, new ResponsibilityInput(responsible, from, reason, version), S.Actor, default));
    private Task<BillingAccountView> Charge(Guid patient, Guid planItem, string key) => Bill(s => s.RecordChargeAsync(patient, new ChargeInput(planItem, key), S.Actor, default));
    private Task SetRegisteredGuarantor(Guid patient, Guid? guarantor) =>
        guarantor is null ? ExecuteAsync("UPDATE [Patients] SET GuarantorPatientId = NULL WHERE Id = {0}", patient) : ExecuteAsync("UPDATE [Patients] SET GuarantorPatientId = {0} WHERE Id = {1}", guarantor, patient);

    // ---------- who is responsible ----------

    [Fact]
    public async Task Without_a_record_the_account_follows_the_registered_guarantor_and_otherwise_the_patient()
    {
        var alone = await Responsibility();
        Assert.Equal((Ann, true, "Self", null), (alone.ResponsiblePatientId, alone.IsSelf, alone.Source, alone.RowVersion));
        Assert.Empty(alone.History);

        await SetRegisteredGuarantor(Ann, Bo);
        var followed = await Responsibility();
        Assert.Equal((Bo, false, "RegisteredGuarantor"), (followed.ResponsiblePatientId, followed.IsSelf, followed.Source));
        Assert.False(string.IsNullOrWhiteSpace(followed.ResponsibleName));

        await ExecuteAsync("UPDATE [Patients] SET IsActive = 0 WHERE Id = {0}", Bo);                           // an inactive guarantor is not followed
        Assert.Equal("Self", (await Responsibility()).Source);
    }

    [Fact]
    public async Task Recording_a_responsible_party_opens_the_account_keeps_the_history_and_never_touches_the_registered_guarantor()
    {
        await SetRegisteredGuarantor(Ann, Bo);
        var recorded = await Record(Ann, Ann, reason: "Ann pays for herself now");
        Assert.Equal((Ann, true, "Recorded"), (recorded.ResponsiblePatientId, recorded.IsSelf, recorded.Source));
        Assert.NotNull(recorded.RowVersion);                                                                    // the account exists now
        var item = Assert.Single(recorded.History);
        Assert.Equal((1, Ann, "Ann pays for herself now"), (item.Sequence, item.ResponsiblePatientId, item.Reason));
        Assert.False(string.IsNullOrWhiteSpace(item.SetByName));

        await using (var db = Fixture.CreateContext())                                                          // independent of patient identity: the registered guarantor is exactly as it was
            Assert.Equal(Bo, (await db.Patients.AsNoTracking().SingleAsync(p => p.Id == Ann)).GuarantorPatientId);

        var next = await Record(Ann, Bo, recorded.RowVersion);
        Assert.Equal((Bo, "Recorded"), (next.ResponsiblePatientId, next.Source));
        Assert.Equal(new[] { 1, 2 }, next.History.Select(h => h.Sequence).ToArray());
        await SetRegisteredGuarantor(Ann, null);                                                                // changing the registered guarantor does not rewrite what billing recorded
        Assert.Equal((Bo, "Recorded"), ((await Responsibility()).ResponsiblePatientId, (await Responsibility()).Source));
    }

    [Fact]
    public async Task Naming_the_party_who_is_already_responsible_changes_nothing()
    {
        var first = await Record(Ann, Bo);
        var again = await Record(Ann, Bo, first.RowVersion);
        Assert.Equal((1, first.RowVersion), (again.History.Count, again.RowVersion));
        Assert.Equal((1, 1), ((await BillingCountsAsync()).Accounts, (await BillingCountsAsync()).Audit));
    }

    [Theory]
    [InlineData("noOne", "responsiblePatientId")]
    [InlineData("unknown", "responsiblePatientId")]
    [InlineData("inactive", "responsiblePatientId")]
    [InlineData("future", "effectiveFrom")]
    [InlineData("longReason", "reason")]
    public async Task A_wrong_responsibility_is_refused_beside_its_field_and_nothing_is_recorded(string which, string field)
    {
        await ExecuteAsync("UPDATE [Patients] SET IsActive = 0 WHERE Id = {0}", Bo);
        var e = await RefusedBilling(() => which switch
        {
            "noOne" => Record(Ann, null),
            "unknown" => Record(Ann, Guid.NewGuid()),
            "inactive" => Record(Ann, Bo),
            "future" => Record(Ann, Ann, from: new DateOnly(2999, 1, 1)),
            _ => Record(Ann, Ann, reason: new string('x', 501)),
        });
        Assert.Equal(("validation_failed", 400), (e.Code, e.StatusCode));
        Assert.Contains(field, e.FieldErrors.Keys);
        Assert.Equal((0, 0), ((await BillingCountsAsync()).Accounts, (await BillingCountsAsync()).Audit));
    }

    [Fact]
    public async Task A_date_before_the_one_it_replaces_is_refused_and_a_stale_account_is_a_conflict()
    {
        var first = await Record(Ann, Bo, from: new DateOnly(2020, 6, 1));
        var early = await RefusedBilling(() => Record(Ann, Ann, first.RowVersion, from: new DateOnly(2020, 5, 31)));
        Assert.Contains("effectiveFrom", early.FieldErrors.Keys);
        var missing = await RefusedBilling(() => Record(Ann, Ann, null));
        Assert.Equal("row_version_required", missing.Code);

        var second = await Record(Ann, Ann, first.RowVersion, from: new DateOnly(2020, 6, 1));
        Assert.Equal(2, second.History.Count);
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => Record(Ann, Bo, first.RowVersion));       // the copy in hand is out of date
        Assert.Equal(2, (await Responsibility()).History.Count);
    }

    [Fact]
    public async Task A_payment_can_be_recorded_as_made_by_the_responsible_party_once_billing_has_recorded_them()
    {
        var plan = await BillablePlanAsync();
        var charged = await Charge(Ann, plan.Completed, "c-1");
        var stranger = await RefusedBilling(() => Bill(s => s.RecordPaymentAsync(Ann, new PaymentInput(10m, "Cash", null, charged.RowVersion, "p-1", null, Bo), S.Actor, default)));
        Assert.Contains("paidBy", stranger.FieldErrors.Keys);

        var recorded = await Record(Ann, Bo, charged.RowVersion);
        var paid = await Bill(s => s.RecordPaymentAsync(Ann, new PaymentInput(10m, "Cash", null, recorded.RowVersion, "p-2", null, Bo), S.Actor, default));
        Assert.False(string.IsNullOrWhiteSpace(paid.Entries[1].PaidByName));
        Assert.Equal(90m, paid.BalanceOwed);
    }

    [Fact]
    public async Task Recording_who_is_responsible_is_audited_without_names_and_a_failed_audit_stores_nothing()
    {
        await Record(Ann, Bo, reason: "Bo pays for Ann");
        await using (var db = Fixture.CreateContext())
        {
            var audit = await db.AuditLogEntries.AsNoTracking().SingleAsync(a => a.EntityType == nameof(BillingAccount));
            Assert.Equal(("BillingResponsibilityChanged", S.Actor, "Bo pays for Ann"), (audit.EventType, audit.PerformedByUserAccountId, audit.Reason));
            Assert.DoesNotContain("Ann", audit.Details);
            Assert.DoesNotContain("Bo", audit.Details);
        }

        await ExecuteAsync(@"CREATE TRIGGER [TR_Test_RefuseBillingAudit] ON [AuditLogEntries] INSTEAD OF INSERT AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted WHERE EntityType IN ('BillingAccount', 'BillingPaymentMethod')) THROW 59030, 'audit unavailable', 1;
    INSERT INTO [AuditLogEntries] SELECT * FROM inserted;
END");
        var version = (await Responsibility()).RowVersion;
        var e = await RefusedBilling(() => Record(Ann, Ann, version));
        Assert.Equal(("save_failed", 503), (e.Code, e.StatusCode));
        Assert.Equal(Bo, (await Responsibility()).ResponsiblePatientId);                                       // unchanged
        var method = await RefusedBilling(() => Bill(s => s.AddPaymentMethodAsync(new PaymentMethodInput("Venmo", "Other"), S.Actor, default)));
        Assert.Equal("save_failed", method.Code);
        Assert.Equal(4, (await Bill(s => s.ListPaymentMethodsAsync(true, default))).Count);
    }

    // ---------- the guarantor's view ----------

    [Fact]
    public async Task A_guarantor_sees_every_account_they_are_responsible_for_with_each_accounts_own_balance_and_a_total()
    {
        var ann = await BillablePlanAsync();
        var bo = await BillablePlanAsync(Bo);
        await Charge(Ann, ann.Completed, "ann-1");
        var boCharged = await Charge(Bo, bo.Completed, "bo-1");
        var own = (await Bill(s => s.GetGuarantorSummaryAsync(Ann, default))).Accounts.Single();               // with no guarantor, Ann answers for her own account
        Assert.Equal((true, 100m), (own.IsGuarantor, own.BalanceOwed));

        await SetRegisteredGuarantor(Ann, Bo);                                                                   // by default Ann's account follows her registered guarantor, Bo
        Assert.Empty((await Bill(s => s.GetGuarantorSummaryAsync(Ann, default))).Accounts);                      // and Ann no longer answers for it
        var followed = await Bill(s => s.GetGuarantorSummaryAsync(Bo, default));
        Assert.Equal(new[] { true, false }, followed.Accounts.Select(a => a.IsGuarantor).ToArray());              // Bo's own first, then the dependents
        Assert.Equal(200m, followed.TotalOwed);

        var paid = await Bill(s => s.RecordPaymentAsync(Bo, new PaymentInput(30m, "Cash", null, boCharged.RowVersion, "bo-p", null), S.Actor, default));
        var after = await Bill(s => s.GetGuarantorSummaryAsync(Bo, default));
        Assert.Equal(new[] { 70m, 100m }, after.Accounts.Select(a => a.BalanceOwed).ToArray());
        Assert.Equal((170m, "USD"), (after.TotalOwed, after.Currency));
        Assert.Equal(paid.BalanceOwed, after.Accounts[0].BalanceOwed);                                            // each figure is that account's own ledger balance

        await Record(Ann, Ann, (await Responsibility()).RowVersion);                                             // billing records Ann as responsible for herself: she leaves Bo's summary
        Assert.Single((await Bill(s => s.GetGuarantorSummaryAsync(Bo, default))).Accounts);
        Assert.Single((await Bill(s => s.GetGuarantorSummaryAsync(Ann, default))).Accounts);
    }

    [Fact]
    public async Task A_patient_who_is_responsible_for_nobody_has_an_empty_summary_and_an_unknown_patient_is_a_404()
    {
        var none = await Bill(s => s.GetGuarantorSummaryAsync(Bo, default));
        Assert.Equal((0m, 0), (none.TotalOwed, none.Accounts.Count));
        Assert.Equal("patient_not_found", (await RefusedBilling(() => Bill(s => s.GetGuarantorSummaryAsync(Guid.NewGuid(), default)))).Code);
    }

    // ---------- payment methods ----------

    [Fact]
    public async Task The_four_built_in_methods_are_listed_first_and_billing_can_add_a_named_method_that_maps_to_a_kind()
    {
        var methods = await Bill(s => s.ListPaymentMethodsAsync(false, default));
        Assert.Equal(new[] { "Card", "Cash", "Check", "Other" }, methods.Select(m => m.Name).ToArray());
        Assert.All(methods, m => Assert.True(m.IsBuiltIn && m.IsActive));

        var venmo = await Bill(s => s.AddPaymentMethodAsync(new PaymentMethodInput("  Venmo  ", "other"), S.Actor, default));
        Assert.Equal(("Venmo", "Other", false, true), (venmo.Name, venmo.Kind, venmo.IsBuiltIn, venmo.IsActive));
        Assert.Equal(5, (await Bill(s => s.ListPaymentMethodsAsync(false, default))).Count);
        Assert.Equal("Venmo", (await Bill(s => s.ListPaymentMethodsAsync(false, default)))[^1].Name);              // custom ones follow the built-ins
    }

    [Fact]
    public async Task Adding_a_method_again_is_quiet_a_different_kind_under_the_same_name_is_refused_and_two_at_once_make_one()
    {
        var first = await Bill(s => s.AddPaymentMethodAsync(new PaymentMethodInput("Venmo", "Other"), S.Actor, default));
        var again = await Bill(s => s.AddPaymentMethodAsync(new PaymentMethodInput("VENMO", "Other"), S.Actor, default));
        Assert.Equal(first.Id, again.Id);
        var clash = await RefusedBilling(() => Bill(s => s.AddPaymentMethodAsync(new PaymentMethodInput("venmo", "Card"), S.Actor, default)));
        Assert.Equal(("method_exists", 409), (clash.Code, clash.StatusCode));
        Assert.Equal("method_exists", (await RefusedBilling(() => Bill(s => s.AddPaymentMethodAsync(new PaymentMethodInput("cash", "Check"), S.Actor, default)))).Code);   // a built-in's name is taken

        var both = await Task.WhenAll(Bill(s => s.AddPaymentMethodAsync(new PaymentMethodInput("Zelle", "Other"), S.Actor, default)), Bill(s => s.AddPaymentMethodAsync(new PaymentMethodInput("Zelle", "Other"), S.Actor, default)));
        Assert.Equal(both[0].Id, both[1].Id);
        Assert.Equal(1, (await Bill(s => s.ListPaymentMethodsAsync(true, default))).Count(m => m.Name == "Zelle"));
    }

    [Theory]
    [InlineData(null, "Other", "name")]
    [InlineData("   ", "Other", "name")]
    [InlineData("Ve\u0007nmo", "Other", "name")]
    [InlineData("Venmo", "Barter", "kind")]
    [InlineData("Venmo", null, "kind")]
    public async Task A_wrong_method_is_refused_beside_its_field(string? name, string? kind, string field)
    {
        var e = await RefusedBilling(() => Bill(s => s.AddPaymentMethodAsync(new PaymentMethodInput(name, kind), S.Actor, default)));
        Assert.Equal(("validation_failed", 400), (e.Code, e.StatusCode));
        Assert.Contains(field, e.FieldErrors.Keys);
        var tooLong = await RefusedBilling(() => Bill(s => s.AddPaymentMethodAsync(new PaymentMethodInput(new string('x', 41), "Other"), S.Actor, default)));
        Assert.Contains("name", tooLong.FieldErrors.Keys);
        Assert.Equal(4, (await Bill(s => s.ListPaymentMethodsAsync(true, default))).Count);
    }

    [Fact]
    public async Task A_method_is_inactivated_not_deleted_the_built_ins_stay_and_old_payments_keep_showing_their_method()
    {
        var plan = await BillablePlanAsync();
        var charged = await Charge(Ann, plan.Completed, "c-1");
        var venmo = await Bill(s => s.AddPaymentMethodAsync(new PaymentMethodInput("Venmo", "Other"), S.Actor, default));
        var paid = await Bill(s => s.RecordPaymentAsync(Ann, new PaymentInput(10m, "Venmo", null, charged.RowVersion, "p-1"), S.Actor, default));

        var off = await Bill(s => s.SetPaymentMethodActiveAsync(venmo.Id, false, venmo.RowVersion, S.Actor, default));
        Assert.False(off.IsActive);
        Assert.DoesNotContain(await Bill(s => s.ListPaymentMethodsAsync(false, default)), m => m.Name == "Venmo");
        Assert.Contains(await Bill(s => s.ListPaymentMethodsAsync(true, default)), m => m.Name == "Venmo" && !m.IsActive);
        var refused = await RefusedBilling(() => Bill(s => s.RecordPaymentAsync(Ann, new PaymentInput(10m, "Venmo", null, paid.RowVersion, "p-2"), S.Actor, default)));
        Assert.Contains("method", refused.FieldErrors.Keys);
        Assert.Equal("Venmo", (await Bill(s => s.GetAccountAsync(Ann, default))).Entries[1].PaymentMethodName);

        var quiet = await Bill(s => s.SetPaymentMethodActiveAsync(venmo.Id, false, "AAAA", S.Actor, default));    // already inactive: nothing to change, whatever the version
        Assert.False(quiet.IsActive);
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => Bill(s => s.SetPaymentMethodActiveAsync(venmo.Id, true, venmo.RowVersion, S.Actor, default)));   // the copy in hand is out of date
        var on = await Bill(s => s.SetPaymentMethodActiveAsync(venmo.Id, true, off.RowVersion, S.Actor, default));
        Assert.True(on.IsActive);

        var cash = (await Bill(s => s.ListPaymentMethodsAsync(false, default))).First(m => m.Name == "Cash");
        var builtIn = await RefusedBilling(() => Bill(s => s.SetPaymentMethodActiveAsync(cash.Id, false, cash.RowVersion, S.Actor, default)));
        Assert.Equal(("built_in_method", 409), (builtIn.Code, builtIn.StatusCode));
        Assert.Equal("method_not_found", (await RefusedBilling(() => Bill(s => s.SetPaymentMethodActiveAsync(Guid.NewGuid(), true, "AAAA", S.Actor, default)))).Code);
        Assert.Equal("row_version_required", (await RefusedBilling(() => Bill(s => s.SetPaymentMethodActiveAsync(venmo.Id, false, null, S.Actor, default)))).Code);
    }

    [Fact]
    public async Task Method_changes_are_audited_with_the_user()
    {
        var venmo = await Bill(s => s.AddPaymentMethodAsync(new PaymentMethodInput("Venmo", "Other"), S.Actor, default));
        await Bill(s => s.SetPaymentMethodActiveAsync(venmo.Id, false, venmo.RowVersion, S.Actor, default));
        await using var db = Fixture.CreateContext();
        var audit = await db.AuditLogEntries.AsNoTracking().Where(a => a.EntityType == nameof(BillingPaymentMethod)).Select(a => new { a.EventType, a.Details, a.PerformedByUserAccountId }).ToListAsync();
        Assert.Equal(new[] { "BillingPaymentMethodAdded", "BillingPaymentMethodChanged" }, audit.Select(a => a.EventType).Order().ToArray());
        Assert.All(audit, a => Assert.Equal(S.Actor, a.PerformedByUserAccountId));
        Assert.All(audit, a => Assert.Contains("Venmo", a.Details));
    }
}
