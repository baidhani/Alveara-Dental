using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Alveara.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddRefundsReleasesAndDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_BillingEntries_Kind",
                table: "BillingEntries");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BillingEntries_Method",
                table: "BillingEntries");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BillingEntries_PaymentOnly",
                table: "BillingEntries");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BillingEntries_Reason",
                table: "BillingEntries");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BillingEntries_Sign",
                table: "BillingEntries");

            migrationBuilder.AddColumn<Guid>(
                name: "RefundsEntryId",
                table: "BillingEntries",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReplacesEntryId",
                table: "BillingEntries",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BillingAllocationReleases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReleaseNumber = table.Column<int>(type: "int", nullable: false),
                    AllocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Cause = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    RefundEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ReleasedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ReleasedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingAllocationReleases", x => x.Id);
                    table.CheckConstraint("CK_BillingAllocationReleases_Amount", "[Amount] > 0 AND [Amount] <= 1000000.00");
                    table.CheckConstraint("CK_BillingAllocationReleases_Cause", "[Cause] COLLATE Latin1_General_CS_AS IN ('Refund','Correction') AND (([Cause] = 'Refund' AND [RefundEntryId] IS NOT NULL AND ([Reason] IS NULL OR (LEN(LTRIM(RTRIM([Reason]))) > 0 AND PATINDEX(N'%[' + NCHAR(1) + N'-' + NCHAR(31) + N']%', [Reason] COLLATE Latin1_General_BIN2) = 0))) OR ([Cause] = 'Correction' AND [RefundEntryId] IS NULL AND [Reason] IS NOT NULL AND LEN(LTRIM(RTRIM([Reason]))) > 0 AND PATINDEX(N'%[' + NCHAR(1) + N'-' + NCHAR(31) + N']%', [Reason] COLLATE Latin1_General_BIN2) = 0))");
                    table.CheckConstraint("CK_BillingAllocationReleases_Key", "LEN(LTRIM(RTRIM([IdempotencyKey]))) > 0 AND PATINDEX(N'%[' + NCHAR(1) + N'-' + NCHAR(31) + N']%', [IdempotencyKey] COLLATE Latin1_General_BIN2) = 0");
                    table.CheckConstraint("CK_BillingAllocationReleases_Number", "[ReleaseNumber] >= 1");
                    table.ForeignKey(
                        name: "FK_BillingAllocationReleases_BillingAccounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "BillingAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BillingAllocationReleases_BillingAllocations_AllocationId",
                        column: x => x.AllocationId,
                        principalTable: "BillingAllocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BillingAllocationReleases_BillingEntries_RefundEntryId",
                        column: x => x.RefundEntryId,
                        principalTable: "BillingEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FinancialDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentType = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    DocumentNumber = table.Column<int>(type: "int", nullable: false),
                    PaymentEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AsOfEntryNumber = table.Column<int>(type: "int", nullable: false),
                    AsOfAllocationNumber = table.Column<int>(type: "int", nullable: false),
                    AsOfReleaseNumber = table.Column<int>(type: "int", nullable: false),
                    AsOfResponsibilitySequence = table.Column<int>(type: "int", nullable: false),
                    AsOfUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    TemplateVersion = table.Column<int>(type: "int", nullable: false),
                    ContentHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    EnvelopeJson = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CorrelationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IssuedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IssuedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinancialDocuments", x => x.Id);
                    table.CheckConstraint("CK_FinancialDocuments_AsOf", "[AsOfUtc] <= [IssuedAtUtc]");
                    table.CheckConstraint("CK_FinancialDocuments_CutOffs", "[AsOfEntryNumber] >= 1 AND [AsOfAllocationNumber] >= 0 AND [AsOfReleaseNumber] >= 0 AND [AsOfResponsibilitySequence] >= 0 AND [TemplateVersion] >= 1");
                    table.CheckConstraint("CK_FinancialDocuments_Envelope", "ISJSON([EnvelopeJson]) = 1");
                    table.CheckConstraint("CK_FinancialDocuments_Hash", "LEN([ContentHash]) = 64 AND [ContentHash] COLLATE Latin1_General_BIN2 NOT LIKE '%[^0-9a-f]%'");
                    table.CheckConstraint("CK_FinancialDocuments_Key", "LEN(LTRIM(RTRIM([IdempotencyKey]))) > 0 AND PATINDEX(N'%[' + NCHAR(1) + N'-' + NCHAR(31) + N']%', [IdempotencyKey] COLLATE Latin1_General_BIN2) = 0");
                    table.CheckConstraint("CK_FinancialDocuments_Number", "[DocumentNumber] >= 1");
                    table.CheckConstraint("CK_FinancialDocuments_Payment", "([DocumentType] = 'Receipt' AND [PaymentEntryId] IS NOT NULL) OR ([DocumentType] = 'Statement' AND [PaymentEntryId] IS NULL)");
                    table.CheckConstraint("CK_FinancialDocuments_Type", "[DocumentType] COLLATE Latin1_General_CS_AS IN ('Statement','Receipt')");
                    table.ForeignKey(
                        name: "FK_FinancialDocuments_BillingAccounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "BillingAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FinancialDocuments_BillingEntries_PaymentEntryId",
                        column: x => x.PaymentEntryId,
                        principalTable: "BillingEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FinancialDocuments_Patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BillingEntries_RefundsEntryId",
                table: "BillingEntries",
                column: "RefundsEntryId",
                filter: "[RefundsEntryId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_BillingEntries_ReplacesEntryId",
                table: "BillingEntries",
                column: "ReplacesEntryId",
                unique: true,
                filter: "[ReplacesEntryId] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BillingEntries_Kind",
                table: "BillingEntries",
                sql: "[Kind] COLLATE Latin1_General_CS_AS IN ('Charge','Payment','Adjustment','Reversal','Refund')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BillingEntries_Method",
                table: "BillingEntries",
                sql: "([Kind] IN ('Payment', 'Refund') AND [PaymentMethod] IS NOT NULL AND [PaymentMethod] COLLATE Latin1_General_CS_AS IN ('Cash','Check','Card','Other')) OR ([Kind] NOT IN ('Payment', 'Refund') AND [PaymentMethod] IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BillingEntries_PaymentOnly",
                table: "BillingEntries",
                sql: "([Kind] = 'Payment' OR [PaidByPatientId] IS NULL) AND ([Kind] IN ('Payment', 'Refund') OR [PaymentMethodId] IS NULL) AND ([Kind] <> 'Refund' OR [PaymentMethodId] IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BillingEntries_Reason",
                table: "BillingEntries",
                sql: "([Kind] IN ('Adjustment', 'Reversal', 'Refund') AND [Reason] IS NOT NULL AND LEN(LTRIM(RTRIM([Reason]))) > 0) OR ([Kind] = 'Payment' AND ([Reason] IS NULL OR LEN(LTRIM(RTRIM([Reason]))) > 0)) OR ([Kind] = 'Charge' AND [Reason] IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BillingEntries_Refunds",
                table: "BillingEntries",
                sql: "([Kind] = 'Refund' AND [RefundsEntryId] IS NOT NULL) OR ([Kind] <> 'Refund' AND [RefundsEntryId] IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BillingEntries_Replaces",
                table: "BillingEntries",
                sql: "[ReplacesEntryId] IS NULL OR [Kind] IN ('Payment', 'Adjustment')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BillingEntries_Sign",
                table: "BillingEntries",
                sql: "([Kind] IN ('Charge', 'Refund') AND [Amount] > 0) OR ([Kind] = 'Payment' AND [Amount] < 0) OR ([Kind] IN ('Adjustment', 'Reversal') AND [Amount] <> 0)");

            migrationBuilder.CreateIndex(
                name: "IX_BillingAllocationReleases_AccountId_IdempotencyKey",
                table: "BillingAllocationReleases",
                columns: new[] { "AccountId", "IdempotencyKey" });

            migrationBuilder.CreateIndex(
                name: "IX_BillingAllocationReleases_AccountId_ReleaseNumber",
                table: "BillingAllocationReleases",
                columns: new[] { "AccountId", "ReleaseNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BillingAllocationReleases_AllocationId",
                table: "BillingAllocationReleases",
                column: "AllocationId");

            migrationBuilder.CreateIndex(
                name: "IX_BillingAllocationReleases_RefundEntryId",
                table: "BillingAllocationReleases",
                column: "RefundEntryId",
                filter: "[RefundEntryId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialDocuments_AccountId_DocumentType_DocumentNumber",
                table: "FinancialDocuments",
                columns: new[] { "AccountId", "DocumentType", "DocumentNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinancialDocuments_AccountId_IdempotencyKey",
                table: "FinancialDocuments",
                columns: new[] { "AccountId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinancialDocuments_PatientId",
                table: "FinancialDocuments",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialDocuments_PaymentEntryId",
                table: "FinancialDocuments",
                column: "PaymentEntryId",
                filter: "[PaymentEntryId] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_BillingEntries_BillingEntries_RefundsEntryId",
                table: "BillingEntries",
                column: "RefundsEntryId",
                principalTable: "BillingEntries",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_BillingEntries_BillingEntries_ReplacesEntryId",
                table: "BillingEntries",
                column: "ReplacesEntryId",
                principalTable: "BillingEntries",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // ---- ALV-007-C02 triggers: what no single-row check can see (error numbers 51157-51171; 51172 and up are free) ----
            // The allocation caps of ALV-007-C01 (51130, 51131) now count net of releases; a payment's cap is also reduced by what was refunded. With no releases and no refunds the results are identical.
            // The reversal trigger of ALV-007-C01 refuses to reverse a charge while a live payment is applied to it; released money no longer counts (51141 now reads the allocation net of its releases).
            migrationBuilder.Sql("DROP TRIGGER [TR_BillingEntries_Reversals]");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_BillingEntries_Reversals] ON [BillingEntries] AFTER INSERT AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted i JOIN [BillingEntries] t ON t.Id = i.ReversesEntryId WHERE t.AccountId <> i.AccountId OR t.PatientId <> i.PatientId)
        THROW 51138, 'A reversal undoes an entry of the same account.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN [BillingEntries] t ON t.Id = i.ReversesEntryId WHERE t.Kind = 'Reversal')
        THROW 51139, 'A reversal cannot itself be reversed.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN [BillingEntries] t ON t.Id = i.ReversesEntryId WHERE i.Amount <> -t.Amount)
        THROW 51140, 'A reversal is exactly the opposite of the entry it undoes.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN [BillingEntries] t ON t.Id = i.ReversesEntryId
               WHERE t.Kind = 'Charge' AND EXISTS (SELECT 1 FROM [BillingAllocations] a WHERE a.ChargeEntryId = t.Id AND NOT EXISTS (SELECT 1 FROM [BillingEntries] r WHERE r.ReversesEntryId = a.PaymentEntryId)
                        AND a.Amount > ISNULL((SELECT SUM(x.Amount) FROM [BillingAllocationReleases] x WHERE x.AllocationId = a.Id), 0)))
        THROW 51141, 'A charge that payments are applied to cannot be reversed until those payments are.', 1;
    IF EXISTS (SELECT 1 FROM inserted i WHERE i.PaidByPatientId IS NOT NULL AND i.PaidByPatientId <> i.PatientId
               AND NOT EXISTS (SELECT 1 FROM [BillingResponsibilities] r WHERE r.AccountId = i.AccountId AND r.ResponsiblePatientId = i.PaidByPatientId))
        THROW 51142, 'A payment is made by the patient or by someone recorded as responsible for the account.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN [BillingPaymentMethods] m ON m.Id = i.PaymentMethodId WHERE m.Kind <> i.PaymentMethod OR m.IsActive = 0)
        THROW 51143, 'A payment uses an active method of its own kind.', 1;
END");
            migrationBuilder.Sql("DROP TRIGGER [TR_BillingAllocations_Links]");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_BillingAllocations_Links] ON [BillingAllocations] AFTER INSERT AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted i JOIN [BillingEntries] p ON p.Id = i.PaymentEntryId JOIN [BillingEntries] c ON c.Id = i.ChargeEntryId WHERE p.Kind <> 'Payment' OR c.Kind <> 'Charge')
        THROW 51128, 'An allocation applies a payment to a charge.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN [BillingEntries] p ON p.Id = i.PaymentEntryId JOIN [BillingEntries] c ON c.Id = i.ChargeEntryId
               WHERE p.AccountId <> i.AccountId OR c.AccountId <> i.AccountId OR p.PatientId <> i.PatientId OR c.PatientId <> i.PatientId)
        THROW 51129, 'An allocation stays within one account.', 1;
    IF EXISTS (SELECT 1 FROM inserted i WHERE EXISTS (SELECT 1 FROM [BillingEntries] r WHERE r.ReversesEntryId IN (i.PaymentEntryId, i.ChargeEntryId)))
        THROW 51132, 'A reversed payment or charge cannot be allocated.', 1;
    IF EXISTS (SELECT 1 FROM (SELECT p.Id, -p.Amount - ISNULL((SELECT SUM(f.Amount) FROM [BillingEntries] f WHERE f.RefundsEntryId = p.Id), 0) AS Cap FROM [BillingEntries] p WHERE p.Id IN (SELECT PaymentEntryId FROM inserted)) pay
               CROSS APPLY (SELECT ISNULL(SUM(a.Amount), 0) - ISNULL((SELECT SUM(rl.Amount) FROM [BillingAllocationReleases] rl JOIN [BillingAllocations] a2 ON a2.Id = rl.AllocationId WHERE a2.PaymentEntryId = pay.Id), 0) AS Used
                            FROM [BillingAllocations] a WHERE a.PaymentEntryId = pay.Id) u WHERE u.Used > pay.Cap)
        THROW 51130, 'A payment cannot be allocated for more than it holds.', 1;
    IF EXISTS (SELECT 1 FROM (SELECT c.Id, c.Amount AS Cap FROM [BillingEntries] c WHERE c.Id IN (SELECT ChargeEntryId FROM inserted)) ch
               CROSS APPLY (SELECT ISNULL(SUM(a.Amount), 0) - ISNULL((SELECT SUM(rl.Amount) FROM [BillingAllocationReleases] rl JOIN [BillingAllocations] a2 ON a2.Id = rl.AllocationId
                                                                      WHERE a2.ChargeEntryId = ch.Id AND NOT EXISTS (SELECT 1 FROM [BillingEntries] r WHERE r.ReversesEntryId = a2.PaymentEntryId)), 0) AS Used
                            FROM [BillingAllocations] a WHERE a.ChargeEntryId = ch.Id AND NOT EXISTS (SELECT 1 FROM [BillingEntries] r WHERE r.ReversesEntryId = a.PaymentEntryId)) u WHERE u.Used > ch.Cap)
        THROW 51131, 'A charge cannot be paid for more than it is.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_BillingEntries_Refunds] ON [BillingEntries] AFTER INSERT AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted i JOIN [BillingEntries] p ON p.Id = i.RefundsEntryId WHERE p.Kind <> 'Payment' OR p.AccountId <> i.AccountId OR p.PatientId <> i.PatientId)
        THROW 51157, 'A refund returns part of a payment of the same account.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN [BillingEntries] p ON p.Id = i.RefundsEntryId
               WHERE (SELECT ISNULL(SUM(r.Amount), 0) FROM [BillingEntries] r WHERE r.RefundsEntryId = p.Id) > -p.Amount)
        THROW 51158, 'A payment cannot be refunded for more than it was.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN [BillingEntries] p ON p.Id = i.RefundsEntryId WHERE EXISTS (SELECT 1 FROM [BillingEntries] v WHERE v.ReversesEntryId = p.Id))
        THROW 51159, 'A reversed payment cannot be refunded.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN [BillingEntries] t ON t.Id = i.ReversesEntryId WHERE t.Kind = 'Payment' AND EXISTS (SELECT 1 FROM [BillingEntries] r WHERE r.RefundsEntryId = t.Id))
        THROW 51160, 'A payment that has been refunded cannot be reversed.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN [BillingPaymentMethods] m ON m.Id = i.PaymentMethodId WHERE i.Kind = 'Refund' AND (m.Kind <> i.PaymentMethod OR m.IsActive = 0))
        THROW 51161, 'A refund is returned by an active method of its own kind.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN [BillingEntries] t ON t.Id = i.ReplacesEntryId
               WHERE t.AccountId <> i.AccountId OR t.PatientId <> i.PatientId OR t.Kind <> i.Kind OR NOT EXISTS (SELECT 1 FROM [BillingEntries] v WHERE v.ReversesEntryId = t.Id))
        THROW 51162, 'A replacement stands in for a reversed entry of the same account and kind.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN [BillingEntries] t ON t.Id = i.ReversesEntryId WHERE t.Kind = 'Refund')
        THROW 51163, 'A refund cannot be reversed.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_BillingAllocationReleases_Immutable] ON [BillingAllocationReleases] INSTEAD OF UPDATE, DELETE AS
