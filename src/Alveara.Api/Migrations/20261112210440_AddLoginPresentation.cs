using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Alveara.Api.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// ALV-N020-C01: account-scoped permission grants, the sign-in presentation singleton (seeded with the bundled default) and the administrator-added
    /// backgrounds, with their triggers (51276-51279, see LoginPresentationModel). Down refuses (51276) once a grant or an added background exists.
    /// </summary>
    public partial class AddLoginPresentation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AccountPermissionGrants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Permission = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    GrantedByUserAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    GrantedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountPermissionGrants", x => x.Id);
                    table.CheckConstraint("CK_AccountPermissionGrants_Permission", "[Permission] COLLATE Latin1_General_CS_AS IN ('ManageLoginPresentation')");
                    table.CheckConstraint("CK_AccountPermissionGrants_Time", "[GrantedAtUtc] >= '2000-01-01' AND [GrantedAtUtc] < '2200-01-01'");
                    table.ForeignKey(
                        name: "FK_AccountPermissionGrants_UserAccounts_GrantedByUserAccountId",
                        column: x => x.GrantedByUserAccountId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AccountPermissionGrants_UserAccounts_UserAccountId",
                        column: x => x.UserAccountId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LoginBackgroundImages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Sha256 = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Extension = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: false),
                    Width = table.Column<int>(type: "int", nullable: false),
                    Height = table.Column<int>(type: "int", nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    StorageName = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SuggestedDisplayMode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    AddedByUserAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AddedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LoginBackgroundImages", x => x.Id);
                    table.CheckConstraint("CK_LoginBackgroundImages_Hash", "[Sha256] COLLATE Latin1_General_BIN2 NOT LIKE '%[^0-9a-f]%'");
                    table.CheckConstraint("CK_LoginBackgroundImages_Mode", "[SuggestedDisplayMode] COLLATE Latin1_General_CS_AS IN ('Tile','Stretch','Fill')");
                    table.CheckConstraint("CK_LoginBackgroundImages_Name", "LEN(LTRIM(RTRIM([DisplayName]))) > 0");
                    table.CheckConstraint("CK_LoginBackgroundImages_Size", "[Width] BETWEEN 1 AND 2560 AND [Height] BETWEEN 1 AND 2560 AND [SizeBytes] > 0");
                    table.CheckConstraint("CK_LoginBackgroundImages_Time", "[AddedAtUtc] >= '2000-01-01' AND [AddedAtUtc] < '2200-01-01'");
                    table.CheckConstraint("CK_LoginBackgroundImages_Type", "([ContentType] = 'image/png' AND [Extension] = 'png') OR ([ContentType] = 'image/jpeg' AND [Extension] = 'jpg')");
                    table.ForeignKey(
                        name: "FK_LoginBackgroundImages_UserAccounts_AddedByUserAccountId",
                        column: x => x.AddedByUserAccountId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LoginPresentationSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BackgroundKind = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    BundledKey = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    AddedImageId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DisplayMode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    FooterNoticeText = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ShowFooterNotice = table.Column<bool>(type: "bit", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedByUserAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LoginPresentationSettings", x => x.Id);
                    table.CheckConstraint("CK_LoginPresentationSettings_BundledKey", "[BundledKey] IS NULL OR [BundledKey] COLLATE Latin1_General_BIN2 NOT LIKE '%[^a-z0-9-]%'");
                    table.CheckConstraint("CK_LoginPresentationSettings_Kind", "[BackgroundKind] COLLATE Latin1_General_CS_AS IN ('Bundled','Added')");
                    table.CheckConstraint("CK_LoginPresentationSettings_Mode", "[DisplayMode] COLLATE Latin1_General_CS_AS IN ('Tile','Stretch','Fill')");
                    table.CheckConstraint("CK_LoginPresentationSettings_Notice", "[FooterNoticeText] IS NULL OR LEN(LTRIM(RTRIM([FooterNoticeText]))) > 0");
                    table.CheckConstraint("CK_LoginPresentationSettings_Shape", "([BackgroundKind] = 'Bundled' AND [BundledKey] IS NOT NULL AND [AddedImageId] IS NULL) OR ([BackgroundKind] = 'Added' AND [AddedImageId] IS NOT NULL AND [BundledKey] IS NULL)");
                    table.CheckConstraint("CK_LoginPresentationSettings_Singleton", "[Id] = '00000000-0000-0000-0000-00000000c01a'");
                    table.CheckConstraint("CK_LoginPresentationSettings_Time", "[UpdatedAtUtc] >= '2000-01-01' AND [UpdatedAtUtc] < '2200-01-01'");
                    table.ForeignKey(
                        name: "FK_LoginPresentationSettings_LoginBackgroundImages_AddedImageId",
                        column: x => x.AddedImageId,
                        principalTable: "LoginBackgroundImages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LoginPresentationSettings_UserAccounts_UpdatedByUserAccountId",
                        column: x => x.UpdatedByUserAccountId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "LoginPresentationSettings",
                columns: new[] { "Id", "AddedImageId", "BackgroundKind", "BundledKey", "DisplayMode", "FooterNoticeText", "ShowFooterNotice", "UpdatedAtUtc", "UpdatedByUserAccountId" },
                values: new object[] { new Guid("00000000-0000-0000-0000-00000000c01a"), null, "Bundled", "teal-doodle", "Tile", null, true, new DateTimeOffset(new DateTime(2026, 11, 12, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null });

            migrationBuilder.CreateIndex(
                name: "IX_AccountPermissionGrants_GrantedByUserAccountId",
                table: "AccountPermissionGrants",
                column: "GrantedByUserAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_AccountPermissionGrants_UserAccountId_Permission",
                table: "AccountPermissionGrants",
                columns: new[] { "UserAccountId", "Permission" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LoginBackgroundImages_AddedByUserAccountId",
                table: "LoginBackgroundImages",
                column: "AddedByUserAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_LoginBackgroundImages_Sha256",
                table: "LoginBackgroundImages",
                column: "Sha256",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LoginBackgroundImages_StorageName",
                table: "LoginBackgroundImages",
                column: "StorageName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LoginPresentationSettings_AddedImageId",
                table: "LoginPresentationSettings",
                column: "AddedImageId");

            migrationBuilder.CreateIndex(
                name: "IX_LoginPresentationSettings_UpdatedByUserAccountId",
                table: "LoginPresentationSettings",
                column: "UpdatedByUserAccountId");

            // An added background is history: never deleted (51276).
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_LoginBackgroundImages_NoDelete] ON [LoginBackgroundImages] AFTER DELETE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM deleted)
        THROW 51276, 'A sign-in background is never deleted; make it unavailable instead.', 1;
END");

            // The stored facts never change (51278); the current selection cannot be made unavailable (51277). READCOMMITTEDLOCK: a concurrent selection is seen.
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_LoginBackgroundImages_Guard] ON [LoginBackgroundImages] AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON d.[Id] = i.[Id]
               WHERE i.[Sha256] <> d.[Sha256] OR i.[StorageName] <> d.[StorageName] OR i.[ContentType] <> d.[ContentType] OR i.[Extension] <> d.[Extension]
                  OR i.[Width] <> d.[Width] OR i.[Height] <> d.[Height] OR i.[SizeBytes] <> d.[SizeBytes]
                  OR i.[AddedByUserAccountId] <> d.[AddedByUserAccountId] OR i.[AddedAtUtc] <> d.[AddedAtUtc])
        THROW 51278, 'The stored facts of a sign-in background never change.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN [LoginPresentationSettings] s WITH (READCOMMITTEDLOCK) ON s.[AddedImageId] = i.[Id]
               WHERE i.[IsActive] = 0 AND s.[BackgroundKind] = 'Added')
        THROW 51277, 'The current sign-in background cannot be made unavailable; choose another background first.', 1;
END");

            // The selection may only name an existing, available added background (51279).
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_LoginPresentationSettings_Links] ON [LoginPresentationSettings] AFTER INSERT, UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM inserted s
               WHERE s.[BackgroundKind] = 'Added'
                 AND NOT EXISTS (SELECT 1 FROM [LoginBackgroundImages] i WITH (READCOMMITTEDLOCK) WHERE i.[Id] = s.[AddedImageId] AND i.[IsActive] = 1))
        THROW 51279, 'The sign-in background must be an available background.', 1;
END");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Granted permissions and added backgrounds are history: a rollback is only possible while neither exists.
            migrationBuilder.Sql(@"IF EXISTS (SELECT 1 FROM [AccountPermissionGrants]) OR EXISTS (SELECT 1 FROM [LoginBackgroundImages])
    THROW 51276, 'Sign-in presentation permissions or backgrounds exist, so this migration cannot be rolled back.', 1;");

            migrationBuilder.DropTable(
                name: "AccountPermissionGrants");

            migrationBuilder.DropTable(
                name: "LoginPresentationSettings");

            migrationBuilder.DropTable(
                name: "LoginBackgroundImages");
        }
    }
}
