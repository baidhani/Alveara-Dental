using System.Text;
using System.Text.Json;
using Alveara.Api.Architecture.Billing;
using Alveara.Api.Architecture.Time;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N021 A1 (A1-DECISION-01) against real SQL Server: a statement or receipt issued under template version 1 (its row and hash made by the verbatim ORIGINAL builder, never by the new code) reprints and
/// exports exactly as issued; the same row forced to version 2 no longer matches (negative control); an unknown version is refused; a version-1 preview made before the upgrade is stale, while retrying an
/// issue that already happened under version 1 returns that document; a version-2 statement stores its frozen basis and reprints identically in another zone, a zone change between preview and issue is
/// refused, and live aging agrees with a statement made at the same instant.
/// </summary>
public class BillingDocumentTemplateVersionServiceTests : BillingTestBase
{
    private static readonly IPracticeClock TokyoClock = new PracticeClock(TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo"));

    private Task<T> BillWith<T>(IPracticeClock clock, Func<BillingService, Task<T>> action) => WithDb(db => action(new BillingService(db, clock)));
    private Task<DocumentPreview> Preview(string type, int? payment = null, IPracticeClock? clock = null) => BillWith(clock ?? Clock, s => s.PreviewDocumentAsync(Ann, new DocumentPreviewInput(type, payment), default));
    private Task<IssuedDocumentView> Issue(string token, string key, IPracticeClock? clock = null) => BillWith(clock ?? Clock, s => s.IssueDocumentAsync(Ann, new IssueDocumentInput(token, key), S.Actor, default));
    private Task<IssuedDocumentDetail> Get(Guid id, IPracticeClock? clock = null) => BillWith(clock ?? Clock, s => s.GetDocumentAsync(Ann, id, S.Actor, default));

    /// <summary>Charges 1 and 2 are 100 each; payment 3 is 150 (60 on charge 1, 40 on charge 2, 50 unapplied).</summary>
    private async Task PaidAsync()
    {
        var plan = await BillablePlanAsync();
        await LegacyComplete(plan.Plan.Id, plan.Accepted);
        await Bill(s => s.RecordChargeAsync(Ann, new ChargeInput(plan.Completed, "c-1"), S.Actor, default));
        var two = await Bill(s => s.RecordChargeAsync(Ann, new ChargeInput(plan.Accepted, "c-2"), S.Actor, default));
        await Bill(s => s.RecordPaymentAsync(Ann, new PaymentInput(150m, "Cash", null, two.RowVersion, "p-1", [new(1, 60m), new(2, 40m)]), S.Actor, default));
    }

    /// <summary>The ledger as the builder reads it, made here from the rows (this ledger has no completed-procedure record, reversal, refund or release, so the service date is the recorded time).</summary>
    private async Task<LedgerSnapshot> SnapshotAsync()
    {
        await using var db = Fixture.CreateContext();
        var account = await db.BillingAccounts.AsNoTracking().FirstAsync(a => a.PatientId == Ann);
        Assert.False(await db.CompletedProcedures.AnyAsync(p => p.PatientId == Ann));
        var entries = await db.BillingEntries.AsNoTracking().Where(e => e.AccountId == account.Id).OrderBy(e => e.EntryNumber).ToListAsync();
        var methods = await db.BillingPaymentMethods.AsNoTracking().ToDictionaryAsync(m => m.Id, m => m.Name);
        var number = entries.ToDictionary(e => e.Id, e => e.EntryNumber);
        var allocations = await db.BillingAllocations.AsNoTracking().Where(a => a.AccountId == account.Id).OrderBy(a => a.AllocationNumber).ToListAsync();
        Assert.True(entries.All(e => e.ReversesEntryId is null && e.RefundsEntryId is null && e.ReplacesEntryId is null));
        return new LedgerSnapshot(
            entries.Select(e => new SnapshotEntry(e.EntryNumber, e.Kind, e.Amount, null, null, null, e.ProcedureCode, e.ProcedureDescription,
                e.PaymentMethodId is Guid m && methods.TryGetValue(m, out var name) ? name : e.PaymentMethod, e.Reason, e.RecordedAtUtc, e.RecordedAtUtc)).ToList(),
            allocations.Select(a => new SnapshotAllocation(a.AllocationNumber, number[a.PaymentEntryId], number[a.ChargeEntryId], a.Amount)).ToList(), []);
    }

    /// <summary>
    /// Stores a document exactly as the pre-version-2 system issued it: version 1, the envelope without a basis, and the hash of what the ORIGINAL builder made at the same cut-offs. The cut-offs, envelope and
    /// payment link are copied from a document the service issued now; only the version, envelope basis and hash differ.
    /// </summary>
    private async Task<(Guid Id, string OriginalJson)> StoreAsVersion1Async(Guid issuedId, int number, string key, int storedVersion = 1)
    {
        await using var db = Fixture.CreateContext();
        var source = await db.FinancialDocuments.AsNoTracking().FirstAsync(d => d.Id == issuedId);
        var envelope = JsonSerializer.Deserialize<DocumentEnvelope>(source.EnvelopeJson)! with { AgingBasis = null };
        var cut = new FinancialCutOffs(source.AsOfEntryNumber, source.AsOfAllocationNumber, source.AsOfReleaseNumber, source.AsOfResponsibilitySequence, source.AsOfUtc);
        var snapshot = await SnapshotAsync();
        var paymentNumber = source.PaymentEntryId is Guid p ? await db.BillingEntries.Where(e => e.Id == p).Select(e => e.EntryNumber).FirstAsync() : 0;
        object content = source.DocumentType == FinancialDocumentTypes.Statement
            ? OriginalFinancialDocumentBuilderV1.Statement(snapshot, cut, envelope)
            : OriginalFinancialDocumentBuilderV1.Receipt(snapshot, cut, envelope, paymentNumber);
        var json = OriginalFinancialDocumentBuilderV1.CanonicalJson(content);
        var row = new FinancialDocument
        {
            Id = Guid.NewGuid(), AccountId = source.AccountId, PatientId = source.PatientId, DocumentType = source.DocumentType, DocumentNumber = number, PaymentEntryId = source.PaymentEntryId,
            AsOfEntryNumber = source.AsOfEntryNumber, AsOfAllocationNumber = source.AsOfAllocationNumber, AsOfReleaseNumber = source.AsOfReleaseNumber, AsOfResponsibilitySequence = source.AsOfResponsibilitySequence,
            AsOfUtc = source.AsOfUtc, TemplateVersion = storedVersion, ContentHash = OriginalFinancialDocumentBuilderV1.Hash(json), EnvelopeJson = JsonSerializer.Serialize(envelope), IdempotencyKey = key,
            CorrelationId = Guid.NewGuid(), IssuedAtUtc = source.IssuedAtUtc, IssuedByUserId = source.IssuedByUserId,
            // ALV-N024 (PD-17): a payment has one receipt, so a second receipt of the same payment is stored as a replacement of the issued one. The replacement fields are not content and not hashed.
            ReplacesDocumentId = source.DocumentType == FinancialDocumentTypes.Receipt ? source.Id : null,
            ReplacementReason = source.DocumentType == FinancialDocumentTypes.Receipt ? "Stored again as a version-1 document (test fixture)" : null,
        };
        db.FinancialDocuments.Add(row);
        await db.SaveChangesAsync();
        return (row.Id, json);
    }

    private static string TokenWith(string token, Action<Dictionary<string, JsonElement>> change)
    {
        var b64 = token[3..].Replace('-', '+').Replace('_', '/');
        var body = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(Convert.FromBase64String(b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '=')))!;
        change(body);
        return "v1." + Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(body))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    [Fact]
    public async Task A_version_1_statement_and_receipt_reprint_and_export_byte_for_byte_as_the_original_builder_made_them()
    {
        await PaidAsync();
        var statement = await Issue((await Preview("Statement")).PreviewToken, "now-s");
        var receipt = await Issue((await Preview("Receipt", 3)).PreviewToken, "now-r");
        var (oldStatement, statementJson) = await StoreAsVersion1Async(statement.Id, 2, "old-s");
        var (oldReceipt, receiptJson) = await StoreAsVersion1Async(receipt.Id, 2, "old-r");

        var s = await Get(oldStatement);
        Assert.Equal((1, statementJson), (s.Document.TemplateVersion, FinancialDocumentBuilder.CanonicalJson(s.Statement!)));
        Assert.Null(s.Statement!.AgingBasis);
        var r = await Get(oldReceipt);
        Assert.Equal((1, receiptJson), (r.Document.TemplateVersion, FinancialDocumentBuilder.CanonicalJson(r.Receipt!)));
        // ALV-N024 (CR-2, route 1): the CSV is a live export, so a version-1 statement exports with the current structure from its own, unchanged content; ReversedBy stays empty (not recomputed)
        var before = await WithDb(db => db.FinancialDocuments.AsNoTracking().SingleAsync(d => d.Id == oldStatement));
        var (_, csv) = await Bill(b => b.GetStatementCsvAsync(Ann, oldStatement, S.Actor, default));
        Assert.Equal(FinancialDocumentBuilder.StatementCsv(JsonSerializer.Deserialize<StatementContent>(statementJson)!, 2, SchedulingTestSupport.Clock.PracticeTimeZone), csv);
        Assert.All(csv.Split("\r\n").SkipWhile(l => !l.StartsWith("Number,")).Skip(1).Where(l => l.Length > 0), l => Assert.EndsWith(",", l));
        var after = await WithDb(db => db.FinancialDocuments.AsNoTracking().SingleAsync(d => d.Id == oldStatement));
        Assert.Equal((before.ContentHash, before.TemplateVersion, before.EnvelopeJson), (after.ContentHash, after.TemplateVersion, after.EnvelopeJson));
    }

