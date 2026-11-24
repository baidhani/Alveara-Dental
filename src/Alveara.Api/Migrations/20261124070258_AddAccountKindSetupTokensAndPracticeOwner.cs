using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Alveara.Api.Migrations
{
    /// <summary>
    /// ALV-N030 slice 1 (brief section 3, approved at R04): accounts, first-run setup and the Practice Owner.
    /// - UserAccounts.AccountKind (0 = Staff, 1 = DeploymentAdministrator; at most one, UX_UserAccounts_OneDeploymentAdministrator) and MustChangePassword.
    /// - SetupTokens: hashed one-time setup tokens (no token is ever stored).
    /// - PracticeOwnerDesignations: at most one active designation (UX_PracticeOwnerDesignations_OneActive on the constant Slot, CK_ Slot = 1);
    ///   TR_PracticeOwnerDesignations_Protected (INSTEAD OF UPDATE, DELETE) refuses deletes and every change except ending an active designation (51311).
    /// - Upgrade data step (deterministic, idempotent): when EXACTLY ONE account holds the Admin role (6) and has no linked staff profile, it becomes the
    ///   Deployment Administrator and one DeploymentAdministratorAssigned audit row (method UpgradeSingleCandidate) is written. With none, or with two or more
    ///   (R01 CR-3 path 1), nothing is converted. No Practice Owner is ever set here. A fresh database has no accounts, so nothing happens.
    /// Down drops the tables, the index and the columns (the audit row, like every audit row, stays).
    /// </summary>
    public partial class AddAccountKindSetupTokensAndPracticeOwner : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte>(
                name: "AccountKind",
                table: "UserAccounts",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.AddColumn<bool>(
                name: "MustChangePassword",
                table: "UserAccounts",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "PracticeOwnerDesignations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StaffProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DesignatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DesignatedByAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Method = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    EndedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Slot = table.Column<byte>(type: "tinyint", nullable: false, defaultValue: (byte)1)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PracticeOwnerDesignations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PracticeOwnerDesignations_StaffProfiles_StaffProfileId",
                        column: x => x.StaffProfileId,
                        principalTable: "StaffProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SetupTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TokenHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    IssuedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ConsumedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    VoidedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    FailedAttempts = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SetupTokens", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "UX_UserAccounts_OneDeploymentAdministrator",
                table: "UserAccounts",
                column: "AccountKind",
                unique: true,
                filter: "[AccountKind] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_PracticeOwnerDesignations_StaffProfileId",
                table: "PracticeOwnerDesignations",
                column: "StaffProfileId");

            migrationBuilder.CreateIndex(
                name: "UX_PracticeOwnerDesignations_OneActive",
                table: "PracticeOwnerDesignations",
                column: "Slot",
                unique: true,
                filter: "[EndedAtUtc] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SetupTokens_TokenHash",
                table: "SetupTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.Sql("ALTER TABLE [PracticeOwnerDesignations] ADD CONSTRAINT [CK_PracticeOwnerDesignations_Slot] CHECK ([Slot] = 1);");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_PracticeOwnerDesignations_Protected] ON [PracticeOwnerDesignations] INSTEAD OF UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM deleted) RETURN;
    IF NOT EXISTS (SELECT 1 FROM inserted)
        THROW 51311, 'A Practice Owner designation is never deleted or changed, except to end an active designation.', 1;
    IF EXISTS (SELECT 1 FROM deleted d LEFT JOIN inserted i ON i.[Id] = d.[Id]
               WHERE i.[Id] IS NULL OR d.[EndedAtUtc] IS NOT NULL OR i.[EndedAtUtc] IS NULL
                  OR i.[StaffProfileId] <> d.[StaffProfileId] OR i.[DesignatedAtUtc] <> d.[DesignatedAtUtc] OR i.[DesignatedByAccountId] <> d.[DesignatedByAccountId]
                  OR i.[Method] <> d.[Method] OR i.[Slot] <> d.[Slot])
        THROW 51311, 'A Practice Owner designation is never deleted or changed, except to end an active designation.', 1;
    UPDATE t SET t.[EndedAtUtc] = i.[EndedAtUtc] FROM [PracticeOwnerDesignations] t JOIN inserted i ON t.[Id] = i.[Id];
END");

            // Upgrade data step: exactly one profile-less Admin becomes the Deployment Administrator (with its audit row); otherwise nothing changes.
            migrationBuilder.Sql(@"DECLARE @candidates TABLE ([Id] uniqueidentifier NOT NULL);
INSERT INTO @candidates ([Id])
    SELECT u.[Id] FROM [UserAccounts] u
    WHERE u.[Role] = 6 AND NOT EXISTS (SELECT 1 FROM [StaffProfiles] s WHERE s.[UserAccountId] = u.[Id]);
IF (SELECT COUNT(*) FROM @candidates) = 1 AND NOT EXISTS (SELECT 1 FROM [UserAccounts] WHERE [AccountKind] = 1)
BEGIN
    DECLARE @id uniqueidentifier = (SELECT [Id] FROM @candidates);
    UPDATE [UserAccounts] SET [AccountKind] = 1 WHERE [Id] = @id;
    INSERT INTO [AuditLogEntries] ([Id], [EventType], [TargetUserAccountId], [PerformedByUserAccountId], [Details], [TimestampUtc], [EntityType], [Reason], [CorrelationId])
    VALUES (NEWID(), N'DeploymentAdministratorAssigned', @id, NULL,
            N'Upgrade (ALV-N030): the only Administrator account without a staff profile became the Deployment Administrator. Method: UpgradeSingleCandidate. Actor: system (upgrade).',
            SYSDATETIMEOFFSET(), N'UserAccount', NULL, NULL);
END");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER [TR_PracticeOwnerDesignations_Protected];");
            migrationBuilder.DropTable(
                name: "PracticeOwnerDesignations");

            migrationBuilder.DropTable(
                name: "SetupTokens");

            migrationBuilder.DropIndex(
                name: "UX_UserAccounts_OneDeploymentAdministrator",
                table: "UserAccounts");

            migrationBuilder.DropColumn(
                name: "AccountKind",
                table: "UserAccounts");

            migrationBuilder.DropColumn(
                name: "MustChangePassword",
                table: "UserAccounts");
        }
    }
}
