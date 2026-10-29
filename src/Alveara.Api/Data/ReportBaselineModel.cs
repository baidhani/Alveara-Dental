using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Reporting;

namespace Alveara.Api.Data;

/// <summary>
/// ALV-N006 slice 5b: the model for report baselines and the metrics stored with them (see <see cref="ReportBaseline"/>, <see cref="ReportBaselineMetric"/>). Both are append-only: a baseline is never changed (error 51270)
/// or deleted (51271); a stored metric is never changed (51272) or deleted (51273); the triggers are created in the AddReportBaselines migration, which refuses to roll back while a baseline exists (51274). One metric
/// per key for each baseline; the same idempotency key cannot make two baselines.
/// </summary>
internal static class ReportBaselineModel
{
    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ReportBaseline>(e =>
        {
            e.ToTable("ReportBaselines");
            e.Property(x => x.Name).HasMaxLength(BaselineLimits.NameMax);
            e.Property(x => x.TimeZoneId).HasMaxLength(64);
            e.Property(x => x.IdempotencyKey).HasMaxLength(BaselineLimits.KeyMax);
            e.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.SetByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Metrics).WithOne().HasForeignKey(x => x.BaselineId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.SetAtUtc);
            e.HasIndex(x => x.IdempotencyKey).IsUnique();
            e.ToTable(t =>
            {
                t.HasTrigger("TR_ReportBaselines_Guard");
                t.HasCheckConstraint("CK_ReportBaselines_Name", "LEN(LTRIM(RTRIM([Name]))) > 0");
                t.HasCheckConstraint("CK_ReportBaselines_Key", "LEN(LTRIM(RTRIM([IdempotencyKey]))) > 0");
                t.HasCheckConstraint("CK_ReportBaselines_Period", "[PeriodFrom] <= [PeriodTo] AND [PeriodStartUtc] < [PeriodEndUtc] AND [PeriodEndUtc] <= [AsOfUtc]");
                t.HasCheckConstraint("CK_ReportBaselines_Times", "[SetAtUtc] >= '2000-01-01' AND [SetAtUtc] < '2200-01-01' AND [AsOfUtc] >= '2000-01-01' AND [AsOfUtc] < '2200-01-01'");
            });
        });

        modelBuilder.Entity<ReportBaselineMetric>(e =>
        {
            e.ToTable("ReportBaselineMetrics");
            e.Property(x => x.MetricKey).HasMaxLength(BaselineLimits.MetricKeyMax);
            e.Property(x => x.Rate).HasPrecision(9, 4);
            e.HasIndex(x => new { x.BaselineId, x.MetricKey }).IsUnique();
            e.ToTable(t =>
            {
                t.HasTrigger("TR_ReportBaselineMetrics_Guard");
                t.HasCheckConstraint("CK_ReportBaselineMetrics_Counts", "[NumeratorCount] >= 0 AND [DenominatorCount] >= 0 AND [DefinitionVersion] >= 1");
                t.HasCheckConstraint("CK_ReportBaselineMetrics_Rate", "([DenominatorCount] = 0 AND [Rate] IS NULL) OR ([DenominatorCount] > 0 AND [Rate] IS NOT NULL AND [Rate] >= 0)");
                t.HasCheckConstraint("CK_ReportBaselineMetrics_Key", "LEN(LTRIM(RTRIM([MetricKey]))) > 0");
            });
        });
    }
}
