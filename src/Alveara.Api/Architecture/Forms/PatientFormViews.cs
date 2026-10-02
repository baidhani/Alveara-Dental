namespace Alveara.Api.Architecture.Forms;

public record SnapshotView(
    Guid Id, Guid PatientFormId, int TemplateVersionNumber, string TemplateKey, string Category, string Title, string Body,
    IReadOnlyList<FormFieldDefinition> Fields, IReadOnlyDictionary<string, string> Responses,
    string SignerName, string SignerRelationship, string? SignerRelationshipNote, string SignatureMethod, string SignatureText, string Attestation,
    DateTimeOffset SignedAtUtc, Guid? CapturedByUserId, string SnapshotHash, bool IntegrityVerified);

public record FormEventView(string EventType, Guid? ActorUserId, DateTimeOffset OccurredAtUtc, int TemplateVersionNumber, string? Detail);

public record PatientFormSummary(
    Guid Id, Guid PatientId, Guid TemplateId, string TemplateKey, string Category, string Title, int TemplateVersionNumber, string Status,
    DateTimeOffset StartedAtUtc, DateTimeOffset? SignedAtUtc, DateTimeOffset? VoidedAtUtc, string? VoidReason, bool WasSigned,
    string? SignerName, string? SignerRelationship, bool NewerVersionAvailable);

public record PatientFormDetail(
    PatientFormSummary Summary, string RowVersion, TemplateVersionView Version, IReadOnlyDictionary<string, string> Responses,
    SnapshotView? Snapshot, IReadOnlyList<FormEventView> Events, TemplateVersionView? NewerVersion);

public record SignInput(string? SignerName, string? Relationship, string? RelationshipNote, string? SignatureText, bool Attested, Guid TemplateVersionId, string? RowVersion);

/// <summary>
/// ALV-N010's extension seam for ALV-010-C01 (the document/image library): everything a library needs to index or display a
/// signed form WITHOUT touching signature semantics - a stable id, the patient, what it is, when it was signed, its integrity
/// hash and whether it has since been voided. The library may link to or list signed forms through this; it cannot change
/// one, because the snapshot itself is immutable and this descriptor is read-only.
/// </summary>
public record SignedFormDocumentDescriptor(
    Guid SnapshotId, Guid PatientFormId, Guid PatientId, string TemplateKey, string Category, string Title, int TemplateVersionNumber,
    DateTimeOffset SignedAtUtc, string SnapshotHash, bool IsVoided)
{
    public const string ContentType = "application/vnd.alveara.signed-form+json";
}