BEGIN
    THROW 51164, 'A release is append-only; it is never changed or deleted.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_BillingAllocationReleases_Links] ON [BillingAllocationReleases] AFTER INSERT AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted i JOIN [BillingAllocations] a ON a.Id = i.AllocationId WHERE a.AccountId <> i.AccountId OR a.PatientId <> i.PatientId)
        THROW 51165, 'A release belongs to the account and patient of the allocation it releases.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN [BillingAllocations] a ON a.Id = i.AllocationId JOIN [BillingEntries] r ON r.Id = i.RefundEntryId
               WHERE r.Kind <> 'Refund' OR r.AccountId <> i.AccountId OR r.RefundsEntryId <> a.PaymentEntryId)
        THROW 51165, 'A release caused by a refund releases an allocation of the payment that was refunded.', 1;
    IF EXISTS (SELECT 1 FROM (SELECT DISTINCT AllocationId FROM inserted) x JOIN [BillingAllocations] a ON a.Id = x.AllocationId
               WHERE (SELECT SUM(rl.Amount) FROM [BillingAllocationReleases] rl WHERE rl.AllocationId = a.Id) > a.Amount)
        THROW 51166, 'A release cannot be for more than its allocation still holds.', 1;
    IF EXISTS (SELECT 1 FROM (SELECT DISTINCT RefundEntryId FROM inserted WHERE RefundEntryId IS NOT NULL) x JOIN [BillingEntries] r ON r.Id = x.RefundEntryId
               WHERE (SELECT SUM(rl.Amount) FROM [BillingAllocationReleases] rl WHERE rl.RefundEntryId = r.Id) > r.Amount)
        THROW 51167, 'The releases of a refund cannot add up to more than the refund.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN [BillingAllocations] a ON a.Id = i.AllocationId WHERE EXISTS (SELECT 1 FROM [BillingEntries] v WHERE v.ReversesEntryId = a.PaymentEntryId))
        THROW 51168, 'A reversed payment''s allocations are already released by its reversal.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_FinancialDocuments_Immutable] ON [FinancialDocuments] INSTEAD OF UPDATE, DELETE AS
