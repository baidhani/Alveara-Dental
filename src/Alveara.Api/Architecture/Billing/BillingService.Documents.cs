using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Clinical;
using AppMoney = Alveara.Api.Architecture.Money.Money;

namespace Alveara.Api.Architecture.Billing;

/// <summary>
/// ALV-007-C02: statements and receipts. A document is never a second source of truth: it is the ledger read at four cut-offs (the last entry, application, release and responsibility change it includes) and at one
/// instant, so what it shows can be rebuilt at any time, and issuing it stores only the cut-offs, a SHA-256 of the canonical content, a number, the template version and a non-financial display envelope (the
/// patient and addressee names as resolved on the day). No amount and no balance is stored.
///
/// The documented rules:
/// - A <b>preview</b> reads the cut-offs and the content in one repeatable-read transaction and hands back a token that binds the cut-offs, template version and hash.
/// - <b>Issuing</b> needs that token. The content is rebuilt at the token's cut-offs; if it is not byte-for-byte what was reviewed (a name, a responsibility, an amendment or the ledger changed in a way that
///   reaches this document) nothing is issued and the answer is <c>document_preview_stale</c>. Numbers go 1, 2, 3 per account and type; the same key returns the same document.
/// - A <b>reprint</b> rebuilds the content at the stored cut-offs with the stored envelope and checks it against the stored hash; if it differs the answer is <c>document_content_changed</c> and nothing is shown.
///   What has happened since (a payment reversed or refunded, newer entries) is listed apart as current status and never changes the issued content or its hash.
/// - One statement per account (a cut-off, not a period); no consolidated guarantor statement. Reading needs ViewBilling; issuing needs ManageBilling (enforced at the route).
/// </summary>
public partial class BillingService
{
    private static readonly JsonSerializerOptions TokenJson = new() { WriteIndented = false };

    private sealed record PreviewToken(string T, int? P, int E, int A, int R, int S, string U, int V, string H);

