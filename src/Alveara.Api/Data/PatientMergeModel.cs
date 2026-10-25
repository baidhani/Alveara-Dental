using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Patients;
using Alveara.Api.Architecture.Patients.Merge;

namespace Alveara.Api.Data;

/// <summary>
/// STORY-017: the model for merging two patient records: the lineage (who was merged into whom, by whom, when, why), what moved and what was decided about each conflict, and the merged mark on the retired patient. As elsewhere,
/// the database refuses by itself what the rules refuse. Triggers (created in the migration; errors 51230 to 51239):
/// the lineage is never updated or deleted (51230); a patient that was merged away cannot be the survivor of another merge, and a patient cannot be merged into itself (51231); a patient is marked merged only by a lineage row and stays
/// inactive and merged (51232, 51233); and the one narrow exception the merge service uses, which is the point of <c>TR_*_MergeScope</c>: inside a transaction that has set the session value <see cref="PatientOwnedData.MergeFlag"/>, the
/// immutable-record triggers stand aside for an UPDATE, and the merge-scope trigger of every table the registry (<see cref="PatientOwnedData"/>) lists refuses that UPDATE unless it sets nothing but the patient-id columns, and refuses any insert or delete (51234);
/// the migration refuses to roll back while any of this data exists (51239).
/// </summary>
internal static class PatientMergeModel
{
    private const string Utc = "'2000-01-01'";
    private const string Never = "'2200-01-01'";

    private static string Text(string column) => $"{NotificationModel.NotBlank(column)} AND {NotificationModel.NoNul(column)} AND {PrescriptionModel.NoControl(column)}";

    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Patient>(e =>
        {
            e.ToTable(t =>
            {
                t.HasTrigger("TR_Patients_Merged");
                t.HasCheckConstraint("CK_Patients_Merged", "[MergedAtUtc] IS NULL OR ([IsActive] = 0 AND [MergedAtUtc] >= '2000-01-01' AND [MergedAtUtc] < '2200-01-01')");
            });
        });