BEGIN
    THROW 51169, 'An issued document is append-only; it is never changed or deleted.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_FinancialDocuments_Links] ON [FinancialDocuments] AFTER INSERT AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted i JOIN [BillingAccounts] a ON a.Id = i.AccountId WHERE a.PatientId <> i.PatientId)
        THROW 51170, 'A document carries the patient of its account.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN [BillingEntries] p ON p.Id = i.PaymentEntryId WHERE p.Kind <> 'Payment' OR p.AccountId <> i.AccountId OR p.EntryNumber > i.AsOfEntryNumber)
        THROW 51170, 'A receipt is for a payment of the same account that existed at its cut-off.', 1;
    IF EXISTS (SELECT 1 FROM inserted i
               WHERE i.AsOfEntryNumber > ISNULL((SELECT MAX(e.EntryNumber) FROM [BillingEntries] e WHERE e.AccountId = i.AccountId), 0)
                  OR i.AsOfAllocationNumber > ISNULL((SELECT MAX(a.AllocationNumber) FROM [BillingAllocations] a WHERE a.AccountId = i.AccountId), 0)
                  OR i.AsOfReleaseNumber > ISNULL((SELECT MAX(r.ReleaseNumber) FROM [BillingAllocationReleases] r WHERE r.AccountId = i.AccountId), 0)
                  OR i.AsOfResponsibilitySequence > ISNULL((SELECT MAX(s.Sequence) FROM [BillingResponsibilities] s WHERE s.AccountId = i.AccountId), 0))
        THROW 51171, 'A document cannot be cut off beyond the history that exists.', 1;
