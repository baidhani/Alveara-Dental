using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Billing;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-007-C02 against real SQL Server: the database refuses, on its own, what the refund, release and document rules refuse and what no service rule can see. These tests go around the service on purpose - a
/// script, a future module or a person with database access cannot refund a payment that is not the account's or for more than it was, refund a reversed payment or reverse a refunded one, return money by an
/// inactive method, reverse a refund, replace an entry that was not reversed, release more than an allocation holds, change or delete a release or an issued document, issue a document for another account's
/// payment or cut off beyond the history, or apply more than a payment holds after a refund.
/// </summary>
public class BillingRefundSchemaTests : BillingTestBase
{
    private sealed record Ledger(BillingAccount Account, BillingEntry Charge1, BillingEntry Charge2, BillingEntry Payment, BillingAllocation Alloc1, BillingAllocation Alloc2, Guid Cash);

    /// <summary>
    /// An account with two 100 charges (entries 1 and 2) and a 150 payment (entry 3): 60 applied to charge 1 (allocation 1) and 40 to charge 2 (allocation 2), so 50 of the payment is unapplied and 50 is owed.
    /// </summary>
    private async Task<Ledger> LedgerAsync()
    {
        var p = await BillablePlanAsync();
        await Svc(s => s.CompleteItemAsync(p.Plan.Id, p.Accepted, S.Actor, default));
        var account = await NewAccountAsync();
        var one = ChargeRow(account, p.Completed, 100m, 1, "c-1");
        var two = ChargeRow(account, p.Accepted, 100m, 2, "c-2");
        await AddRowAsync(one);
        await AddRowAsync(two);
        var pay = PaymentRow(account, -150m, 3, "p-3");
        await AddRowAsync(pay);
        var a1 = AllocationRow(account, pay, one, 60m, 1, "a-1");
        var a2 = AllocationRow(account, pay, two, 40m, 2, "a-2");
        await AddRowAsync(a1);
        await AddRowAsync(a2);
        return new Ledger(account, one, two, pay, a1, a2, await MethodIdAsync(BillingPaymentMethods.Cash));
    }

    private async Task<Guid> MethodIdAsync(string kind)
    {
        await using var db = Fixture.CreateContext();
        return await db.BillingPaymentMethods.AsNoTracking().Where(m => m.IsBuiltIn && m.Kind == kind).Select(m => m.Id).SingleAsync();
    }

    private BillingEntry RefundRow(Ledger l, decimal amount, int number, string key, BillingEntry? payment = null)
    {
        var e = NewEntry(l.Account, BillingKinds.Refund, amount, number, key);
        (e.RefundsEntryId, e.PaymentMethod, e.PaymentMethodId, e.Reason) = ((payment ?? l.Payment).Id, BillingPaymentMethods.Cash, l.Cash, "Returned to the patient");
        return e;
    }

    private BillingAllocationRelease ReleaseRow(Ledger l, BillingAllocation allocation, decimal amount, int number, string key, BillingEntry? refund = null) => new()
    {
        Id = Guid.NewGuid(), AccountId = l.Account.Id, PatientId = l.Account.PatientId, ReleaseNumber = number, AllocationId = allocation.Id, Amount = amount,
        Cause = refund is null ? ReleaseCauses.Correction : ReleaseCauses.Refund, RefundEntryId = refund?.Id, Reason = refund is null ? "Applied to the wrong charge" : null,
        IdempotencyKey = key, ReleasedAtUtc = DateTimeOffset.UtcNow, ReleasedByUserId = S.Actor,
    };

