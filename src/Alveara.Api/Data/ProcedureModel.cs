using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Procedures;

namespace Alveara.Api.Data;

/// <summary>
/// ALV-N005: the model for the procedure catalog. The database itself refuses what <see cref="ProcedureRules"/> refuses (a code that does not fit its code system, a local code that looks like a
/// CDT code, a fee that is negative, over the limit or has more than two decimals' worth of cents, a value outside the allowed lists, a blank or control-character description, a last valid date
/// before the start date, a dentition other than Both on a procedure that is not tooth-level), and what no rule can see from one row: triggers (created in the migrations) refuse deleting a
/// procedure, changing its code or code system, any edit or delete of a version or an event, and a version that starts before the one it follows or skips a number. The only way history changes
/// is a new version appended after the last.
/// </summary>
internal static class ProcedureModel
{
    private static string In(IEnumerable<string> values) => string.Join(",", values.Select(v => $"'{v}'"));

    /// <summary>True when the column holds no control character (1 to 31); written so the check is the same whatever the column's collation.</summary>
    private static string NoControl(string column) => $"PATINDEX(N'%[' + NCHAR(1) + N'-' + NCHAR(31) + N']%', [{column}] COLLATE Latin1_General_BIN2) = 0";

    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProcedureDefinition>(e =>
        {
            e.Property(x => x.CodeSystem).HasMaxLength(10).UseCollation("Latin1_General_CS_AS");
            e.Property(x => x.Code).HasMaxLength(ProcedureRules.CodeMax).UseCollation("Latin1_General_CS_AS");
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasIndex(x => new { x.CodeSystem, x.Code }).IsUnique();
            e.HasIndex(x => x.IsActive);
            e.ToTable(t =>
            {
                t.HasTrigger("TR_ProcedureDefinitions_NoDelete");
                t.HasTrigger("TR_ProcedureDefinitions_Identity");
                t.HasCheckConstraint("CK_ProcedureDefinitions_CodeSystem", $"[CodeSystem] COLLATE Latin1_General_CS_AS IN ({In(ProcedureCodeSystems.All)})");
                t.HasCheckConstraint("CK_ProcedureDefinitions_CurrentVersion", "[CurrentVersionNumber] >= 1");
                // the code is stored upper case, has only the characters a code can have, and each system's own shape; a local code never looks like a CDT code
                t.HasCheckConstraint("CK_ProcedureDefinitions_CodeShape",
                    "LEN([Code]) >= 1 AND [Code] COLLATE Latin1_General_BIN2 NOT LIKE '%[^-A-Z0-9.]%' AND LEFT([Code], 1) COLLATE Latin1_General_BIN2 LIKE '[A-Z0-9]'");
                t.HasCheckConstraint("CK_ProcedureDefinitions_CdtShape", "[CodeSystem] COLLATE Latin1_General_CS_AS <> 'CDT' OR [Code] COLLATE Latin1_General_BIN2 LIKE 'D[0-9][0-9][0-9][0-9]'");
                t.HasCheckConstraint("CK_ProcedureDefinitions_LocalNotCdt",
                    "[CodeSystem] COLLATE Latin1_General_CS_AS <> 'Local' OR ([Code] COLLATE Latin1_General_BIN2 NOT LIKE 'D[0-9][0-9][0-9][0-9]' AND LEN([Code]) >= 2 AND LEN([Code]) <= 20 AND [Code] COLLATE Latin1_General_BIN2 NOT LIKE '%.%')");
            });
        });
        modelBuilder.Entity<ProcedureVersion>(e =>
        {
            e.Property(x => x.Description).HasMaxLength(ProcedureRules.DescriptionMax);
            e.Property(x => x.Category).HasMaxLength(16);
            e.Property(x => x.Scope).HasMaxLength(12);
            e.Property(x => x.Dentition).HasMaxLength(9);
            e.Property(x => x.Fee).HasPrecision(18, 2);
            e.Property(x => x.SourceName).HasMaxLength(ProcedureRules.SourceNameMax);
            e.Property(x => x.SourceVersion).HasMaxLength(ProcedureRules.SourceVersionMax);
            e.Property(x => x.Reason).HasMaxLength(ProcedureRules.ReasonMax);
            e.HasOne<ProcedureDefinition>().WithMany().HasForeignKey(x => x.ProcedureId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.ProcedureId, x.VersionNumber }).IsUnique();
            e.HasIndex(x => new { x.ProcedureId, x.EffectiveFrom });
            e.ToTable(t =>
            {
                t.HasTrigger("TR_ProcedureVersions_Immutable");
                t.HasTrigger("TR_ProcedureVersions_Order");
                t.HasTrigger("TR_ProcedureVersions_Provenance");
                t.HasCheckConstraint("CK_ProcedureVersions_VersionNumber", "[VersionNumber] >= 1");
                t.HasCheckConstraint("CK_ProcedureVersions_Category", $"[Category] COLLATE Latin1_General_CS_AS IN ({In(ProcedureCategories.All)})");
                t.HasCheckConstraint("CK_ProcedureVersions_Scope", $"[Scope] COLLATE Latin1_General_CS_AS IN ({In(ProcedureScopes.All)})");
                t.HasCheckConstraint("CK_ProcedureVersions_Dentition", $"[Dentition] COLLATE Latin1_General_CS_AS IN ({In(ProcedureDentitions.All)})");
                t.HasCheckConstraint("CK_ProcedureVersions_DentitionMatchesScope", "[Scope] COLLATE Latin1_General_CS_AS IN ('Tooth','ToothSurface') OR [Dentition] COLLATE Latin1_General_CS_AS = 'Both'");
                t.HasCheckConstraint("CK_ProcedureVersions_Fee", $"[Fee] >= 0 AND [Fee] <= {ProcedureRules.FeeMax:F2} AND [Fee] = ROUND([Fee], 2)");
                t.HasCheckConstraint("CK_ProcedureVersions_Description", $"LEN(LTRIM(RTRIM([Description]))) > 0 AND {NoControl("Description")}");
                // a source name or edition, when given, is not blank (which code systems need or refuse a source is the TR_ProcedureVersions_Provenance trigger: a check cannot see the definition's code system)
                t.HasCheckConstraint("CK_ProcedureVersions_SourceText",
                    "([SourceName] IS NULL OR LEN(LTRIM(RTRIM([SourceName]))) > 0) AND ([SourceVersion] IS NULL OR LEN(LTRIM(RTRIM([SourceVersion]))) > 0)");
                t.HasCheckConstraint("CK_ProcedureVersions_Dates", "[EffectiveFrom] >= '2000-01-01' AND ([ValidThrough] IS NULL OR [ValidThrough] >= [EffectiveFrom])");
            });
        });
        modelBuilder.Entity<ProcedureEvent>(e =>
        {
            e.Property(x => x.ChangeType).HasMaxLength(12);
            e.Property(x => x.Reason).HasMaxLength(ProcedureRules.ReasonMax);
            e.HasOne<ProcedureDefinition>().WithMany().HasForeignKey(x => x.ProcedureId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.ProcedureId, x.EventNumber }).IsUnique();
            e.ToTable(t =>
            {
                t.HasTrigger("TR_ProcedureEvents_Immutable");
                t.HasCheckConstraint("CK_ProcedureEvents_ChangeType", $"[ChangeType] COLLATE Latin1_General_CS_AS IN ({In(ProcedureChanges.All)})");
                t.HasCheckConstraint("CK_ProcedureEvents_Version",
                    "([ChangeType] COLLATE Latin1_General_CS_AS IN ('Created','Revised') AND [VersionNumber] IS NOT NULL) OR ([ChangeType] COLLATE Latin1_General_CS_AS IN ('Inactivated','Reactivated') AND [VersionNumber] IS NULL)");
                // revising and inactivating always carry the reason
                t.HasCheckConstraint("CK_ProcedureEvents_Reason",
                    "[ChangeType] COLLATE Latin1_General_CS_AS NOT IN ('Revised','Inactivated') OR ([Reason] IS NOT NULL AND LEN(LTRIM(RTRIM([Reason]))) > 0)");
            });
        });
    }
}