    private static string EncodeToken(PreviewToken token) => "v1." + Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(token, TokenJson)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static PreviewToken DecodeToken(string? text)
    {
        try
        {
            if (text is null || !text.StartsWith("v1.", StringComparison.Ordinal) || text.Length > 600) throw new FormatException();
            var b64 = text[3..].Replace('-', '+').Replace('_', '/');
            var token = JsonSerializer.Deserialize<PreviewToken>(Convert.FromBase64String(b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '=')), TokenJson);
            if (token is null || token.T is null || token.U is null || token.H is null) throw new FormatException();
            return token;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException)
        {
            throw new BillingException("document_preview_invalid", "The preview could not be read. Preview the document again and issue that.", 400, new Dictionary<string, string> { ["previewToken"] = "Preview the document again." }, ex);
        }
    }

    /// <summary>The ledger of an account as the document builder reads it. Completion records finalized after <paramref name="amendmentCap"/>, and amendments made after it, are ignored (null: use the latest), which is how an issued document ages as it did on the day. A record can post-date its charge since ALV-008-C02 (adopting a procedure that billing charged by hand), so a record that did not exist when a document was issued must not change what it says.</summary>
    private async Task<LedgerSnapshot> LoadSnapshotAsync(Guid accountId, DateTimeOffset? amendmentCap, CancellationToken ct)
    {
        var ledger = await LoadLedgerAsync(accountId, ct);
        var methods = await db.BillingPaymentMethods.AsNoTracking().ToDictionaryAsync(m => m.Id, m => m.Name, ct);
        var planItemIds = ledger.Entries.Where(e => e.PlanItemId is not null).Select(e => e.PlanItemId!.Value).ToList();
        var procedures = await db.CompletedProcedures.AsNoTracking().Where(p => planItemIds.Contains(p.PlanItemId) && (amendmentCap == null || p.FinalizedAtUtc <= amendmentCap)).ToListAsync(ct);
        var procedureIds = procedures.Select(p => p.Id).ToList();
        var amendments = (await db.CompletedProcedureAmendments.AsNoTracking().Where(a => procedureIds.Contains(a.CompletedProcedureId) && a.PerformedAtUtc != null).ToListAsync(ct))
            .Where(a => amendmentCap is null || a.AmendedAtUtc <= amendmentCap).GroupBy(a => a.CompletedProcedureId).ToDictionary(g => g.Key, g => g.OrderBy(a => a.AmendmentNumber).Last().PerformedAtUtc!.Value);
        var performed = procedures.ToDictionary(p => p.PlanItemId, p => amendments.TryGetValue(p.Id, out var amended) ? amended : p.PerformedAtUtc);

        int? NumberOrNull(Guid? id) => id is Guid g ? ledger.NumberOf(g) : null;
        var entries = ledger.Entries.Select(e => new SnapshotEntry(e.EntryNumber, e.Kind, e.Amount, NumberOrNull(e.ReversesEntryId), NumberOrNull(e.RefundsEntryId), NumberOrNull(e.ReplacesEntryId), e.ProcedureCode,
            e.ProcedureDescription, e.PaymentMethodId is Guid m && methods.TryGetValue(m, out var name) ? name : e.PaymentMethod, e.Reason, e.RecordedAtUtc,
            e.PlanItemId is Guid item && performed.TryGetValue(item, out var when) ? when : e.RecordedAtUtc)).ToList();
        var byId = ledger.Allocations.ToDictionary(a => a.Id);
        var allocations = ledger.Allocations.Select(a => new SnapshotAllocation(a.AllocationNumber, ledger.NumberOf(a.PaymentEntryId), ledger.NumberOf(a.ChargeEntryId), a.Amount)).ToList();
        var releases = ledger.Releases.Select(r => new SnapshotRelease(r.ReleaseNumber, ledger.NumberOf(byId[r.AllocationId].PaymentEntryId), ledger.NumberOf(byId[r.AllocationId].ChargeEntryId), r.Amount, NumberOrNull(r.RefundEntryId))).ToList();
        return new LedgerSnapshot(entries, allocations, releases);
    }

    private async Task<FinancialCutOffs> CaptureCutOffsAsync(Guid accountId, LedgerSnapshot snapshot, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var latest = snapshot.Entries.Count == 0 ? now : snapshot.Entries.Max(e => e.RecordedAtUtc);
        var asOf = latest > now ? latest : now;
        var sequence = await db.BillingResponsibilities.AsNoTracking().Where(r => r.AccountId == accountId && r.SetAtUtc <= asOf).Select(r => (int?)r.Sequence).MaxAsync(ct) ?? 0;
        return new FinancialCutOffs(snapshot.Entries.Max(e => e.Number), snapshot.Allocations.Select(a => a.Number).DefaultIfEmpty(0).Max(), snapshot.Releases.Select(r => r.Number).DefaultIfEmpty(0).Max(), sequence, asOf);
    }

    /// <summary>Who the document is about and who it is addressed to, as the responsibility history stood at the cut-offs. A payer, when given, is who a receipt is for.</summary>
    private async Task<DocumentEnvelope> ResolveEnvelopeAsync(BillingAccount account, FinancialCutOffs cutOffs, Guid? payer, CancellationToken ct)
    {
        var patient = await db.Patients.AsNoTracking().FirstAsync(p => p.Id == account.PatientId, ct);
        var rows = await db.BillingResponsibilities.AsNoTracking().Where(r => r.AccountId == account.Id && r.Sequence <= cutOffs.ResponsibilitySequence).OrderBy(r => r.Sequence).ToListAsync(ct);
        var asOfDate = DateOnly.FromDateTime(clock.ToPracticeLocal(cutOffs.AsOfUtc).DateTime);
        var recorded = rows.LastOrDefault(r => r.EffectiveFrom <= asOfDate);
        Guid responsible;
        string source;
        if (recorded is not null) (responsible, source) = (recorded.ResponsiblePatientId, "Recorded");
        else if (patient.GuarantorPatientId is Guid registered && await db.Patients.AsNoTracking().AnyAsync(p => p.Id == registered && p.IsActive, ct)) (responsible, source) = (registered, "RegisteredGuarantor");
        else (responsible, source) = (patient.Id, "Self");
        if (payer is Guid paid) (responsible, source) = (paid, "Payer");
        var addressee = responsible == patient.Id ? patient : await db.Patients.AsNoTracking().FirstAsync(p => p.Id == responsible, ct);
        var practice = await db.PracticeSettings.AsNoTracking().FirstOrDefaultAsync(ct);
        return new DocumentEnvelope(new DocumentParty(patient.Id, NameOf(patient), true), new DocumentParty(addressee.Id, NameOf(addressee), addressee.Id == patient.Id), source, AppMoney.Currency,
            practice is null ? null : new DocumentPractice(practice.Name, practice.Phone, practice.AddressLine));
    }

    private static string RequireType(string? type) =>
        FinancialDocumentTypes.All.FirstOrDefault(t => string.Equals(t, type?.Trim(), StringComparison.OrdinalIgnoreCase))
        ?? throw BillingRules.Invalid(new Dictionary<string, string> { ["documentType"] = $"Choose the kind of document: {string.Join(", ", FinancialDocumentTypes.All)}." });

    private async Task<BillingAccount> AccountForDocumentAsync(Guid patientId, CancellationToken ct) =>
        await db.BillingAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.PatientId == patientId, ct)
            ?? throw new BillingException("nothing_owed", "Nothing has been billed to this patient yet, so there is nothing to put on a document.", 409);

    /// <summary>Builds the content at the cut-offs. For a receipt the payment must exist then and not have been reversed by then.</summary>
    private async Task<(object Content, string Hash, DocumentEnvelope Envelope)> BuildAsync(BillingAccount account, LedgerSnapshot snapshot, FinancialCutOffs cutOffs, string type, int? paymentNumber, DocumentEnvelope? envelope, CancellationToken ct)
    {
        if (type == FinancialDocumentTypes.Statement)
        {
            envelope ??= await ResolveEnvelopeAsync(account, cutOffs, null, ct);
            var statement = FinancialDocumentBuilder.Statement(snapshot, cutOffs, envelope);
            return (statement, FinancialDocumentBuilder.HashOf(statement), envelope);
        }
        var payment = paymentNumber is int n ? snapshot.Entries.FirstOrDefault(e => e.Number == n && e.Number <= cutOffs.EntryNumber) : null;
        if (payment is not { Kind: BillingKinds.Payment }) throw new BillingException("payment_not_found", "That payment was not found on this account.", 404);
        if (snapshot.Entries.Any(e => e.ReversesNumber == payment.Number && e.Number <= cutOffs.EntryNumber)) throw new BillingException("payment_reversed", $"Payment {payment.Number} has been reversed, so it has no receipt.", 409);
        if (envelope is null)
        {
            var payer = await db.BillingEntries.AsNoTracking().Where(e => e.AccountId == account.Id && e.EntryNumber == payment.Number).Select(e => e.PaidByPatientId).FirstAsync(ct);
            envelope = await ResolveEnvelopeAsync(account, cutOffs, payer ?? account.PatientId, ct);
        }
        var receipt = FinancialDocumentBuilder.Receipt(snapshot, cutOffs, envelope, payment.Number);
        return (receipt, FinancialDocumentBuilder.HashOf(receipt), envelope);
    }

    /// <summary>The statement or receipt as it would be issued now, with the token issuing needs. Nothing is stored.</summary>
    public async Task<DocumentPreview> PreviewDocumentAsync(Guid patientId, DocumentPreviewInput input, CancellationToken ct)
    {
        await RequirePatientAsync(patientId, ct);
        var type = RequireType(input.DocumentType);
        if (type == FinancialDocumentTypes.Receipt && input.PaymentEntryNumber is null) throw BillingRules.Invalid(new Dictionary<string, string> { ["paymentEntryNumber"] = "Choose the payment to receipt." });
        var account = await AccountForDocumentAsync(patientId, ct);

        await using var transaction = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct) : null;   // the cut-offs and the content come from one consistent read
        var snapshot = await LoadSnapshotAsync(account.Id, null, ct);
        var cutOffs = await CaptureCutOffsAsync(account.Id, snapshot, ct);
        // the amendments in force at the instant are the document's own; reload when that instant ends the cap differently from "latest"
        snapshot = await LoadSnapshotAsync(account.Id, cutOffs.AsOfUtc, ct);
        var (content, hash, _) = await BuildAsync(account, snapshot, cutOffs, type, type == FinancialDocumentTypes.Receipt ? input.PaymentEntryNumber : null, null, ct);
        if (transaction is not null) await transaction.CommitAsync(ct);

        var token = EncodeToken(new PreviewToken(type, type == FinancialDocumentTypes.Receipt ? input.PaymentEntryNumber : null, cutOffs.EntryNumber, cutOffs.AllocationNumber, cutOffs.ReleaseNumber,
            cutOffs.ResponsibilitySequence, FinancialDocumentBuilder.Instant(cutOffs.AsOfUtc), FinancialDocumentBuilder.TemplateVersion, hash));
        return new DocumentPreview(type, cutOffs, FinancialDocumentBuilder.TemplateVersion, hash, token, content as StatementContent, content as ReceiptContent);
    }

    /// <summary>Issues the previewed document: the same content, or nothing (<c>document_preview_stale</c>). The same key again returns the same document.</summary>
    public async Task<IssuedDocumentView> IssueDocumentAsync(Guid patientId, IssueDocumentInput input, Guid actor, CancellationToken ct)
    {
        await RequirePatientAsync(patientId, ct);
        var errors = new Dictionary<string, string>();
        var key = BillingRules.CleanKey(errors, input.IdempotencyKey);
        if (string.IsNullOrWhiteSpace(input.PreviewToken)) errors["previewToken"] = "Preview the document first and issue that preview.";
        if (errors.Count > 0) throw BillingRules.Invalid(errors);
        var token = DecodeToken(input.PreviewToken);
        var type = RequireType(token.T);
        if (!DateTimeOffset.TryParse(token.U, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var asOf))
            throw new BillingException("document_preview_invalid", "The preview could not be read. Preview the document again and issue that.", 400);
        var cutOffs = new FinancialCutOffs(token.E, token.A, token.R, token.S, asOf);

        var account = await AccountForDocumentAsync(patientId, ct);
        var prior = await db.FinancialDocuments.AsNoTracking().FirstOrDefaultAsync(d => d.AccountId == account.Id && d.IdempotencyKey == key, ct);
        if (prior is not null)
            return prior.DocumentType == type && prior.ContentHash == token.H && prior.AsOfEntryNumber == token.E && prior.AsOfUtc == asOf && (prior.PaymentEntryId is null) == (token.P is null)
                ? await ViewOfDocumentAsync(prior, ct) : throw KeyReused();

        DocumentEnvelope envelope;
        string hash;
        await using (var transaction = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct) : null)
        {
            var snapshot = await LoadSnapshotAsync(account.Id, asOf, ct);
            if (token.E < 1 || token.E > snapshot.Entries.Max(e => e.Number) || token.A > snapshot.Allocations.Select(a => a.Number).DefaultIfEmpty(0).Max() || token.R > snapshot.Releases.Select(r => r.Number).DefaultIfEmpty(0).Max()
                || token.V != FinancialDocumentBuilder.TemplateVersion)
                throw new BillingException("document_preview_stale", "The account has changed since this preview was made. Preview the document again.", 409);
            try
            {
                (_, hash, envelope) = await BuildAsync(account, snapshot, cutOffs, type, token.P, null, ct);
            }
            catch (BillingException ex) when (ex.Code is "payment_not_found" or "payment_reversed")
            {
                throw new BillingException("document_preview_stale", "The account has changed since this preview was made. Preview the document again.", 409, inner: ex);
            }
            if (transaction is not null) await transaction.CommitAsync(ct);
        }
        if (!string.Equals(hash, token.H, StringComparison.Ordinal))
            throw new BillingException("document_preview_stale", "The account has changed since this preview was made. Preview the document again.", 409);

        var number = (await db.FinancialDocuments.AsNoTracking().Where(d => d.AccountId == account.Id && d.DocumentType == type).Select(d => (int?)d.DocumentNumber).MaxAsync(ct) ?? 0) + 1;
        var paymentId = token.P is int p ? await db.BillingEntries.AsNoTracking().Where(e => e.AccountId == account.Id && e.EntryNumber == p).Select(e => (Guid?)e.Id).FirstAsync(ct) : null;
        var now = clock.UtcNow;                                                                          // read after the number, so the number's uniqueness (not this clock) is what two issues at once race on
        var document = new FinancialDocument
        {
            Id = Guid.NewGuid(), AccountId = account.Id, PatientId = patientId, DocumentType = type, DocumentNumber = number, PaymentEntryId = paymentId, AsOfEntryNumber = cutOffs.EntryNumber,
            AsOfAllocationNumber = cutOffs.AllocationNumber, AsOfReleaseNumber = cutOffs.ReleaseNumber, AsOfResponsibilitySequence = cutOffs.ResponsibilitySequence, AsOfUtc = asOf, TemplateVersion = token.V,
            ContentHash = hash, EnvelopeJson = JsonSerializer.Serialize(envelope), IdempotencyKey = key!, CorrelationId = Guid.NewGuid(), IssuedAtUtc = now > asOf ? now : asOf, IssuedByUserId = actor,
        };
        db.FinancialDocuments.Add(document);
        AuditService.Record(db, "FinancialDocumentIssued", nameof(BillingAccount), account.Id, actor, $"{type} {number} issued at entry {cutOffs.EntryNumber}.", correlationId: document.CorrelationId);
        await SaveAsync(account.Id, ct);
        return await ViewOfDocumentAsync(document, ct);
    }

    private async Task<IssuedDocumentView> ViewOfDocumentAsync(FinancialDocument d, CancellationToken ct)
    {
        var names = await ClinicalNames.ResolveAsync(db, [(Guid?)d.IssuedByUserId], ct);
        var paymentNumber = d.PaymentEntryId is Guid id ? await db.BillingEntries.AsNoTracking().Where(e => e.Id == id).Select(e => (int?)e.EntryNumber).FirstAsync(ct) : null;
        return new IssuedDocumentView(d.Id, d.DocumentType, d.DocumentNumber, paymentNumber, new FinancialCutOffs(d.AsOfEntryNumber, d.AsOfAllocationNumber, d.AsOfReleaseNumber, d.AsOfResponsibilitySequence, d.AsOfUtc),
            d.TemplateVersion, d.ContentHash, d.IssuedAtUtc, ClinicalNames.Name(names, d.IssuedByUserId));
    }

    public async Task<IReadOnlyList<IssuedDocumentView>> ListDocumentsAsync(Guid patientId, CancellationToken ct)
    {
        await RequirePatientAsync(patientId, ct);
        var rows = await db.FinancialDocuments.AsNoTracking().Where(d => d.PatientId == patientId).OrderBy(d => d.IssuedAtUtc).ThenBy(d => d.DocumentType).ThenBy(d => d.DocumentNumber).ToListAsync(ct);
        var views = new List<IssuedDocumentView>();
        foreach (var row in rows) views.Add(await ViewOfDocumentAsync(row, ct));
        return views;
    }

    /// <summary>
    /// An issued document rebuilt exactly as issued, with the showing audited (who looked at which document, in the same save as nothing else: it changes no ledger row). If the rebuilt content does not match
    /// the stored hash, nothing is shown (<c>document_content_changed</c>).
    /// </summary>
    public async Task<IssuedDocumentDetail> GetDocumentAsync(Guid patientId, Guid documentId, Guid actor, CancellationToken ct)
    {
        var detail = await RebuildDocumentAsync(patientId, documentId, ct);
        var account = await db.BillingAccounts.AsNoTracking().FirstAsync(a => a.PatientId == patientId, ct);
        AuditService.Record(db, "FinancialDocumentReprinted", nameof(BillingAccount), account.Id, actor, $"{detail.Document.DocumentType} {detail.Document.DocumentNumber} shown as issued.");
        await SaveAsync(account.Id, ct);
        return detail;
    }

    private async Task<IssuedDocumentDetail> RebuildDocumentAsync(Guid patientId, Guid documentId, CancellationToken ct)
    {
        await RequirePatientAsync(patientId, ct);
        var d = await db.FinancialDocuments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == documentId && x.PatientId == patientId, ct) ?? throw new BillingException("document_not_found", "That document was not found.", 404);
        var account = await db.BillingAccounts.AsNoTracking().FirstAsync(a => a.Id == d.AccountId, ct);
        var cutOffs = new FinancialCutOffs(d.AsOfEntryNumber, d.AsOfAllocationNumber, d.AsOfReleaseNumber, d.AsOfResponsibilitySequence, d.AsOfUtc);
        var view = await ViewOfDocumentAsync(d, ct);
        var envelope = JsonSerializer.Deserialize<DocumentEnvelope>(d.EnvelopeJson) ?? throw new BillingException("document_content_changed", "This document can no longer be shown exactly as it was issued.", 409);

        var snapshot = await LoadSnapshotAsync(account.Id, d.AsOfUtc, ct);
        object content;
        string hash;
        try
        {
            (content, hash, _) = await BuildAsync(account, snapshot, cutOffs, d.DocumentType, view.PaymentEntryNumber, envelope, ct);
        }
        catch (BillingException ex)
        {
            logger?.LogError("billing_document_rebuild_failed error_class={ErrorClass} code={Code}", "ContractViolation", ex.Code);
            throw new BillingException("document_content_changed", "This document can no longer be shown exactly as it was issued.", 409, inner: ex);
        }
        if (!string.Equals(hash, d.ContentHash, StringComparison.Ordinal))
        {
            logger?.LogError("billing_document_hash_mismatch error_class={ErrorClass} document_type={Type} document_number={Number}", "ContractViolation", d.DocumentType, d.DocumentNumber);
            throw new BillingException("document_content_changed", "This document can no longer be shown exactly as it was issued.", 409);
        }
        return new IssuedDocumentDetail(view, content as StatementContent, content as ReceiptContent, await CurrentStatusAsync(account.Id, d, view.PaymentEntryNumber, ct));
    }

    /// <summary>What has happened since a document was issued, as plain notes that sit beside it. They are not part of the document and not in its hash.</summary>
    private async Task<IReadOnlyList<string>> CurrentStatusAsync(Guid accountId, FinancialDocument d, int? paymentNumber, CancellationToken ct)
    {
        var ledger = await LoadLedgerAsync(accountId, ct);
        var notes = new List<string>();
        var later = ledger.Entries.Count(e => e.EntryNumber > d.AsOfEntryNumber);
        if (later > 0) notes.Add($"{later} ledger {(later == 1 ? "entry has" : "entries have")} been recorded since this {d.DocumentType.ToLowerInvariant()} was issued; the account's current balance is {BillingLedgerMath.Balance(ledger.Math):F2} {AppMoney.Currency}.");
        if (paymentNumber is int number)
        {
            var math = ledger.Math;
            if (BillingLedgerMath.Reversed(math).Contains(number)) notes.Add($"Payment {number} has since been reversed.");
            var refunded = BillingLedgerMath.Refunded(math, number);
            if (refunded > 0) notes.Add($"{refunded:F2} {AppMoney.Currency} of payment {number} has since been refunded.");
        }
        return notes;
    }

    /// <summary>The issued statement as CSV (one row per ledger line). Only statements have a CSV.</summary>
    public async Task<(string FileName, string Csv)> GetStatementCsvAsync(Guid patientId, Guid documentId, Guid actor, CancellationToken ct)
    {
        var detail = await RebuildDocumentAsync(patientId, documentId, ct);
        if (detail.Statement is null) throw new BillingException("csv_statement_only", "Only a statement can be exported as CSV.", 409);
        var account = await db.BillingAccounts.AsNoTracking().FirstAsync(a => a.PatientId == patientId, ct);
        AuditService.Record(db, "FinancialDocumentExported", nameof(BillingAccount), account.Id, actor, $"Statement {detail.Document.DocumentNumber} exported as CSV.");
        await SaveAsync(account.Id, ct);
        return ($"statement-{detail.Document.DocumentNumber}.csv", FinancialDocumentBuilder.StatementCsv(detail.Statement));
    }
}
