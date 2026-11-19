using Alveara.Api.Architecture.Billing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N024 (PD-17, OQ-4, F-114, F-115, F-208, Q-5cs; briefing revision 2) against real SQL Server, through the service: one receipt per payment.
/// - A second receipt preview is refused (<c>receipt_exists</c>, naming the receipt); a replacement names the latest receipt, needs a reason, gets the next number, is audited with the reason, and the
///   original then reads "superseded by"; a replaced receipt cannot be replaced again (<c>receipt_replaced</c>); a statement is never a replacement.
/// - The list says what is true now: replaces, superseded by, payment reversed, amount refunded; notes write money as "$20.00".
/// - A legacy pair (issued before replacements existed) is left as it is, marked with a neutral notice, and replaced only through its latest receipt.
/// </summary>
public class BillingDocumentReplacementTests : BillingTestBase
{
    private static AllocationInput To(int charge, decimal amount) => new(charge, amount);
    private Task<DocumentPreview> Preview(int payment, Guid? replaces = null) => Bill(s => s.PreviewDocumentAsync(Ann, new DocumentPreviewInput("Receipt", payment, replaces), default));
    private Task<IssuedDocumentView> Issue(DocumentPreview preview, string key, string? reason = null) =>
        Bill(s => s.IssueDocumentAsync(Ann, new IssueDocumentInput(preview.PreviewToken, key, reason), S.Actor, default));
    private Task<IReadOnlyList<IssuedDocumentView>> ListAsync() => Bill(s => s.ListDocumentsAsync(Ann, default));
    private Task<int> DocumentsAsync() => WithDb(db => db.FinancialDocuments.CountAsync());

    /// <summary>Charges 1 and 2 are 100 each; payment 3 is 150 (60 on charge 1, 40 on charge 2, 50 unapplied).</summary>
    private async Task<BillingAccountView> PaidAsync()
    {
        var plan = await BillablePlanAsync();
        await LegacyComplete(plan.Plan.Id, plan.Accepted);
        await Bill(s => s.RecordChargeAsync(Ann, new ChargeInput(plan.Completed, "c-1"), S.Actor, default));
        var two = await Bill(s => s.RecordChargeAsync(Ann, new ChargeInput(plan.Accepted, "c-2"), S.Actor, default));
        return await Bill(s => s.RecordPaymentAsync(Ann, new PaymentInput(150m, "Cash", null, two.RowVersion, "p-1", [To(1, 60m), To(2, 40m)]), S.Actor, default));
    }

    [Fact]
    public async Task A_second_receipt_is_refused_and_a_replacement_with_a_reason_supersedes_the_first()
    {
        await PaidAsync();
        var first = await Issue(await Preview(3), "r-1");

        var exists = await RefusedBilling(() => Preview(3));
        Assert.Equal(("receipt_exists", 409), (exists.Code, exists.StatusCode));
        Assert.Contains("already has receipt 1", exists.Message);

        var replacement = await Preview(3, first.Id);
        Assert.Equal(1, replacement.ReplacesNumber);
        foreach (var blank in new string?[] { null, "   " })
        {
            var missing = await RefusedBilling(() => Issue(replacement, "r-2", blank));
            Assert.Equal("Say why the receipt is being replaced.", missing.FieldErrors["replacementReason"]);
        }
        Assert.Equal(1, await DocumentsAsync());                                                          // nothing stored by the refusals

        var second = await Issue(replacement, "r-2", "Patient lost the first copy");
        Assert.Equal((2, 1, "Patient lost the first copy"), (second.DocumentNumber, second.ReplacesNumber, second.ReplacementReason));
        Assert.Equal(second.Id, (await Issue(replacement, "r-2", "Patient lost the first copy")).Id);      // a retry returns the same document
        Assert.Equal(2, await DocumentsAsync());

        var audit = await WithDb(db => db.AuditLogEntries.AsNoTracking().SingleAsync(a => a.EventType == "FinancialDocumentReplaced"));
        Assert.Equal(("Receipt 2 replaces receipt 1 for payment 3.", "Patient lost the first copy"), (audit.Details, audit.Reason));

        var list = await ListAsync();
        Assert.Equal(2, list.Single(d => d.Id == first.Id).SupersededByNumber);
        Assert.Equal(1, list.Single(d => d.Id == second.Id).ReplacesNumber);
        Assert.All(list, d => Assert.False(d.LegacyDuplicate));
        var opened = await Bill(s => s.GetDocumentAsync(Ann, first.Id, S.Actor, default));
        Assert.Contains("Superseded by receipt 2.", opened.CurrentStatus);

        Assert.Equal("receipt_replaced", (await RefusedBilling(() => Preview(3, first.Id))).Code);        // the first is already replaced
        Assert.Equal("receipt_not_found", (await RefusedBilling(() => Preview(3, Guid.NewGuid()))).Code);
        var third = await Issue(await Preview(3, second.Id), "r-3", "Name corrected");                  // a chain through the latest
        Assert.Equal((3, 2), (third.DocumentNumber, third.ReplacesNumber));

        var statement = await RefusedBilling(() => Bill(s => s.PreviewDocumentAsync(Ann, new DocumentPreviewInput("Statement", null, first.Id), default)));
        Assert.Contains("replacesDocumentId", statement.FieldErrors.Keys);
    }

