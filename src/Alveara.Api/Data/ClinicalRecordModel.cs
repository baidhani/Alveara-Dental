using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Clinical;

namespace Alveara.Api.Data;

/// <summary>
/// ALV-005-C01: the model for the longitudinal clinical record, vitals, encounter notes and note templates. Kept out of <see cref="AlveraDbContext"/> (already over the
/// size ceiling) so the clinical module's tables are configured in one place. Check constraints are case-sensitive on purpose; the triggers (created in the migration)
/// make history append-only and finalized notes immutable at the database as well as in the application. EF is told about the triggers.
/// </summary>
internal static class ClinicalRecordModel
{
    private const string Kinds = "'MedicalHistory','DentalHistory','Allergy','Medication'";
    private const string NoteSectionList = "'Subjective','Objective','Assessment','Plan','Progress','Treatment'";

    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ClinicalRecordItem>(e =>
        {
            e.Property(x => x.Kind).HasMaxLength(20);
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Detail).HasMaxLength(1000);
            e.Property(x => x.Reaction).HasMaxLength(200);
            e.Property(x => x.Severity).HasMaxLength(10);
            e.Property(x => x.Dose).HasMaxLength(100);
            e.Property(x => x.Frequency).HasMaxLength(100);
            e.Property(x => x.Status).HasMaxLength(12);
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasOne<Architecture.Patients.Patient>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.PatientId, x.Kind });
            // one live item per name within a section of a patient's record (a resolved allergy is changed back, not entered a second time)
            e.HasIndex(x => new { x.PatientId, x.Kind, x.Name }).IsUnique().HasFilter("[RemovedAtUtc] IS NULL");
            e.ToTable(t =>
            {
                t.HasTrigger("TR_ClinicalRecordItems_NoDelete");
                t.HasCheckConstraint("CK_ClinicalRecordItems_Kind", $"[Kind] COLLATE Latin1_General_CS_AS IN ({Kinds})");
                t.HasCheckConstraint("CK_ClinicalRecordItems_Severity", "[Severity] IS NULL OR [Severity] COLLATE Latin1_General_CS_AS IN ('Mild','Moderate','Severe')");
                t.HasCheckConstraint("CK_ClinicalRecordItems_Status",
                    "([Kind] COLLATE Latin1_General_CS_AS = 'Medication' AND [Status] COLLATE Latin1_General_CS_AS IN ('Active','Inactive','Discontinued')) " +
                    "OR ([Kind] COLLATE Latin1_General_CS_AS <> 'Medication' AND [Status] COLLATE Latin1_General_CS_AS IN ('Active','Inactive','Resolved'))");
                t.HasCheckConstraint("CK_ClinicalRecordItems_NameNotBlank", "LEN(LTRIM(RTRIM([Name]))) > 0");
            });
        });
        modelBuilder.Entity<ClinicalRecordItemVersion>(e =>
        {
            e.Property(x => x.ChangeType).HasMaxLength(16);
            e.Property(x => x.Kind).HasMaxLength(20);
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Detail).HasMaxLength(1000);
            e.Property(x => x.Reaction).HasMaxLength(200);
            e.Property(x => x.Severity).HasMaxLength(10);
            e.Property(x => x.Dose).HasMaxLength(100);
            e.Property(x => x.Frequency).HasMaxLength(100);
            e.Property(x => x.Status).HasMaxLength(12);
            e.Property(x => x.Reason).HasMaxLength(500);
            e.HasOne<ClinicalRecordItem>().WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Encounter>().WithMany().HasForeignKey(x => x.EncounterId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.ItemId, x.VersionNumber }).IsUnique();
            e.HasIndex(x => new { x.PatientId, x.OccurredAtUtc });
            e.ToTable(t =>
            {
                t.HasTrigger("TR_ClinicalRecordItemVersions_Immutable");
                t.HasCheckConstraint("CK_ClinicalRecordItemVersions_ChangeType", "[ChangeType] COLLATE Latin1_General_CS_AS IN ('Added','Changed','StatusChanged','RemovedInError')");
            });
        });
        modelBuilder.Entity<ClinicalSectionReview>(e =>
        {
            e.Property(x => x.Section).HasMaxLength(20);
            e.Property(x => x.State).HasMaxLength(12);
            e.HasOne<Architecture.Patients.Patient>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.PatientId, x.Section }).IsUnique();
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_ClinicalSectionReviews_Section", $"[Section] COLLATE Latin1_General_CS_AS IN ({Kinds})");
                t.HasCheckConstraint("CK_ClinicalSectionReviews_State", "[State] COLLATE Latin1_General_CS_AS IN ('Reviewed','NoneKnown','Unknown')");
            });
        });
        modelBuilder.Entity<ClinicalRecordEvent>(e =>
        {
            e.Property(x => x.Section).HasMaxLength(20);
            e.Property(x => x.EventType).HasMaxLength(24);
            e.Property(x => x.Detail).HasMaxLength(400);
            e.HasOne<Architecture.Patients.Patient>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Encounter>().WithMany().HasForeignKey(x => x.EncounterId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.PatientId, x.OccurredAtUtc });
            e.ToTable(t => t.HasTrigger("TR_ClinicalRecordEvents_Immutable"));
        });

        modelBuilder.Entity<EncounterNote>(e =>
        {
            e.Property(x => x.Section).HasMaxLength(20);
            e.Property(x => x.Body).HasMaxLength(8000);
            e.Property(x => x.StarterText).HasMaxLength(2000);
            e.HasOne<Encounter>().WithMany().HasForeignKey(x => x.EncounterId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.EncounterId, x.Section }).IsUnique();
            e.ToTable(t =>
            {
                t.HasTrigger("TR_EncounterNotes_FinalizedImmutable");
                t.HasTrigger("TR_EncounterNotes_NoDelete");
                t.HasCheckConstraint("CK_EncounterNotes_Section", $"[Section] COLLATE Latin1_General_CS_AS IN ({NoteSectionList})");
            });
        });
        modelBuilder.Entity<EncounterVitals>(e =>
        {
            e.ToTable("EncounterVitals");
            e.Property(x => x.TemperatureC).HasPrecision(4, 1);
            e.Property(x => x.WeightKg).HasPrecision(5, 1);
            e.Property(x => x.HeightCm).HasPrecision(4, 1);
            e.Property(x => x.Note).HasMaxLength(200);
            e.Property(x => x.ClientKey).HasMaxLength(100);
            e.Property(x => x.VoidReason).HasMaxLength(500);
            e.HasOne<Encounter>().WithMany().HasForeignKey(x => x.EncounterId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Architecture.Patients.Patient>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.EncounterId, x.ClientKey }).IsUnique();
            e.HasIndex(x => new { x.PatientId, x.MeasuredAtUtc });
            e.ToTable(t =>
            {
                t.HasTrigger("TR_EncounterVitals_FinalizedImmutable");
                t.HasTrigger("TR_EncounterVitals_NoDelete");
                t.HasCheckConstraint("CK_EncounterVitals_AtLeastOneValue",
                    "[SystolicMmHg] IS NOT NULL OR [DiastolicMmHg] IS NOT NULL OR [PulseBpm] IS NOT NULL OR [RespirationsPerMinute] IS NOT NULL OR [TemperatureC] IS NOT NULL " +
                    "OR [OxygenSaturationPercent] IS NOT NULL OR [WeightKg] IS NOT NULL OR [HeightCm] IS NOT NULL");
                t.HasCheckConstraint("CK_EncounterVitals_BloodPressurePair", "([SystolicMmHg] IS NULL AND [DiastolicMmHg] IS NULL) OR ([SystolicMmHg] IS NOT NULL AND [DiastolicMmHg] IS NOT NULL AND [DiastolicMmHg] < [SystolicMmHg])");
                t.HasCheckConstraint("CK_EncounterVitals_VoidStamp",
                    "([VoidedAtUtc] IS NULL AND [VoidedByUserId] IS NULL AND [VoidReason] IS NULL) OR ([VoidedAtUtc] IS NOT NULL AND [VoidedByUserId] IS NOT NULL AND [VoidReason] IS NOT NULL)");
            });
        });

        modelBuilder.Entity<NoteTemplate>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(120);
            e.Property(x => x.Description).HasMaxLength(500);
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasIndex(x => x.Name).IsUnique();
            e.ToTable(t =>
            {
                t.HasTrigger("TR_NoteTemplates_NoDelete");
                t.HasCheckConstraint("CK_NoteTemplates_NameNotBlank", "LEN(LTRIM(RTRIM([Name]))) > 0");
            });
        });
        modelBuilder.Entity<NoteTemplateSection>(e =>
        {
            e.Property(x => x.Section).HasMaxLength(20);
            e.Property(x => x.StarterText).HasMaxLength(2000);
            e.HasOne<NoteTemplate>().WithMany().HasForeignKey(x => x.TemplateId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.TemplateId, x.Section }).IsUnique();
            e.ToTable(t => t.HasCheckConstraint("CK_NoteTemplateSections_Section", $"[Section] COLLATE Latin1_General_CS_AS IN ({NoteSectionList})"));
        });
    }
}
