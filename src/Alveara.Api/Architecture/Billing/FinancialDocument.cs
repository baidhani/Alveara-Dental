namespace Alveara.Api.Architecture.Billing;

public static class FinancialDocumentTypes
{
    public const string Statement = "Statement";
    public const string Receipt = "Receipt";
    public static readonly IReadOnlyList<string> All = [Statement, Receipt];
}

public static class FinancialDocumentLimits
{
    /// <summary>A SHA-256 content hash written as lower-case hexadecimal.</summary>
    public const int HashLength = 64;
    public const int EnvelopeMax = 2000;
}

/// <summary>
/// ALV-007-C02: the record that a statement or a receipt was issued. It is not the document and holds no amount or balance: a document is a pure function of the preserved ledger at the cut-offs stored here
/// (the entries, allocations, releases and responsibility history up to those numbers, as of <see cref="AsOfUtc"/>), so it is regenerated, never read back, and <see cref="ContentHash"/> says whether the
/// regenerated content is exactly what was issued. The envelope is the non-financial identity text as it was displayed (practice, patient, who is responsible), kept so a reprint reads the same even if a name changes
/// or the responsible party was resolved from a fallback with no history. Appended, never edited or deleted.
/// </summary>
public class FinancialDocument
{
    public Guid Id { get; set; }
    public Guid AccountId { get; set; }
    public Guid PatientId { get; set; }
    /// <summary>One of <see cref="FinancialDocumentTypes.All"/>.</summary>
    public required string DocumentType { get; set; }
    /// <summary>1, 2, 3... per account and type (a statement and a receipt are numbered separately).</summary>
    public int DocumentNumber { get; set; }
    /// <summary>Receipt only: the payment the receipt is for.</summary>
    public Guid? PaymentEntryId { get; set; }
    public int AsOfEntryNumber { get; set; }
    public int AsOfAllocationNumber { get; set; }
    public int AsOfReleaseNumber { get; set; }
    public int AsOfResponsibilitySequence { get; set; }
    /// <summary>The precise instant the document is as of; responsibility, completed-procedure amendments and aging are resolved as of it.</summary>
    public DateTimeOffset AsOfUtc { get; set; }
    /// <summary>Which version of the document's layout and wording produced <see cref="ContentHash"/>.</summary>
    public int TemplateVersion { get; set; }
    /// <summary>SHA-256 (lower-case hexadecimal) of the canonical content.</summary>
    public required string ContentHash { get; set; }
    /// <summary>JSON: the practice, the patient and the responsible party as displayed. No amounts.</summary>
    public required string EnvelopeJson { get; set; }
    public required string IdempotencyKey { get; set; }
    public Guid CorrelationId { get; set; }
    public DateTimeOffset IssuedAtUtc { get; set; }
    public Guid IssuedByUserId { get; set; }
}
