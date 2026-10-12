using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Billing;

namespace Alveara.Api.Data;

/// <summary>
/// ALV-007-C02: the model for releasing part of an allocation (the append-only negative of an allocation, caused by a refund or a correction) and for the record that a statement or receipt was issued. The database
/// itself refuses what the rules refuse (a release or document that is not what it says it is: a cause without its refund or reason, an amount that is not above zero, a hash that is not a SHA-256, an envelope
/// that is not JSON, a receipt without a payment or a statement with one) and, with triggers created in the migration (error numbers 51157 to 51171), what no single row can show: changing or deleting either
/// table, a release of another account's allocation, one beyond what its allocation still holds, a refund's releases of an allocation of another payment or beyond the refund, a correction of a reversed
/// payment's allocation, a document for another patient's account or payment, and a document cut off beyond the history that exists. Unique indexes allow one release number and one key per account, and
/// one document number per account and type.
/// </summary>
internal static class FinancialDocumentModel
{
    private static string In(IEnumerable<string> values) => TreatmentPlanModel.In(values);
    private static string NoControl(string column) => TreatmentPlanModel.NoControl(column);

    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BillingAllocationRelease>(e =>
        {
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.Property(x => x.Cause).HasMaxLength(10);
            e.Property(x => x.Reason).HasMaxLength(BillingLimits.ReasonMax);
            e.Property(x => x.IdempotencyKey).HasMaxLength(BillingLimits.KeyMax);
            e.HasOne<BillingAccount>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<BillingAllocation>().WithMany().HasForeignKey(x => x.AllocationId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<BillingEntry>().WithMany().HasForeignKey(x => x.RefundEntryId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.AccountId, x.ReleaseNumber }).IsUnique();
            e.HasIndex(x => new { x.AccountId, x.IdempotencyKey });
            e.HasIndex(x => x.AllocationId);
            e.HasIndex(x => x.RefundEntryId).HasFilter("[RefundEntryId] IS NOT NULL");
            e.ToTable(t =>
            {
                t.HasTrigger("TR_BillingAllocationReleases_Immutable");
                t.HasTrigger("TR_BillingAllocationReleases_Links");
                t.HasCheckConstraint("CK_BillingAllocationReleases_Number", "[ReleaseNumber] >= 1");
                t.HasCheckConstraint("CK_BillingAllocationReleases_Key", $"LEN(LTRIM(RTRIM([IdempotencyKey]))) > 0 AND {NoControl("IdempotencyKey")}");
                t.HasCheckConstraint("CK_BillingAllocationReleases_Amount", $"[Amount] > 0 AND [Amount] <= {BillingLimits.AmountMax:F2}");
                t.HasCheckConstraint("CK_BillingAllocationReleases_Cause",
                    $"[Cause] COLLATE Latin1_General_CS_AS IN ({In(ReleaseCauses.All)}) AND " +
                    "(([Cause] = 'Refund' AND [RefundEntryId] IS NOT NULL AND ([Reason] IS NULL OR (LEN(LTRIM(RTRIM([Reason]))) > 0 AND " + NoControl("Reason") + "))) " +
                    "OR ([Cause] = 'Correction' AND [RefundEntryId] IS NULL AND [Reason] IS NOT NULL AND LEN(LTRIM(RTRIM([Reason]))) > 0 AND " + NoControl("Reason") + "))");
            });
        });

        modelBuilder.Entity<FinancialDocument>(e =>
        {
            e.Property(x => x.DocumentType).HasMaxLength(10);
            e.Property(x => x.ContentHash).HasMaxLength(FinancialDocumentLimits.HashLength).IsFixedLength().IsUnicode(false);
            e.Property(x => x.EnvelopeJson).HasMaxLength(FinancialDocumentLimits.EnvelopeMax);
            e.Property(x => x.IdempotencyKey).HasMaxLength(BillingLimits.KeyMax);
            e.HasOne<BillingAccount>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Architecture.Patients.Patient>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<BillingEntry>().WithMany().HasForeignKey(x => x.PaymentEntryId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.AccountId, x.DocumentType, x.DocumentNumber }).IsUnique();
            e.HasIndex(x => new { x.AccountId, x.IdempotencyKey }).IsUnique();
            e.HasIndex(x => x.PaymentEntryId).HasFilter("[PaymentEntryId] IS NOT NULL");
            e.ToTable(t =>
            {
                t.HasTrigger("TR_FinancialDocuments_Immutable");
                t.HasTrigger("TR_FinancialDocuments_Links");
                t.HasCheckConstraint("CK_FinancialDocuments_Type", $"[DocumentType] COLLATE Latin1_General_CS_AS IN ({In(FinancialDocumentTypes.All)})");
                t.HasCheckConstraint("CK_FinancialDocuments_Number", "[DocumentNumber] >= 1");
                t.HasCheckConstraint("CK_FinancialDocuments_Payment", "([DocumentType] = 'Receipt' AND [PaymentEntryId] IS NOT NULL) OR ([DocumentType] = 'Statement' AND [PaymentEntryId] IS NULL)");
                t.HasCheckConstraint("CK_FinancialDocuments_CutOffs", "[AsOfEntryNumber] >= 1 AND [AsOfAllocationNumber] >= 0 AND [AsOfReleaseNumber] >= 0 AND [AsOfResponsibilitySequence] >= 0 AND [TemplateVersion] >= 1");
                t.HasCheckConstraint("CK_FinancialDocuments_Hash", "LEN([ContentHash]) = 64 AND [ContentHash] COLLATE Latin1_General_BIN2 NOT LIKE '%[^0-9a-f]%'");
                t.HasCheckConstraint("CK_FinancialDocuments_Envelope", "ISJSON([EnvelopeJson]) = 1");
                t.HasCheckConstraint("CK_FinancialDocuments_AsOf", "[AsOfUtc] <= [IssuedAtUtc]");
                t.HasCheckConstraint("CK_FinancialDocuments_Key", $"LEN(LTRIM(RTRIM([IdempotencyKey]))) > 0 AND {NoControl("IdempotencyKey")}");
            });
        });
    }
}
