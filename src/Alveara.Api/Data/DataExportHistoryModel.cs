using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Patients.Portability.Package;

namespace Alveara.Api.Data;

/// <summary>
/// ALV-018-C01: the model for the export history (see <see cref="DataExportHistory"/>). As elsewhere, the database refuses by itself what the rules refuse. Triggers (created in the migration; errors 51250 to 51253):
/// a row is never deleted (51250); it keeps what it recorded (who, when, what, for whom) (51251); a finished row never changes (51252); the migration refuses to roll back while any row exists (51253). Only a row that is
/// <c>Started</c> may change, and only into a finished state with its totals.
/// </summary>
internal static class DataExportHistoryModel
{
    private const string Utc = "'2000-01-01'";
    private const string Never = "'2200-01-01'";

    private static string InList(string column, IEnumerable<string> values) => $"[{column}] IN ({string.Join(", ", values.Select(v => $"'{v}'"))})";

    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DataExportHistory>(e =>
        {
            e.ToTable("DataExportHistory");
            e.Property(x => x.Kind).HasMaxLength(20);
            e.Property(x => x.Scope).HasMaxLength(20);
            e.Property(x => x.Result).HasMaxLength(20);
            e.Property(x => x.Sha256).HasMaxLength(64).IsFixedLength();
            e.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.RequestedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.ExportId).IsUnique();
            e.HasIndex(x => x.RequestedAtUtc);
            e.HasIndex(x => x.RequestedByUserId);
            e.ToTable(t =>
            {
                t.HasTrigger("TR_DataExportHistory_Guard");
                t.HasCheckConstraint("CK_DataExportHistory_Kind", InList("Kind", DataExportKind.All));
                t.HasCheckConstraint("CK_DataExportHistory_Scope", InList("Scope", DataExportScope.All));
                t.HasCheckConstraint("CK_DataExportHistory_Result", InList("Result", DataExportResult.All));
                t.HasCheckConstraint("CK_DataExportHistory_Subject", "([Scope] = 'OnePatient' AND [SubjectRecordId] IS NOT NULL) OR ([Scope] = 'AllPatients' AND [SubjectRecordId] IS NULL)");
                t.HasCheckConstraint("CK_DataExportHistory_Times", $"[RequestedAtUtc] >= {Utc} AND [RequestedAtUtc] < {Never} AND ([FinishedAtUtc] IS NULL OR ([FinishedAtUtc] >= [RequestedAtUtc] AND [FinishedAtUtc] < {Never}))");
                t.HasCheckConstraint("CK_DataExportHistory_Finished", "([Result] = 'Started' AND [FinishedAtUtc] IS NULL) OR ([Result] <> 'Started' AND [FinishedAtUtc] IS NOT NULL)");
                t.HasCheckConstraint("CK_DataExportHistory_Totals", "[PatientCount] >= 0 AND [FileCount] >= 0 AND [SizeBytes] >= 0 AND [ProblemCount] >= 0");
                t.HasCheckConstraint("CK_DataExportHistory_Hash", "[Sha256] IS NULL OR (LEN([Sha256]) = 64 AND [Sha256] NOT LIKE '%[^0-9a-f]%' COLLATE Latin1_General_BIN2)");
                t.HasCheckConstraint("CK_DataExportHistory_ReadableRecord", "[Kind] <> 'ReadableRecord' OR ([Scope] = 'OnePatient' AND [IncludesDocumentFiles] = 0)");
            });
        });
    }
}
