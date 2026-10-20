namespace Alveara.Api.Architecture.Documents;

/// <summary>One thing wrong with a field of a request, named so the screen can point at it.</summary>
public sealed record DocumentProblem(string Field, string Code, string Message);

/// <summary>A document action that was refused; nothing was saved. <see cref="Code"/> is the stable <c>error</c> code of the API (docs/DOCUMENTS.md).</summary>
public class DocumentException(string code, string message, int statusCode, IReadOnlyList<DocumentProblem>? problems = null, object? detail = null) : Exception(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
    public IReadOnlyList<DocumentProblem> Problems { get; } = problems ?? [];
    public object? Detail { get; } = detail;
}

/// <summary>An import: the file's stream (seekable) and its declared length, and everything the importer typed. Nothing in it is trusted until the service has checked it.</summary>
public sealed record DocumentImportRequest(Guid PatientId, Stream Content, long Length, string? FileName, Guid? CategoryId, DateOnly? DocumentDate, string? Description, string? IdempotencyKey, Guid? ConfirmDuplicateOf,
    Guid? EncounterId = null, string? ToothKey = null, string? RegionKey = null);

public sealed record DocumentView(Guid Id, Guid PatientId, Guid CategoryId, string CategoryCode, string CategoryName, DateOnly DocumentDate, string? Description, Guid? DuplicateOfDocumentId,
    string OriginalFileName, string DetectedType, bool TypeVerified, long SizeBytes, string Sha256, string ImportedByName, DateTimeOffset ImportedAtUtc, string RowVersion,
    Guid? EncounterId = null, string? ToothKey = null, string? RegionKey = null, string Status = "Active", string? InactiveReason = null, string? InactivatedByName = null, DateTimeOffset? InactivatedAtUtc = null,
    string IntegrityStatus = "Unchecked", DateTimeOffset? IntegrityCheckedAtUtc = null);

public sealed record DocumentImportResult(DocumentView Document, bool Replayed);

/// <summary>What the duplicate warning shows about the document already filed (docs/DOCUMENTS.md, "Store, then check for a duplicate").</summary>
public sealed record DuplicateSummary(Guid DocumentId, string CategoryName, DateOnly DocumentDate, string ImportedByName, DateTimeOffset ImportedAtUtc);

/// <summary>
/// What an edit may change: the category, the date, the description and the clinical context (encounter, tooth or region). An edit states all of them: a context field left null is cleared. The row version is what
/// the caller last read, so a change made by someone else in between is refused.
/// </summary>
public sealed record DocumentEditInput(Guid? CategoryId, DateOnly? DocumentDate, string? Description, string? RowVersion, Guid? EncounterId = null, string? ToothKey = null, string? RegionKey = null);

public sealed record DocumentHistoryEntry(long EventNumber, string Kind, DateTimeOffset AtUtc, string ByName, string? FromCategoryName, string? ToCategoryName, Guid? RelatedDocumentId);

/// <summary>One inactivation or reactivation: what the document moved to, why (an inactivation always says), who and when.</summary>
public sealed record StatusChangeEntry(string ToStatus, string? Reason, string ByName, DateTimeOffset AtUtc);

public sealed record DocumentDetail(DocumentView Document, IReadOnlyList<DocumentHistoryEntry> History, IReadOnlyList<StatusChangeEntry>? StatusChanges = null);

/// <summary>
/// One line of a patient's library: an imported document or a signed form (read-only, listed through the existing signed-form seam; the form itself is not copied). For a signed form <see cref="Id"/> is the
/// snapshot's id and <see cref="Document"/> is null.
/// </summary>
public sealed record LibraryEntry(string Kind, Guid Id, Guid? PatientFormId, string Title, string CategoryName, DateOnly Date, string DetectedType, long? SizeBytes, string? ImportedByName,
    DateTimeOffset AtUtc, bool IsVoided, bool IsDuplicate, DocumentView? Document, bool IsInactive = false)
{
    public const string DocumentKind = "Document";
    public const string SignedFormKind = "SignedForm";
    public const string SignedFormCategory = "Signed form";
}

/// <summary>A file ready to be served. <see cref="InlineSafe"/> is true only for PDF, PNG and JPEG, which may be shown in the browser under a restrictive policy; everything else is a download.</summary>
public sealed class OpenedDocument(Stream content, string fileName, string contentType, bool inlineSafe, long sizeBytes) : IAsyncDisposable
{
    public Stream Content { get; } = content;
    public string FileName { get; } = fileName;
    public string ContentType { get; } = contentType;
    public bool InlineSafe { get; } = inlineSafe;
    public long SizeBytes { get; } = sizeBytes;
    public ValueTask DisposeAsync() => Content.DisposeAsync();
}

public sealed record CategoryView(Guid Id, string Code, string Name, int SortOrder, bool IsActive, string RowVersion);

/// <summary>A rename and/or a deactivation or reactivation. Leave a field null to leave it as it is.</summary>
public sealed record CategoryEditInput(string? Name, bool? IsActive, string? RowVersion);

public sealed record DocumentSettingsView(long MaxUploadBytes, long MinAllowedBytes, long MaxAllowedBytes, string RowVersion);

public sealed record BlockedExtensionView(string Extension, bool IsBuiltIn, DateTimeOffset AddedAtUtc);

/// <summary>One encounter as the document pickers show it: which one, when, and its state. Nothing the clinical record holds.</summary>
public sealed record EncounterChoice(Guid Id, DateTimeOffset EncounterAtUtc, string Status);