    private FinancialDocument DocumentRow(Ledger l, string type = FinancialDocumentTypes.Statement, int number = 1, string key = "d-1", int asOfEntry = 3, int asOfAllocation = 2, BillingEntry? payment = null) => new()
    {
        Id = Guid.NewGuid(), AccountId = l.Account.Id, PatientId = l.Account.PatientId, DocumentType = type, DocumentNumber = number, PaymentEntryId = type == FinancialDocumentTypes.Receipt ? (payment ?? l.Payment).Id : null,
        AsOfEntryNumber = asOfEntry, AsOfAllocationNumber = asOfAllocation, AsOfReleaseNumber = 0, AsOfResponsibilitySequence = 0, AsOfUtc = DateTimeOffset.UtcNow.AddSeconds(-1), TemplateVersion = 1,
        ContentHash = new string('a', FinancialDocumentLimits.HashLength), EnvelopeJson = "{\"practice\":\"Alveara Dental\"}", IdempotencyKey = key, CorrelationId = Guid.NewGuid(), IssuedAtUtc = DateTimeOffset.UtcNow, IssuedByUserId = S.Actor,
    };

    private async Task AddAsync(params object[] rows)
    {
        await using var db = Fixture.CreateContext();
        db.AddRange(rows);
        await db.SaveChangesAsync();
    }

    private async Task<(int Entries, int Releases, int Documents)> CountsOfAsync()
    {
        await using var db = Fixture.CreateContext();
        return (await db.BillingEntries.CountAsync(), await db.BillingAllocationReleases.CountAsync(), await db.FinancialDocuments.CountAsync());
    }

    // ---------- the rows that are allowed ----------

    [Fact]
    public async Task A_charge_is_reversible_once_every_application_to_it_has_been_released_and_not_before()
    {
        var l = await LedgerAsync();
        await AddRowAsync(AdjustmentRow(l.Account, 100m, 4, "adj-4"));                                     // so the account can afford the reversal (balance 150 - 100)
        var reversal = () => AddRowAsync(ReversalRow(l.Account, l.Charge1, 5, "r-5"));
        Assert.Equal(51141, (await SqlRefusal(reversal)).Number);                                          // 60 of payment 3 is applied to it
        await AddAsync(ReleaseRow(l, l.Alloc1, 20m, 1, "move-1"));
        Assert.Equal(51141, (await SqlRefusal(() => AddRowAsync(ReversalRow(l.Account, l.Charge1, 5, "r-5")))).Number);   // 40 is still applied
        await AddAsync(ReleaseRow(l, l.Alloc1, 40m, 2, "move-2"));
        await AddRowAsync(ReversalRow(l.Account, l.Charge1, 5, "r-5"));                                    // nothing is applied now: the database allows it
        await using var db = Fixture.CreateContext();
        Assert.Equal(5, await db.BillingEntries.CountAsync());
    }

    [Fact]
    public async Task A_refund_of_the_unapplied_part_and_then_of_an_applied_part_with_its_release_is_accepted_and_the_balance_is_the_plain_sum()
    {
        var l = await LedgerAsync();
        var first = RefundRow(l, 30m, 4, "r-4");                                        // 30 of the 50 unapplied: no allocation is touched
        await AddAsync(first);
        var second = RefundRow(l, 50m, 5, "r-5");                                       // 20 unapplied left, so 30 of an application is released: the newest first
        await AddAsync(second);
        await AddAsync(ReleaseRow(l, l.Alloc2, 30m, 1, "r-5", second));

        await using var db = Fixture.CreateContext();
        Assert.Equal(200m - 150m + 80m, await db.BillingEntries.Where(e => e.AccountId == l.Account.Id).SumAsync(e => e.Amount));          // 130 owed: 100 + 100 - 150 + 30 + 50
        Assert.Equal(1, await db.BillingAllocationReleases.CountAsync());
    }

    [Fact]
    public async Task A_payment_can_be_refunded_in_parts_up_to_exactly_what_it_was()
    {
        var l = await LedgerAsync();
        var parts = new[] { 50m, 50m, 50m };
        var number = 4;
        foreach (var part in parts) await AddAsync(RefundRow(l, part, number, $"r-{number++}"));
        var e = await SqlRefusal(() => AddAsync(RefundRow(l, 0.01m, number, $"r-{number}")));
        Assert.Equal(51158, e.Number);
        await using var db = Fixture.CreateContext();
        Assert.Equal(150m, await db.BillingEntries.Where(x => x.Kind == BillingKinds.Refund).SumAsync(x => x.Amount));
    }