        modelBuilder.Entity<PatientMerge>(e =>
        {
            e.ToTable("PatientMerges");
            e.Property(x => x.MergeKey).HasMaxLength(100);
            e.Property(x => x.Reason).HasMaxLength(500);
            e.HasOne<Patient>().WithMany().HasForeignKey(x => x.SourcePatientId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Patient>().WithMany().HasForeignKey(x => x.SurvivorPatientId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.MergedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.MergeKey).IsUnique();                // a repeated request is the same merge
            e.HasIndex(x => x.SourcePatientId);                    // a patient is retired once at a time: the database refuses a second merge of a record whose first merge was not undone (TR_PatientMerges_Survivor)
            e.Property(x => x.HasLedger).HasDefaultValue(false);
            e.HasIndex(x => x.SurvivorPatientId);
            e.ToTable(t =>
            {
                t.HasTrigger("TR_PatientMerges_Immutable");
                t.HasTrigger("TR_PatientMerges_Survivor");
                t.HasCheckConstraint("CK_PatientMerges_Distinct", "[SourcePatientId] <> [SurvivorPatientId]");
                t.HasCheckConstraint("CK_PatientMerges_Key", $"LEN(LTRIM(RTRIM([MergeKey]))) > 0 AND {PrescriptionModel.NoControl("MergeKey")}");
                t.HasCheckConstraint("CK_PatientMerges_Reason", Text("Reason"));
                t.HasCheckConstraint("CK_PatientMerges_Time", $"[MergedAtUtc] >= {Utc} AND [MergedAtUtc] < {Never}");
            });
        });

        modelBuilder.Entity<PatientMergeMovedRow>(e =>
        {
            e.ToTable("PatientMergeMovedRows");
            e.HasKey(x => new { x.MergeId, x.TableName, x.ColumnName, x.RowKey });
            e.Property(x => x.TableName).HasMaxLength(64);
            e.Property(x => x.ColumnName).HasMaxLength(64);
            e.Property(x => x.RowKey).HasMaxLength(100);
            e.HasOne<PatientMerge>().WithMany().HasForeignKey(x => x.MergeId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t =>
            {
                t.HasTrigger("TR_PatientMergeMovedRows_Immutable");
                t.HasCheckConstraint("CK_PatientMergeMovedRows_Names", $"{NotificationModel.NotBlank("TableName")} AND {NotificationModel.NotBlank("ColumnName")} AND {NotificationModel.NotBlank("RowKey")}");
            });
        });

        modelBuilder.Entity<PatientUnmerge>(e =>
        {
            e.ToTable("PatientUnmerges");
            e.Property(x => x.UnmergeKey).HasMaxLength(100);
            e.Property(x => x.Reason).HasMaxLength(500);
            e.HasOne<PatientMerge>().WithMany().HasForeignKey(x => x.MergeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.UnmergedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.MergeId).IsUnique();                 // a merge is undone once
            e.HasIndex(x => x.UnmergeKey).IsUnique();              // a repeated request is the same undo
            e.ToTable(t =>
            {
                t.HasTrigger("TR_PatientUnmerges_Immutable");
                t.HasTrigger("TR_PatientUnmerges_Active");
                t.HasCheckConstraint("CK_PatientUnmerges_Key", $"LEN(LTRIM(RTRIM([UnmergeKey]))) > 0 AND {PrescriptionModel.NoControl("UnmergeKey")}");
                t.HasCheckConstraint("CK_PatientUnmerges_Reason", Text("Reason"));
                t.HasCheckConstraint("CK_PatientUnmerges_Rows", "[RowsMovedBack] >= 0");
                t.HasCheckConstraint("CK_PatientUnmerges_Time", $"[UnmergedAtUtc] >= {Utc} AND [UnmergedAtUtc] < {Never}");
            });
        });

        modelBuilder.Entity<PatientMergeMove>(e =>
        {
            e.ToTable("PatientMergeMoves");
            e.HasKey(x => new { x.MergeId, x.TableName, x.ColumnName });
            e.Property(x => x.TableName).HasMaxLength(100);
            e.Property(x => x.ColumnName).HasMaxLength(100);
            e.HasOne<PatientMerge>().WithMany().HasForeignKey(x => x.MergeId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t =>
            {
                t.HasTrigger("TR_PatientMergeMoves_Immutable");
                t.HasCheckConstraint("CK_PatientMergeMoves_Rows", "[RowsMoved] >= 0");
                t.HasCheckConstraint("CK_PatientMergeMoves_Names", $"{NotificationModel.NotBlank("TableName")} AND {NotificationModel.NotBlank("ColumnName")}");
            });
        });

        modelBuilder.Entity<PatientMergeResolution>(e =>
        {
            e.ToTable("PatientMergeResolutions");
            e.Property(x => x.Kind).HasMaxLength(10);
            e.Property(x => x.Subject).HasMaxLength(200);
            e.Property(x => x.Choice).HasMaxLength(20);
            e.Property(x => x.ReplacedValue).HasMaxLength(500);
            e.HasOne<PatientMerge>().WithMany().HasForeignKey(x => x.MergeId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.MergeId, x.Kind, x.Subject }).IsUnique();
            e.ToTable(t =>
            {
                t.HasTrigger("TR_PatientMergeResolutions_Immutable");
                t.HasCheckConstraint("CK_PatientMergeResolutions_Kind", $"[Kind] COLLATE Latin1_General_CS_AS IN ({PrescriptionModel.In(PatientMergeResolutionKinds.All)})");
                t.HasCheckConstraint("CK_PatientMergeResolutions_Choice", $"[Choice] COLLATE Latin1_General_CS_AS IN ({PrescriptionModel.In(PatientMergeChoices.All)})");
                // a field is kept or taken (and says what it replaced when it was taken); a record that could not move is left where it was
                t.HasCheckConstraint("CK_PatientMergeResolutions_Shape",
                    "([Kind] = 'Field' AND [Choice] IN ('KeptSurvivor','TookSource') AND ([Choice] = 'TookSource' OR [ReplacedValue] IS NULL)) OR ([Kind] = 'Record' AND [Choice] = 'LeftWithSource' AND [ReplacedValue] IS NULL)");
                t.HasCheckConstraint("CK_PatientMergeResolutions_Subject", Text("Subject"));
                t.HasCheckConstraint("CK_PatientMergeResolutions_Replaced", $"[ReplacedValue] IS NULL OR {NotificationModel.NoNul("ReplacedValue")}");
            });
        });

        // every table that holds a patient id gets the merge-scope trigger (declared so that EF does not use OUTPUT without INTO against it)
        var tables = PatientOwnedData.Tables.ToHashSet(StringComparer.Ordinal);
        foreach (var entity in modelBuilder.Model.GetEntityTypes().Where(e => e.GetTableName() is { } name && tables.Contains(name)).ToList())
        {
            var table = entity.GetTableName()!;
            modelBuilder.Entity(entity.ClrType).ToTable(t => t.HasTrigger(PatientOwnedData.MergeScopeTrigger(table)));
        }
    }
}
