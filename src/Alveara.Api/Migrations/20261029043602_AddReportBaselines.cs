using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Alveara.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddReportBaselines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReportBaselines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PeriodFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodTo = table.Column<DateOnly>(type: "date", nullable: false),
                    PeriodStartUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PeriodEndUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    TimeZoneId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    AsOfUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SetByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SetAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportBaselines", x => x.Id);
                    table.CheckConstraint("CK_ReportBaselines_Key", "LEN(LTRIM(RTRIM([IdempotencyKey]))) > 0");
                    table.CheckConstraint("CK_ReportBaselines_Name", "LEN(LTRIM(RTRIM([Name]))) > 0");
                    table.CheckConstraint("CK_ReportBaselines_Period", "[PeriodFrom] <= [PeriodTo] AND [PeriodStartUtc] < [PeriodEndUtc] AND [PeriodEndUtc] <= [AsOfUtc]");
                    table.CheckConstraint("CK_ReportBaselines_Times", "[SetAtUtc] >= '2000-01-01' AND [SetAtUtc] < '2200-01-01' AND [AsOfUtc] >= '2000-01-01' AND [AsOfUtc] < '2200-01-01'");
                    table.ForeignKey(
                        name: "FK_ReportBaselines_UserAccounts_SetByUserId",
                        column: x => x.SetByUserId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReportBaselineMetrics",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BaselineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MetricKey = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    DefinitionVersion = table.Column<int>(type: "int", nullable: false),
                    NumeratorCount = table.Column<int>(type: "int", nullable: false),
                    DenominatorCount = table.Column<int>(type: "int", nullable: false),
                    Rate = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportBaselineMetrics", x => x.Id);
                    table.CheckConstraint("CK_ReportBaselineMetrics_Counts", "[NumeratorCount] >= 0 AND [DenominatorCount] >= 0 AND [DefinitionVersion] >= 1");
                    table.CheckConstraint("CK_ReportBaselineMetrics_Key", "LEN(LTRIM(RTRIM([MetricKey]))) > 0");
                    table.CheckConstraint("CK_ReportBaselineMetrics_Rate", "([DenominatorCount] = 0 AND [Rate] IS NULL) OR ([DenominatorCount] > 0 AND [Rate] IS NOT NULL AND [Rate] >= 0)");
                    table.ForeignKey(
                        name: "FK_ReportBaselineMetrics_ReportBaselines_BaselineId",
                        column: x => x.BaselineId,
                        principalTable: "ReportBaselines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReportBaselineMetrics_BaselineId_MetricKey",
                table: "ReportBaselineMetrics",
                columns: new[] { "BaselineId", "MetricKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReportBaselines_IdempotencyKey",
                table: "ReportBaselines",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReportBaselines_SetAtUtc",
                table: "ReportBaselines",
                column: "SetAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ReportBaselines_SetByUserId",
                table: "ReportBaselines",
                column: "SetByUserId");

            // ALV-N006 slice 5b (errors 51270 to 51273): a baseline is recorded once and never changed or deleted, and neither is a metric stored with it, so a baseline always says what it said when it was set.
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_ReportBaselines_Guard] ON [ReportBaselines] AFTER UPDATE, DELETE AS
BEGIN
    IF EXISTS (SELECT 1 FROM deleted) AND NOT EXISTS (SELECT 1 FROM inserted)
        THROW 51271, 'A report baseline is never deleted.', 1;
    THROW 51270, 'A report baseline is never changed.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_ReportBaselineMetrics_Guard] ON [ReportBaselineMetrics] AFTER UPDATE, DELETE AS
BEGIN
    IF EXISTS (SELECT 1 FROM deleted) AND NOT EXISTS (SELECT 1 FROM inserted)
        THROW 51273, 'A metric stored with a report baseline is never deleted.', 1;
    THROW 51272, 'A metric stored with a report baseline is never changed.', 1;
END");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // a rollback is only possible while no baseline has been recorded
            migrationBuilder.Sql(@"IF EXISTS (SELECT 1 FROM [ReportBaselines])
    THROW 51274, 'Report baselines exist, so this migration cannot be rolled back.', 1;");
            migrationBuilder.DropTable(
                name: "ReportBaselineMetrics");

            migrationBuilder.DropTable(
                name: "ReportBaselines");
        }
    }
}