    [Fact]
    public async Task A_correction_release_frees_room_and_a_new_application_up_to_the_net_is_accepted()
    {
        var l = await LedgerAsync();
        await AddAsync(ReleaseRow(l, l.Alloc1, 20m, 1, "move-1"));                      // 20 of the 60 applied to charge 1 is released
        await AddAsync(AllocationRow(l.Account, l.Payment, l.Charge1, 20m, 3, "move-1b"));   // and applied again
        await AddAsync(AllocationRow(l.Account, l.Payment, l.Charge2, 10m, 4, "later-1"));   // charge 2 had 40 applied of 100 and the payment has 50 unapplied: 10 more is fine
        await using var db = Fixture.CreateContext();
        Assert.Equal(4, await db.BillingAllocations.CountAsync());
    }

    [Fact]
    public async Task A_reversed_payment_and_a_replacement_payment_are_accepted_once_the_reversal_exists()
    {
        var l = await LedgerAsync();
        var undone = PaymentRow(l.Account, -10m, 4, "p-4");
        await AddAsync(undone);
        var reversal = ReversalRow(l.Account, undone, 5, "rev-5");
        await AddAsync(reversal);
        var replacement = PaymentRow(l.Account, -12m, 6, "p-6");
        replacement.ReplacesEntryId = undone.Id;
        await AddAsync(replacement);
        await using var db = Fixture.CreateContext();
        Assert.Equal(undone.Id, (await db.BillingEntries.AsNoTracking().SingleAsync(e => e.EntryNumber == 6 && e.AccountId == l.Account.Id)).ReplacesEntryId);
    }

    [Fact]
    public async Task An_issued_statement_and_receipt_are_accepted_and_numbered_apart()
    {
        var l = await LedgerAsync();
        await AddAsync(DocumentRow(l, FinancialDocumentTypes.Statement, 1, "d-1"), DocumentRow(l, FinancialDocumentTypes.Receipt, 1, "d-2"));
        await AddAsync(DocumentRow(l, FinancialDocumentTypes.Statement, 2, "d-3"));
        Assert.Equal((3, 0, 3), await CountsOfAsync());
    }

    // ---------- shape rules (check constraints) ----------

    [Theory]
    [InlineData("noPayment", "CK_BillingEntries_Refunds")]
    [InlineData("negativeAmount", "CK_BillingEntries_Sign")]
    [InlineData("noReason", "CK_BillingEntries_Reason")]
    [InlineData("noMethodKind", "CK_BillingEntries_Method")]
    [InlineData("noConfiguredMethod", "CK_BillingEntries_PaymentOnly")]
    [InlineData("withPayer", "CK_BillingEntries_PaymentOnly")]
    [InlineData("adjustmentWithRefundTarget", "CK_BillingEntries_Refunds")]
    [InlineData("replacementOnACharge", "CK_BillingEntries_Replaces")]
    public async Task The_database_refuses_a_refund_or_replacement_that_breaks_a_shape_rule(string which, string constraint)
    {
        var l = await LedgerAsync();
        var refund = RefundRow(l, 10m, 4, "r-4");
        var entry = which switch
        {
            "noPayment" => refund,
            "negativeAmount" => refund,
            "noReason" => refund,
            "noMethodKind" => refund,
            "noConfiguredMethod" => refund,
            "withPayer" => refund,
            "adjustmentWithRefundTarget" => AdjustmentRow(l.Account, 5m, 4, "a-4"),
            _ => refund,                                                                // a refund is not a payment or an adjustment, so it cannot replace anything
        };
        switch (which)
        {
            case "noPayment": entry.RefundsEntryId = null; break;
            case "negativeAmount": entry.Amount = -10m; break;
            case "noReason": entry.Reason = "  "; break;
            case "noMethodKind": entry.PaymentMethod = null; break;
            case "noConfiguredMethod": entry.PaymentMethodId = null; break;
            case "withPayer": entry.PaidByPatientId = l.Account.PatientId; break;
            case "adjustmentWithRefundTarget": entry.RefundsEntryId = l.Payment.Id; break;
            default: entry.ReplacesEntryId = l.Charge1.Id; break;
        }
        var e = await SqlRefusal(() => AddAsync(entry));
        Assert.Contains(constraint, e.Message);
    }

