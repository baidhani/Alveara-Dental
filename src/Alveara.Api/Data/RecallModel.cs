using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Patients;
using Alveara.Api.Architecture.Recall;

namespace Alveara.Api.Data;

/// <summary>
/// ALV-009-C01: the model for the practice's recall settings (one row, seeded with recall OFF and the defaults of <see cref="RecallRules"/>) and each patient's own recall plan. The numbers are held to the same limits
/// the code uses; the settings row is never deleted and there is only ever one (error 51214); a patient's plan means something (an interval, an opt-out or both).
/// </summary>
internal static class RecallModel
{
    private static readonly DateTimeOffset SeededAt = new(2026, 10, 24, 0, 0, 0, TimeSpan.Zero);

    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RecallSettings>(e =>
        {
            e.ToTable("RecallSettings");
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.UpdatedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t =>
            {
                t.HasTrigger("TR_RecallSettings_Guard");
                t.HasCheckConstraint("CK_RecallSettings_OneRow", "[Id] = 1");
                t.HasCheckConstraint("CK_RecallSettings_Interval", $"[DefaultIntervalDays] >= {RecallRules.MinIntervalDays} AND [DefaultIntervalDays] <= {RecallRules.MaxIntervalDays}");
                t.HasCheckConstraint("CK_RecallSettings_Lead", $"[LeadDays] >= 0 AND [LeadDays] <= {RecallRules.MaxLeadDays}");
                t.HasCheckConstraint("CK_RecallSettings_VisitAge", $"[MaxVisitAgeMonths] >= 1 AND [MaxVisitAgeMonths] <= {RecallRules.MaxVisitAgeMonthsLimit}");
                t.HasCheckConstraint("CK_RecallSettings_DueSoon", "[DueSoonDays] >= 1 AND [DueSoonDays] <= 30");
            });
            e.HasData(new RecallSettings
            {
                Id = 1, Enabled = false, DefaultIntervalDays = RecallRules.DefaultIntervalDays, LeadDays = RecallRules.DefaultLeadDays, MaxVisitAgeMonths = RecallRules.DefaultMaxVisitAgeMonths, DueSoonDays = 7, UpdatedAtUtc = SeededAt,
            });
        });

        modelBuilder.Entity<PatientRecallPlan>(e =>
        {
            e.ToTable("PatientRecallPlans");
            e.HasKey(x => x.PatientId);
            e.Property(x => x.PatientId).ValueGeneratedNever();
            e.Property(x => x.Reason).HasMaxLength(200);
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasOne<Patient>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.UpdatedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_PatientRecallPlans_Interval", $"[IntervalDays] IS NULL OR ([IntervalDays] >= {RecallRules.MinIntervalDays} AND [IntervalDays] <= {RecallRules.MaxIntervalDays})");
                t.HasCheckConstraint("CK_PatientRecallPlans_Means", "[OptedOut] = 1 OR [IntervalDays] IS NOT NULL");
                t.HasCheckConstraint("CK_PatientRecallPlans_Reason", $"[Reason] IS NULL OR ({NotificationModel.NotBlank("Reason")} AND {NotificationModel.NoNul("Reason")} AND {PrescriptionModel.NoControl("Reason")})");
                t.HasCheckConstraint("CK_PatientRecallPlans_Time", "[UpdatedAtUtc] >= '2000-01-01' AND [UpdatedAtUtc] < '2200-01-01'");
            });
        });
    }
}
