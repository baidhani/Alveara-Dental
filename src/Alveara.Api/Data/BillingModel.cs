using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Billing;
using Alveara.Api.Architecture.Treatment;

namespace Alveara.Api.Data;

/// <summary>
/// STORY-007: the model for billing accounts and their append-only entries. The database itself refuses what the billing rules refuse (an amount that is too large (the column itself holds whole cents), the wrong
/// sign for the kind, a charge without its procedure or a payment without its method, an adjustment without a reason) and, with triggers created in the migration (error numbers 51118 and up),
/// what no single row can show: deleting an account, changing its patient, editing or deleting an entry, an entry whose patient is not the account's, a charge for another patient's, withdrawn or
/// not-completed plan procedure, a charge whose amount is not that procedure's fee, and an entry that would leave the balance below zero. Unique indexes allow one charge per plan procedure,
/// one account per patient, and one entry per key and number within an account.
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
            e.HasIndex(x => new { x.AccountId, x.EntryNumber }).IsUnique();
            e.HasIndex(x => new { x.AccountId, x.IdempotencyKey }).IsUnique();
            // at most one charge per plan procedure (only charges carry a plan procedure)
            e.HasIndex(x => x.PlanItemId).IsUnique().HasFilter("[PlanItemId] IS NOT NULL");
            e.ToTable(t =>
            {
                t.HasTrigger("TR_BillingEntries_Immutable");
                t.HasTrigger("TR_BillingEntries_Links");
                t.HasCheckConstraint("CK_BillingEntries_Number", "[EntryNumber] >= 1");
                t.HasCheckConstraint("CK_BillingEntries_Key", $"LEN(LTRIM(RTRIM([IdempotencyKey]))) > 0 AND {NoControl("IdempotencyKey")}");
                t.HasCheckConstraint("CK_BillingEntries_Kind", $"[Kind] COLLATE Latin1_General_CS_AS IN ({In(BillingKinds.All)})");
                t.HasCheckConstraint("CK_BillingEntries_Max", $"ABS([Amount]) <= {BillingLimits.AmountMax:F2}");
                t.HasCheckConstraint("CK_BillingEntries_Sign",
                    "([Kind] = 'Charge' AND [Amount] > 0) OR ([Kind] = 'Payment' AND [Amount] < 0) OR ([Kind] = 'Adjustment' AND [Amount] <> 0)");
                // only a charge names a plan procedure and carries its code and description
                t.HasCheckConstraint("CK_BillingEntries_ChargeSource",
                    "([Kind] = 'Charge' AND [PlanItemId] IS NOT NULL AND [ProcedureCode] IS NOT NULL AND LEN(LTRIM(RTRIM([ProcedureCode]))) > 0 AND [ProcedureDescription] IS NOT NULL AND LEN(LTRIM(RTRIM([ProcedureDescription]))) > 0) " +
                    "OR ([Kind] <> 'Charge' AND [PlanItemId] IS NULL AND [ProcedureCode] IS NULL AND [ProcedureDescription] IS NULL)");
                t.HasCheckConstraint("CK_BillingEntries_Method",
                    $"([Kind] = 'Payment' AND [PaymentMethod] IS NOT NULL AND [PaymentMethod] COLLATE Latin1_General_CS_AS IN ({In(BillingPaymentMethods.All)})) OR ([Kind] <> 'Payment' AND [PaymentMethod] IS NULL)");
                // an adjustment always says why; a charge carries no note; a payment's note is optional but never blank
                t.HasCheckConstraint("CK_BillingEntries_Reason",
                    "([Kind] = 'Adjustment' AND [Reason] IS NOT NULL AND LEN(LTRIM(RTRIM([Reason]))) > 0) " +
                    "OR ([Kind] = 'Payment' AND ([Reason] IS NULL OR LEN(LTRIM(RTRIM([Reason]))) > 0)) OR ([Kind] = 'Charge' AND [Reason] IS NULL)");
            });
        });
    }
}