    [Theory]
    [InlineData("zero", "CK_BillingAllocationReleases_Amount")]
    [InlineData("correctionNoReason", "CK_BillingAllocationReleases_Cause")]
    [InlineData("correctionWithRefund", "CK_BillingAllocationReleases_Cause")]
    [InlineData("refundCauseNoRefund", "CK_BillingAllocationReleases_Cause")]
    [InlineData("unknownCause", "CK_BillingAllocationReleases_Cause")]
    [InlineData("numberZero", "CK_BillingAllocationReleases_Number")]
    [InlineData("blankKey", "CK_BillingAllocationReleases_Key")]
    public async Task The_database_refuses_a_release_that_breaks_a_shape_rule(string which, string constraint)
    {
        var l = await LedgerAsync();
        var refund = RefundRow(l, 70m, 4, "r-4");
        await AddAsync(refund);
        var release = ReleaseRow(l, l.Alloc2, 5m, 1, "x-1");
        switch (which)
        {
            case "zero": release.Amount = 0m; break;
            case "correctionNoReason": release.Reason = null; break;
            case "correctionWithRefund": release.RefundEntryId = refund.Id; break;
            case "refundCauseNoRefund": (release.Cause, release.RefundEntryId, release.Reason) = (ReleaseCauses.Refund, null, null); break;
            case "unknownCause": (release.Cause, release.RefundEntryId) = ("Other", null); break;
            case "numberZero": release.ReleaseNumber = 0; break;
            default: release.IdempotencyKey = " "; break;
        }
        var e = await SqlRefusal(() => AddAsync(release));
        Assert.Contains(constraint, e.Message);
        Assert.Equal(0, (await CountsOfAsync()).Releases);
    }

    [Theory]
    [InlineData("unknownType", "CK_FinancialDocuments_Type")]
    [InlineData("numberZero", "CK_FinancialDocuments_Number")]
    [InlineData("receiptWithoutPayment", "CK_FinancialDocuments_Payment")]
    [InlineData("statementWithPayment", "CK_FinancialDocuments_Payment")]
    [InlineData("entryCutOffZero", "CK_FinancialDocuments_CutOffs")]
    [InlineData("negativeCutOff", "CK_FinancialDocuments_CutOffs")]
    [InlineData("templateZero", "CK_FinancialDocuments_CutOffs")]
    [InlineData("hashShort", "CK_FinancialDocuments_Hash")]
    [InlineData("hashUpper", "CK_FinancialDocuments_Hash")]
    [InlineData("envelopeNotJson", "CK_FinancialDocuments_Envelope")]
    [InlineData("asOfAfterIssued", "CK_FinancialDocuments_AsOf")]
    [InlineData("blankKey", "CK_FinancialDocuments_Key")]
    public async Task The_database_refuses_a_document_that_breaks_a_shape_rule(string which, string constraint)
    {
        var l = await LedgerAsync();
        var d = DocumentRow(l);
        switch (which)
        {
            case "unknownType": d.DocumentType = "Invoice"; break;
            case "numberZero": d.DocumentNumber = 0; break;
            case "receiptWithoutPayment": (d.DocumentType, d.PaymentEntryId) = (FinancialDocumentTypes.Receipt, null); break;
            case "statementWithPayment": d.PaymentEntryId = l.Payment.Id; break;
            case "entryCutOffZero": d.AsOfEntryNumber = 0; break;
            case "negativeCutOff": d.AsOfAllocationNumber = -1; break;
            case "templateZero": d.TemplateVersion = 0; break;
            case "hashShort": d.ContentHash = "abc"; break;
            case "hashUpper": d.ContentHash = new string('A', 64); break;
            case "envelopeNotJson": d.EnvelopeJson = "practice: Alveara"; break;
            case "asOfAfterIssued": d.AsOfUtc = d.IssuedAtUtc.AddMinutes(1); break;
            default: d.IdempotencyKey = ""; break;
        }
        var e = await SqlRefusal(() => AddAsync(d));
        if (which == "unknownType") Assert.True(e.Message.Contains("CK_FinancialDocuments_Type") || e.Message.Contains("CK_FinancialDocuments_Payment"), e.Message);      // an unknown type breaks both rules and the database names whichever it meets first
        else Assert.Contains(constraint, e.Message);
        Assert.Equal(0, (await CountsOfAsync()).Documents);
    }

