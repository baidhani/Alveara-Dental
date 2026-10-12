using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Billing;
using Alveara.Api.Architecture.Treatment;

namespace Alveara.Api.Data;

/// <summary>
/// STORY-007 and ALV-007-C01: the model for billing accounts, their append-only entries, how payments are allocated to charges, who is responsible for an account and the payment methods. The database itself
/// refuses what the billing rules refuse (an amount that is too large, the wrong sign for the kind, a charge without its procedure, a payment without its method, an adjustment or reversal without a reason, a
/// reversal that does not point at an entry) and, with triggers created in the migrations (error numbers 51118 and up), what no single row can show: deleting an account, changing its patient, editing or deleting
/// an entry, an allocation or a responsibility, an entry whose patient is not the account's, a charge for another patient's, withdrawn or not-completed plan procedure, a charge that is not the procedure's fee,
/// an entry that would leave the balance below zero, a reversal that is not exactly the opposite of an entry of the same account (or that undoes a reversal, or a charge still being paid), an allocation of more
/// than a payment or a charge holds, and a payer who is not responsible for the account. Unique indexes allow one charge per plan procedure, one reversal per entry, one account per patient, and one entry per key
/// and number within an account.
/// </summary>
internal static class BillingModel
{
    private static string In(IEnumerable<string> values) => TreatmentPlanModel.In(values);
    private static string NoControl(string column) => TreatmentPlanModel.NoControl(column);

    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BillingAccount>(e =>
        {
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasOne<Architecture.Patients.Patient>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.PatientId).IsUnique();
            e.ToTable(t =>
            {
                t.HasTrigger("TR_BillingAccounts_NoDelete");
                t.HasTrigger("TR_BillingAccounts_Identity");
            });
        });

        modelBuilder.Entity<BillingEntry>(e =>
        {
            e.Property(x => x.Kind).HasMaxLength(10);
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.Property(x => x.ProcedureCode).HasMaxLength(BillingLimits.CodeMax);
            e.Property(x => x.ProcedureDescription).HasMaxLength(BillingLimits.DescriptionMax);
            e.Property(x => x.PaymentMethod).HasMaxLength(10);
            e.Property(x => x.Reason).HasMaxLength(BillingLimits.ReasonMax);
            e.Property(x => x.IdempotencyKey).HasMaxLength(BillingLimits.KeyMax);
            e.HasOne<BillingAccount>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<TreatmentPlanItem>().WithMany().HasForeignKey(x => x.PlanItemId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<BillingEntry>().WithMany().HasForeignKey(x => x.ReversesEntryId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Architecture.Patients.Patient>().WithMany().HasForeignKey(x => x.PaidByPatientId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<BillingPaymentMethod>().WithMany().HasForeignKey(x => x.PaymentMethodId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<BillingEntry>().WithMany().HasForeignKey(x => x.RefundsEntryId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<BillingEntry>().WithMany().HasForeignKey(x => x.ReplacesEntryId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.AccountId, x.EntryNumber }).IsUnique();
            e.HasIndex(x => new { x.AccountId, x.IdempotencyKey }).IsUnique();
            // at most one charge per plan procedure (only charges carry a plan procedure), and an entry is reversed at most once (only reversals carry the entry they undo)
            e.HasIndex(x => x.PlanItemId).IsUnique().HasFilter("[PlanItemId] IS NOT NULL");
            e.HasIndex(x => x.ReversesEntryId).IsUnique().HasFilter("[ReversesEntryId] IS NOT NULL");
            e.HasIndex(x => x.CorrelationId).HasFilter("[CorrelationId] IS NOT NULL");
            // ALV-007-C02: a payment is refunded in parts, so many refunds may name it; an entry is replaced at most once
            e.HasIndex(x => x.RefundsEntryId).HasFilter("[RefundsEntryId] IS NOT NULL");
            e.HasIndex(x => x.ReplacesEntryId).IsUnique().HasFilter("[ReplacesEntryId] IS NOT NULL");
            e.ToTable(t =>
            {
                t.HasTrigger("TR_BillingEntries_Immutable");
                t.HasTrigger("TR_BillingEntries_Links");
                t.HasTrigger("TR_BillingEntries_Reversals");
                t.HasTrigger("TR_BillingEntries_Refunds");
                t.HasCheckConstraint("CK_BillingEntries_Number", "[EntryNumber] >= 1");
                t.HasCheckConstraint("CK_BillingEntries_Key", $"LEN(LTRIM(RTRIM([IdempotencyKey]))) > 0 AND {NoControl("IdempotencyKey")}");
                t.HasCheckConstraint("CK_BillingEntries_Kind", $"[Kind] COLLATE Latin1_General_CS_AS IN ({In(BillingKinds.All)})");
                t.HasCheckConstraint("CK_BillingEntries_Max", $"ABS([Amount]) <= {BillingLimits.AmountMax:F2}");
                t.HasCheckConstraint("CK_BillingEntries_Sign",
                    "([Kind] IN ('Charge', 'Refund') AND [Amount] > 0) OR ([Kind] = 'Payment' AND [Amount] < 0) OR ([Kind] IN ('Adjustment', 'Reversal') AND [Amount] <> 0)");
                // only a charge names a plan procedure and carries its code and description
                t.HasCheckConstraint("CK_BillingEntries_ChargeSource",
                    "([Kind] = 'Charge' AND [PlanItemId] IS NOT NULL AND [ProcedureCode] IS NOT NULL AND LEN(LTRIM(RTRIM([ProcedureCode]))) > 0 AND [ProcedureDescription] IS NOT NULL AND LEN(LTRIM(RTRIM([ProcedureDescription]))) > 0) " +
                    "OR ([Kind] <> 'Charge' AND [PlanItemId] IS NULL AND [ProcedureCode] IS NULL AND [ProcedureDescription] IS NULL)");
                t.HasCheckConstraint("CK_BillingEntries_Method",
                    $"([Kind] IN ('Payment', 'Refund') AND [PaymentMethod] IS NOT NULL AND [PaymentMethod] COLLATE Latin1_General_CS_AS IN ({In(BillingPaymentMethods.All)})) OR ([Kind] NOT IN ('Payment', 'Refund') AND [PaymentMethod] IS NULL)");
                // an adjustment and a reversal always say why; a charge carries no note; a payment's note is optional but never blank
                t.HasCheckConstraint("CK_BillingEntries_Reason",
                    "([Kind] IN ('Adjustment', 'Reversal', 'Refund') AND [Reason] IS NOT NULL AND LEN(LTRIM(RTRIM([Reason]))) > 0) " +
                    "OR ([Kind] = 'Payment' AND ([Reason] IS NULL OR LEN(LTRIM(RTRIM([Reason]))) > 0)) OR ([Kind] = 'Charge' AND [Reason] IS NULL)");
                // ALV-007-C01: a reversal names the entry it undoes and nothing else does; who paid and which configured method are about payments only
                t.HasCheckConstraint("CK_BillingEntries_Reverses", "([Kind] = 'Reversal' AND [ReversesEntryId] IS NOT NULL) OR ([Kind] <> 'Reversal' AND [ReversesEntryId] IS NULL)");
                // ALV-007-C02: who paid is about payments only (a refund goes back to the recorded payer, derived from the payment, never chosen); the configured method is about a payment or a refund (a refund always names one)
                t.HasCheckConstraint("CK_BillingEntries_PaymentOnly", "([Kind] = 'Payment' OR [PaidByPatientId] IS NULL) AND ([Kind] IN ('Payment', 'Refund') OR [PaymentMethodId] IS NULL) AND ([Kind] <> 'Refund' OR [PaymentMethodId] IS NOT NULL)");
                t.HasCheckConstraint("CK_BillingEntries_Refunds", "([Kind] = 'Refund' AND [RefundsEntryId] IS NOT NULL) OR ([Kind] <> 'Refund' AND [RefundsEntryId] IS NULL)");
                t.HasCheckConstraint("CK_BillingEntries_Replaces", "[ReplacesEntryId] IS NULL OR [Kind] IN ('Payment', 'Adjustment')");
            });
        });

        modelBuilder.Entity<BillingAllocation>(e =>
        {
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.Property(x => x.IdempotencyKey).HasMaxLength(BillingLimits.KeyMax);
            e.HasOne<BillingAccount>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<BillingEntry>().WithMany().HasForeignKey(x => x.PaymentEntryId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<BillingEntry>().WithMany().HasForeignKey(x => x.ChargeEntryId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.AccountId, x.AllocationNumber }).IsUnique();
            e.HasIndex(x => new { x.AccountId, x.IdempotencyKey });
            e.HasIndex(x => x.PaymentEntryId);
            e.HasIndex(x => x.ChargeEntryId);
            e.ToTable(t =>
            {
                t.HasTrigger("TR_BillingAllocations_Immutable");
                t.HasTrigger("TR_BillingAllocations_Links");
                t.HasCheckConstraint("CK_BillingAllocations_Number", "[AllocationNumber] >= 1");
                t.HasCheckConstraint("CK_BillingAllocations_Key", $"LEN(LTRIM(RTRIM([IdempotencyKey]))) > 0 AND {NoControl("IdempotencyKey")}");
                t.HasCheckConstraint("CK_BillingAllocations_Amount", $"[Amount] > 0 AND [Amount] <= {BillingLimits.AmountMax:F2}");
            });
        });

        modelBuilder.Entity<BillingResponsibility>(e =>
        {
            e.Property(x => x.Reason).HasMaxLength(BillingLimits.ReasonMax);
            e.HasOne<BillingAccount>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Architecture.Patients.Patient>().WithMany().HasForeignKey(x => x.ResponsiblePatientId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.AccountId, x.Sequence }).IsUnique();
            e.HasIndex(x => x.ResponsiblePatientId);
            e.ToTable(t =>
            {
                t.HasTrigger("TR_BillingResponsibilities_Immutable");
                t.HasTrigger("TR_BillingResponsibilities_Links");
                t.HasCheckConstraint("CK_BillingResponsibilities_Sequence", "[Sequence] >= 1");
                t.HasCheckConstraint("CK_BillingResponsibilities_Reason", $"[Reason] IS NULL OR (LEN(LTRIM(RTRIM([Reason]))) > 0 AND {NoControl("Reason")})");
            });
        });

        modelBuilder.Entity<BillingPaymentMethod>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(BillingLimits.MethodNameMax);
            e.Property(x => x.Kind).HasMaxLength(10);
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasIndex(x => x.Name).IsUnique();
            e.ToTable(t =>
            {
                t.HasTrigger("TR_BillingPaymentMethods_NoDelete");
                t.HasTrigger("TR_BillingPaymentMethods_Identity");
                t.HasCheckConstraint("CK_BillingPaymentMethods_Name", $"LEN(LTRIM(RTRIM([Name]))) > 0 AND {NoControl("Name")}");
                t.HasCheckConstraint("CK_BillingPaymentMethods_Kind", $"[Kind] COLLATE Latin1_General_CS_AS IN ({In(BillingPaymentMethods.All)})");
                t.HasCheckConstraint("CK_BillingPaymentMethods_BuiltInActive", "[IsBuiltIn] = 0 OR [IsActive] = 1");
            });
            var seeded = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
            e.HasData(
                new BillingPaymentMethod { Id = BillingPaymentMethodIds.Cash, Name = BillingPaymentMethods.Cash, Kind = BillingPaymentMethods.Cash, IsBuiltIn = true, IsActive = true, CreatedAtUtc = seeded },
                new BillingPaymentMethod { Id = BillingPaymentMethodIds.Check, Name = BillingPaymentMethods.Check, Kind = BillingPaymentMethods.Check, IsBuiltIn = true, IsActive = true, CreatedAtUtc = seeded },
                new BillingPaymentMethod { Id = BillingPaymentMethodIds.Card, Name = BillingPaymentMethods.Card, Kind = BillingPaymentMethods.Card, IsBuiltIn = true, IsActive = true, CreatedAtUtc = seeded },
                new BillingPaymentMethod { Id = BillingPaymentMethodIds.Other, Name = BillingPaymentMethods.Other, Kind = BillingPaymentMethods.Other, IsBuiltIn = true, IsActive = true, CreatedAtUtc = seeded });
        });
    }
}