    [Fact]
    public async Task The_list_says_when_a_receipts_payment_was_later_refunded_or_reversed_and_notes_write_money_as_on_screen()
    {
        var paid = await PaidAsync();
        var receipt = await Issue(await Preview(3), "r-1");
        var refunded = await Bill(s => s.RefundPaymentAsync(Ann, new RefundInput(3, 20m, "Cash", "Patient request", paid.RowVersion, "rf-1"), S.Actor, default));
        var row = (await ListAsync()).Single();
        Assert.Equal((20m, false), (row.PaymentRefunded, row.PaymentReversed));
        var notes = (await Bill(s => s.GetDocumentAsync(Ann, receipt.Id, S.Actor, default))).CurrentStatus;
        Assert.Contains("$20.00 of payment 3 has since been refunded.", notes);
        Assert.DoesNotContain(notes, n => n.Contains("USD"));
        Assert.NotNull(refunded);

        var plan = await BillablePlanAsync(Bo);                                                            // a reversed payment, on another patient
        await Bill(s => s.RecordChargeAsync(Bo, new ChargeInput(plan.Completed, "bc-1"), S.Actor, default));
        var boAccount = await Bill(s => s.GetAccountAsync(Bo, default));
        var bo = await Bill(s => s.RecordPaymentAsync(Bo, new PaymentInput(30m, "Cash", null, boAccount.RowVersion, "bp-1"), S.Actor, default));
        var boPreview = await Bill(s => s.PreviewDocumentAsync(Bo, new DocumentPreviewInput("Receipt", 2), default));
        var boReceipt = await Bill(s => s.IssueDocumentAsync(Bo, new IssueDocumentInput(boPreview.PreviewToken, "br-1"), S.Actor, default));
        await Bill(s => s.ReverseEntryAsync(Bo, new ReversalInput(2, "Entered on the wrong patient", bo.RowVersion, "rv-1"), S.Actor, default));
        Assert.True((await Bill(s => s.ListDocumentsAsync(Bo, default))).Single(d => d.Id == boReceipt.Id).PaymentReversed);
    }

    [Fact]
    public async Task A_legacy_pair_is_left_as_it_is_with_a_neutral_notice_and_is_replaced_through_its_latest_receipt()
    {
        await PaidAsync();
        // Data issued before this story existed: two receipts for payment 3. The trigger that now forbids it is switched off only to write that history, then switched back on.
        var first = await Issue(await Preview(3), "legacy-1");
        await ExecuteAsync("DISABLE TRIGGER [TR_FinancialDocuments_Links] ON [FinancialDocuments]");
        try
        {
            await ExecuteAsync(@"INSERT INTO [FinancialDocuments] ([Id],[AccountId],[PatientId],[DocumentType],[DocumentNumber],[PaymentEntryId],[AsOfEntryNumber],[AsOfAllocationNumber],[AsOfReleaseNumber],
                [AsOfResponsibilitySequence],[AsOfUtc],[TemplateVersion],[ContentHash],[EnvelopeJson],[IdempotencyKey],[CorrelationId],[IssuedAtUtc],[IssuedByUserId])
                SELECT NEWID(),[AccountId],[PatientId],'Receipt',2,[PaymentEntryId],[AsOfEntryNumber],[AsOfAllocationNumber],[AsOfReleaseNumber],[AsOfResponsibilitySequence],[AsOfUtc],[TemplateVersion],
                    [ContentHash],[EnvelopeJson],'legacy-2',NEWID(),[IssuedAtUtc],[IssuedByUserId] FROM [FinancialDocuments] WHERE [Id] = {0}", first.Id);
        }
        finally
        {
            await ExecuteAsync("ENABLE TRIGGER [TR_FinancialDocuments_Links] ON [FinancialDocuments]");
        }
        var list = await ListAsync();
        Assert.Equal(2, list.Count);
        Assert.All(list, d => Assert.True(d.LegacyDuplicate));
        Assert.All(list, d => Assert.Null(d.ReplacesNumber));
        Assert.Contains("Multiple legacy receipts exist for this payment.", (await Bill(s => s.GetDocumentAsync(Ann, first.Id, S.Actor, default))).CurrentStatus);

        Assert.Contains("already has receipt 2", (await RefusedBilling(() => Preview(3))).Message);
        Assert.Equal("receipt_replaced", (await RefusedBilling(() => Preview(3, first.Id))).Code);       // not the latest
        var latest = list.Single(d => d.DocumentNumber == 2);
        var replacement = await Issue(await Preview(3, latest.Id), "r-3", "Duplicate cleared up");
        Assert.Equal((3, 2), (replacement.DocumentNumber, replacement.ReplacesNumber));
        Assert.False((await ListAsync()).Single(d => d.Id == replacement.Id).LegacyDuplicate);
    }
}