    // ---------- append-only ----------

    [Theory]
    [InlineData("UPDATE [BillingAllocationReleases] SET [Amount] = 1", 51164)]
    [InlineData("DELETE FROM [BillingAllocationReleases]", 51164)]
    [InlineData("UPDATE [FinancialDocuments] SET [ContentHash] = REPLICATE('b', 64)", 51169)]
    [InlineData("DELETE FROM [FinancialDocuments]", 51169)]
    public async Task A_release_and_an_issued_document_can_never_be_changed_or_deleted(string statement, int number)
    {
        var l = await LedgerAsync();
        await AddAsync(ReleaseRow(l, l.Alloc1, 10m, 1, "m-1"), DocumentRow(l));
        var e = await SqlRefusal(() => ExecuteAsync(statement));
        Assert.Equal(number, e.Number);
        Assert.Equal((3, 1, 1), await CountsOfAsync());
    }

    // ---------- refunds and replacements ----------

    [Fact]
    public async Task A_refund_must_return_part_of_a_payment_of_the_same_account()
    {
        var l = await LedgerAsync();
        var ofACharge = await SqlRefusal(() => AddAsync(RefundRow(l, 10m, 4, "r-4", payment: l.Charge1)));
        Assert.Equal(51157, ofACharge.Number);

        var elsewhere = await NewAccountAsync(Bo);
        var their = PaymentRow(elsewhere, -20m, 1, "bo-p1");                            // a payment needs a balance to pay down: give Bo a charge first
        var plan = await BillablePlanAsync(Bo);
        await AddRowAsync(ChargeRow(elsewhere, plan.Completed, 100m, 1, "bo-c1"));
        their.EntryNumber = 2; their.IdempotencyKey = "bo-p2";
        await AddRowAsync(their);
        var other = await SqlRefusal(() => AddAsync(RefundRow(l, 10m, 4, "r-5", payment: their)));
        Assert.Equal(51157, other.Number);
        Assert.Equal(3, (await CountsOfAsync()).Entries - 2);                           // Bo's two entries are the only others; Ann's account still has its three
    }

    [Theory]
    [InlineData("oneTooMuch")]
    [InlineData("twoRefundsTooMuch")]
    public async Task A_payment_cannot_be_refunded_for_more_than_it_was(string which)
    {
        var l = await LedgerAsync();
        if (which == "twoRefundsTooMuch") await AddAsync(RefundRow(l, 100m, 4, "r-4"));
        var e = await SqlRefusal(() => AddAsync(RefundRow(l, which == "oneTooMuch" ? 150.01m : 50.01m, 5, "r-5")));
        Assert.Equal(51158, e.Number);
    }

    [Fact]
    public async Task A_reversed_payment_cannot_be_refunded_and_a_refunded_payment_cannot_be_reversed()
    {
        var l = await LedgerAsync();
        var reversed = PaymentRow(l.Account, -20m, 4, "p-4");
        var refunded = PaymentRow(l.Account, -20m, 5, "p-5");
        await AddAsync(reversed, refunded);
        await AddAsync(ReversalRow(l.Account, reversed, 6, "rev-6"));

        var refundOfReversed = await SqlRefusal(() => AddAsync(RefundRow(l, 5m, 7, "r-7", payment: reversed)));
        Assert.Equal(51159, refundOfReversed.Number);

        await AddAsync(RefundRow(l, 5m, 7, "r-8", payment: refunded));
        var reverseRefunded = await SqlRefusal(() => AddAsync(ReversalRow(l.Account, refunded, 8, "rev-8")));
        Assert.Equal(51160, reverseRefunded.Number);
    }

