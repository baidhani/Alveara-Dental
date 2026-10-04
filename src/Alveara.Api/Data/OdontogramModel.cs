using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Odontogram;

namespace Alveara.Api.Data;

/// <summary>
/// STORY-006: the model for tooth findings and their append-only history. Check constraints are case-sensitive on purpose (the default collation would accept 'caries'). The database
/// itself refuses a tooth that is not one of the 52 FDI keys, a surface that does not exist on that tooth type, a surface on a whole-tooth condition (or none on a surface
/// condition), and a withdrawn finding without who, when and why. Triggers (created in the migration) refuse deleting a finding and any edit of the history.
/// </summary>
internal static class OdontogramModel
{
    private static string In(IEnumerable<string> values) => string.Join(",", values.Select(v => $"'{v}'"));

    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ToothFinding>(e =>
        {
            e.Property(x => x.ToothKey).HasMaxLength(2).IsFixedLength();
            e.Property(x => x.Surface).HasMaxLength(1).IsFixedLength();
            e.Property(x => x.Condition).HasMaxLength(12);
            e.Property(x => x.State).HasMaxLength(10);
            e.Property(x => x.Status).HasMaxLength(10);
            e.Property(x => x.WithdrawnReason).HasMaxLength(500);
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasOne<Architecture.Patients.Patient>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.PatientId, x.Status });
            // one ACTIVE finding per tooth, surface and condition: a retried or simultaneous record cannot create a twin (a withdrawn one can be entered again)
            e.HasIndex(x => new { x.PatientId, x.ToothKey, x.Surface, x.Condition }).IsUnique().HasFilter("[Status] = 'Active'");
            e.ToTable(t =>
            {
                t.HasTrigger("TR_ToothFindings_NoDelete");
                t.HasCheckConstraint("CK_ToothFindings_ToothKey", $"[ToothKey] COLLATE Latin1_General_CS_AS IN ({In(ToothKeys.All)})");
                t.HasCheckConstraint("CK_ToothFindings_Condition", $"[Condition] COLLATE Latin1_General_CS_AS IN ({In(FindingConditions.All)})");
                t.HasCheckConstraint("CK_ToothFindings_State", $"[State] COLLATE Latin1_General_CS_AS IN ({In(FindingStates.All)})");
                t.HasCheckConstraint("CK_ToothFindings_Status", "[Status] COLLATE Latin1_General_CS_AS IN ('Active','Withdrawn')");
                t.HasCheckConstraint("CK_ToothFindings_Surface", $"[Surface] IS NULL OR [Surface] COLLATE Latin1_General_CS_AS IN ({In(ToothSurfaces.Any)})");
                // surface conditions carry a surface; whole-tooth conditions carry none
                t.HasCheckConstraint("CK_ToothFindings_SurfaceMatchesCondition",
                    $"([Condition] COLLATE Latin1_General_CS_AS IN ({In(FindingConditions.SurfaceConditions)}) AND [Surface] IS NOT NULL) " +
                    $"OR ([Condition] COLLATE Latin1_General_CS_AS NOT IN ({In(FindingConditions.SurfaceConditions)}) AND [Surface] IS NULL)");
                // Occlusal exists only on posterior teeth (position 4-8), Incisal and Facial only on anterior ones (position 1-3), Buccal only on posterior
                t.HasCheckConstraint("CK_ToothFindings_SurfaceExistsOnTooth",
                    "[Surface] IS NULL OR [Surface] COLLATE Latin1_General_CS_AS IN ('M','D','L') " +
                    "OR ([Surface] COLLATE Latin1_General_CS_AS IN ('I','F') AND CAST(SUBSTRING([ToothKey], 2, 1) AS int) <= 3) " +
                    "OR ([Surface] COLLATE Latin1_General_CS_AS IN ('O','B') AND CAST(SUBSTRING([ToothKey], 2, 1) AS int) >= 4)");
                t.HasCheckConstraint("CK_ToothFindings_WithdrawnStamp",
                    "([Status] = 'Withdrawn' AND [WithdrawnAtUtc] IS NOT NULL AND [WithdrawnByUserId] IS NOT NULL AND [WithdrawnReason] IS NOT NULL AND LEN(LTRIM(RTRIM([WithdrawnReason]))) > 0) " +
                    "OR ([Status] = 'Active' AND [WithdrawnAtUtc] IS NULL AND [WithdrawnByUserId] IS NULL AND [WithdrawnReason] IS NULL)");
            });
        });
        modelBuilder.Entity<ToothFindingVersion>(e =>
        {
            e.Property(x => x.ChangeType).HasMaxLength(12);
            e.Property(x => x.ToothKey).HasMaxLength(2).IsFixedLength();
            e.Property(x => x.Surface).HasMaxLength(1).IsFixedLength();
            e.Property(x => x.Condition).HasMaxLength(12);
            e.Property(x => x.State).HasMaxLength(10);
            e.Property(x => x.Status).HasMaxLength(10);
            e.Property(x => x.Reason).HasMaxLength(500);
            e.HasOne<ToothFinding>().WithMany().HasForeignKey(x => x.FindingId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.FindingId, x.VersionNumber }).IsUnique();
            e.HasIndex(x => x.PatientId);
            e.ToTable(t =>
            {
                t.HasTrigger("TR_ToothFindingVersions_Immutable");
                t.HasCheckConstraint("CK_ToothFindingVersions_ChangeType", "[ChangeType] COLLATE Latin1_General_CS_AS IN ('Recorded','StateChanged','Withdrawn')");
            });
        });
    }
}
