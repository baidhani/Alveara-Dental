using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Alveara.Api.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// ALV-N024 (owner decisions PD-17, PD-18, OQ-1, OQ-4; briefing revision 2 approved by review BRIEFING-R02):
    /// - FinancialDocuments gains ReplacesDocumentId and ReplacementReason (set only on a NEW replacement receipt; the original row is never changed) with a CHECK, a self foreign key and a
    ///   filtered unique index (a document is replaced at most once).
    /// - TR_FinancialDocuments_Links is re-created: for every receipt inserted it takes the transaction-owned application lock alveara.receipt:{payment} (51297 if not granted within 5 s;
    ///   the service already holds it, so on that path it does not wait) and then, reading with READCOMMITTEDLOCK (the database runs READ_COMMITTED_SNAPSHOT, and a snapshot read taken before
    ///   the wait could miss a receipt committed meanwhile): a replacement belongs to the same account, type and payment as the receipt it replaces (51294); a receipt for a payment that already
    ///   has another receipt nobody has replaced must replace the latest such receipt (51295; the row being inserted is excluded when looking for that prior receipt).
    /// - TR_BillingEntries_Reversals is re-created with the payer rule (51142) set to the service's: the patient, or an ACTIVE patient who is recorded as responsible for the account, the
    ///   patient's registered guarantor or a member of the patient's recorded household (R02, CR-1), as read at the moment of the insert. Only inserted rows are checked, so stored rows are never re-judged.
    /// No existing row is changed. Down is refused (51296) once any replacement receipt exists, so no replacement history is lost; otherwise it restores both triggers exactly.
    /// </summary>
    public partial class AddReceiptReplacementAndPayerEligibility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReplacementReason",
                table: "FinancialDocuments",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReplacesDocumentId",
                table: "FinancialDocuments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinancialDocuments_ReplacesDocumentId",
                table: "FinancialDocuments",
                column: "ReplacesDocumentId",
                unique: true,
                filter: "[ReplacesDocumentId] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_FinancialDocuments_Replacement",
                table: "FinancialDocuments",
                sql: "([ReplacesDocumentId] IS NULL AND [ReplacementReason] IS NULL) OR ([ReplacesDocumentId] IS NOT NULL AND [DocumentType] = 'Receipt' AND [ReplacementReason] IS NOT NULL AND LEN(LTRIM(RTRIM([ReplacementReason]))) > 0 AND PATINDEX(N'%[' + NCHAR(1) + N'-' + NCHAR(31) + N']%', [ReplacementReason] COLLATE Latin1_General_BIN2) = 0)");

            migrationBuilder.AddForeignKey(
                name: "FK_FinancialDocuments_FinancialDocuments_ReplacesDocumentId",
                table: "FinancialDocuments",
                column: "ReplacesDocumentId",
                principalTable: "FinancialDocuments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql("DROP TRIGGER [TR_FinancialDocuments_Links]");
            migrationBuilder.Sql(LinksTriggerWithReceiptRules);
            migrationBuilder.Sql("DROP TRIGGER [TR_BillingEntries_Reversals]");
            migrationBuilder.Sql(ReversalsTrigger(WidenedPayerRule));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"IF EXISTS (SELECT 1 FROM [FinancialDocuments] WHERE [ReplacesDocumentId] IS NOT NULL)
    THROW 51296, 'Replacement receipts have been issued, so this migration cannot be rolled back.', 1;");
            migrationBuilder.Sql("DROP TRIGGER [TR_BillingEntries_Reversals]");
            migrationBuilder.Sql(ReversalsTrigger(OriginalPayerRule));
            migrationBuilder.Sql("DROP TRIGGER [TR_FinancialDocuments_Links]");
            migrationBuilder.Sql(OriginalLinksTrigger);

            migrationBuilder.DropForeignKey(
                name: "FK_FinancialDocuments_FinancialDocuments_ReplacesDocumentId",
                table: "FinancialDocuments");

            migrationBuilder.DropIndex(
                name: "IX_FinancialDocuments_ReplacesDocumentId",
                table: "FinancialDocuments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_FinancialDocuments_Replacement",
                table: "FinancialDocuments");

            migrationBuilder.DropColumn(
                name: "ReplacementReason",
                table: "FinancialDocuments");

            migrationBuilder.DropColumn(
                name: "ReplacesDocumentId",
                table: "FinancialDocuments");
        }

        // The three checks of ALV-007-C02 (51170, 51171), unchanged; shared by both versions of the trigger.
        private const string OriginalLinkChecks = @"
    IF EXISTS (SELECT 1 FROM inserted i JOIN [BillingAccounts] a ON a.Id = i.AccountId WHERE a.PatientId <> i.PatientId)
        THROW 51170, 'A document carries the patient of its account.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN [BillingEntries] p ON p.Id = i.PaymentEntryId WHERE p.Kind <> 'Payment' OR p.AccountId <> i.AccountId OR p.EntryNumber > i.AsOfEntryNumber)
        THROW 51170, 'A receipt is for a payment of the same account that existed at its cut-off.', 1;
    IF EXISTS (SELECT 1 FROM inserted i
               WHERE i.AsOfEntryNumber > ISNULL((SELECT MAX(e.EntryNumber) FROM [BillingEntries] e WHERE e.AccountId = i.AccountId), 0)
                  OR i.AsOfAllocationNumber > ISNULL((SELECT MAX(a.AllocationNumber) FROM [BillingAllocations] a WHERE a.AccountId = i.AccountId), 0)
                  OR i.AsOfReleaseNumber > ISNULL((SELECT MAX(r.ReleaseNumber) FROM [BillingAllocationReleases] r WHERE r.AccountId = i.AccountId), 0)
                  OR i.AsOfResponsibilitySequence > ISNULL((SELECT MAX(s.Sequence) FROM [BillingResponsibilities] s WHERE s.AccountId = i.AccountId), 0))
        THROW 51171, 'A document cannot be cut off beyond the history that exists.', 1;";

        private const string OriginalLinksTrigger = @"CREATE TRIGGER [TR_FinancialDocuments_Links] ON [FinancialDocuments] AFTER INSERT AS
