using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Alveara.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddBillingLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_BillingEntries_Kind",
                table: "BillingEntries");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BillingEntries_Reason",
                table: "BillingEntries");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BillingEntries_Sign",
                table: "BillingEntries");

            migrationBuilder.AddColumn<Guid>(
                name: "CorrelationId",
                table: "BillingEntries",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PaidByPatientId",
                table: "BillingEntries",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PaymentMethodId",
                table: "BillingEntries",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReversesEntryId",
                table: "BillingEntries",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BillingAllocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AllocationNumber = table.Column<int>(type: "int", nullable: false),
                    PaymentEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChargeEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    AllocatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AllocatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingAllocations", x => x.Id);
                    table.CheckConstraint("CK_BillingAllocations_Amount", "[Amount] > 0 AND [Amount] <= 1000000.00");
                    table.CheckConstraint("CK_BillingAllocations_Key", "LEN(LTRIM(RTRIM([IdempotencyKey]))) > 0 AND PATINDEX(N'%[' + NCHAR(1) + N'-' + NCHAR(31) + N']%', [IdempotencyKey] COLLATE Latin1_General_BIN2) = 0");
                    table.CheckConstraint("CK_BillingAllocations_Number", "[AllocationNumber] >= 1");
                    table.ForeignKey(
                        name: "FK_BillingAllocations_BillingAccounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "BillingAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BillingAllocations_BillingEntries_ChargeEntryId",
                        column: x => x.ChargeEntryId,
                        principalTable: "BillingEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BillingAllocations_BillingEntries_PaymentEntryId",
                        column: x => x.PaymentEntryId,
                        principalTable: "BillingEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BillingPaymentMethods",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    IsBuiltIn = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingPaymentMethods", x => x.Id);
                    table.CheckConstraint("CK_BillingPaymentMethods_BuiltInActive", "[IsBuiltIn] = 0 OR [IsActive] = 1");
                    table.CheckConstraint("CK_BillingPaymentMethods_Kind", "[Kind] COLLATE Latin1_General_CS_AS IN ('Cash','Check','Card','Other')");
                    table.CheckConstraint("CK_BillingPaymentMethods_Name", "LEN(LTRIM(RTRIM([Name]))) > 0 AND PATINDEX(N'%[' + NCHAR(1) + N'-' + NCHAR(31) + N']%', [Name] COLLATE Latin1_General_BIN2) = 0");
                });

            migrationBuilder.CreateTable(
                name: "BillingResponsibilities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    ResponsiblePatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SetAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SetByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingResponsibilities", x => x.Id);
                    table.CheckConstraint("CK_BillingResponsibilities_Reason", "[Reason] IS NULL OR (LEN(LTRIM(RTRIM([Reason]))) > 0 AND PATINDEX(N'%[' + NCHAR(1) + N'-' + NCHAR(31) + N']%', [Reason] COLLATE Latin1_General_BIN2) = 0)");
                    table.CheckConstraint("CK_BillingResponsibilities_Sequence", "[Sequence] >= 1");
                    table.ForeignKey(
                        name: "FK_BillingResponsibilities_BillingAccounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "BillingAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BillingResponsibilities_Patients_ResponsiblePatientId",
                        column: x => x.ResponsiblePatientId,
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "BillingPaymentMethods",
                columns: new[] { "Id", "CreatedAtUtc", "CreatedByUserId", "IsActive", "IsBuiltIn", "Kind", "Name", "UpdatedAtUtc", "UpdatedByUserId" },
                values: new object[,]
                {
                    { new Guid("b1100000-0000-0000-0000-000000000001"), new DateTimeOffset(new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("00000000-0000-0000-0000-000000000000"), true, true, "Cash", "Cash", null, null },
                    { new Guid("b1100000-0000-0000-0000-000000000002"), new DateTimeOffset(new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("00000000-0000-0000-0000-000000000000"), true, true, "Check", "Check", null, null },
                    { new Guid("b1100000-0000-0000-0000-000000000003"), new DateTimeOffset(new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("00000000-0000-0000-0000-000000000000"), true, true, "Card", "Card", null, null },
                    { new Guid("b1100000-0000-0000-0000-000000000004"), new DateTimeOffset(new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("00000000-0000-0000-0000-000000000000"), true, true, "Other", "Other", null, null }
                });

            migrationBuilder.CreateIndex(
                name: "IX_BillingEntries_CorrelationId",
                table: "BillingEntries",
                column: "CorrelationId",
                filter: "[CorrelationId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_BillingEntries_PaidByPatientId",
                table: "BillingEntries",
                column: "PaidByPatientId");

            migrationBuilder.CreateIndex(
                name: "IX_BillingEntries_PaymentMethodId",
                table: "BillingEntries",
                column: "PaymentMethodId");

            migrationBuilder.CreateIndex(
                name: "IX_BillingEntries_ReversesEntryId",
                table: "BillingEntries",
                column: "ReversesEntryId",
                unique: true,
                filter: "[ReversesEntryId] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BillingEntries_Kind",
                table: "BillingEntries",
                sql: "[Kind] COLLATE Latin1_General_CS_AS IN ('Charge','Payment','Adjustment','Reversal')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BillingEntries_PaymentOnly",
                table: "BillingEntries",
                sql: "[Kind] = 'Payment' OR ([PaidByPatientId] IS NULL AND [PaymentMethodId] IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BillingEntries_Reason",
                table: "BillingEntries",
                sql: "([Kind] IN ('Adjustment', 'Reversal') AND [Reason] IS NOT NULL AND LEN(LTRIM(RTRIM([Reason]))) > 0) OR ([Kind] = 'Payment' AND ([Reason] IS NULL OR LEN(LTRIM(RTRIM([Reason]))) > 0)) OR ([Kind] = 'Charge' AND [Reason] IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BillingEntries_Reverses",
                table: "BillingEntries",
                sql: "([Kind] = 'Reversal' AND [ReversesEntryId] IS NOT NULL) OR ([Kind] <> 'Reversal' AND [ReversesEntryId] IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BillingEntries_Sign",
                table: "BillingEntries",
                sql: "([Kind] = 'Charge' AND [Amount] > 0) OR ([Kind] = 'Payment' AND [Amount] < 0) OR ([Kind] IN ('Adjustment', 'Reversal') AND [Amount] <> 0)");

            migrationBuilder.CreateIndex(
                name: "IX_BillingAllocations_AccountId_AllocationNumber",
                table: "BillingAllocations",
                columns: new[] { "AccountId", "AllocationNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BillingAllocations_AccountId_IdempotencyKey",
                table: "BillingAllocations",
                columns: new[] { "AccountId", "IdempotencyKey" });

            migrationBuilder.CreateIndex(
                name: "IX_BillingAllocations_ChargeEntryId",
                table: "BillingAllocations",
                column: "ChargeEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_BillingAllocations_PaymentEntryId",
                table: "BillingAllocations",
                column: "PaymentEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_BillingPaymentMethods_Name",
                table: "BillingPaymentMethods",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BillingResponsibilities_AccountId_Sequence",
                table: "BillingResponsibilities",
                columns: new[] { "AccountId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BillingResponsibilities_ResponsiblePatientId",
                table: "BillingResponsibilities",
                column: "ResponsiblePatientId");

            migrationBuilder.AddForeignKey(
                name: "FK_BillingEntries_BillingEntries_ReversesEntryId",
                table: "BillingEntries",
                column: "ReversesEntryId",
                principalTable: "BillingEntries",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_BillingEntries_BillingPaymentMethods_PaymentMethodId",
                table: "BillingEntries",
                column: "PaymentMethodId",
                principalTable: "BillingPaymentMethods",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_BillingEntries_Patients_PaidByPatientId",
                table: "BillingEntries",
                column: "PaidByPatientId",
                principalTable: "Patients",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // ---- ALV-007-C01 triggers: what no single-row check can see (error numbers 51127-51143; 51144 and up are free) ----
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_BillingAllocations_Immutable] ON [BillingAllocations] INSTEAD OF UPDATE, DELETE AS
BEGIN
    THROW 51127, 'Allocations are append-only; reversing a payment releases its allocations.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_BillingAllocations_Links] ON [BillingAllocations] AFTER INSERT AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted i JOIN [BillingEntries] p ON p.Id = i.PaymentEntryId JOIN [BillingEntries] c ON c.Id = i.ChargeEntryId WHERE p.Kind <> 'Payment' OR c.Kind <> 'Charge')
        THROW 51128, 'An allocation applies a payment to a charge.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN [BillingEntries] p ON p.Id = i.PaymentEntryId JOIN [BillingEntries] c ON c.Id = i.ChargeEntryId
               WHERE p.AccountId <> i.AccountId OR c.AccountId <> i.AccountId OR p.PatientId <> i.PatientId OR c.PatientId <> i.PatientId)
        THROW 51129, 'An allocation stays within one account.', 1;
    IF EXISTS (SELECT 1 FROM inserted i WHERE EXISTS (SELECT 1 FROM [BillingEntries] r WHERE r.ReversesEntryId IN (i.PaymentEntryId, i.ChargeEntryId)))
        THROW 51132, 'A reversed payment or charge cannot be allocated.', 1;
    IF EXISTS (SELECT 1 FROM (SELECT p.Id, -p.Amount AS Cap FROM [BillingEntries] p WHERE p.Id IN (SELECT PaymentEntryId FROM inserted)) pay
               CROSS APPLY (SELECT ISNULL(SUM(a.Amount), 0) AS Used FROM [BillingAllocations] a WHERE a.PaymentEntryId = pay.Id) u WHERE u.Used > pay.Cap)
        THROW 51130, 'A payment cannot be allocated for more than it holds.', 1;
    IF EXISTS (SELECT 1 FROM (SELECT c.Id, c.Amount AS Cap FROM [BillingEntries] c WHERE c.Id IN (SELECT ChargeEntryId FROM inserted)) ch
               CROSS APPLY (SELECT ISNULL(SUM(a.Amount), 0) AS Used FROM [BillingAllocations] a
                            WHERE a.ChargeEntryId = ch.Id AND NOT EXISTS (SELECT 1 FROM [BillingEntries] r WHERE r.ReversesEntryId = a.PaymentEntryId)) u WHERE u.Used > ch.Cap)
        THROW 51131, 'A charge cannot be paid for more than it is.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_BillingResponsibilities_Immutable] ON [BillingResponsibilities] INSTEAD OF UPDATE, DELETE AS
BEGIN
    THROW 51133, 'Who is responsible for an account is append-only; record the new responsible party instead.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_BillingResponsibilities_Links] ON [BillingResponsibilities] AFTER INSERT AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted i JOIN [BillingAccounts] a ON a.Id = i.AccountId WHERE a.PatientId <> i.PatientId)
        THROW 51134, 'A responsibility must carry the same patient as its account.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN [Patients] p ON p.Id = i.ResponsiblePatientId WHERE p.IsActive = 0)
        THROW 51135, 'Only an active patient can be responsible for an account.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_BillingPaymentMethods_NoDelete] ON [BillingPaymentMethods] INSTEAD OF DELETE AS
BEGIN
    THROW 51136, 'A payment method is never deleted; one that is no longer used is inactivated.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_BillingPaymentMethods_Identity] ON [BillingPaymentMethods] AFTER UPDATE AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON d.Id = i.Id
               WHERE i.Name <> d.Name OR i.Kind <> d.Kind OR i.IsBuiltIn <> d.IsBuiltIn OR i.CreatedAtUtc <> d.CreatedAtUtc OR i.CreatedByUserId <> d.CreatedByUserId)
        THROW 51137, 'A payment method''s name and kind never change; inactivate it and add another.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_BillingEntries_Reversals] ON [BillingEntries] AFTER INSERT AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted i JOIN [BillingEntries] t ON t.Id = i.ReversesEntryId WHERE t.AccountId <> i.AccountId OR t.PatientId <> i.PatientId)
        THROW 51138, 'A reversal undoes an entry of the same account.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN [BillingEntries] t ON t.Id = i.ReversesEntryId WHERE t.Kind = 'Reversal')
        THROW 51139, 'A reversal cannot itself be reversed.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN [BillingEntries] t ON t.Id = i.ReversesEntryId WHERE i.Amount <> -t.Amount)
        THROW 51140, 'A reversal is exactly the opposite of the entry it undoes.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN [BillingEntries] t ON t.Id = i.ReversesEntryId
               WHERE t.Kind = 'Charge' AND EXISTS (SELECT 1 FROM [BillingAllocations] a WHERE a.ChargeEntryId = t.Id AND NOT EXISTS (SELECT 1 FROM [BillingEntries] r WHERE r.ReversesEntryId = a.PaymentEntryId)))
        THROW 51141, 'A charge that payments are applied to cannot be reversed until those payments are.', 1;
    IF EXISTS (SELECT 1 FROM inserted i WHERE i.PaidByPatientId IS NOT NULL AND i.PaidByPatientId <> i.PatientId
               AND NOT EXISTS (SELECT 1 FROM [BillingResponsibilities] r WHERE r.AccountId = i.AccountId AND r.ResponsiblePatientId = i.PaidByPatientId))
        THROW 51142, 'A payment is made by the patient or by someone recorded as responsible for the account.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN [BillingPaymentMethods] m ON m.Id = i.PaymentMethodId WHERE m.Kind <> i.PaymentMethod OR m.IsActive = 0)
        THROW 51143, 'A payment uses an active method of its own kind.', 1;
END");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER [TR_BillingEntries_Reversals]");
            migrationBuilder.DropForeignKey(
                name: "FK_BillingEntries_BillingEntries_ReversesEntryId",
                table: "BillingEntries");

            migrationBuilder.DropForeignKey(
                name: "FK_BillingEntries_BillingPaymentMethods_PaymentMethodId",
                table: "BillingEntries");

            migrationBuilder.DropForeignKey(
                name: "FK_BillingEntries_Patients_PaidByPatientId",
                table: "BillingEntries");

            migrationBuilder.DropTable(
                name: "BillingAllocations");

            migrationBuilder.DropTable(
                name: "BillingPaymentMethods");

            migrationBuilder.DropTable(
                name: "BillingResponsibilities");

            migrationBuilder.DropIndex(
                name: "IX_BillingEntries_CorrelationId",
                table: "BillingEntries");

            migrationBuilder.DropIndex(
                name: "IX_BillingEntries_PaidByPatientId",
                table: "BillingEntries");

            migrationBuilder.DropIndex(
                name: "IX_BillingEntries_PaymentMethodId",
                table: "BillingEntries");

            migrationBuilder.DropIndex(
                name: "IX_BillingEntries_ReversesEntryId",
                table: "BillingEntries");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BillingEntries_Kind",
                table: "BillingEntries");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BillingEntries_PaymentOnly",
                table: "BillingEntries");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BillingEntries_Reason",
                table: "BillingEntries");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BillingEntries_Reverses",
                table: "BillingEntries");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BillingEntries_Sign",
                table: "BillingEntries");

            migrationBuilder.DropColumn(
                name: "CorrelationId",
                table: "BillingEntries");

            migrationBuilder.DropColumn(
                name: "PaidByPatientId",
                table: "BillingEntries");

            migrationBuilder.DropColumn(
                name: "PaymentMethodId",
                table: "BillingEntries");

            migrationBuilder.DropColumn(
                name: "ReversesEntryId",
                table: "BillingEntries");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BillingEntries_Kind",
                table: "BillingEntries",
                sql: "[Kind] COLLATE Latin1_General_CS_AS IN ('Charge','Payment','Adjustment')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BillingEntries_Reason",
                table: "BillingEntries",
                sql: "([Kind] = 'Adjustment' AND [Reason] IS NOT NULL AND LEN(LTRIM(RTRIM([Reason]))) > 0) OR ([Kind] = 'Payment' AND ([Reason] IS NULL OR LEN(LTRIM(RTRIM([Reason]))) > 0)) OR ([Kind] = 'Charge' AND [Reason] IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BillingEntries_Sign",
                table: "BillingEntries",
                sql: "([Kind] = 'Charge' AND [Amount] > 0) OR ([Kind] = 'Payment' AND [Amount] < 0) OR ([Kind] = 'Adjustment' AND [Amount] <> 0)");
        }
    }
}
