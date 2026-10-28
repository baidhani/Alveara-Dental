using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Alveara.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddImportCommit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DecidedAtUtc",
                table: "ImportBatchRows",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DecisionByUserId",
                table: "ImportBatchRows",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ResultRecordId",
                table: "ImportBatchRows",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CommitConfirmedByUserId",
                table: "ImportBatches",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CommitOutcome",
                table: "ImportBatches",
                type: "nvarchar(24)",
                maxLength: 24,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CommitRound",
                table: "ImportBatches",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CommitStartedAtUtc",
                table: "ImportBatches",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CommittedAtUtc",
                table: "ImportBatches",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ImportBatchRows_DecisionByUserId",
                table: "ImportBatchRows",
                column: "DecisionByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ImportBatchRows_ResultRecordId",
                table: "ImportBatchRows",
                column: "ResultRecordId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ImportBatchRows_Accepted",
                table: "ImportBatchRows",
                sql: "[Status] <> 'Accepted' OR [ResultRecordId] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ImportBatchRows_Decision",
                table: "ImportBatchRows",
                sql: "([DecisionByUserId] IS NULL AND [DecidedAtUtc] IS NULL) OR ([DecisionByUserId] IS NOT NULL AND [DecidedAtUtc] IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_ImportBatches_CommitConfirmedByUserId",
                table: "ImportBatches",
                column: "CommitConfirmedByUserId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ImportBatches_CommitOutcome",
                table: "ImportBatches",
                sql: "[CommitOutcome] IS NULL OR [CommitOutcome] IN ('Completed', 'CompletedWithExceptions', 'Refused', 'Interrupted')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ImportBatches_CommitRound",
                table: "ImportBatches",
                sql: "[CommitRound] >= 0");

            migrationBuilder.AddForeignKey(
                name: "FK_ImportBatches_UserAccounts_CommitConfirmedByUserId",
                table: "ImportBatches",
                column: "CommitConfirmedByUserId",
                principalTable: "UserAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ImportBatchRows_Patients_ResultRecordId",
                table: "ImportBatchRows",
                column: "ResultRecordId",
                principalTable: "Patients",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ImportBatchRows_UserAccounts_DecisionByUserId",
                table: "ImportBatchRows",
                column: "DecisionByUserId",
                principalTable: "UserAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // a rollback is only possible while no commit has been confirmed (it would drop who committed what, and which patient each row registered)
            migrationBuilder.Sql(@"IF EXISTS (SELECT 1 FROM [ImportBatches] WHERE [CommitRound] > 0)
    THROW 51268, 'An import commit has been confirmed, so this migration cannot be rolled back.', 1;");
            migrationBuilder.DropForeignKey(
                name: "FK_ImportBatches_UserAccounts_CommitConfirmedByUserId",
                table: "ImportBatches");

            migrationBuilder.DropForeignKey(
                name: "FK_ImportBatchRows_Patients_ResultRecordId",
                table: "ImportBatchRows");

            migrationBuilder.DropForeignKey(
                name: "FK_ImportBatchRows_UserAccounts_DecisionByUserId",
                table: "ImportBatchRows");

            migrationBuilder.DropIndex(
                name: "IX_ImportBatchRows_DecisionByUserId",
                table: "ImportBatchRows");

            migrationBuilder.DropIndex(
                name: "IX_ImportBatchRows_ResultRecordId",
                table: "ImportBatchRows");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ImportBatchRows_Accepted",
                table: "ImportBatchRows");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ImportBatchRows_Decision",
                table: "ImportBatchRows");

            migrationBuilder.DropIndex(
                name: "IX_ImportBatches_CommitConfirmedByUserId",
                table: "ImportBatches");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ImportBatches_CommitOutcome",
                table: "ImportBatches");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ImportBatches_CommitRound",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "DecidedAtUtc",
                table: "ImportBatchRows");

            migrationBuilder.DropColumn(
                name: "DecisionByUserId",
                table: "ImportBatchRows");

            migrationBuilder.DropColumn(
                name: "ResultRecordId",
                table: "ImportBatchRows");

            migrationBuilder.DropColumn(
                name: "CommitConfirmedByUserId",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "CommitOutcome",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "CommitRound",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "CommitStartedAtUtc",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "CommittedAtUtc",
                table: "ImportBatches");
        }
    }
}