    [Fact]
    public async Task Negative_control_a_version_1_row_rebuilt_as_version_2_does_not_match_and_is_not_shown()
    {
        await PaidAsync();
        var statement = await Issue((await Preview("Statement")).PreviewToken, "now-s");
        var (forced, _) = await StoreAsVersion1Async(statement.Id, 2, "forced", storedVersion: 2);
        Assert.Equal("document_content_changed", (await RefusedBilling(() => Get(forced))).Code);
    }

    [Fact]
    public async Task A_document_with_an_unknown_template_version_is_refused_not_rebuilt_with_the_current_one()
    {
        await PaidAsync();
        var statement = await Issue((await Preview("Statement")).PreviewToken, "now-s");
        var (unknown, _) = await StoreAsVersion1Async(statement.Id, 2, "unknown", storedVersion: 4);                      // ALV-N024: 3 is supported now, so the unknown version is 4
        Assert.Equal("document_template_unsupported", (await RefusedBilling(() => Get(unknown))).Code);
        Assert.Equal("document_template_unsupported", (await RefusedBilling(() => Bill(b => b.GetStatementCsvAsync(Ann, unknown, S.Actor, default)))).Code);
    }

    [Fact]
    public async Task A_version_1_preview_made_before_the_upgrade_is_stale_and_must_be_previewed_again()
    {
        await PaidAsync();
        var preview = await Preview("Statement");
        Assert.Equal(3, preview.TemplateVersion);                                                         // the current version (3 since ALV-N024)
        var oldToken = TokenWith(preview.PreviewToken, t => t["V"] = JsonSerializer.SerializeToElement(1));
        Assert.Equal("document_preview_stale", (await RefusedBilling(() => Issue(oldToken, "late"))).Code);
        Assert.Empty(await Bill(s => s.ListDocumentsAsync(Ann, default)));
    }

