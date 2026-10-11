using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Alveara.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddBilling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BillingAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingAccounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BillingAccounts_Patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BillingEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EntryNumber = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    PlanItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ProcedureCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ProcedureDescription = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    PaymentMethod = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RecordedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingEntries", x => x.Id);
                    table.CheckConstraint("CK_BillingEntries_ChargeSource", "([Kind] = 'Charge' AND [PlanItemId] IS NOT NULL AND [ProcedureCode] IS NOT NULL AND LEN(LTRIM(RTRIM([ProcedureCode]))) > 0 AND [ProcedureDescription] IS NOT NULL AND LEN(LTRIM(RTRIM([ProcedureDescription]))) > 0) OR ([Kind] <> 'Charge' AND [PlanItemId] IS NULL AND [ProcedureCode] IS NULL AND [ProcedureDescription] IS NULL)");
                    table.CheckConstraint("CK_BillingEntries_Key", "LEN(LTRIM(RTRIM([IdempotencyKey]))) > 0 AND PATINDEX(N'%[' + NCHAR(1) + N'-' + NCHAR(31) + N']%', [IdempotencyKey] COLLATE Latin1_General_BIN2) = 0");
                    table.CheckConstraint("CK_BillingEntries_Kind", "[Kind] COLLATE Latin1_General_CS_AS IN ('Charge','Payment','Adjustment')");
                    table.CheckConstraint("CK_BillingEntries_Max", "ABS([Amount]) <= 1000000.00");
                    table.CheckConstraint("CK_BillingEntries_Method", "([Kind] = 'Payment' AND [PaymentMethod] IS NOT NULL AND [PaymentMethod] COLLATE Latin1_General_CS_AS IN ('Cash','Check','Card','Other')) OR ([Kind] <> 'Payment' AND [PaymentMethod] IS NULL)");
                    table.CheckConstraint("CK_BillingEntries_Number", "[EntryNumber] >= 1");
                    table.CheckConstraint("CK_BillingEntries_Reason", "([Kind] = 'Adjustment' AND [Reason] IS NOT NULL AND LEN(LTRIM(RTRIM([Reason]))) > 0) OR ([Kind] = 'Payment' AND ([Reason] IS NULL OR LEN(LTRIM(RTRIM([Reason]))) > 0)) OR ([Kind] = 'Charge' AND [Reason] IS NULL)");
                    table.CheckConstraint("CK_BillingEntries_Sign", "([Kind] = 'Charge' AND [Amount] > 0) OR ([Kind] = 'Payment' AND [Amount] < 0) OR ([Kind] = 'Adjustment' AND [Amount] <> 0)");
                    table.ForeignKey(
                        name: "FK_BillingEntries_BillingAccounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "BillingAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BillingEntries_TreatmentPlanItems_PlanItemId",
                        column: x => x.PlanItemId,
                        principalTable: "TreatmentPlanItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BillingAccounts_PatientId",
                table: "BillingAccounts",
                column: "PatientId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BillingEntries_AccountId_EntryNumber",
                table: "BillingEntries",
                columns: new[] { "AccountId", "EntryNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BillingEntries_AccountId_IdempotencyKey",
                table: "BillingEntries",
                columns: new[] { "AccountId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BillingEntries_PlanItemId",
                table: "BillingEntries",
                column: "PlanItemId",
                unique: true,
                filter: "[PlanItemId] IS NOT NULL");

            // ---- STORY-007 triggers: what no single-row check can see (error numbers 51118-51126; 51127 and up are free) ----
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_BillingAccounts_NoDelete] ON [BillingAccounts] INSTEAD OF DELETE AS
BEGIN
    THROW 51118, 'A billing account is never deleted.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_BillingAccounts_Identity] ON [BillingAccounts] AFTER UPDATE AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON d.Id = i.Id WHERE i.PatientId <> d.PatientId OR i.CreatedAtUtc <> d.CreatedAtUtc OR i.CreatedByUserId <> d.CreatedByUserId)
        THROW 51119, 'A billing account''s patient and origin never change.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_BillingEntries_Immutable] ON [BillingEntries] INSTEAD OF UPDATE, DELETE AS
BEGIN
    THROW 51120, 'Financial history is append-only; a mistake is corrected with a new entry.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_BillingEntries_Links] ON [BillingEntries] AFTER INSERT AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted i JOIN [BillingAccounts] a ON a.Id = i.AccountId WHERE a.PatientId <> i.PatientId)
        THROW 51121, 'A billing entry must carry the same patient as its account.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN [TreatmentPlanItems] t ON t.Id = i.PlanItemId WHERE t.PatientId <> i.PatientId)
        THROW 51122, 'A charge must bill a procedure of the same patient.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN [TreatmentPlanItems] t ON t.Id = i.PlanItemId JOIN [TreatmentPlans] p ON p.Id = t.PlanId WHERE t.WithdrawnAtUtc IS NOT NULL OR p.Status = 'Withdrawn')
        THROW 51123, 'A withdrawn procedure cannot be billed.', 1;
    IF EXISTS (SELECT 1 FROM inserted i WHERE i.PlanItemId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM [TreatmentPlanItemStates] s WHERE s.ItemId = i.PlanItemId AND s.Completion = 'Completed'))
        THROW 51124, 'Only a completed procedure can be billed.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN [TreatmentPlanItems] t ON t.Id = i.PlanItemId WHERE i.Amount <> t.Fee)
        THROW 51125, 'A charge is for the fee the procedure was proposed at, and no other amount.', 1;
    IF EXISTS (SELECT 1 FROM (SELECT AccountId, SUM(Amount) AS Balance FROM [BillingEntries] WHERE AccountId IN (SELECT AccountId FROM inserted) GROUP BY AccountId) b WHERE b.Balance < 0)
        THROW 51126, 'An entry cannot leave the account with a negative balance.', 1;
END");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BillingEntries");

            migrationBuilder.DropTable(
                name: "BillingAccounts");
        }
    }
}
