using Alveara.Api.Architecture.Billing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-007-C02 against real SQL Server: a statement or receipt is previewed, issued only from that preview (a deterministic stale-preview control proves a changed name stops it), numbered per account and type,
/// stored with no amount, reprinted exactly as issued whatever happens to the ledger afterwards (with the later changes listed apart), refused with <c>document_content_changed</c> if its stored hash no longer
/// matches, addressed to the party who was responsible on the day, and exported as CSV only for a statement.
/// </summary>
public class BillingDocumentServiceTests : BillingTestBase
{
    private Task<BillingAccountView> Charge(Guid planItem, string key) => Bill(s => s.RecordChargeAsync(Ann, new ChargeInput(planItem, key), S.Actor, default));
    private Task<BillingAccountView> Pay(BillingAccountView account, decimal amount, string key, IReadOnlyList<AllocationInput>? allocations = null) =>
        Bill(s => s.RecordPaymentAsync(Ann, new PaymentInput(amount, "Cash", null, account.RowVersion, key, allocations), S.Actor, default));
    private Task<DocumentPreview> Preview(string type, int? payment = null) => Bill(s => s.PreviewDocumentAsync(Ann, new DocumentPreviewInput(type, payment), default));
    private Task<IssuedDocumentView> Issue(DocumentPreview preview, string key) => Bill(s => s.IssueDocumentAsync(Ann, new IssueDocumentInput(preview.PreviewToken, key), S.Actor, default));
    private Task<IssuedDocumentDetail> Get(Guid id) => Bill(s => s.GetDocumentAsync(Ann, id, S.Actor, default));
    private static AllocationInput To(int charge, decimal amount) => new(charge, amount);

    /// <summary>Charges 1 and 2 are 100 each; payment 3 is 150 (60 on charge 1, 40 on charge 2, 50 unapplied).</summary>
    private async Task<BillingAccountView> PaidAsync()
    {
        var plan = await BillablePlanAsync();
        await LegacyComplete(plan.Plan.Id, plan.Accepted);
        await Charge(plan.Completed, "c-1");
        var two = await Charge(plan.Accepted, "c-2");
        return await Pay(two, 150m, "p-1", [To(1, 60m), To(2, 40m)]);
    }

    [Fact]
    public async Task A_statement_preview_shows_the_ledger_and_issuing_it_stores_a_numbered_document_with_no_amount()
    {
        await PaidAsync();
        var preview = await Preview("Statement");
        Assert.Equal(("Statement", "50.00", "100.00", "50.00"), (preview.DocumentType, preview.Statement!.Balance, preview.Statement.OpenChargeTotal, preview.Statement.UnappliedTotal));
        Assert.Equal([1, 2, 3], preview.Statement.Lines.Select(l => l.Number));
        Assert.Null(preview.Receipt);
        Assert.Equal((3, 2, 0), (preview.CutOffs.EntryNumber, preview.CutOffs.AllocationNumber, preview.CutOffs.ReleaseNumber));

        var first = await Issue(preview, "doc-1");
        Assert.Equal(("Statement", 1, preview.ContentHash), (first.DocumentType, first.DocumentNumber, first.ContentHash));
        var second = await Issue(await Preview("Statement"), "doc-2");
        Assert.Equal(2, second.DocumentNumber);
        Assert.Equal([1, 2], (await Bill(s => s.ListDocumentsAsync(Ann, default))).Select(d => d.DocumentNumber));

        await using var db = Fixture.CreateContext();
        var stored = await db.FinancialDocuments.AsNoTracking().FirstAsync(d => d.Id == first.Id);
        Assert.DoesNotContain("100", stored.EnvelopeJson);                                              // the envelope is names and currency, no amount
        Assert.Equal(2, await db.AuditLogEntries.CountAsync(a => a.EventType == "FinancialDocumentIssued" && a.TargetUserAccountId == stored.AccountId));
    }

    [Fact]
    public async Task Issuing_the_same_preview_with_the_same_key_returns_the_same_document_and_another_use_of_the_key_is_refused()
    {
        await PaidAsync();
        var preview = await Preview("Statement");
        var first = await Issue(preview, "doc-1");
        var again = await Issue(preview, "doc-1");
        Assert.Equal((first.Id, first.DocumentNumber), (again.Id, again.DocumentNumber));
        Assert.Single(await Bill(s => s.ListDocumentsAsync(Ann, default)));
        await Pay(await Bill(s => s.GetAccountAsync(Ann, default)), 10m, "p-2");
        var another = await Preview("Statement");
        Assert.Equal("idempotency_key_reused", (await RefusedBilling(() => Issue(another, "doc-1"))).Code);
    }