    [Fact]
    public async Task A_refund_is_returned_by_an_active_method_of_its_own_kind()
    {
        var l = await LedgerAsync();
        var wrongKind = RefundRow(l, 10m, 4, "r-4");
        wrongKind.PaymentMethodId = await MethodIdAsync(BillingPaymentMethods.Card);   // the kind says Cash
        Assert.Contains((await SqlRefusal(() => AddAsync(wrongKind))).Number, new[] { 51143, 51161 });   // two triggers guard this rule (51143 from ALV-007-C01 covers every method-bearing entry); either may fire first

        var retired = new BillingPaymentMethod { Id = Guid.NewGuid(), Name = "Old till", Kind = BillingPaymentMethods.Cash, IsBuiltIn = false, IsActive = false, CreatedAtUtc = DateTimeOffset.UtcNow, CreatedByUserId = S.Actor };
        await using (var db = Fixture.CreateContext()) { db.BillingPaymentMethods.Add(retired); await db.SaveChangesAsync(); }
        var inactive = RefundRow(l, 10m, 4, "r-5");
        inactive.PaymentMethodId = retired.Id;
        Assert.Contains((await SqlRefusal(() => AddAsync(inactive))).Number, new[] { 51143, 51161 });
    }

    [Theory]
    [InlineData("TR_BillingEntries_Reversals", 51161)]                       // with the C01 trigger off, the C02 trigger alone refuses
    [InlineData("TR_BillingEntries_Refunds", 51143)]                         // with the C02 trigger off, the C01 trigger alone refuses
    public async Task Each_of_the_two_triggers_that_guard_a_refunds_method_refuses_on_its_own(string disabled, int expected)
    {
        var l = await LedgerAsync();
        var wrongKind = RefundRow(l, 10m, 4, "r-4");
        wrongKind.PaymentMethodId = await MethodIdAsync(BillingPaymentMethods.Card);
        await ExecuteAsync($"DISABLE TRIGGER {disabled} ON BillingEntries");
        try { Assert.Equal(expected, (await SqlRefusal(() => AddAsync(wrongKind))).Number); }
        finally { await ExecuteAsync($"ENABLE TRIGGER {disabled} ON BillingEntries"); }
    }

    [Fact]
    public async Task A_refund_cannot_be_reversed()
    {
        var l = await LedgerAsync();
        var refund = RefundRow(l, 10m, 4, "r-4");
        await AddAsync(refund);
        var e = await SqlRefusal(() => AddAsync(ReversalRow(l.Account, refund, 5, "rev-5")));
        Assert.Equal(51163, e.Number);
    }

    [Theory]
    [InlineData("notReversed")]
    [InlineData("otherKind")]
    [InlineData("secondReplacement")]
    public async Task A_replacement_stands_in_only_for_a_reversed_entry_of_the_same_kind_and_only_once(string which)
    {
        var l = await LedgerAsync();
        var undone = PaymentRow(l.Account, -10m, 4, "p-4");
        await AddAsync(undone);
        if (which != "notReversed") await AddAsync(ReversalRow(l.Account, undone, 5, "rev-5"));
        var replacement = which == "otherKind" ? AdjustmentRow(l.Account, 3m, 6, "adj-6") : PaymentRow(l.Account, -10m, 6, "p-6");
        replacement.ReplacesEntryId = undone.Id;
        if (which == "secondReplacement")
        {
            await AddAsync(replacement);
            var again = PaymentRow(l.Account, -10m, 7, "p-7");
            again.ReplacesEntryId = undone.Id;
            var dup = await SqlRefusal(() => AddAsync(again));
            Assert.Contains(dup.Number, new[] { 2601, 2627 });
            return;
        }
        var e = await SqlRefusal(() => AddAsync(replacement));
        Assert.Equal(51162, e.Number);
    }

