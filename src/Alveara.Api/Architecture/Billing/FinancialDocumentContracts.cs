namespace Alveara.Api.Architecture.Billing;

/// <summary>The practice as a document shows it (name, phone, address), kept in the issued document's envelope so a reprint shows the practice as it was on the day.</summary>
public sealed record DocumentPractice(string Name, string? Phone, string? AddressLine);

/// <summary>Who a document is about or addressed to. <c>IsSelf</c> is true when it is the account's own patient.</summary>
public sealed record DocumentParty(Guid PatientId, string Name, bool IsSelf);

/// <summary>One ledger entry as a document shows it. Amounts are fixed two-place text so the canonical bytes (and so the hash) never depend on how a number was stored or computed.</summary>
public sealed record DocumentLine(int Number, string Kind, string Amount, string BalanceAfter, string? ProcedureCode, string? ProcedureDescription, string? Method, string? Reason, string RecordedAtUtc,
    int? ReversesNumber, int? RefundsNumber, int? ReplacesNumber);

public sealed record DocumentOpenCharge(int Number, string? ProcedureCode, string? ProcedureDescription, string Amount, string Paid, string Open, string ServiceDateUtc, int AgeDays, string Bucket);
public sealed record DocumentUnapplied(int PaymentNumber, string Amount);
public sealed record DocumentAging(string Current, string Days31To60, string Days61To90, string Over90);

/// <summary>The canonical content of a statement at its cut-offs. Everything in it is derived from the preserved ledger as it stood at those cut-offs plus the display envelope.</summary>
public sealed record StatementContent(string DocumentType, int TemplateVersion, string AsOfUtc, string Currency, DocumentParty Patient, DocumentParty Addressee, IReadOnlyList<DocumentLine> Lines,
    IReadOnlyList<DocumentOpenCharge> OpenCharges, IReadOnlyList<DocumentUnapplied> Unapplied, DocumentAging Aging, string Balance, string OpenChargeTotal, string UnappliedTotal, DocumentPractice? Practice = null);

public sealed record ReceiptApplication(int ChargeNumber, string? ProcedureCode, string? ProcedureDescription, string Amount);

/// <summary>The canonical content of a receipt for one payment at its cut-offs: what was received, how, from whom, what it paid and what was left unapplied, and the balance owed then.</summary>
public sealed record ReceiptContent(string DocumentType, int TemplateVersion, string AsOfUtc, string Currency, DocumentParty Patient, DocumentParty ReceivedFrom, int PaymentNumber, string Amount, string? Method,
    string ReceivedAtUtc, IReadOnlyList<ReceiptApplication> Applications, string Unapplied, string RefundedToDate, string BalanceOwed, DocumentPractice? Practice = null);

/// <summary>The four cut-offs and the instant that fix what a document contains. Appending to the ledger never changes what is at or before them.</summary>
public sealed record FinancialCutOffs(int EntryNumber, int AllocationNumber, int ReleaseNumber, int ResponsibilitySequence, DateTimeOffset AsOfUtc);

/// <summary>The non-financial display envelope stored with an issued document, so a reprint shows the same names (the addressee as resolved on the day) whatever has changed since.</summary>
public sealed record DocumentEnvelope(DocumentParty Patient, DocumentParty Addressee, string AddresseeSource, string Currency, DocumentPractice? Practice = null);

/// <summary>A preview: the content a person is reviewing, its hash, and the token that issuing must present. Exactly one of <c>Statement</c> and <c>Receipt</c> is set.</summary>
public sealed record DocumentPreview(string DocumentType, FinancialCutOffs CutOffs, int TemplateVersion, string ContentHash, string PreviewToken, StatementContent? Statement, ReceiptContent? Receipt);

public sealed record DocumentPreviewInput(string? DocumentType, int? PaymentEntryNumber);
public sealed record IssueDocumentInput(string? PreviewToken, string? IdempotencyKey);

/// <summary>An issued document as listed.</summary>
public sealed record IssuedDocumentView(Guid Id, string DocumentType, int DocumentNumber, int? PaymentEntryNumber, FinancialCutOffs CutOffs, int TemplateVersion, string ContentHash, DateTimeOffset IssuedAtUtc, string? IssuedByName);

/// <summary>An issued document rebuilt exactly as issued, plus separate notes on what has happened since (never part of the issued content or its hash).</summary>
public sealed record IssuedDocumentDetail(IssuedDocumentView Document, StatementContent? Statement, ReceiptContent? Receipt, IReadOnlyList<string> CurrentStatus);

/// <summary>One thing that happened on an account, from the preserved history, with the completed procedure behind a charge named in <c>Source</c>.</summary>
public sealed record TimelineEvent(DateTimeOffset AtUtc, string Type, int? Number, string Summary, decimal? Amount, string? Source, string? ActorName, Guid? PlanItemId = null, Guid? CompletedProcedureId = null);

/// <summary>What is open on the account now, by age. Live aging uses the latest amendment of a completed procedure's performed time; an issued document ages as it did on its own day.</summary>
public sealed record AgingView(DateTimeOffset AsOfUtc, string Currency, DocumentAging Buckets, IReadOnlyList<DocumentOpenCharge> OpenCharges);