END");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // restore the ALV-007-C01 allocation trigger and remove the entry trigger before the new tables go (a rollback is only possible while no refund has been recorded)
            migrationBuilder.Sql("DROP TRIGGER [TR_BillingEntries_Reversals]");
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
            migrationBuilder.Sql("DROP TRIGGER [TR_BillingEntries_Refunds]");
            migrationBuilder.Sql("DROP TRIGGER [TR_BillingAllocations_Links]");
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
            migrationBuilder.DropForeignKey(
                name: "FK_BillingEntries_BillingEntries_RefundsEntryId",
                table: "BillingEntries");

            migrationBuilder.DropForeignKey(
                name: "FK_BillingEntries_BillingEntries_ReplacesEntryId",
                table: "BillingEntries");

            migrationBuilder.DropTable(
                name: "BillingAllocationReleases");

            migrationBuilder.DropTable(
                name: "FinancialDocuments");

            migrationBuilder.DropIndex(
                name: "IX_BillingEntries_RefundsEntryId",
                table: "BillingEntries");

            migrationBuilder.DropIndex(
                name: "IX_BillingEntries_ReplacesEntryId",
                table: "BillingEntries");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BillingEntries_Kind",
                table: "BillingEntries");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BillingEntries_Method",
                table: "BillingEntries");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BillingEntries_PaymentOnly",
                table: "BillingEntries");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BillingEntries_Reason",
                table: "BillingEntries");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BillingEntries_Refunds",
                table: "BillingEntries");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BillingEntries_Replaces",
                table: "BillingEntries");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BillingEntries_Sign",
                table: "BillingEntries");

            migrationBuilder.DropColumn(
                name: "RefundsEntryId",
                table: "BillingEntries");

            migrationBuilder.DropColumn(
                name: "ReplacesEntryId",
                table: "BillingEntries");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BillingEntries_Kind",
                table: "BillingEntries",
                sql: "[Kind] COLLATE Latin1_General_CS_AS IN ('Charge','Payment','Adjustment','Reversal')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BillingEntries_Method",
                table: "BillingEntries",
                sql: "([Kind] = 'Payment' AND [PaymentMethod] IS NOT NULL AND [PaymentMethod] COLLATE Latin1_General_CS_AS IN ('Cash','Check','Card','Other')) OR ([Kind] <> 'Payment' AND [PaymentMethod] IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BillingEntries_PaymentOnly",
                table: "BillingEntries",
                sql: "[Kind] = 'Payment' OR ([PaidByPatientId] IS NULL AND [PaymentMethodId] IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BillingEntries_Reason",
                table: "BillingEntries",
                sql: "([Kind] IN ('Adjustment', 'Reversal') AND [Reason] IS NOT NULL AND LEN(LTRIM(RTRIM([Reason]))) > 0) OR ([Kind] = 'Payment' AND ([Reason] IS NULL OR LEN(LTRIM(RTRIM([Reason]))) > 0)) OR ([Kind] = 'Charge' AND [Reason] IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_BillingEntries_Sign",
                table: "BillingEntries",
                sql: "([Kind] = 'Charge' AND [Amount] > 0) OR ([Kind] = 'Payment' AND [Amount] < 0) OR ([Kind] IN ('Adjustment', 'Reversal') AND [Amount] <> 0)");
        }
    }
}
