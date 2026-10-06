using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Clinical;
using Alveara.Api.Architecture.Odontogram;

namespace Alveara.Api.Data;

/// <summary>
/// STORY-013: the model for diagnoses and their append-only history. The database itself refuses what <see cref="DiagnosisRules"/> refuses (a blank label, a tooth that is not one of the 52 FDI keys, a
/// blank treatment-plan reference, control characters in a line of text, a withdrawn diagnosis without who, when and why), and what no rule can see from one row: an encounter that is not the
/// patient's, and any change to a diagnosis's patient, encounter or origin. Triggers (created in the migration) refuse deleting a diagnosis and any edit of its history. The treatment-plan
/// reference is plain text with no foreign key, by design (plans do not exist yet).
/// </summary>
internal static class DiagnosisModel
{
    private static string In(IEnumerable<string> values) => string.Join(",", values.Select(v => $"'{v}'"));

    /// <summary>True when the column holds no control character (1 to 31); written so the check is the same whatever the column's collation.</summary>
    private static string NoControl(string column) => $"PATINDEX(N'%[' + NCHAR(1) + N'-' + NCHAR(31) + N']%', [{column}] COLLATE Latin1_General_BIN2) = 0";

    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Diagnosis>(e =>
        {
            e.Property(x => x.IdempotencyKey).HasMaxLength(64);
            e.Property(x => x.Label).HasMaxLength(DiagnosisRules.LabelMax);
            e.Property(x => x.ToothKey).HasMaxLength(2).IsFixedLength();
            e.Property(x => x.Notes).HasMaxLength(DiagnosisRules.NotesMax);
            e.Property(x => x.TreatmentPlanReference).HasMaxLength(DiagnosisRules.ReferenceMax);
            e.Property(x => x.Status).HasMaxLength(10);
            e.Property(x => x.WithdrawnReason).HasMaxLength(500);
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasOne<Architecture.Patients.Patient>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Encounter>().WithMany().HasForeignKey(x => x.EncounterId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.PatientId, x.Status });
            e.HasIndex(x => x.EncounterId);
            // a retried or double-clicked save with the same key can only ever create one diagnosis
            e.HasIndex(x => new { x.PatientId, x.IdempotencyKey }).IsUnique();
            e.ToTable(t =>
            {
                t.HasTrigger("TR_Diagnoses_NoDelete");
                t.HasTrigger("TR_Diagnoses_Links");
                t.HasCheckConstraint("CK_Diagnoses_Key", "LEN(LTRIM(RTRIM([IdempotencyKey]))) > 0");
                t.HasCheckConstraint("CK_Diagnoses_Label", "LEN(LTRIM(RTRIM([Label]))) > 0");
                t.HasCheckConstraint("CK_Diagnoses_LabelLine", NoControl("Label"));
                t.HasCheckConstraint("CK_Diagnoses_ToothKey", $"[ToothKey] IS NULL OR [ToothKey] COLLATE Latin1_General_CS_AS IN ({In(ToothKeys.All)})");
                t.HasCheckConstraint("CK_Diagnoses_PlanReference", "[TreatmentPlanReference] IS NULL OR LEN(LTRIM(RTRIM([TreatmentPlanReference]))) > 0");
                t.HasCheckConstraint("CK_Diagnoses_PlanReferenceLine", $"[TreatmentPlanReference] IS NULL OR {NoControl("TreatmentPlanReference")}");
                t.HasCheckConstraint("CK_Diagnoses_Status", $"[Status] COLLATE Latin1_General_CS_AS IN ({In(DiagnosisStatuses.All)})");
                t.HasCheckConstraint("CK_Diagnoses_WithdrawnStamp",
                    "([Status] = 'Withdrawn' AND [WithdrawnAtUtc] IS NOT NULL AND [WithdrawnByUserId] IS NOT NULL AND [WithdrawnReason] IS NOT NULL AND LEN(LTRIM(RTRIM([WithdrawnReason]))) > 0) " +
                    "OR ([Status] = 'Active' AND [WithdrawnAtUtc] IS NULL AND [WithdrawnByUserId] IS NULL AND [WithdrawnReason] IS NULL)");
            });
        });

        modelBuilder.Entity<DiagnosisVersion>(e =>
        {
            e.Property(x => x.ChangeType).HasMaxLength(10);
            e.Property(x => x.Label).HasMaxLength(DiagnosisRules.LabelMax);
            e.Property(x => x.ToothKey).HasMaxLength(2).IsFixedLength();
            e.Property(x => x.Notes).HasMaxLength(DiagnosisRules.NotesMax);
            e.Property(x => x.TreatmentPlanReference).HasMaxLength(DiagnosisRules.ReferenceMax);
            e.Property(x => x.Status).HasMaxLength(10);
            e.Property(x => x.Reason).HasMaxLength(500);
            e.HasOne<Diagnosis>().WithMany().HasForeignKey(x => x.DiagnosisId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.DiagnosisId, x.VersionNumber }).IsUnique();
            e.HasIndex(x => x.PatientId);
            e.ToTable(t =>
            {
                t.HasTrigger("TR_DiagnosisVersions_Immutable");
                t.HasCheckConstraint("CK_DiagnosisVersions_ChangeType", $"[ChangeType] COLLATE Latin1_General_CS_AS IN ({In(DiagnosisChangeTypes.All)})");
                t.HasCheckConstraint("CK_DiagnosisVersions_Status", $"[Status] COLLATE Latin1_General_CS_AS IN ({In(DiagnosisStatuses.All)})");
            });
        });
    }
}
