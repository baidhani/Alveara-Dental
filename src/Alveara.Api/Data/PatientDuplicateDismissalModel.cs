using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Patients;
using Alveara.Api.Architecture.Patients.Merge;

namespace Alveara.Api.Data;

/// <summary>
/// ALV-017-C01: the model for pairs an administrator dismissed as not a duplicate. As elsewhere, the database refuses by itself what the rules refuse. Triggers (created in the migration; errors 51240 to 51243):
/// a dismissal is never deleted (51240); it keeps its pair, reason, who and when, and can only be restored, once (51241); a record that was merged away cannot be dismissed (51242); the migration refuses to roll back
/// while any dismissal exists (51243). One dismissal in force per pair, in either order, through a unique index on the computed <c>PairKey</c> over the rows not yet restored.
/// </summary>
internal static class PatientDuplicateDismissalModel
{
    private const string Utc = "'2000-01-01'";
    private const string Never = "'2200-01-01'";

    private static string Text(string column) => $"{NotificationModel.NotBlank(column)} AND {NotificationModel.NoNul(column)} AND {PrescriptionModel.NoControl(column)}";

    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PatientDuplicateDismissal>(e =>
        {
            e.ToTable("PatientDuplicateDismissals");
            e.Property(x => x.Reason).HasMaxLength(500);
            e.Property(x => x.PairKey).HasMaxLength(80).HasComputedColumnSql("CASE WHEN [FirstPatientId] < [SecondPatientId] THEN CONVERT(char(36), [FirstPatientId]) + '|' + CONVERT(char(36), [SecondPatientId]) ELSE CONVERT(char(36), [SecondPatientId]) + '|' + CONVERT(char(36), [FirstPatientId]) END", stored: true);
            e.HasOne<Patient>().WithMany().HasForeignKey(x => x.FirstPatientId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Patient>().WithMany().HasForeignKey(x => x.SecondPatientId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.DismissedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.RestoredByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.PairKey).IsUnique().HasFilter("[RestoredAtUtc] IS NULL").HasDatabaseName("IX_PatientDuplicateDismissals_OneInForce");
            e.HasIndex(x => x.FirstPatientId); e.HasIndex(x => x.SecondPatientId);
            e.ToTable(t =>
            {
                t.HasTrigger("TR_PatientDuplicateDismissals_Guard");
                t.HasTrigger("TR_PatientDuplicateDismissals_Active");
                t.HasCheckConstraint("CK_PatientDuplicateDismissals_Distinct", "[FirstPatientId] <> [SecondPatientId]");
                t.HasCheckConstraint("CK_PatientDuplicateDismissals_Reason", Text("Reason"));
                t.HasCheckConstraint("CK_PatientDuplicateDismissals_Times", $"[DismissedAtUtc] >= {Utc} AND [DismissedAtUtc] < {Never}");
                t.HasCheckConstraint("CK_PatientDuplicateDismissals_Restored", "([RestoredAtUtc] IS NULL AND [RestoredByUserId] IS NULL) OR ([RestoredAtUtc] IS NOT NULL AND [RestoredByUserId] IS NOT NULL AND [RestoredAtUtc] >= [DismissedAtUtc])");
            });
        });
    }
}
