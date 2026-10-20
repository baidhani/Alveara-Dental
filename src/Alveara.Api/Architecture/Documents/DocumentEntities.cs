namespace Alveara.Api.Architecture.Documents;

/// <summary>The kinds of rows in the document history (docs/DOCUMENTS.md, "The model").</summary>
public static class DocumentEventKinds
{
    public const string Imported = "Imported";
    public const string ImportedAsDuplicate = "ImportedAsDuplicate";
    public const string Categorized = "Categorized";
    public const string MetadataEdited = "MetadataEdited";
    public const string Opened = "Opened";
    public const string Inactivated = "Inactivated";
    public const string Reactivated = "Reactivated";

    public static readonly IReadOnlyList<string> All = [Imported, ImportedAsDuplicate, Categorized, MetadataEdited, Opened, Inactivated, Reactivated];
}

/// <summary>ALV-010-C01: whether a document is in use. An inactive document is hidden by default and kept for ever (never deleted); inactivating needs a reason and is reversible.</summary>
public static class DocumentStatuses
{
    public const string Active = "Active";
    public const string Inactive = "Inactive";

    public static readonly IReadOnlyList<string> All = [Active, Inactive];
}

/// <summary>ALV-010-C01: what the last storage check found for a document's file. Unchecked is a document that has never been checked (it has no <see cref="DocumentIntegrity"/> row); Missing is a file that is not on disk; Corrupt is a file whose size or hash no longer matches what was recorded at import.</summary>
public static class IntegrityStatuses
{
    public const string Unchecked = "Unchecked";
    public const string Ok = "Ok";
    public const string Missing = "Missing";
    public const string Corrupt = "Corrupt";

    public static readonly IReadOnlyList<string> All = [Unchecked, Ok, Missing, Corrupt];
}

/// <summary>The categories every installation starts with. Fixed ids and codes: a category is renamed or deactivated, never deleted, and its code never changes.</summary>
public static class DocumentCategorySeeds
{
    public static readonly Guid Radiograph = Guid.Parse("d0c00000-0000-0000-0000-000000000001");
    public static readonly Guid Photo = Guid.Parse("d0c00000-0000-0000-0000-000000000002");
    public static readonly Guid ReferralLetter = Guid.Parse("d0c00000-0000-0000-0000-000000000003");
    public static readonly Guid MedicalClearance = Guid.Parse("d0c00000-0000-0000-0000-000000000004");
    public static readonly Guid LabReport = Guid.Parse("d0c00000-0000-0000-0000-000000000005");
    public static readonly Guid InsuranceCard = Guid.Parse("d0c00000-0000-0000-0000-000000000006");
    public static readonly Guid Identification = Guid.Parse("d0c00000-0000-0000-0000-000000000007");
    public static readonly Guid Correspondence = Guid.Parse("d0c00000-0000-0000-0000-000000000008");
    public static readonly Guid Other = Guid.Parse("d0c00000-0000-0000-0000-000000000009");

    public static readonly IReadOnlyList<(Guid Id, string Code, string Name)> All =
    [
        (Radiograph, "radiograph", "Radiograph"),
        (Photo, "photo", "Photo"),
        (ReferralLetter, "referral_letter", "Referral letter"),
        (MedicalClearance, "medical_clearance", "Medical clearance"),
        (LabReport, "lab_report", "Lab report"),
        (InsuranceCard, "insurance_card", "Insurance card"),
        (Identification, "identification", "Identification"),
        (Correspondence, "correspondence", "Correspondence"),
        (Other, "other", "Other"),
    ];
}

