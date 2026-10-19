using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Alveara.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BlockedFileExtensions",
                columns: table => new
                {
                    Extension = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    IsBuiltIn = table.Column<bool>(type: "bit", nullable: false),
                    AddedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AddedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlockedFileExtensions", x => x.Extension);
                    table.CheckConstraint("CK_BlockedFileExtensions_Extension", "[Extension] COLLATE Latin1_General_BIN2 NOT LIKE '%[^a-z0-9]%' AND LEN([Extension]) > 0");
                    table.CheckConstraint("CK_BlockedFileExtensions_Owner", "[IsBuiltIn] = 1 OR [AddedByUserId] IS NOT NULL");
                    table.ForeignKey(
                        name: "FK_BlockedFileExtensions_UserAccounts_AddedByUserId",
                        column: x => x.AddedByUserId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DocumentCategories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentCategories", x => x.Id);
                    table.CheckConstraint("CK_DocumentCategories_Code", "[Code] COLLATE Latin1_General_BIN2 NOT LIKE '%[^a-z0-9_]%' AND LEN([Code]) > 0");
                    table.CheckConstraint("CK_DocumentCategories_Name", "LEN(LTRIM(RTRIM([Name]))) > 0 AND PATINDEX(N'%[' + NCHAR(1) + N'-' + NCHAR(31) + N']%', [Name] COLLATE Latin1_General_BIN2) = 0");
                });

            migrationBuilder.CreateTable(
                name: "DocumentEventCounter",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    LastEventNumber = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentEventCounter", x => x.Id);
                    table.CheckConstraint("CK_DocumentEventCounter_Last", "[LastEventNumber] >= 0");
                    table.CheckConstraint("CK_DocumentEventCounter_OneRow", "[Id] = 1");
                });

            migrationBuilder.CreateTable(
                name: "DocumentSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    MaxUploadBytes = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentSettings", x => x.Id);
                    table.CheckConstraint("CK_DocumentSettings_Max", "[MaxUploadBytes] >= 1048576 AND [MaxUploadBytes] <= 1073741824");
                    table.CheckConstraint("CK_DocumentSettings_OneRow", "[Id] = 1");
                    table.ForeignKey(
                        name: "FK_DocumentSettings_UserAccounts_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Documents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    DuplicateOfDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OriginalFileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    DetectedType = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    BlobId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ImportedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ImportedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    RequestHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Documents", x => x.Id);
                    table.CheckConstraint("CK_Documents_ContentType", "LEN(LTRIM(RTRIM([ContentType]))) > 0");
                    table.CheckConstraint("CK_Documents_Date", "[DocumentDate] >= '1900-01-01' AND [DocumentDate] < '2200-01-01'");
                    table.CheckConstraint("CK_Documents_Description", "[Description] IS NULL OR (LEN(LTRIM(RTRIM([Description]))) > 0 AND PATINDEX(N'%[' + NCHAR(1) + N'-' + NCHAR(31) + N']%', [Description] COLLATE Latin1_General_BIN2) = 0)");
                    table.CheckConstraint("CK_Documents_Hash", "[Sha256] COLLATE Latin1_General_BIN2 NOT LIKE '%[^0-9a-f]%' AND LEN([Sha256]) = 64");
                    table.CheckConstraint("CK_Documents_Imported", "[ImportedAtUtc] >= '2000-01-01' AND [ImportedAtUtc] < '2200-01-01'");
                    table.CheckConstraint("CK_Documents_Key", "LEN(LTRIM(RTRIM([IdempotencyKey]))) > 0 AND PATINDEX(N'%[' + NCHAR(1) + N'-' + NCHAR(31) + N']%', [IdempotencyKey] COLLATE Latin1_General_BIN2) = 0");
                    table.CheckConstraint("CK_Documents_Name", "LEN(LTRIM(RTRIM([OriginalFileName]))) > 0 AND PATINDEX(N'%[' + NCHAR(1) + N'-' + NCHAR(31) + N']%', [OriginalFileName] COLLATE Latin1_General_BIN2) = 0");
                    table.CheckConstraint("CK_Documents_NotOwnDuplicate", "[DuplicateOfDocumentId] IS NULL OR [DuplicateOfDocumentId] <> [Id]");
                    table.CheckConstraint("CK_Documents_RequestHash", "[RequestHash] COLLATE Latin1_General_BIN2 NOT LIKE '%[^0-9a-f]%' AND LEN([RequestHash]) = 64");
                    table.CheckConstraint("CK_Documents_Size", "[SizeBytes] > 0 AND [SizeBytes] <= 1073741824");
                    table.CheckConstraint("CK_Documents_Type", "[DetectedType] COLLATE Latin1_General_CS_AS IN ('pdf','png','jpeg','tiff','other')");
                    table.ForeignKey(
                        name: "FK_Documents_DocumentCategories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "DocumentCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Documents_Documents_DuplicateOfDocumentId",
                        column: x => x.DuplicateOfDocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Documents_Patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Documents_UserAccounts_ImportedByUserId",
                        column: x => x.ImportedByUserId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DocumentEvents",
                columns: table => new
                {
                    EventNumber = table.Column<long>(type: "bigint", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RelatedDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FromCategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ToCategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentEvents", x => x.EventNumber);
                    table.CheckConstraint("CK_DocumentEvents_Categories", "(([Kind] = 'Categorized' AND [FromCategoryId] IS NOT NULL AND [ToCategoryId] IS NOT NULL AND [FromCategoryId] <> [ToCategoryId]) OR ([Kind] <> 'Categorized' AND [FromCategoryId] IS NULL AND [ToCategoryId] IS NULL))");
                    table.CheckConstraint("CK_DocumentEvents_Kind", "[Kind] COLLATE Latin1_General_CS_AS IN ('Imported','ImportedAsDuplicate','Categorized','MetadataEdited','Opened')");
                    table.CheckConstraint("CK_DocumentEvents_Number", "[EventNumber] > 0");
                    table.CheckConstraint("CK_DocumentEvents_Related", "(([Kind] = 'ImportedAsDuplicate' AND [RelatedDocumentId] IS NOT NULL) OR ([Kind] <> 'ImportedAsDuplicate' AND [RelatedDocumentId] IS NULL)) AND ([RelatedDocumentId] IS NULL OR [RelatedDocumentId] <> [DocumentId])");
                    table.CheckConstraint("CK_DocumentEvents_Time", "[AtUtc] >= '2000-01-01' AND [AtUtc] < '2200-01-01'");
                    table.ForeignKey(
                        name: "FK_DocumentEvents_DocumentCategories_FromCategoryId",
                        column: x => x.FromCategoryId,
                        principalTable: "DocumentCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DocumentEvents_DocumentCategories_ToCategoryId",
                        column: x => x.ToCategoryId,
                        principalTable: "DocumentCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DocumentEvents_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DocumentEvents_Documents_RelatedDocumentId",
                        column: x => x.RelatedDocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DocumentEvents_Patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DocumentEvents_UserAccounts_ByUserId",
                        column: x => x.ByUserId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "BlockedFileExtensions",
                columns: new[] { "Extension", "AddedAtUtc", "AddedByUserId", "IsBuiltIn" },
                values: new object[,]
                {
                    { "bat", new DateTimeOffset(new DateTime(2026, 10, 19, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, true },
                    { "cmd", new DateTimeOffset(new DateTime(2026, 10, 19, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, true },
                    { "com", new DateTimeOffset(new DateTime(2026, 10, 19, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, true },
                    { "dll", new DateTimeOffset(new DateTime(2026, 10, 19, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, true },
                    { "exe", new DateTimeOffset(new DateTime(2026, 10, 19, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, true },
                    { "hta", new DateTimeOffset(new DateTime(2026, 10, 19, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, true },
                    { "htm", new DateTimeOffset(new DateTime(2026, 10, 19, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, true },
                    { "html", new DateTimeOffset(new DateTime(2026, 10, 19, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, true },
                    { "jar", new DateTimeOffset(new DateTime(2026, 10, 19, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, true },
                    { "js", new DateTimeOffset(new DateTime(2026, 10, 19, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, true },
                    { "lnk", new DateTimeOffset(new DateTime(2026, 10, 19, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, true },
                    { "msi", new DateTimeOffset(new DateTime(2026, 10, 19, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, true },
                    { "ps1", new DateTimeOffset(new DateTime(2026, 10, 19, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, true },
                    { "scr", new DateTimeOffset(new DateTime(2026, 10, 19, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, true },
                    { "vbs", new DateTimeOffset(new DateTime(2026, 10, 19, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, true }
                });

            migrationBuilder.InsertData(
                table: "DocumentCategories",
                columns: new[] { "Id", "Code", "IsActive", "Name", "SortOrder" },
                values: new object[,]
                {
                    { new Guid("d0c00000-0000-0000-0000-000000000001"), "radiograph", true, "Radiograph", 10 },
                    { new Guid("d0c00000-0000-0000-0000-000000000002"), "photo", true, "Photo", 20 },
                    { new Guid("d0c00000-0000-0000-0000-000000000003"), "referral_letter", true, "Referral letter", 30 },
                    { new Guid("d0c00000-0000-0000-0000-000000000004"), "medical_clearance", true, "Medical clearance", 40 },
                    { new Guid("d0c00000-0000-0000-0000-000000000005"), "lab_report", true, "Lab report", 50 },
                    { new Guid("d0c00000-0000-0000-0000-000000000006"), "insurance_card", true, "Insurance card", 60 },
                    { new Guid("d0c00000-0000-0000-0000-000000000007"), "identification", true, "Identification", 70 },
                    { new Guid("d0c00000-0000-0000-0000-000000000008"), "correspondence", true, "Correspondence", 80 },
                    { new Guid("d0c00000-0000-0000-0000-000000000009"), "other", true, "Other", 90 }
                });

            migrationBuilder.InsertData(
                table: "DocumentEventCounter",
                columns: new[] { "Id", "LastEventNumber" },
                values: new object[] { 1, 0L });

            migrationBuilder.InsertData(
                table: "DocumentSettings",
                columns: new[] { "Id", "MaxUploadBytes", "UpdatedAtUtc", "UpdatedByUserId" },
                values: new object[] { 1, 104857600L, null, null });

            migrationBuilder.CreateIndex(
                name: "IX_BlockedFileExtensions_AddedByUserId",
                table: "BlockedFileExtensions",
                column: "AddedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentCategories_Code",
                table: "DocumentCategories",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentCategories_Name",
                table: "DocumentCategories",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentEvents_ByUserId",
                table: "DocumentEvents",
                column: "ByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentEvents_DocumentId_EventNumber",
                table: "DocumentEvents",
                columns: new[] { "DocumentId", "EventNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentEvents_FromCategoryId",
                table: "DocumentEvents",
                column: "FromCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentEvents_PatientId_EventNumber",
                table: "DocumentEvents",
                columns: new[] { "PatientId", "EventNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentEvents_RelatedDocumentId",
                table: "DocumentEvents",
                column: "RelatedDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentEvents_ToCategoryId",
                table: "DocumentEvents",
                column: "ToCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_BlobId",
                table: "Documents",
                column: "BlobId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Documents_CategoryId",
                table: "Documents",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_DuplicateOfDocumentId",
                table: "Documents",
                column: "DuplicateOfDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_ImportedByUserId",
                table: "Documents",
                column: "ImportedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_PatientId_CategoryId_DocumentDate",
                table: "Documents",
                columns: new[] { "PatientId", "CategoryId", "DocumentDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Documents_PatientId_IdempotencyKey",
                table: "Documents",
                columns: new[] { "PatientId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Documents_PatientId_Sha256",
                table: "Documents",
                columns: new[] { "PatientId", "Sha256" });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentSettings_UpdatedByUserId",
                table: "DocumentSettings",
                column: "UpdatedByUserId");

            // ---- STORY-010 triggers (errors 51182 to 51191; 51192 and up are free). The imported file never changes; the history is append-only with issued numbers; the locked rows stay locked. ----
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_Documents_NoDelete] ON [Documents] INSTEAD OF DELETE AS
BEGIN
    THROW 51182, 'An imported document is never deleted.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_Documents_Immutable] ON [Documents] AFTER UPDATE AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON d.[Id] = i.[Id]
               WHERE i.[PatientId] <> d.[PatientId] OR i.[BlobId] <> d.[BlobId] OR i.[Sha256] <> d.[Sha256] OR i.[SizeBytes] <> d.[SizeBytes] OR i.[OriginalFileName] <> d.[OriginalFileName]
                  OR i.[DetectedType] <> d.[DetectedType] OR i.[ContentType] <> d.[ContentType] OR i.[ImportedByUserId] <> d.[ImportedByUserId] OR i.[ImportedAtUtc] <> d.[ImportedAtUtc]
                  OR i.[IdempotencyKey] <> d.[IdempotencyKey] OR i.[RequestHash] <> d.[RequestHash]
                  OR ISNULL(i.[DuplicateOfDocumentId], '00000000-0000-0000-0000-000000000000') <> ISNULL(d.[DuplicateOfDocumentId], '00000000-0000-0000-0000-000000000000'))
        THROW 51183, 'The file of an imported document never changes; only its category, date and description can be edited.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_Documents_Links] ON [Documents] AFTER INSERT AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted i JOIN [Documents] o ON o.[Id] = i.[DuplicateOfDocumentId] WHERE o.[PatientId] <> i.[PatientId] OR o.[Sha256] <> i.[Sha256])
        THROW 51184, 'A duplicate must name a document of the same patient with the same file content.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_DocumentEvents_Immutable] ON [DocumentEvents] INSTEAD OF UPDATE, DELETE AS
BEGIN
    THROW 51185, 'The document history is append-only.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_DocumentEvents_Links] ON [DocumentEvents] AFTER INSERT AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted i WHERE i.[EventNumber] > (SELECT [LastEventNumber] FROM [DocumentEventCounter] WHERE [Id] = 1))
        THROW 51186, 'A history row must use a number the counter has issued.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN [Documents] d ON d.[Id] = i.[DocumentId] WHERE d.[PatientId] <> i.[PatientId])
        THROW 51186, 'A history row must carry the patient of its document.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN [Documents] r ON r.[Id] = i.[RelatedDocumentId] WHERE r.[PatientId] <> i.[PatientId])
        THROW 51186, 'A history row can only relate to a document of the same patient.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_DocumentEventCounter_Guard] ON [DocumentEventCounter] AFTER UPDATE, DELETE AS
BEGIN
    IF EXISTS (SELECT 1 FROM deleted) AND NOT EXISTS (SELECT 1 FROM inserted)
        THROW 51187, 'The document event counter is never deleted.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON d.[Id] = i.[Id] WHERE i.[LastEventNumber] <> d.[LastEventNumber] + 1)
        THROW 51187, 'The document event counter only moves forward by one.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_DocumentCategories_Guard] ON [DocumentCategories] AFTER UPDATE, DELETE AS
BEGIN
    IF EXISTS (SELECT 1 FROM deleted) AND NOT EXISTS (SELECT 1 FROM inserted)
        THROW 51188, 'A document category is never deleted; it is deactivated.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON d.[Id] = i.[Id] WHERE i.[Code] <> d.[Code])
        THROW 51188, 'The code of a document category never changes.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_BlockedFileExtensions_Guard] ON [BlockedFileExtensions] AFTER UPDATE, DELETE AS
BEGIN
    IF EXISTS (SELECT 1 FROM deleted WHERE [IsBuiltIn] = 1)
        THROW 51189, 'A built-in blocked file extension is never changed or deleted.', 1;
    IF EXISTS (SELECT 1 FROM inserted)
        THROW 51189, 'A blocked file extension is added or removed, never changed.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_DocumentSettings_NoDelete] ON [DocumentSettings] AFTER DELETE AS
BEGIN
    THROW 51190, 'The document settings row is never deleted.', 1;
END");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // a rollback is only possible while nothing has been imported: afterwards these tables are patient records that a script must not drop
            migrationBuilder.Sql(@"IF EXISTS (SELECT 1 FROM [Documents]) OR EXISTS (SELECT 1 FROM [DocumentEvents])
    THROW 51191, 'Imported documents exist, so this migration cannot be rolled back.', 1;");
            migrationBuilder.DropTable(
                name: "BlockedFileExtensions");

            migrationBuilder.DropTable(
                name: "DocumentEventCounter");

            migrationBuilder.DropTable(
                name: "DocumentEvents");

            migrationBuilder.DropTable(
                name: "DocumentSettings");

            migrationBuilder.DropTable(
                name: "Documents");

            migrationBuilder.DropTable(
                name: "DocumentCategories");
        }
    }
}
