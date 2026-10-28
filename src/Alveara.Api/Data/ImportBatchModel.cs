using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Imports;

namespace Alveara.Api.Data;

/// <summary>
/// ALV-018-C02: the model for import batches and their row results (see <see cref="ImportBatch"/>, <see cref="ImportBatchRow"/>; the rows' triggers are errors 51264 to 51266, created in AddImportBatchRows). As elsewhere, the database refuses by itself what the rules refuse. Triggers (created in the migration; errors 51260 to 51263): a batch is
/// never deleted (51260); it keeps what it recorded about its file and its maker (kind, file, size, rows, columns, who, when) (51261); a finished batch (Committed, Failed or Cancelled) never changes (51262); the migration
/// refuses to roll back while any batch exists (51263). The filtered unique index allows one open batch for the same file of the same kind.
/// </summary>
internal static class ImportBatchModel
{
    private const string Utc = "'2000-01-01'";
    private const string Never = "'2200-01-01'";

    private static string InList(string column, IEnumerable<string> values) => $"[{column}] IN ({string.Join(", ", values.Select(v => $"'{v}'"))})";

    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ImportBatch>(e =>
        {
            e.ToTable("ImportBatches");
            e.Property(x => x.SourceKind).HasMaxLength(20);
            e.Property(x => x.SourceLabel).HasMaxLength(ImportLimits.LabelMax);
            e.Property(x => x.Status).HasMaxLength(20);
            e.Property(x => x.Policy).HasMaxLength(20);
            e.Property(x => x.FileSha256).HasMaxLength(64).IsFixedLength();
            e.Property(x => x.ColumnNamesJson);
            e.Property(x => x.RowVersion).IsRowVersion();
            e.Property(x => x.CommitOutcome).HasMaxLength(24);
            e.Property(x => x.ValidAmount).HasPrecision(18, 2);
            e.Property(x => x.AcceptedAmount).HasPrecision(18, 2);
            e.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.CommitConfirmedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.CreatedAtUtc);
            e.HasIndex(x => x.CreatedByUserId);
            e.HasIndex(x => new { x.SourceKind, x.FileSha256 }).IsUnique().HasFilter("[Status] IN ('Uploaded', 'DryRunComplete', 'Committing')").HasDatabaseName("UX_ImportBatches_OpenFile");
            e.ToTable(t =>
            {
                t.HasTrigger("TR_ImportBatches_Guard");
                t.HasCheckConstraint("CK_ImportBatches_Kind", InList("SourceKind", ImportSourceKind.All));
                t.HasCheckConstraint("CK_ImportBatches_Status", InList("Status", ImportBatchStatus.All));
                t.HasCheckConstraint("CK_ImportBatches_Policy", InList("Policy", ImportBatchPolicy.All));
                t.HasCheckConstraint("CK_ImportBatches_CommitOutcome", "[CommitOutcome] IS NULL OR " + InList("CommitOutcome", ImportCommitOutcome.All));
                t.HasCheckConstraint("CK_ImportBatches_CommitRound", "[CommitRound] >= 0");
                t.HasCheckConstraint("CK_ImportBatches_Amounts", "[ValidAmount] >= 0 AND [AcceptedAmount] >= 0");
                t.HasCheckConstraint("CK_ImportBatches_Label", "LEN(LTRIM(RTRIM([SourceLabel]))) > 0");
                t.HasCheckConstraint("CK_ImportBatches_Hash", "LEN([FileSha256]) = 64 AND [FileSha256] NOT LIKE '%[^0-9a-f]%' COLLATE Latin1_General_BIN2");
                t.HasCheckConstraint("CK_ImportBatches_Sizes", "[FileSizeBytes] > 0 AND [RowCount] > 0 AND [ColumnCount] > 0");
                t.HasCheckConstraint("CK_ImportBatches_Counters",
                    "[AttemptedRows] >= 0 AND [AcceptedRows] >= 0 AND [RejectedRows] >= 0 AND [SkippedRows] >= 0 AND [UnresolvedRows] >= 0 AND [AcceptedRows] + [RejectedRows] + [SkippedRows] + [UnresolvedRows] <= [AttemptedRows] AND [AttemptedRows] <= [RowCount]");
                t.HasCheckConstraint("CK_ImportBatches_Times", $"[CreatedAtUtc] >= {Utc} AND [CreatedAtUtc] < {Never} AND ([UpdatedAtUtc] IS NULL OR ([UpdatedAtUtc] >= [CreatedAtUtc] AND [UpdatedAtUtc] < {Never}))");
            });
        });

        modelBuilder.Entity<ImportBatchRow>(e =>
        {
            e.ToTable("ImportBatchRows");
            e.Property(x => x.Status).HasMaxLength(20);
            e.Property(x => x.Reason).HasMaxLength(40);
            e.Property(x => x.ErrorsJson).HasMaxLength(4000);
            e.Property(x => x.MatchedPatientIdsJson).HasMaxLength(600);
            e.HasOne<ImportBatch>().WithMany().HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.Restrict);
            // ResultRecordId names the record a row produced: a patient for a patient file, an appointment for an appointment file. It is a record of what the import did, so it has no foreign key (as with the export history).
            e.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.DecisionByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.ResultRecordId);
            e.HasIndex(x => new { x.BatchId, x.RowNumber }).IsUnique().HasDatabaseName("UX_ImportBatchRows_Row");
            e.HasIndex(x => new { x.BatchId, x.Status });
            e.ToTable(t =>
            {
                t.HasTrigger("TR_ImportBatchRows_Guard");
                t.HasCheckConstraint("CK_ImportBatchRows_Status", InList("Status", ImportRowStatus.All));
                t.HasCheckConstraint("CK_ImportBatchRows_Number", "[RowNumber] >= 2");
                t.HasCheckConstraint("CK_ImportBatchRows_Accepted", "[Status] <> 'Accepted' OR [ResultRecordId] IS NOT NULL");
                t.HasCheckConstraint("CK_ImportBatchRows_Decision", "([DecisionByUserId] IS NULL AND [DecidedAtUtc] IS NULL) OR ([DecisionByUserId] IS NOT NULL AND [DecidedAtUtc] IS NOT NULL)");
                t.HasCheckConstraint("CK_ImportBatchRows_Repeat", "[DuplicateOfRow] IS NULL OR ([DuplicateOfRow] >= 2 AND [DuplicateOfRow] < [RowNumber])");
            });
        });
    }
}