    // ---------- releases ----------

    [Fact]
    public async Task A_release_belongs_to_the_account_and_patient_of_its_allocation_and_a_refunds_release_to_the_refunded_payment()
    {
        var l = await LedgerAsync();
        var elsewhere = await NewAccountAsync(Bo);
        var wrongAccount = ReleaseRow(l, l.Alloc1, 5m, 1, "x-1");
        wrongAccount.AccountId = elsewhere.Id;
        wrongAccount.PatientId = elsewhere.PatientId;
        Assert.Equal(51165, (await SqlRefusal(() => AddAsync(wrongAccount))).Number);

        var second = PaymentRow(l.Account, -50m, 4, "p-4");                            // another payment, with its own refund
        await AddAsync(second);
        var refundOfSecond = RefundRow(l, 10m, 5, "r-5", payment: second);
        await AddAsync(refundOfSecond);
        var wrongPayment = ReleaseRow(l, l.Alloc1, 5m, 1, "x-2", refundOfSecond);       // an allocation of payment 3, released for a refund of payment 4
        Assert.Equal(51165, (await SqlRefusal(() => AddAsync(wrongPayment))).Number);
        Assert.Equal(0, (await CountsOfAsync()).Releases);
    }

    [Fact]
    public async Task A_release_cannot_be_for_more_than_its_allocation_still_holds_even_across_several_releases()
    {
        var l = await LedgerAsync();
        var tooMuch = await SqlRefusal(() => AddAsync(ReleaseRow(l, l.Alloc1, 60.01m, 1, "x-1")));
        Assert.Equal(51166, tooMuch.Number);

        await AddAsync(ReleaseRow(l, l.Alloc1, 40m, 1, "x-2"));
        var across = await SqlRefusal(() => AddAsync(ReleaseRow(l, l.Alloc1, 20.01m, 2, "x-3")));
        Assert.Equal(51166, across.Number);
        await AddAsync(ReleaseRow(l, l.Alloc1, 20m, 2, "x-4"));                          // exactly what is left is fine
        Assert.Equal(2, (await CountsOfAsync()).Releases);
    }

    [Fact]
    public async Task The_releases_of_a_refund_cannot_add_up_to_more_than_the_refund()
    {
        var l = await LedgerAsync();
        var refund = RefundRow(l, 60m, 4, "r-4");                                       // 50 unapplied, so at most 10 of an application is released
        await AddAsync(refund);
        await AddAsync(ReleaseRow(l, l.Alloc2, 30m, 1, "r-4", refund), ReleaseRow(l, l.Alloc1, 30m, 2, "r-4", refund));
        var over = await SqlRefusal(() => AddAsync(ReleaseRow(l, l.Alloc1, 1m, 3, "r-4", refund)));
        Assert.Equal(51167, over.Number);                                               // 61 released for a 60 refund
    }

    [Fact]
    public async Task A_reversed_payments_allocations_are_already_released_by_its_reversal_and_cannot_be_released_again()
    {
        var l = await LedgerAsync();
        await AddAsync(ReversalRow(l.Account, l.Payment, 4, "rev-4"));
        var e = await SqlRefusal(() => AddAsync(ReleaseRow(l, l.Alloc1, 10m, 1, "x-1")));
        Assert.Equal(51168, e.Number);
    }

    // ---------- the allocation caps now count net of releases and refunds ----------