BEGIN" + OriginalLinkChecks + @"
END";

        private const string LinksTriggerWithReceiptRules = @"CREATE TRIGGER [TR_FinancialDocuments_Links] ON [FinancialDocuments] AFTER INSERT AS
BEGIN
    SET NOCOUNT ON;" + OriginalLinkChecks + @"
    IF EXISTS (SELECT 1 FROM inserted i JOIN [FinancialDocuments] d ON d.Id = i.ReplacesDocumentId
               WHERE d.AccountId <> i.AccountId OR d.DocumentType <> i.DocumentType OR d.PaymentEntryId IS NULL OR i.PaymentEntryId IS NULL OR d.PaymentEntryId <> i.PaymentEntryId)
        THROW 51294, 'A replacement receipt is for the same account and payment as the receipt it replaces.', 1;

    -- One payment at a time: the lock is owned by the transaction and released when it ends (commit or rollback).
    DECLARE @payments TABLE (PaymentEntryId uniqueidentifier PRIMARY KEY);
    INSERT INTO @payments SELECT DISTINCT i.PaymentEntryId FROM inserted i WHERE i.DocumentType = 'Receipt' AND i.PaymentEntryId IS NOT NULL;
    DECLARE @payment uniqueidentifier, @resource nvarchar(255), @r int, @latest uniqueidentifier;
    WHILE EXISTS (SELECT 1 FROM @payments)
    BEGIN
        SELECT TOP 1 @payment = PaymentEntryId FROM @payments ORDER BY PaymentEntryId;
        DELETE FROM @payments WHERE PaymentEntryId = @payment;
        SET @resource = N'alveara.receipt:' + LOWER(REPLACE(CONVERT(nvarchar(36), @payment), N'-', N''));
        EXEC @r = sp_getapplock @Resource = @resource, @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 5000;
        IF @r < 0
            THROW 51297, 'Receipts are being issued for this payment right now. Try again.', 1;
        IF (SELECT COUNT(*) FROM inserted i WHERE i.DocumentType = 'Receipt' AND i.PaymentEntryId = @payment) > 1
            THROW 51295, 'A payment has one receipt; a further receipt must replace the latest one.', 1;
        -- the latest OTHER receipt of this payment that nobody else has replaced (the row being inserted is excluded on both sides)
        SET @latest = (SELECT TOP 1 d.Id FROM [FinancialDocuments] d WITH (READCOMMITTEDLOCK)
                       WHERE d.PaymentEntryId = @payment AND d.DocumentType = 'Receipt' AND NOT EXISTS (SELECT 1 FROM inserted x WHERE x.Id = d.Id)
                         AND NOT EXISTS (SELECT 1 FROM [FinancialDocuments] r WITH (READCOMMITTEDLOCK) WHERE r.ReplacesDocumentId = d.Id AND NOT EXISTS (SELECT 1 FROM inserted y WHERE y.Id = r.Id))
                       ORDER BY d.DocumentNumber DESC);
        IF EXISTS (SELECT 1 FROM inserted i WHERE i.DocumentType = 'Receipt' AND i.PaymentEntryId = @payment
                   AND ((@latest IS NOT NULL AND (i.ReplacesDocumentId IS NULL OR i.ReplacesDocumentId <> @latest)) OR (@latest IS NULL AND i.ReplacesDocumentId IS NOT NULL)))
            THROW 51295, 'A payment has one receipt; a further receipt must replace the latest one.', 1;
    END
