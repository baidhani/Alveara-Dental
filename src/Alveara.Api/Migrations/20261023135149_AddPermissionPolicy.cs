using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Alveara.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPermissionPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PermissionDelegations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DelegateRole = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Permission = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TargetRole = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ChangedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ChangedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PermissionDelegations", x => x.Id);
                    table.CheckConstraint("CK_PermissionDelegations_Time", "[ChangedAtUtc] >= '2000-01-01' AND [ChangedAtUtc] < '2200-01-01'");
                    table.ForeignKey(
                        name: "FK_PermissionDelegations_UserAccounts_ChangedByUserId",
                        column: x => x.ChangedByUserId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PermissionPolicyState",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PermissionPolicyState", x => x.Id);
                    table.CheckConstraint("CK_PermissionPolicyState_OneRow", "[Id] = 1");
                    table.CheckConstraint("CK_PermissionPolicyState_Version", "[Version] >= 0");
                });

            migrationBuilder.CreateTable(
                name: "RolePermissionHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PolicyVersion = table.Column<long>(type: "bigint", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Role = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Permission = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TargetRole = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    OldEffective = table.Column<bool>(type: "bit", nullable: false),
                    NewEffective = table.Column<bool>(type: "bit", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ChangedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ViaDelegation = table.Column<bool>(type: "bit", nullable: false),
                    ChangedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RolePermissionHistories", x => x.Id);
                    table.CheckConstraint("CK_RolePermissionHistories_Kind", "[Kind] COLLATE Latin1_General_CS_AS IN ('Granted','Revoked','Reverted','DelegationAdded','DelegationRemoved','InvalidRowRemoved')");
                    table.CheckConstraint("CK_RolePermissionHistories_Reason", "LEN(LTRIM(RTRIM([Reason]))) > 0");
                    table.CheckConstraint("CK_RolePermissionHistories_Time", "[ChangedAtUtc] >= '2000-01-01' AND [ChangedAtUtc] < '2200-01-01'");
                    table.CheckConstraint("CK_RolePermissionHistories_Version", "[PolicyVersion] > 0");
                    table.ForeignKey(
                        name: "FK_RolePermissionHistories_UserAccounts_ChangedByUserId",
                        column: x => x.ChangedByUserId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RolePermissionOverrides",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Role = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Permission = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Effect = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ChangedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChangedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RolePermissionOverrides", x => x.Id);
                    table.CheckConstraint("CK_RolePermissionOverrides_Effect", "[Effect] COLLATE Latin1_General_CS_AS IN ('Grant','Revoke')");
                    table.CheckConstraint("CK_RolePermissionOverrides_Reason", "LEN(LTRIM(RTRIM([Reason]))) > 0");
                    table.CheckConstraint("CK_RolePermissionOverrides_Time", "[ChangedAtUtc] >= '2000-01-01' AND [ChangedAtUtc] < '2200-01-01'");
                    table.ForeignKey(
                        name: "FK_RolePermissionOverrides_UserAccounts_ChangedByUserId",
                        column: x => x.ChangedByUserId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "PermissionDelegations",
                columns: new[] { "Id", "ChangedAtUtc", "ChangedByUserId", "DelegateRole", "Permission", "TargetRole" },
                values: new object[,]
                {
                    { new Guid("0e1e9a7e-0000-4000-8000-000000000000"), new DateTimeOffset(new DateTime(2026, 10, 23, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "OfficeManager", "ImportDocuments", "Dentist" },
                    { new Guid("0e1e9a7e-0000-4000-8000-000000000001"), new DateTimeOffset(new DateTime(2026, 10, 23, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "OfficeManager", "ImportDocuments", "Hygienist" },
                    { new Guid("0e1e9a7e-0000-4000-8000-000000000002"), new DateTimeOffset(new DateTime(2026, 10, 23, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "OfficeManager", "ImportDocuments", "Assistant" },
                    { new Guid("0e1e9a7e-0000-4000-8000-000000000003"), new DateTimeOffset(new DateTime(2026, 10, 23, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "OfficeManager", "ImportDocuments", "FrontDesk" },
                    { new Guid("0e1e9a7e-0000-4000-8000-000000000004"), new DateTimeOffset(new DateTime(2026, 10, 23, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "OfficeManager", "ImportDocuments", "Billing" }
                });

            migrationBuilder.InsertData(
                table: "PermissionPolicyState",
                columns: new[] { "Id", "UpdatedAtUtc", "Version" },
                values: new object[] { 1, new DateTimeOffset(new DateTime(2026, 10, 23, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 0L });

            migrationBuilder.CreateIndex(
                name: "IX_PermissionDelegations_ChangedByUserId",
                table: "PermissionDelegations",
                column: "ChangedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PermissionDelegations_DelegateRole_Permission_TargetRole",
                table: "PermissionDelegations",
                columns: new[] { "DelegateRole", "Permission", "TargetRole" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RolePermissionHistories_ChangedByUserId",
                table: "RolePermissionHistories",
                column: "ChangedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RolePermissionHistories_PolicyVersion",
                table: "RolePermissionHistories",
                column: "PolicyVersion");

            migrationBuilder.CreateIndex(
                name: "IX_RolePermissionHistories_Role_Permission_PolicyVersion",
                table: "RolePermissionHistories",
                columns: new[] { "Role", "Permission", "PolicyVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_RolePermissionOverrides_ChangedByUserId",
                table: "RolePermissionOverrides",
                column: "ChangedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RolePermissionOverrides_Role_Permission",
                table: "RolePermissionOverrides",
                columns: new[] { "Role", "Permission" },
                unique: true);

            migrationBuilder.Sql(@"CREATE TRIGGER [TR_RolePermissionHistories_Immutable] ON [RolePermissionHistories] AFTER UPDATE, DELETE AS
BEGIN
    THROW 51204, 'Permission policy history is never changed or deleted.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_PermissionPolicyState_Guard] ON [PermissionPolicyState] AFTER UPDATE, DELETE AS
BEGIN
    IF EXISTS (SELECT 1 FROM deleted) AND NOT EXISTS (SELECT 1 FROM inserted)
        THROW 51205, 'The permission policy state is never deleted.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON d.[Id] = i.[Id] WHERE i.[Version] <> d.[Version] + 1)
        THROW 51205, 'The permission policy version only moves forward by one.', 1;
END");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // a rollback is only possible while no policy change has been recorded: afterwards the history is a record that a script must not drop
            migrationBuilder.Sql(@"IF EXISTS (SELECT 1 FROM [RolePermissionHistories])
    THROW 51206, 'Permission policy history exists, so this migration cannot be rolled back.', 1;");
            migrationBuilder.DropTable(
                name: "PermissionDelegations");

            migrationBuilder.DropTable(
                name: "PermissionPolicyState");

            migrationBuilder.DropTable(
                name: "RolePermissionHistories");

            migrationBuilder.DropTable(
                name: "RolePermissionOverrides");
        }
    }
}