/// <summary>A document category, chosen from a list an admin maintains. Never deleted (the database refuses it); <see cref="Code"/> never changes. A document keeps its category when that is deactivated.</summary>
public class DocumentCategory
{
    public Guid Id { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>
/// One imported file. The file itself is immutable (the database refuses a change to the patient, the blob, the hash, the size, the original name, the importer or the time, and any deletion); only the category,
/// the date and the description can be edited, with <see cref="RowVersion"/> refusing a stale edit. <see cref="OriginalFileName"/> is metadata only: the stored file is found by <see cref="BlobId"/>.
/// </summary>
public class PatientDocument
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    public Guid CategoryId { get; set; }
    public DateOnly DocumentDate { get; set; }
    public string? Description { get; set; }
    /// <summary>Set only when the importer confirmed adding a file that is already filed for this patient: the earlier document with the same hash.</summary>
    public Guid? DuplicateOfDocumentId { get; set; }
    public required string OriginalFileName { get; set; }
    /// <summary>pdf, png, jpeg, tiff or other, decided by the server from the content.</summary>
    public required string DetectedType { get; set; }
    /// <summary>The content type the file is served with, decided by the server and never taken from the client.</summary>
    public required string ContentType { get; set; }
    public long SizeBytes { get; set; }
    public required string Sha256 { get; set; }
    public Guid BlobId { get; set; }
    public Guid ImportedByUserId { get; set; }
    public DateTimeOffset ImportedAtUtc { get; set; }
    public required string IdempotencyKey { get; set; }
    public required string RequestHash { get; set; }

    // ---- ALV-010-C01: clinical context, status and storage integrity (all editable or maintained after import; none of them is part of the immutable file) ----

    /// <summary>The encounter this document belongs to, when it does (the database refuses an encounter of another patient).</summary>
    public Guid? EncounterId { get; set; }
    /// <summary>The tooth it is about, as its two-digit FDI key; never together with <see cref="RegionKey"/>.</summary>
    public string? ToothKey { get; set; }
    /// <summary>The oral region it is about (one of <c>DiagnosisRegions</c>); never together with <see cref="ToothKey"/>.</summary>
    public string? RegionKey { get; set; }
    /// <summary>One of <see cref="DocumentStatuses"/>.</summary>
    public string Status { get; set; } = DocumentStatuses.Active;
    /// <summary>Why it was inactivated; set only while inactive.</summary>
    public string? InactiveReason { get; set; }
    public Guid? InactivatedByUserId { get; set; }
    public DateTimeOffset? InactivatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>
/// ALV-010-C01: the result of the last storage check of a document's file. A separate row, not columns on the document, so that a check (which may run over every document at once) never moves a document's row
/// version and so never makes someone's open edit look stale. No row means the document was never checked.
/// </summary>
public class DocumentIntegrity
{
    public Guid DocumentId { get; set; }
    /// <summary>Ok, Missing or Corrupt (<see cref="IntegrityStatuses"/>).</summary>
    public required string Status { get; set; }
    public DateTimeOffset CheckedAtUtc { get; set; }
    public Guid? CheckedByUserId { get; set; }
}

/// <summary>
/// ALV-010-C01: one inactivation or reactivation of a document, with who, when and (for an inactivation) the reason. Append-only: the document row only says what it is now, this keeps every reason for ever.
/// The reason is free text, so it lives here and never in <see cref="DocumentEvent"/>, whose columns stay ids and codes only.
/// </summary>
public class DocumentStatusChange
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    /// <summary>The status the document moved to.</summary>
    public required string ToStatus { get; set; }
    public string? Reason { get; set; }
    public Guid ByUserId { get; set; }
    public DateTimeOffset AtUtc { get; set; }
}

/// <summary>
/// The append-only document history. <see cref="EventNumber"/> comes from <see cref="DocumentEventCounter"/> under a row lock held until commit, so it is safe as a cursor: a reader that has seen N has seen every
/// number below N. Holds ids and codes only (no filename, description or content). <see cref="PatientId"/> is internal and is never written to the audit log.
/// </summary>
public class DocumentEvent
{
    public long EventNumber { get; set; }
    public required string Kind { get; set; }
    public Guid DocumentId { get; set; }
    /// <summary>For ImportedAsDuplicate, the document that was duplicated.</summary>
    public Guid? RelatedDocumentId { get; set; }
    public Guid PatientId { get; set; }
    /// <summary>For Categorized, the category the document left and the one it entered.</summary>
    public Guid? FromCategoryId { get; set; }
    public Guid? ToCategoryId { get; set; }
    public Guid ByUserId { get; set; }
    public DateTimeOffset AtUtc { get; set; }
}

/// <summary>The one row that issues <see cref="DocumentEvent.EventNumber"/>. Only ever moves forward by one (the database refuses anything else).</summary>
public class DocumentEventCounter
{
    public int Id { get; set; } = 1;
    public long LastEventNumber { get; set; }
}

/// <summary>The one row of document settings.</summary>
public class DocumentSettings
{
    public int Id { get; set; } = 1;
    public long MaxUploadBytes { get; set; } = DocumentFileRules.DefaultMaxUploadBytes;
    public Guid? UpdatedByUserId { get; set; }
    public DateTimeOffset? UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>A file extension nobody may import. The built-in rows (<see cref="DocumentFileRules.BuiltInBlockedExtensions"/>) are locked by the database; admin-added rows can be removed.</summary>
public class BlockedFileExtension
{
    public required string Extension { get; set; }
    public bool IsBuiltIn { get; set; }
    public Guid? AddedByUserId { get; set; }
    public DateTimeOffset AddedAtUtc { get; set; }
}