END";

        private const string OriginalPayerRule = @"
    IF EXISTS (SELECT 1 FROM inserted i WHERE i.PaidByPatientId IS NOT NULL AND i.PaidByPatientId <> i.PatientId
               AND NOT EXISTS (SELECT 1 FROM [BillingResponsibilities] r WHERE r.AccountId = i.AccountId AND r.ResponsiblePatientId = i.PaidByPatientId))
        THROW 51142, 'A payment is made by the patient or by someone recorded as responsible for the account.', 1;";

        // ALV-N024 R02 (CR-1, owner decision 2026-11-19 "active only"): exactly the service's set - besides the patient, a payer must be an ACTIVE patient who is recorded as responsible for the
        // account, the patient's registered guarantor, or a member of the patient's recorded household. (The rule of ALV-007-C01 accepted an inactive recorded-responsible party; it no longer does.)
        private const string WidenedPayerRule = @"
    IF EXISTS (SELECT 1 FROM inserted i WHERE i.PaidByPatientId IS NOT NULL AND i.PaidByPatientId <> i.PatientId
               AND (NOT EXISTS (SELECT 1 FROM [Patients] a WHERE a.Id = i.PaidByPatientId AND a.IsActive = 1)
                    OR (NOT EXISTS (SELECT 1 FROM [BillingResponsibilities] r WHERE r.AccountId = i.AccountId AND r.ResponsiblePatientId = i.PaidByPatientId)
                        AND NOT EXISTS (SELECT 1 FROM [Patients] p WHERE p.Id = i.PatientId AND p.GuarantorPatientId = i.PaidByPatientId)
                        AND NOT EXISTS (SELECT 1 FROM [Patients] p JOIN [Patients] q ON q.HouseholdId = p.HouseholdId WHERE p.Id = i.PatientId AND p.HouseholdId IS NOT NULL AND q.Id = i.PaidByPatientId))))
        THROW 51142, 'A payment is made by the patient, or by an active guarantor, household member or person recorded as responsible for the account.', 1;";

        // ALV-007-C02's reversal trigger with the payer rule passed in; every other check is unchanged.
        private static string ReversalsTrigger(string payerRule) => @"CREATE TRIGGER [TR_BillingEntries_Reversals] ON [BillingEntries] AFTER INSERT AS
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
        THROW 51141, 'A charge that payments are applied to cannot be reversed until those payments are.', 1;" + payerRule + @"
    IF EXISTS (SELECT 1 FROM inserted i JOIN [BillingPaymentMethods] m ON m.Id = i.PaymentMethodId WHERE m.Kind <> i.PaymentMethod OR m.IsActive = 0)
        THROW 51143, 'A payment uses an active method of its own kind.', 1;
END";
    }
}