    [Fact]
    public async Task An_issued_statement_reprints_exactly_as_issued_and_what_happened_since_is_listed_apart()
    {
        var paid = await PaidAsync();
        var preview = await Preview("Statement");
        var issued = await Issue(preview, "doc-1");
        var more = await Pay(paid, 20m, "p-2");
        await Bill(s => s.RefundPaymentAsync(Ann, new RefundInput(3, 10m, "Cash", "Patient request", more.RowVersion, "rf-1"), S.Actor, default));

        var detail = await Get(issued.Id);
        Assert.Equal(FinancialDocumentBuilder.CanonicalJson(preview.Statement!), FinancialDocumentBuilder.CanonicalJson(detail.Statement!));
        Assert.Equal(preview.ContentHash, FinancialDocumentBuilder.HashOf(detail.Statement!));
        Assert.Equal("50.00", detail.Statement!.Balance);                                               // as issued, not as of today
        Assert.Contains(detail.CurrentStatus, n => n.Contains("ledger entr") && n.Contains("since"));
    }

    [Fact]
    public async Task A_preview_whose_content_would_now_differ_is_refused_as_stale_and_nothing_is_issued()
    {
        await PaidAsync();
        var preview = await Preview("Statement");
        await ExecuteAsync("UPDATE Patients SET LastName = 'Renamed' WHERE Id = {0}", Ann);            // a name on the document changes between preview and issue
        var stale = await RefusedBilling(() => Issue(preview, "doc-1"));
        Assert.Equal(("document_preview_stale", 409), (stale.Code, stale.StatusCode));
        Assert.Empty(await Bill(s => s.ListDocumentsAsync(Ann, default)));

        var fresh = await Preview("Statement");                                                         // previewing again and issuing that works
        Assert.NotEqual(preview.ContentHash, fresh.ContentHash);
        Assert.Equal(1, (await Issue(fresh, "doc-2")).DocumentNumber);
    }

    [Fact]
    public async Task A_ledger_that_moved_on_after_the_preview_still_issues_exactly_the_reviewed_content()
    {
        var paid = await PaidAsync();
        var preview = await Preview("Statement");
        await Pay(paid, 20m, "p-2");                                                                    // later history does not reach a document cut off before it
        var issued = await Issue(preview, "doc-1");
        Assert.Equal(preview.ContentHash, issued.ContentHash);
        Assert.Equal(3, issued.CutOffs.EntryNumber);
        Assert.Equal("50.00", (await Get(issued.Id)).Statement!.Balance);
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("v1.AAAA")]
    [InlineData("v1.")]
    [InlineData("")]
    public async Task A_token_that_is_not_a_preview_is_refused(string token)
    {
        await PaidAsync();
        var e = await RefusedBilling(() => Bill(s => s.IssueDocumentAsync(Ann, new IssueDocumentInput(token, "doc-1"), S.Actor, default)));
        Assert.Equal(400, e.StatusCode);
        Assert.Contains("previewToken", e.FieldErrors.Keys.Concat(e.Code == "document_preview_invalid" ? ["previewToken"] : []));
    }

