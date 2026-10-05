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
            // case-sensitive, like the catalogue code it must equal: the default collation would let a finding name "caries" for "Caries"
            e.Property(x => x.Condition).HasMaxLength(32).UseCollation("Latin1_General_CS_AS");
            e.Property(x => x.ConditionScope).HasMaxLength(10);
            e.Property(x => x.State).HasMaxLength(10);
            e.Property(x => x.Status).HasMaxLength(10);
            e.Property(x => x.WithdrawnReason).HasMaxLength(500);
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasOne<Architecture.Patients.Patient>().WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
            // a finding can only name a condition that exists in the catalogue; retiring a condition never removes it, so this never dangles
            e.HasOne<ConditionType>().WithMany().HasForeignKey(x => x.Condition).HasPrincipalKey(x => x.Code).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.PatientId, x.Status });
            // one ACTIVE finding per tooth, surface and condition: a retried or simultaneous record cannot create a twin (a withdrawn one can be entered again)
            e.HasIndex(x => new { x.PatientId, x.ToothKey, x.Surface, x.Condition }).IsUnique().HasFilter("[Status] = 'Active'");
            e.ToTable(t =>
            {
                t.HasTrigger("TR_ToothFindings_NoDelete");
                t.HasCheckConstraint("CK_ToothFindings_ToothKey", $"[ToothKey] COLLATE Latin1_General_CS_AS IN ({In(ToothKeys.All)})");
                t.HasCheckConstraint("CK_ToothFindings_ConditionScope", $"[ConditionScope] COLLATE Latin1_General_CS_AS IN ({In(ConditionScopes.All)})");
                t.HasCheckConstraint("CK_ToothFindings_State", $"[State] COLLATE Latin1_General_CS_AS IN ({In(FindingStates.All)})");
                t.HasCheckConstraint("CK_ToothFindings_Status", "[Status] COLLATE Latin1_General_CS_AS IN ('Active','Withdrawn')");
                t.HasCheckConstraint("CK_ToothFindings_Surface", $"[Surface] IS NULL OR [Surface] COLLATE Latin1_General_CS_AS IN ({In(ToothSurfaces.Any)})");
                // a surface condition carries a surface; a whole-tooth condition carries none (the scope is stored on the finding, so the database can check it without reading the catalogue)
                t.HasCheckConstraint("CK_ToothFindings_SurfaceMatchesScope",
                    "([ConditionScope] = 'Surface' AND [Surface] IS NOT NULL) OR ([ConditionScope] = 'WholeTooth' AND [Surface] IS NULL)");
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
            e.Property(x => x.Condition).HasMaxLength(32);
            e.Property(x => x.State).HasMaxLength(10);
            e.Property(x => x.Status).HasMaxLength(10);
            e.Property(x => x.Reason).HasMaxLength(500);
            e.HasOne<ToothFinding>().WithMany().HasForeignKey(x => x.FindingId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.FindingId, x.VersionNumber }).IsUnique();
            e.HasIndex(x => x.PatientId);
            e.ToTable(t =>
            {
                t.HasTrigger("TR_ToothFindingVersions_Immutable");
                t.HasCheckConstraint("CK_ToothFindingVersions_ChangeType", "[ChangeType] COLLATE Latin1_General_CS_AS IN ('Recorded','StateChanged','Withdrawn','Linked')");
            });
        });
        modelBuilder.Entity<ConditionType>(e =>
        {
            e.Property(x => x.Code).HasMaxLength(32).UseCollation("Latin1_General_CS_AS");
            e.Property(x => x.Label).HasMaxLength(80);
            e.Property(x => x.Scope).HasMaxLength(10);
            e.Property(x => x.AppliesTo).HasMaxLength(10);
            e.Property(x => x.ToothEffect).HasMaxLength(12);
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasAlternateKey(x => x.Code);
            e.HasData(FindingConditions.Seeds.Select(c => new
            {
                c.Id, c.Code, c.Label, c.Scope, c.AppliesTo, ToothEffect = c.Effect, IsActive = true, CreatedAtUtc = new DateTimeOffset(2026, 10, 4, 0, 0, 0, TimeSpan.Zero),
            }));
            e.ToTable(t =>
            {
                t.HasTrigger("TR_ConditionTypes_NoDelete");
                t.HasTrigger("TR_ConditionTypes_Definition");
                t.HasCheckConstraint("CK_ConditionTypes_Scope", $"[Scope] COLLATE Latin1_General_CS_AS IN ({In(ConditionScopes.All)})");
                t.HasCheckConstraint("CK_ConditionTypes_AppliesTo", $"[AppliesTo] COLLATE Latin1_General_CS_AS IN ({In(ConditionDentitions.All)})");
                t.HasCheckConstraint("CK_ConditionTypes_ToothEffect", $"[ToothEffect] COLLATE Latin1_General_CS_AS IN ({In(ToothEffects.All)})");
                t.HasCheckConstraint("CK_ConditionTypes_CodeShape", "LEN([Code]) >= 2 AND [Code] COLLATE Latin1_General_BIN NOT LIKE '%[^A-Za-z0-9]%' AND LEFT([Code], 1) COLLATE Latin1_General_BIN LIKE '[A-Z]'");
                t.HasCheckConstraint("CK_ConditionTypes_LabelNotBlank", "LEN(LTRIM(RTRIM([Label]))) > 0");
            });
        });
        modelBuilder.Entity<ConditionTypeEvent>(e =>
        {
            e.Property(x => x.Code).HasMaxLength(32);
            e.Property(x => x.ChangeType).HasMaxLength(12);
            e.Property(x => x.Reason).HasMaxLength(500);
            e.HasOne<ConditionType>().WithMany().HasForeignKey(x => x.ConditionTypeId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.ConditionTypeId, x.EventNumber }).IsUnique();
            e.ToTable(t =>
            {
                t.HasTrigger("TR_ConditionTypeEvents_Immutable");
                t.HasCheckConstraint("CK_ConditionTypeEvents_ChangeType", "[ChangeType] COLLATE Latin1_General_CS_AS IN ('Created','Retired','Reactivated')");
            });
        });
        modelBuilder.Entity<ToothFindingLink>(e =>
        {
            e.Property(x => x.LinkType).HasMaxLength(16);
            e.Property(x => x.Reference).HasMaxLength(100);
            e.HasOne<ToothFinding>().WithMany().HasForeignKey(x => x.FindingId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.FindingId, x.LinkType, x.Reference }).IsUnique();
            e.HasIndex(x => x.PatientId);
            e.ToTable(t =>
            {
                t.HasTrigger("TR_ToothFindingLinks_Immutable");
                t.HasCheckConstraint("CK_ToothFindingLinks_LinkType", $"[LinkType] COLLATE Latin1_General_CS_AS IN ({In(LinkTypes.All)})");
                t.HasCheckConstraint("CK_ToothFindingLinks_ReferenceNotBlank", "LEN(LTRIM(RTRIM([Reference]))) > 0");
            });
        });
    }
}
