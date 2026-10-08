using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Clinical;
using Alveara.Api.Architecture.Odontogram;
using Alveara.Api.Architecture.Procedures;
using Alveara.Api.Architecture.Treatment;

namespace Alveara.Api.Data;

/// <summary>
/// STORY-015: the model for treatment plans, their items and their append-only history. The database itself refuses what <see cref="TreatmentPlanRules"/> refuses (a blank title or key, a fee out of
/// range, a tooth that is not one of the 52 FDI keys, a surface without a tooth, a withdrawn plan or item without who, when and why) and what no rule can see from one row; triggers (created in the
/// migration) refuse deleting a plan or item, changing a plan's patient or origin, editing an item, editing the history, an item whose diagnosis is another patient's or withdrawn, whose procedure is
/// inactive, whose fee is not the fee of the catalog version it names, or whose tooth and surface do not fit the procedure, and an item added to a withdrawn plan. Item numbers are unique per plan (an index); the service hands them out in the order items are added.
/// </summary>
internal static class TreatmentPlanModel
{
    private static string In(IEnumerable<string> values) => string.Join(",", values.Select(v => $"'{v}'"));

    /// <summary>True when the column holds no control character (1 to 31); written so the check is the same whatever the column's collation.</summary>
    private static string NoControl(string column) => $"PATINDEX(N'%[' + NCHAR(1) + N'-' + NCHAR(31) + N']%', [{column}] COLLATE Latin1_General_BIN2) = 0";

    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TreatmentPlan>(e =>
        {
            e.Property(x => x.IdempotencyKey).HasMaxLength(TreatmentPlanRules.KeyMax);
            e.Property(x => x.Title).HasMaxLength(TreatmentPlanRules.TitleMax);
            e.Property(x => x.Status).HasMaxLength(10);
            e.Property(x => x.WithdrawnReason).HasMaxLength(TreatmentPlanRules.ReasonMax);
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasOne<Architecture.Patients.Patient>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.PatientId, x.Status });
            // a retried or double-clicked save with the same key can only ever create one plan
            e.HasIndex(x => new { x.PatientId, x.IdempotencyKey }).IsUnique();
            e.ToTable(t =>
            {
                t.HasTrigger("TR_TreatmentPlans_NoDelete");
                t.HasTrigger("TR_TreatmentPlans_Identity");
                t.HasCheckConstraint("CK_TreatmentPlans_Key", "LEN(LTRIM(RTRIM([IdempotencyKey]))) > 0");
                t.HasCheckConstraint("CK_TreatmentPlans_Title", "LEN(LTRIM(RTRIM([Title]))) > 0");
                t.HasCheckConstraint("CK_TreatmentPlans_TitleLine", NoControl("Title"));
                t.HasCheckConstraint("CK_TreatmentPlans_Status", $"[Status] COLLATE Latin1_General_CS_AS IN ({In(TreatmentPlanStatuses.All)})");
                t.HasCheckConstraint("CK_TreatmentPlans_WithdrawnStamp",
                    "([Status] = 'Withdrawn' AND [WithdrawnAtUtc] IS NOT NULL AND [WithdrawnByUserId] IS NOT NULL AND [WithdrawnReason] IS NOT NULL AND LEN(LTRIM(RTRIM([WithdrawnReason]))) > 0) " +
                    "OR ([Status] = 'Proposed' AND [WithdrawnAtUtc] IS NULL AND [WithdrawnByUserId] IS NULL AND [WithdrawnReason] IS NULL)");
            });
        });

        modelBuilder.Entity<TreatmentPlanItem>(e =>
        {
            e.Property(x => x.IdempotencyKey).HasMaxLength(TreatmentPlanRules.KeyMax);
            e.Property(x => x.ToothKey).HasMaxLength(2).IsFixedLength();
            e.Property(x => x.Surface).HasMaxLength(1).IsFixedLength();
            e.Property(x => x.Fee).HasPrecision(18, 2);
            e.Property(x => x.WithdrawnReason).HasMaxLength(TreatmentPlanRules.ReasonMax);
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasOne<TreatmentPlan>().WithMany().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Diagnosis>().WithMany().HasForeignKey(x => x.DiagnosisId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ProcedureDefinition>().WithMany().HasForeignKey(x => x.ProcedureId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ProcedureVersion>().WithMany().HasForeignKey(x => x.ProcedureVersionId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.PlanId, x.ItemNumber }).IsUnique();
            e.HasIndex(x => new { x.PlanId, x.IdempotencyKey }).IsUnique();
            e.HasIndex(x => x.DiagnosisId);
            e.HasIndex(x => x.ProcedureId);
            e.ToTable(t =>
            {
                t.HasTrigger("TR_TreatmentPlanItems_NoDelete");
                t.HasTrigger("TR_TreatmentPlanItems_Immutable");
                t.HasTrigger("TR_TreatmentPlanItems_Links");
                t.HasCheckConstraint("CK_TreatmentPlanItems_Number", "[ItemNumber] >= 1");
                t.HasCheckConstraint("CK_TreatmentPlanItems_Key", "LEN(LTRIM(RTRIM([IdempotencyKey]))) > 0");
                t.HasCheckConstraint("CK_TreatmentPlanItems_Fee", $"[Fee] >= 0 AND [Fee] <= {ProcedureRules.FeeMax:F2} AND [Fee] = ROUND([Fee], 2)");
                t.HasCheckConstraint("CK_TreatmentPlanItems_ToothKey", $"[ToothKey] IS NULL OR [ToothKey] COLLATE Latin1_General_CS_AS IN ({In(ToothKeys.All)})");
                t.HasCheckConstraint("CK_TreatmentPlanItems_Surface", $"[Surface] IS NULL OR ([ToothKey] IS NOT NULL AND [Surface] COLLATE Latin1_General_CS_AS IN ({In(ToothSurfaces.Any)}))");
                t.HasCheckConstraint("CK_TreatmentPlanItems_WithdrawnStamp",
                    "([WithdrawnAtUtc] IS NULL AND [WithdrawnByUserId] IS NULL AND [WithdrawnReason] IS NULL) " +
                    "OR ([WithdrawnAtUtc] IS NOT NULL AND [WithdrawnByUserId] IS NOT NULL AND [WithdrawnReason] IS NOT NULL AND LEN(LTRIM(RTRIM([WithdrawnReason]))) > 0)");
            });
        });

        modelBuilder.Entity<TreatmentPlanEvent>(e =>
        {
            e.Property(x => x.ChangeType).HasMaxLength(13);
            e.Property(x => x.Title).HasMaxLength(TreatmentPlanRules.TitleMax);
            e.Property(x => x.Reason).HasMaxLength(TreatmentPlanRules.ReasonMax);
            e.HasOne<TreatmentPlan>().WithMany().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.PlanId, x.EventNumber }).IsUnique();
            e.ToTable(t =>
            {
                t.HasTrigger("TR_TreatmentPlanEvents_Immutable");
                t.HasCheckConstraint("CK_TreatmentPlanEvents_Number", "[EventNumber] >= 1");
                t.HasCheckConstraint("CK_TreatmentPlanEvents_ChangeType", $"[ChangeType] COLLATE Latin1_General_CS_AS IN ({In(TreatmentPlanChanges.All)})");
                // an item event names its item; the title is on Created and Renamed only; withdrawing (an item or the plan) always carries the reason
                t.HasCheckConstraint("CK_TreatmentPlanEvents_Item",
                    "([ChangeType] IN ('ItemAdded','ItemWithdrawn') AND [ItemId] IS NOT NULL) OR ([ChangeType] IN ('Created','Renamed','Withdrawn') AND [ItemId] IS NULL)");
                t.HasCheckConstraint("CK_TreatmentPlanEvents_Title",
                    "([ChangeType] IN ('Created','Renamed') AND [Title] IS NOT NULL AND LEN(LTRIM(RTRIM([Title]))) > 0) OR ([ChangeType] NOT IN ('Created','Renamed') AND [Title] IS NULL)");
                t.HasCheckConstraint("CK_TreatmentPlanEvents_Reason",
                    "[ChangeType] NOT IN ('ItemWithdrawn','Withdrawn') OR ([Reason] IS NOT NULL AND LEN(LTRIM(RTRIM([Reason]))) > 0)");
            });
        });
    }
}
