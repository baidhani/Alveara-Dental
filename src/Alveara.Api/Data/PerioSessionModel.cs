using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Alveara.Api.Architecture.Odontogram;
using Alveara.Api.Architecture.Periodontal;

namespace Alveara.Api.Data;

/// <summary>
/// ALV-012-C01: the model for chart sessions, whole-tooth records and chart links, beside <see cref="PerioModel"/>. As with the charts themselves, the database refuses what the rules refuse (tooth,
/// site, millimetre and grade ranges, a furcation on a single-rooted tooth, grades on an excluded tooth, a session whose closing facts do not match its status), so a writer that bypasses the service
/// still cannot store bad data. Triggers (created in the migration) refuse deleting a session, editing a closed one, changing the entries of a closed one, and any edit of a whole-tooth record or link.
/// </summary>
internal static class PerioSessionModel
{
    private static string In(IEnumerable<string> values) => string.Join(",", values.Select(v => $"'{v}'"));
    private static readonly string MultiRooted = In(ToothKeys.Permanent.Where(PerioSiteModel.IsMultiRooted));

    private static void ToothChecks(TableBuilder<PerioToothRecord> t, string table) => AddToothChecks(t, table);
    private static void ToothChecks(TableBuilder<PerioSessionTooth> t, string table) => AddToothChecks(t, table);

    private static void AddToothChecks<T>(TableBuilder<T> t, string table) where T : class
    {
        t.HasCheckConstraint($"CK_{table}_ToothKey", $"[ToothKey] COLLATE Latin1_General_CS_AS IN ({In(ToothKeys.Permanent)})");
        t.HasCheckConstraint($"CK_{table}_Mobility", $"[Mobility] BETWEEN {PerioChartValidator.MinGrade} AND {PerioChartValidator.MaxGrade}");
        t.HasCheckConstraint($"CK_{table}_Furcation", $"[Furcation] BETWEEN {PerioChartValidator.MinGrade} AND {PerioChartValidator.MaxGrade}");
        // only a multi-rooted tooth has a furcation to grade
        t.HasCheckConstraint($"CK_{table}_FurcationTooth", $"[Furcation] IS NULL OR [ToothKey] COLLATE Latin1_General_CS_AS IN ({MultiRooted})");
        // a tooth that is not charted carries no grades
        t.HasCheckConstraint($"CK_{table}_ExcludedHasNoGrades", "[Excluded] = 0 OR ([Mobility] IS NULL AND [Furcation] IS NULL)");
    }

    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PerioToothRecord>(e =>
        {
            e.Property(x => x.ToothKey).HasMaxLength(2).IsFixedLength();
            e.HasOne<PerioExam>().WithMany().HasForeignKey(x => x.ExamId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.ExamId, x.ToothKey }).IsUnique();
            e.ToTable(t => { t.HasTrigger("TR_PerioToothRecords_Immutable"); ToothChecks(t, "PerioToothRecords"); });
        });

        modelBuilder.Entity<PerioSession>(e =>
        {
            e.Property(x => x.Status).HasMaxLength(10);
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasOne<Architecture.Patients.Patient>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<PerioExam>().WithMany().HasForeignKey(x => x.ExamId).OnDelete(DeleteBehavior.Restrict);
            // one open draft per patient: starting a second returns the first, and two writers cannot both create one
            e.HasIndex(x => x.PatientId).IsUnique().HasFilter("[Status] = 'Draft'").HasDatabaseName("IX_PerioSessions_OneDraftPerPatient");
            e.HasIndex(x => x.ExamId).IsUnique().HasFilter("[ExamId] IS NOT NULL");
            e.ToTable(t =>
            {
                t.HasTrigger("TR_PerioSessions_NoDelete");
                t.HasTrigger("TR_PerioSessions_ClosedIsFinal");
                t.HasCheckConstraint("CK_PerioSessions_Status", $"[Status] COLLATE Latin1_General_CS_AS IN ({In(PerioSessionStatuses.All)})");
                t.HasCheckConstraint("CK_PerioSessions_ClosingStamp",
                    "([Status] = 'Draft' AND [ClosedAtUtc] IS NULL AND [ClosedByUserId] IS NULL) OR ([Status] <> 'Draft' AND [ClosedAtUtc] IS NOT NULL AND [ClosedByUserId] IS NOT NULL)");
                t.HasCheckConstraint("CK_PerioSessions_ExamMatchesStatus",
                    "([Status] = 'Finalized' AND [ExamId] IS NOT NULL) OR ([Status] <> 'Finalized' AND [ExamId] IS NULL)");
            });
        });

        modelBuilder.Entity<PerioSessionReading>(e =>
        {
            e.Property(x => x.ToothKey).HasMaxLength(2).IsFixedLength();
            e.Property(x => x.Site).HasMaxLength(2);
            e.HasOne<PerioSession>().WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.SessionId, x.ToothKey, x.Site }).IsUnique();
            e.ToTable(t =>
            {
                t.HasTrigger("TR_PerioSessionReadings_DraftOnly");
                t.HasCheckConstraint("CK_PerioSessionReadings_ToothKey", $"[ToothKey] COLLATE Latin1_General_CS_AS IN ({In(ToothKeys.Permanent)})");
                t.HasCheckConstraint("CK_PerioSessionReadings_Site", $"[Site] COLLATE Latin1_General_CS_AS IN ({In(PerioRules.Sites)})");
                t.HasCheckConstraint("CK_PerioSessionReadings_ProbingDepth", $"[ProbingDepthMm] BETWEEN {PerioRules.MinMm} AND {PerioRules.MaxMm}");
                t.HasCheckConstraint("CK_PerioSessionReadings_Recession", $"[RecessionMm] BETWEEN {PerioRules.MinMm} AND {PerioRules.MaxMm}");
            });
        });

        modelBuilder.Entity<PerioSessionTooth>(e =>
        {
            e.Property(x => x.ToothKey).HasMaxLength(2).IsFixedLength();
            e.HasOne<PerioSession>().WithMany().HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.SessionId, x.ToothKey }).IsUnique();
            e.ToTable(t => { t.HasTrigger("TR_PerioSessionTeeth_DraftOnly"); ToothChecks(t, "PerioSessionTeeth"); });
        });

        modelBuilder.Entity<PerioExamLink>(e =>
        {
            e.Property(x => x.LinkType).HasMaxLength(16);
            e.Property(x => x.Reference).HasMaxLength(100);
            e.HasOne<PerioExam>().WithMany().HasForeignKey(x => x.ExamId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.ExamId, x.LinkType, x.Reference }).IsUnique();
            e.HasIndex(x => x.PatientId);
            e.ToTable(t =>
            {
                t.HasTrigger("TR_PerioExamLinks_Immutable");
                t.HasCheckConstraint("CK_PerioExamLinks_LinkType", $"[LinkType] COLLATE Latin1_General_CS_AS IN ({In(PerioLinkTypes.All)})");
                t.HasCheckConstraint("CK_PerioExamLinks_ReferenceNotBlank", "LEN(LTRIM(RTRIM([Reference]))) > 0");
            });
        });
    }
}
