using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Clinical;
using Alveara.Api.Architecture.Documents;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Odontogram;
using Alveara.Api.Architecture.Patients;

namespace Alveara.Api.Data;

/// <summary>
/// STORY-010: the model for imported documents (docs/DOCUMENTS.md). As elsewhere, the database refuses by itself what the rules refuse. Triggers (created in the migration; errors 51182 to 51190, 51191 and up are free):
/// a document is never deleted (51182) and its file never changes (51183); a duplicate must name an earlier document of the same patient with the same hash (51184); the history is append-only (51185) and every row
/// has an issued number and the right patient (51186); the event counter only moves forward by one and is never deleted (51187); a category is never deleted and its code never changes (51188); a built-in blocked
/// extension is never changed or deleted (51189); the settings row is never deleted (51190). ALV-010-C01 adds: a documents encounter must belong to its patient (51192), a status change is append-only (51193) and a rollback is refused over library data (51194); 51195 and up are free.
/// </summary>
internal static class DocumentModel
{
    private static string Hex(string column) => $"[{column}] COLLATE Latin1_General_BIN2 NOT LIKE '%[^0-9a-f]%' AND LEN([{column}]) = 64";

    private static readonly DateTimeOffset Seeded = new(2026, 10, 19, 0, 0, 0, TimeSpan.Zero);

    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DocumentCategory>(e =>
        {
            e.ToTable("DocumentCategories");
            e.Property(x => x.Code).HasMaxLength(40);
            e.Property(x => x.Name).HasMaxLength(80);
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasIndex(x => x.Code).IsUnique();
            e.HasIndex(x => x.Name).IsUnique();
            e.ToTable(t =>
            {
                t.HasTrigger("TR_DocumentCategories_Guard");
                t.HasCheckConstraint("CK_DocumentCategories_Code", "[Code] COLLATE Latin1_General_BIN2 NOT LIKE '%[^a-z0-9_]%' AND LEN([Code]) > 0");
                t.HasCheckConstraint("CK_DocumentCategories_Name", $"LEN(LTRIM(RTRIM([Name]))) > 0 AND {PrescriptionModel.NoControl("Name")}");
            });
            e.HasData(DocumentCategorySeeds.All.Select((c, i) => new DocumentCategory { Id = c.Id, Code = c.Code, Name = c.Name, SortOrder = (i + 1) * 10, IsActive = true }));
        });

