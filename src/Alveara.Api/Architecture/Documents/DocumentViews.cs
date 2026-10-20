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
public sealed record DocumentImportRequest(Guid PatientId, Stream Content, long Length, string? FileName, Guid? CategoryId, DateOnly? DocumentDate, string? Description, string? IdempotencyKey, Guid? ConfirmDuplicateOf);

public sealed record DocumentView(Guid Id, Guid PatientId, Guid CategoryId, string CategoryCode, string CategoryName, DateOnly DocumentDate, string? Description, Guid? DuplicateOfDocumentId,
    string OriginalFileName, string DetectedType, bool TypeVerified, long SizeBytes, string Sha256, string ImportedByName, DateTimeOffset ImportedAtUtc, string RowVersion);

public sealed record DocumentImportResult(DocumentView Document, bool Replayed);

/// <summary>What the duplicate warning shows about the document already filed (docs/DOCUMENTS.md, "Store, then check for a duplicate").</summary>
public sealed record DuplicateSummary(Guid DocumentId, string CategoryName, DateOnly DocumentDate, string ImportedByName, DateTimeOffset ImportedAtUtc);