    [Fact]
    public async Task After_a_refund_a_payment_cannot_be_applied_for_more_than_what_is_left_of_it()
    {
        var l = await LedgerAsync();
        var refund = RefundRow(l, 80m, 4, "r-4");                                       // 70 left: 100 applied, 30 released
        await AddAsync(refund);
        await AddAsync(ReleaseRow(l, l.Alloc2, 30m, 1, "r-4", refund));
        var e = await SqlRefusal(() => AddAsync(AllocationRow(l.Account, l.Payment, l.Charge1, 5m, 3, "later-1")));      // 70 applied + 5 > 70
        Assert.Equal(51130, e.Number);
    }

    [Fact]
    public async Task A_charge_cannot_be_paid_for_more_than_it_is_counting_only_what_remains_after_releases()
    {
        var l = await LedgerAsync();
        var second = PaymentRow(l.Account, -50m, 4, "p-4");
        await AddAsync(second);
        await AddAsync(ReleaseRow(l, l.Alloc2, 30m, 1, "move-1"));                      // charge 2 now holds 10 of payment 3
        await AddAsync(AllocationRow(l.Account, second, l.Charge2, 50m, 3, "later-1")); // 10 + 50 = 60 of 100
        var over = await SqlRefusal(() => AddAsync(AllocationRow(l.Account, l.Payment, l.Charge2, 40.01m, 4, "later-2")));    // 60 + 40.01 > 100
        Assert.Equal(51131, over.Number);
        await AddAsync(AllocationRow(l.Account, l.Payment, l.Charge2, 40m, 4, "later-3"));                                  // exactly full is fine
    }

    // ---------- documents ----------

    [Fact]
    public async Task A_document_carries_the_patient_of_its_account_and_a_receipt_is_for_a_payment_that_existed_at_its_cut_off()
    {
        var l = await LedgerAsync();
        var wrongPatient = DocumentRow(l);
        wrongPatient.PatientId = Bo;
        Assert.Equal(51170, (await SqlRefusal(() => AddAsync(wrongPatient))).Number);

        var ofACharge = DocumentRow(l, FinancialDocumentTypes.Receipt, 1, "d-2", payment: l.Charge1);
        Assert.Equal(51170, (await SqlRefusal(() => AddAsync(ofACharge))).Number);

        var tooEarly = DocumentRow(l, FinancialDocumentTypes.Receipt, 1, "d-3", asOfEntry: 2);        // payment 3 did not yet exist at entry 2
        Assert.Equal(51170, (await SqlRefusal(() => AddAsync(tooEarly))).Number);
        Assert.Equal(0, (await CountsOfAsync()).Documents);
    }

    [Theory]
    [InlineData("entry")]
    [InlineData("allocation")]
    [InlineData("release")]
    [InlineData("responsibility")]
    public async Task A_document_cannot_be_cut_off_beyond_the_history_that_exists(string which)
    {
        var l = await LedgerAsync();
        var d = DocumentRow(l);
        switch (which)
        {
            case "entry": d.AsOfEntryNumber = 4; break;
            case "allocation": d.AsOfAllocationNumber = 3; break;
            case "release": d.AsOfReleaseNumber = 1; break;
            default: d.AsOfResponsibilitySequence = 1; break;
        }
        Assert.Equal(51171, (await SqlRefusal(() => AddAsync(d))).Number);
    }

    [Fact]
    public async Task A_document_number_and_a_document_key_are_each_used_once_per_account()
    {
        var l = await LedgerAsync();
        await AddAsync(DocumentRow(l, FinancialDocumentTypes.Statement, 1, "d-1"));
        var sameNumber = await SqlRefusal(() => AddAsync(DocumentRow(l, FinancialDocumentTypes.Statement, 1, "d-2")));
        Assert.Contains(sameNumber.Number, new[] { 2601, 2627 });
        var sameKey = await SqlRefusal(() => AddAsync(DocumentRow(l, FinancialDocumentTypes.Statement, 2, "d-1")));
        Assert.Contains(sameKey.Number, new[] { 2601, 2627 });
        await AddAsync(DocumentRow(l, FinancialDocumentTypes.Receipt, 1, "d-3"));                  // the same number for a receipt is fine
        Assert.Equal(2, (await CountsOfAsync()).Documents);
    }
}
