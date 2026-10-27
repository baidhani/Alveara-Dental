using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Imports;

namespace Alveara.Api.Data;

/// <summary>
/// ALV-018-C02: the model for import batches (see <see cref="ImportBatch"/>). As elsewhere, the database refuses by itself what the rules refuse. Triggers (created in the migration; errors 51260 to 51263): a batch is
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
            e.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.CreatedAtUtc);
            e.HasIndex(x => x.CreatedByUserId);
            e.HasIndex(x => new { x.SourceKind, x.FileSha256 }).IsUnique().HasFilter("[Status] IN ('Uploaded', 'DryRunComplete', 'Committing')").HasDatabaseName("UX_ImportBatches_OpenFile");
            e.ToTable(t =>
            {
                t.HasTrigger("TR_ImportBatches_Guard");
                t.HasCheckConstraint("CK_ImportBatches_Kind", InList("SourceKind", ImportSourceKind.All));
                t.HasCheckConstraint("CK_ImportBatches_Status", InList("Status", ImportBatchStatus.All));
                t.HasCheckConstraint("CK_ImportBatches_Policy", InList("Policy", ImportBatchPolicy.All));
                t.HasCheckConstraint("CK_ImportBatches_Label", "LEN(LTRIM(RTRIM([SourceLabel]))) > 0");
                t.HasCheckConstraint("CK_ImportBatches_Hash", "LEN([FileSha256]) = 64 AND [FileSha256] NOT LIKE '%[^0-9a-f]%' COLLATE Latin1_General_BIN2");
                t.HasCheckConstraint("CK_ImportBatches_Sizes", "[FileSizeBytes] > 0 AND [RowCount] > 0 AND [ColumnCount] > 0");
                t.HasCheckConstraint("CK_ImportBatches_Counters",
                    "[AttemptedRows] >= 0 AND [AcceptedRows] >= 0 AND [RejectedRows] >= 0 AND [SkippedRows] >= 0 AND [UnresolvedRows] >= 0 AND [AcceptedRows] + [RejectedRows] + [SkippedRows] + [UnresolvedRows] <= [AttemptedRows] AND [AttemptedRows] <= [RowCount]");
                t.HasCheckConstraint("CK_ImportBatches_Times", $"[CreatedAtUtc] >= {Utc} AND [CreatedAtUtc] < {Never} AND ([UpdatedAtUtc] IS NULL OR ([UpdatedAtUtc] >= [CreatedAtUtc] AND [UpdatedAtUtc] < {Never}))");
            });
        });
    }
}