    [Fact]
    public async Task Retrying_an_issue_that_already_happened_under_version_1_returns_that_original_document()
    {
        await PaidAsync();
        var preview = await Preview("Statement");
        var statement = await Issue(preview.PreviewToken, "now-s");
        var (old, _) = await StoreAsVersion1Async(statement.Id, 2, "issued-before-upgrade");
        Guid stored;
        string hash;
        await using (var db = Fixture.CreateContext())
        {
            var row = await db.FinancialDocuments.AsNoTracking().FirstAsync(d => d.Id == old);
            (stored, hash) = (row.Id, row.ContentHash);
        }
        var originalRequest = TokenWith(preview.PreviewToken, t => { t["V"] = JsonSerializer.SerializeToElement(1); t["H"] = JsonSerializer.SerializeToElement(hash); });
        var replay = await Issue(originalRequest, "issued-before-upgrade");
        Assert.Equal((stored, 1, hash), (replay.Id, replay.TemplateVersion, replay.ContentHash));
        Assert.Equal(2, (await Bill(s => s.ListDocumentsAsync(Ann, default))).Count);
    }

    [Fact]
    public async Task A_version_2_statement_stores_its_frozen_basis_and_reprints_identically_after_the_practice_zone_changes()
    {
        await PaidAsync();
        var preview = await Preview("Statement");
        var issued = await Issue(preview.PreviewToken, "now-s");
        Assert.Equal((3, preview.ContentHash), (issued.TemplateVersion, issued.ContentHash));             // a new statement is version 3 (ALV-N024), which keeps version 2's frozen basis
        await using (var db = Fixture.CreateContext())
        {
            var envelope = JsonSerializer.Deserialize<DocumentEnvelope>((await db.FinancialDocuments.AsNoTracking().FirstAsync(d => d.Id == issued.Id)).EnvelopeJson)!;
            Assert.Equal(preview.Statement!.AgingBasis, envelope.AgingBasis! with { ServiceDays = preview.Statement.AgingBasis!.ServiceDays });
            Assert.Equal(preview.Statement.AgingBasis.ServiceDays, envelope.AgingBasis.ServiceDays);
            Assert.Equal(Clock.PracticeTimeZone.Id, envelope.AgingBasis.TimeZoneId);
        }
        var reprinted = await Get(issued.Id, TokyoClock);
        Assert.Equal(FinancialDocumentBuilder.CanonicalJson(preview.Statement!), FinancialDocumentBuilder.CanonicalJson(reprinted.Statement!));
        var receipt = await Issue((await Preview("Receipt", 3)).PreviewToken, "now-r");
        await using var check = Fixture.CreateContext();
        Assert.Null(JsonSerializer.Deserialize<DocumentEnvelope>((await check.FinancialDocuments.AsNoTracking().FirstAsync(d => d.Id == receipt.Id)).EnvelopeJson)!.AgingBasis);
    }

    [Fact]
    public async Task A_practice_zone_change_between_preview_and_issue_makes_the_preview_stale()
    {
        await PaidAsync();
        var preview = await Preview("Statement");
        Assert.Equal("document_preview_stale", (await RefusedBilling(() => Issue(preview.PreviewToken, "moved", TokyoClock))).Code);
        Assert.Empty(await Bill(s => s.ListDocumentsAsync(Ann, default)));
        Assert.Equal(3, (await Issue(preview.PreviewToken, "same-zone")).TemplateVersion);                // the current version (3 since ALV-N024)
    }

    [Fact]
    public async Task Live_aging_and_a_statement_made_at_the_same_instant_agree()
    {
        await PaidAsync();
        var now = new TestClock(DateTimeOffset.UtcNow.AddMinutes(1));
        var statement = (await Preview("Statement", clock: now)).Statement!;
        var live = await BillWith(now, s => s.GetAgingAsync(Ann, default));
        Assert.Equal(statement.Aging, live.Buckets);
        Assert.Equal(statement.OpenCharges, live.OpenCharges);
    }
}