        modelBuilder.Entity<PatientDocument>(e =>
        {
            e.ToTable("Documents");
            e.Property(x => x.Description).HasMaxLength(500);
            e.Property(x => x.OriginalFileName).HasMaxLength(DocumentFileRules.MaxFileNameLength);
            e.Property(x => x.DetectedType).HasMaxLength(10);
            e.Property(x => x.ContentType).HasMaxLength(100);
            e.Property(x => x.Sha256).HasMaxLength(64).IsFixedLength().IsUnicode(false);
            e.Property(x => x.IdempotencyKey).HasMaxLength(64);
            e.Property(x => x.RequestHash).HasMaxLength(64).IsFixedLength().IsUnicode(false);
            e.Property(x => x.RowVersion).IsRowVersion();
            e.Property(x => x.ToothKey).HasMaxLength(2).IsFixedLength();
            e.Property(x => x.RegionKey).HasMaxLength(20);
            e.Property(x => x.Status).HasMaxLength(10).HasDefaultValue(DocumentStatuses.Active);
            e.Property(x => x.InactiveReason).HasMaxLength(500);
            e.Property(x => x.IntegrityStatus).HasMaxLength(10).HasDefaultValue(IntegrityStatuses.Unchecked);
            e.HasOne<Encounter>().WithMany().HasForeignKey(x => x.EncounterId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.InactivatedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.PatientId, x.Status });
            e.HasIndex(x => x.EncounterId);
            e.HasOne<Patient>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<DocumentCategory>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<PatientDocument>().WithMany().HasForeignKey(x => x.DuplicateOfDocumentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.ImportedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.PatientId, x.IdempotencyKey }).IsUnique();                  // a retried or double-clicked import with the same key can only ever make one document
            e.HasIndex(x => new { x.PatientId, x.Sha256 });                                      // the duplicate check
            e.HasIndex(x => new { x.PatientId, x.CategoryId, x.DocumentDate });
            e.HasIndex(x => x.BlobId).IsUnique();                                                // one stored file belongs to one document
            e.ToTable(t =>
            {
                t.HasTrigger("TR_Documents_NoDelete");
                t.HasTrigger("TR_Documents_Immutable");
                t.HasTrigger("TR_Documents_Links");
                t.HasTrigger("TR_Documents_EncounterLinks");
                t.HasCheckConstraint("CK_Documents_Hash", Hex("Sha256"));
                t.HasCheckConstraint("CK_Documents_RequestHash", Hex("RequestHash"));
                t.HasCheckConstraint("CK_Documents_Key", $"LEN(LTRIM(RTRIM([IdempotencyKey]))) > 0 AND {PrescriptionModel.NoControl("IdempotencyKey")}");
                t.HasCheckConstraint("CK_Documents_Name", $"LEN(LTRIM(RTRIM([OriginalFileName]))) > 0 AND {PrescriptionModel.NoControl("OriginalFileName")}");
                t.HasCheckConstraint("CK_Documents_Description", $"[Description] IS NULL OR (LEN(LTRIM(RTRIM([Description]))) > 0 AND {PrescriptionModel.NoControl("Description")})");
                t.HasCheckConstraint("CK_Documents_Type", "[DetectedType] COLLATE Latin1_General_CS_AS IN ('pdf','png','jpeg','tiff','other')");
                t.HasCheckConstraint("CK_Documents_ContentType", "LEN(LTRIM(RTRIM([ContentType]))) > 0");
                t.HasCheckConstraint("CK_Documents_Size", $"[SizeBytes] > 0 AND [SizeBytes] <= {DocumentFileRules.MaxUploadLimitBytes}");
                t.HasCheckConstraint("CK_Documents_Date", "[DocumentDate] >= '1900-01-01' AND [DocumentDate] < '2200-01-01'");
                t.HasCheckConstraint("CK_Documents_Imported", "[ImportedAtUtc] >= '2000-01-01' AND [ImportedAtUtc] < '2200-01-01'");
                t.HasCheckConstraint("CK_Documents_NotOwnDuplicate", "[DuplicateOfDocumentId] IS NULL OR [DuplicateOfDocumentId] <> [Id]");
                // ALV-010-C01: a document is about one tooth or one region, never both; an inactive document says why, who and when and an active one carries none of that; the storage check's result and its time go together
                t.HasCheckConstraint("CK_Documents_ToothKey", $"[ToothKey] IS NULL OR [ToothKey] COLLATE Latin1_General_CS_AS IN ({PrescriptionModel.In(ToothKeys.All)})");
                t.HasCheckConstraint("CK_Documents_Region", $"[RegionKey] IS NULL OR ([RegionKey] COLLATE Latin1_General_CS_AS IN ({PrescriptionModel.In(DiagnosisRegions.All)}) AND [ToothKey] IS NULL)");
                t.HasCheckConstraint("CK_Documents_Status", $"[Status] COLLATE Latin1_General_CS_AS IN ({PrescriptionModel.In(DocumentStatuses.All)})");
                t.HasCheckConstraint("CK_Documents_Inactive",
                    $"([Status] = '{DocumentStatuses.Active}' AND [InactiveReason] IS NULL AND [InactivatedByUserId] IS NULL AND [InactivatedAtUtc] IS NULL) " +
                    $"OR ([Status] = '{DocumentStatuses.Inactive}' AND [InactiveReason] IS NOT NULL AND LEN(LTRIM(RTRIM([InactiveReason]))) > 0 AND {PrescriptionModel.NoControl("InactiveReason")} AND [InactivatedByUserId] IS NOT NULL AND [InactivatedAtUtc] IS NOT NULL)");
                t.HasCheckConstraint("CK_Documents_Integrity",
                    $"[IntegrityStatus] COLLATE Latin1_General_CS_AS IN ({PrescriptionModel.In(IntegrityStatuses.All)}) " +
                    $"AND (([IntegrityStatus] = '{IntegrityStatuses.Unchecked}' AND [IntegrityCheckedAtUtc] IS NULL) OR ([IntegrityStatus] <> '{IntegrityStatuses.Unchecked}' AND [IntegrityCheckedAtUtc] IS NOT NULL))");
            });
        });

        modelBuilder.Entity<DocumentStatusChange>(e =>
        {
            e.ToTable("DocumentStatusChanges");
            e.Property(x => x.ToStatus).HasMaxLength(10);
            e.Property(x => x.Reason).HasMaxLength(500);
            e.HasOne<PatientDocument>().WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.ByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.DocumentId, x.AtUtc });
            e.ToTable(t =>
            {
                t.HasTrigger("TR_DocumentStatusChanges_Immutable");
                t.HasCheckConstraint("CK_DocumentStatusChanges_Status", $"[ToStatus] COLLATE Latin1_General_CS_AS IN ({PrescriptionModel.In(DocumentStatuses.All)})");
                t.HasCheckConstraint("CK_DocumentStatusChanges_Reason",
                    $"([ToStatus] = '{DocumentStatuses.Inactive}' AND [Reason] IS NOT NULL AND LEN(LTRIM(RTRIM([Reason]))) > 0 AND {PrescriptionModel.NoControl("Reason")}) OR ([ToStatus] = '{DocumentStatuses.Active}' AND [Reason] IS NULL)");
                t.HasCheckConstraint("CK_DocumentStatusChanges_Time", "[AtUtc] >= '2000-01-01' AND [AtUtc] < '2200-01-01'");
            });
        });

        modelBuilder.Entity<DocumentEvent>(e =>
        {
            e.ToTable("DocumentEvents");
            e.HasKey(x => x.EventNumber);
            e.Property(x => x.EventNumber).ValueGeneratedNever();                                // issued from DocumentEventCounter, never by the database identity
            e.Property(x => x.Kind).HasMaxLength(20);
            e.HasOne<PatientDocument>().WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<PatientDocument>().WithMany().HasForeignKey(x => x.RelatedDocumentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Patient>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<DocumentCategory>().WithMany().HasForeignKey(x => x.FromCategoryId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<DocumentCategory>().WithMany().HasForeignKey(x => x.ToCategoryId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.ByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.DocumentId, x.EventNumber });
            e.HasIndex(x => new { x.PatientId, x.EventNumber });
            e.ToTable(t =>
            {
                t.HasTrigger("TR_DocumentEvents_Immutable");
                t.HasTrigger("TR_DocumentEvents_Links");
                t.HasCheckConstraint("CK_DocumentEvents_Number", "[EventNumber] > 0");
                t.HasCheckConstraint("CK_DocumentEvents_Kind", $"[Kind] COLLATE Latin1_General_CS_AS IN ({PrescriptionModel.In(DocumentEventKinds.All)})");
                // a duplicate names the document it duplicates and nothing else does; only a recategorization names the categories it moved between
                t.HasCheckConstraint("CK_DocumentEvents_Related",
                    $"(([Kind] = '{DocumentEventKinds.ImportedAsDuplicate}' AND [RelatedDocumentId] IS NOT NULL) OR ([Kind] <> '{DocumentEventKinds.ImportedAsDuplicate}' AND [RelatedDocumentId] IS NULL)) " +
                    "AND ([RelatedDocumentId] IS NULL OR [RelatedDocumentId] <> [DocumentId])");
                t.HasCheckConstraint("CK_DocumentEvents_Categories",
                    $"(([Kind] = '{DocumentEventKinds.Categorized}' AND [FromCategoryId] IS NOT NULL AND [ToCategoryId] IS NOT NULL AND [FromCategoryId] <> [ToCategoryId]) " +
                    $"OR ([Kind] <> '{DocumentEventKinds.Categorized}' AND [FromCategoryId] IS NULL AND [ToCategoryId] IS NULL))");
                t.HasCheckConstraint("CK_DocumentEvents_Time", "[AtUtc] >= '2000-01-01' AND [AtUtc] < '2200-01-01'");
            });
        });

        modelBuilder.Entity<DocumentEventCounter>(e =>
        {
            e.ToTable("DocumentEventCounter");
            e.Property(x => x.Id).ValueGeneratedNever();
            e.ToTable(t =>
            {
                t.HasTrigger("TR_DocumentEventCounter_Guard");
                t.HasCheckConstraint("CK_DocumentEventCounter_OneRow", "[Id] = 1");
                t.HasCheckConstraint("CK_DocumentEventCounter_Last", "[LastEventNumber] >= 0");
            });
            e.HasData(new DocumentEventCounter { Id = 1, LastEventNumber = 0 });
        });

        modelBuilder.Entity<DocumentSettings>(e =>
        {
            e.ToTable("DocumentSettings");
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.UpdatedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t =>
            {
                t.HasTrigger("TR_DocumentSettings_NoDelete");
                t.HasCheckConstraint("CK_DocumentSettings_OneRow", "[Id] = 1");
                t.HasCheckConstraint("CK_DocumentSettings_Max", $"[MaxUploadBytes] >= {DocumentFileRules.MinUploadLimitBytes} AND [MaxUploadBytes] <= {DocumentFileRules.MaxUploadLimitBytes}");
            });
            e.HasData(new DocumentSettings { Id = 1, MaxUploadBytes = DocumentFileRules.DefaultMaxUploadBytes });
        });

        modelBuilder.Entity<BlockedFileExtension>(e =>
        {
            e.ToTable("BlockedFileExtensions");
            e.HasKey(x => x.Extension);
            e.Property(x => x.Extension).HasMaxLength(16);
            e.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.AddedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t =>
            {
                t.HasTrigger("TR_BlockedFileExtensions_Guard");
                t.HasCheckConstraint("CK_BlockedFileExtensions_Extension", "[Extension] COLLATE Latin1_General_BIN2 NOT LIKE '%[^a-z0-9]%' AND LEN([Extension]) > 0");
                t.HasCheckConstraint("CK_BlockedFileExtensions_Owner", "[IsBuiltIn] = 1 OR [AddedByUserId] IS NOT NULL");
            });
            e.HasData(DocumentFileRules.BuiltInBlockedExtensions.OrderBy(x => x, StringComparer.Ordinal).Select(x => new BlockedFileExtension { Extension = x, IsBuiltIn = true, AddedAtUtc = Seeded }));
        });
    }
}