    [Fact]
    public async Task A_token_with_a_changed_hash_or_cut_offs_beyond_the_history_is_stale()
    {
        await PaidAsync();
        var preview = await Preview("Statement");
        var json = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(Pad(preview.PreviewToken[3..]))));
        string Rebuild(string name, object value)
        {
            var d = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(json.GetRawText())!;
            d[name] = value;
            return "v1." + Convert.ToBase64String(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(d)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }
        Assert.Equal("document_preview_stale", (await RefusedBilling(() => Bill(s => s.IssueDocumentAsync(Ann, new IssueDocumentInput(Rebuild("H", new string('a', 64)), "doc-1"), S.Actor, default)))).Code);
        Assert.Equal("document_preview_stale", (await RefusedBilling(() => Bill(s => s.IssueDocumentAsync(Ann, new IssueDocumentInput(Rebuild("E", 99), "doc-2"), S.Actor, default)))).Code);
        Assert.Equal("document_preview_stale", (await RefusedBilling(() => Bill(s => s.IssueDocumentAsync(Ann, new IssueDocumentInput(Rebuild("V", 2), "doc-3"), S.Actor, default)))).Code);
        Assert.Empty(await Bill(s => s.ListDocumentsAsync(Ann, default)));
    }

    private static string Pad(string base64Url) { var s = base64Url.Replace('-', '+').Replace('_', '/'); return s.PadRight(s.Length + (4 - s.Length % 4) % 4, '='); }

    [Fact]
    public async Task A_reprint_whose_content_no_longer_matches_its_stored_hash_is_refused()
    {
        await PaidAsync();
        var issued = await Issue(await Preview("Statement"), "doc-1");
        await ExecuteAsync("DISABLE TRIGGER TR_FinancialDocuments_Immutable ON FinancialDocuments");
        try { await ExecuteAsync("UPDATE FinancialDocuments SET ContentHash = REPLICATE('b', 64) WHERE Id = {0}", issued.Id); }
        finally { await ExecuteAsync("ENABLE TRIGGER TR_FinancialDocuments_Immutable ON FinancialDocuments"); }
        var e = await RefusedBilling(() => Get(issued.Id));
        Assert.Equal(("document_content_changed", 409), (e.Code, e.StatusCode));
        Assert.Equal("document_content_changed", (await RefusedBilling(() => Bill(s => s.GetStatementCsvAsync(Ann, issued.Id, S.Actor, default)))).Code);
    }

    [Fact]
    public async Task A_receipt_shows_the_payment_and_later_a_reversal_or_refund_is_a_separate_note()
    {
        var paid = await PaidAsync();
        var preview = await Preview("Receipt", 3);
        Assert.Equal(("150.00", "50.00", "0.00"), (preview.Receipt!.Amount, preview.Receipt.Unapplied, preview.Receipt.RefundedToDate));
        Assert.Equal([("1", "60.00"), ("2", "40.00")], preview.Receipt.Applications.Select(a => (a.ChargeNumber.ToString(), a.Amount)));
        var issued = await Issue(preview, "rc-1");
        Assert.Equal((("Receipt", 1), 3), ((issued.DocumentType, issued.DocumentNumber), issued.PaymentEntryNumber));

        var refunded = await Bill(s => s.RefundPaymentAsync(Ann, new RefundInput(3, 20m, "Cash", "Patient request", paid.RowVersion, "rf-1"), S.Actor, default));
        var detail = await Get(issued.Id);
        Assert.Equal("0.00", detail.Receipt!.RefundedToDate);                                          // as issued
        Assert.Contains(detail.CurrentStatus, n => n.Contains("20.00") && n.Contains("refunded"));
        Assert.Equal(preview.ContentHash, issued.ContentHash);
        Assert.NotNull(refunded);

        Assert.Equal("document_not_found", (await RefusedBilling(() => Get(Guid.NewGuid()))).Code);
        Assert.Equal("csv_statement_only", (await RefusedBilling(() => Bill(s => s.GetStatementCsvAsync(Ann, issued.Id, S.Actor, default)))).Code);
    }

    [Fact]
    public async Task A_receipt_for_a_reversed_payment_or_something_that_is_not_a_payment_is_refused()
    {
        var paid = await PaidAsync();
        Assert.Equal("payment_not_found", (await RefusedBilling(() => Preview("Receipt", 1))).Code);                           // a charge
        Assert.Equal("payment_not_found", (await RefusedBilling(() => Preview("Receipt", 99))).Code);
        Assert.Contains("paymentEntryNumber", (await RefusedBilling(() => Preview("Receipt"))).FieldErrors.Keys);
        Assert.Contains("documentType", (await RefusedBilling(() => Preview("Invoice"))).FieldErrors.Keys);
        await Bill(s => s.ReverseEntryAsync(Ann, new ReversalInput(3, "Entered in error", paid.RowVersion, "r-1"), S.Actor, default));
        Assert.Equal("payment_reversed", (await RefusedBilling(() => Preview("Receipt", 3))).Code);
    }

    [Fact]
    public async Task A_statement_is_addressed_to_who_was_responsible_on_the_day_and_a_reprint_keeps_that_addressee()
    {
        var paid = await PaidAsync();
        await Bill(s => s.RecordResponsibilityAsync(Ann, new ResponsibilityInput(Bo, null, "Parent pays", paid.RowVersion), S.Actor, default));
        var toBo = await Issue(await Preview("Statement"), "doc-1");
        var detail = await Get(toBo.Id);
        Assert.Equal((Bo, false), (detail.Statement!.Addressee.PatientId, detail.Statement.Addressee.IsSelf));
        Assert.Equal(Ann, detail.Statement.Patient.PatientId);

        var account = await Bill(s => s.GetAccountAsync(Ann, default));
        await Bill(s => s.RecordResponsibilityAsync(Ann, new ResponsibilityInput(Ann, null, "Now self", account.RowVersion), S.Actor, default));
        await ExecuteAsync("UPDATE Patients SET LastName = 'Changed' WHERE Id = {0}", Bo);
        var again = await Get(toBo.Id);                                                                 // the envelope kept the addressee as resolved on the day
        Assert.Equal(detail.Statement.Addressee, again.Statement!.Addressee);
        Assert.Equal(toBo.ContentHash, FinancialDocumentBuilder.HashOf(again.Statement));

        var now = await Preview("Statement");
        Assert.Equal((Ann, true), (now.Statement!.Addressee.PatientId, now.Statement.Addressee.IsSelf));
    }

    private Task SetPracticeAsync(string name) => ExecuteAsync(
        "IF EXISTS (SELECT 1 FROM PracticeSettings) UPDATE PracticeSettings SET Name = {0}, Phone = '555-0100', AddressLine = '1 Main St' " +
        "ELSE INSERT INTO PracticeSettings (Id, Name, Phone, AddressLine, UpdatedAtUtc) VALUES ('00000000-0000-0000-0000-00000000a1b3', {0}, '555-0100', '1 Main St', SYSDATETIMEOFFSET())", name);

    [Fact]
    public async Task The_practice_as_it_was_on_the_day_is_kept_with_the_document_and_a_change_before_issue_makes_the_preview_stale()
    {
        await PaidAsync();
        await SetPracticeAsync("Alder Dental");
        var statement = await Preview("Statement");
        var receipt = await Preview("Receipt", 3);
        Assert.Equal(new DocumentPractice("Alder Dental", "555-0100", "1 Main St"), statement.Statement!.Practice);
        Assert.Equal(statement.Statement.Practice, receipt.Receipt!.Practice);

        await SetPracticeAsync("Alder Dental Group");                                                   // the practice is renamed between preview and issue
        Assert.Equal("document_preview_stale", (await RefusedBilling(() => Issue(statement, "doc-1"))).Code);
        var fresh = await Preview("Statement");
        Assert.Equal("Alder Dental Group", fresh.Statement!.Practice!.Name);
        var issued = await Issue(fresh, "doc-2");

        await SetPracticeAsync("Somewhere Else");                                                       // renamed again after issue: the reprint keeps the day's name and the same hash
        var again = await Get(issued.Id);
        Assert.Equal("Alder Dental Group", again.Statement!.Practice!.Name);
        Assert.Equal(issued.ContentHash, FinancialDocumentBuilder.HashOf(again.Statement));
        Assert.Equal("Somewhere Else", (await Preview("Statement")).Statement!.Practice!.Name);
    }

    [Fact]
    public async Task Showing_and_exporting_a_document_are_audited_and_a_refused_reprint_leaves_no_trace()
    {
        await PaidAsync();
        var issued = await Issue(await Preview("Statement"), "doc-1");
        async Task<(int Reprinted, int Exported)> Counts()
        {
            await using var db = Fixture.CreateContext();
            return (await db.AuditLogEntries.CountAsync(a => a.EventType == "FinancialDocumentReprinted"), await db.AuditLogEntries.CountAsync(a => a.EventType == "FinancialDocumentExported"));
        }
        Assert.Equal((0, 0), await Counts());
        await Get(issued.Id);
        Assert.Equal((1, 0), await Counts());
        await Bill(s => s.GetStatementCsvAsync(Ann, issued.Id, S.Actor, default));
        Assert.Equal((1, 1), await Counts());

        await ExecuteAsync("DISABLE TRIGGER TR_FinancialDocuments_Immutable ON FinancialDocuments");
        try { await ExecuteAsync("UPDATE FinancialDocuments SET ContentHash = REPLICATE('b', 64) WHERE Id = {0}", issued.Id); }
        finally { await ExecuteAsync("ENABLE TRIGGER TR_FinancialDocuments_Immutable ON FinancialDocuments"); }
        await RefusedBilling(() => Get(issued.Id));
        await RefusedBilling(() => Bill(s => s.GetStatementCsvAsync(Ann, issued.Id, S.Actor, default)));
        Assert.Equal((1, 1), await Counts());                                                           // nothing was shown, so nothing is logged as shown
    }

    [Fact]
    public async Task A_statement_exports_as_csv_with_one_row_per_ledger_line()
    {
        await PaidAsync();
        var issued = await Issue(await Preview("Statement"), "doc-1");
        var (name, csv) = await Bill(s => s.GetStatementCsvAsync(Ann, issued.Id, S.Actor, default));
        Assert.Equal("statement-1.csv", name);
        var rows = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(1 + 3 + 1, rows.Length);                                                           // header, three entries, the total
        Assert.StartsWith("3,Payment,-150.00,50.00,", rows[3]);
    }

    [Fact]
    public async Task Nothing_can_be_put_on_a_document_before_anything_is_billed()
    {
        Assert.Equal("nothing_owed", (await RefusedBilling(() => Preview("Statement"))).Code);
        Assert.Equal("patient_not_found", (await RefusedBilling(() => Bill(s => s.PreviewDocumentAsync(Guid.NewGuid(), new DocumentPreviewInput("Statement", null), default)))).Code);
    }
}
