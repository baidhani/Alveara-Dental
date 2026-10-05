using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Odontogram;
using Alveara.Api.Architecture.Periodontal;

namespace Alveara.Api.Data;

/// <summary>
/// STORY-012: the model for periodontal charts. The database itself refuses a tooth that is not one of the 32 permanent FDI keys, a site that is not one of the six, and probing depth
/// or recession outside 0 to 15 mm, so a writer that bypasses <see cref="PerioRules"/> still cannot store bad data. Triggers (created in the migration) refuse any edit or delete
/// of a chart or its readings. Check constraints are case-sensitive on purpose.
/// </summary>
internal static class PerioModel
{
    private static string In(IEnumerable<string> values) => string.Join(",", values.Select(v => $"'{v}'"));

    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PerioExam>(e =>
        {
            e.Property(x => x.IdempotencyKey).HasMaxLength(64);
            e.HasOne<Architecture.Patients.Patient>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.PatientId, x.RecordedAtUtc });
            // a retried or double-clicked save with the same key can only ever create one chart
            e.HasIndex(x => new { x.PatientId, x.IdempotencyKey }).IsUnique();
            e.ToTable(t =>
            {
                t.HasTrigger("TR_PerioExams_Immutable");
                t.HasCheckConstraint("CK_PerioExams_Key", "LEN(LTRIM(RTRIM([IdempotencyKey]))) > 0");
                t.HasCheckConstraint("CK_PerioExams_ReadingCount", $"[ReadingCount] BETWEEN 1 AND {PerioRules.MaxReadings}");
            });
        });
        modelBuilder.Entity<PerioReading>(e =>
        {
            e.Property(x => x.ToothKey).HasMaxLength(2).IsFixedLength();
            e.Property(x => x.Site).HasMaxLength(2);
            e.HasOne<PerioExam>().WithMany().HasForeignKey(x => x.ExamId).OnDelete(DeleteBehavior.Restrict);
            // one reading per tooth and site within a chart
            e.HasIndex(x => new { x.ExamId, x.ToothKey, x.Site }).IsUnique();
            e.HasIndex(x => x.ToothKey);
            e.ToTable(t =>
            {
                t.HasTrigger("TR_PerioReadings_Immutable");
                t.HasCheckConstraint("CK_PerioReadings_ToothKey", $"[ToothKey] COLLATE Latin1_General_CS_AS IN ({In(ToothKeys.Permanent)})");
                t.HasCheckConstraint("CK_PerioReadings_Site", $"[Site] COLLATE Latin1_General_CS_AS IN ({In(PerioRules.Sites)})");
                t.HasCheckConstraint("CK_PerioReadings_ProbingDepth", $"[ProbingDepthMm] BETWEEN {PerioRules.MinMm} AND {PerioRules.MaxMm}");
                t.HasCheckConstraint("CK_PerioReadings_Recession", $"[RecessionMm] BETWEEN {PerioRules.MinMm} AND {PerioRules.MaxMm}");
            });
        });
    }
}
