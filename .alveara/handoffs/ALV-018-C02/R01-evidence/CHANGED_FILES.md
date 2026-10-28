# ALV-018-C02 R01 - Changed files

Range `cb0bcd0` (the ALV-018-C01 R02 closure, origin/main when work began) to the implementation commit `a13d5b4`, excluding the handoff folder itself. 116 files changed, 60107 insertions(+), 44 deletions(-). The product was built in 11 local `wip:` commits (`adb0c89` to `3442b37`); `a13d5b4` changes PROGRESS.md only.

Existing (modified) product files, all disclosed in the handoff: `SchedulingConfiguration.cs` (an optional lookup cache argument), `PatientDuplicateDetector.cs` (the comparison extracted as a pure function), `PatientRegistrationService.cs` (patient staging extracted), `ExportDomainCatalog.cs` (the new tables placed in the catalog), `AuditLogEntry.cs`, `Permission.cs`, `PermissionRegistry.json`, `AlveraDbContext.cs`, the model snapshot and `Program.cs` (registrations), `App.tsx`, `moduleRegistry.ts` and `playwright.config.ts` (the new route, module and the exclusion of the new real-backend spec from the mocked suite).

## Backend source (38)
- A src/Alveara.Api/Architecture/Billing/BillingService.OpeningBalance.cs
- M src/Alveara.Api/Architecture/Configuration/SchedulingConfiguration.cs
- M src/Alveara.Api/Architecture/Identity/AuditLogEntry.cs
- M src/Alveara.Api/Architecture/Identity/Permission.cs
- M src/Alveara.Api/Architecture/Identity/PermissionRegistry.json
- A src/Alveara.Api/Architecture/Imports/AppointmentImportFields.cs
- A src/Alveara.Api/Architecture/Imports/CsvParser.cs
- A src/Alveara.Api/Architecture/Imports/ImportAppointmentCommit.cs
- A src/Alveara.Api/Architecture/Imports/ImportAppointmentEvaluator.cs
- A src/Alveara.Api/Architecture/Imports/ImportAppointmentResolver.cs
- A src/Alveara.Api/Architecture/Imports/ImportBalanceCommit.cs
- A src/Alveara.Api/Architecture/Imports/ImportBalanceEvaluator.cs
- A src/Alveara.Api/Architecture/Imports/ImportBatch.cs
- A src/Alveara.Api/Architecture/Imports/ImportBatchRow.cs
- A src/Alveara.Api/Architecture/Imports/ImportCommitEngine.cs
- A src/Alveara.Api/Architecture/Imports/ImportCommitJobs.cs
- A src/Alveara.Api/Architecture/Imports/ImportCommitService.cs
- A src/Alveara.Api/Architecture/Imports/ImportDecisionService.cs
- A src/Alveara.Api/Architecture/Imports/ImportDryRunService.cs
- A src/Alveara.Api/Architecture/Imports/ImportFileStorage.cs
- A src/Alveara.Api/Architecture/Imports/ImportFileSweepService.cs
- A src/Alveara.Api/Architecture/Imports/ImportIntakeService.cs
- A src/Alveara.Api/Architecture/Imports/ImportMappingService.cs
- A src/Alveara.Api/Architecture/Imports/ImportOptions.cs
- A src/Alveara.Api/Architecture/Imports/ImportPatientLookup.cs
- A src/Alveara.Api/Architecture/Imports/ImportPatientRows.cs
- A src/Alveara.Api/Architecture/Imports/ImportReportService.cs
- A src/Alveara.Api/Architecture/Imports/ImportRetryService.cs
- A src/Alveara.Api/Architecture/Imports/ImportWorkScope.cs
- A src/Alveara.Api/Architecture/Imports/OpeningBalanceImportFields.cs
- A src/Alveara.Api/Architecture/Imports/PatientImportFields.cs
- M src/Alveara.Api/Architecture/Patients/PatientDuplicateDetector.cs
- M src/Alveara.Api/Architecture/Patients/PatientRegistrationService.cs
- M src/Alveara.Api/Architecture/Patients/Portability/Package/ExportDomainCatalog.cs
- A src/Alveara.Api/Controllers/ImportBatchesController.cs
- M src/Alveara.Api/Data/AlveraDbContext.cs
- A src/Alveara.Api/Data/ImportBatchModel.cs
- M src/Alveara.Api/Program.cs

## Migrations (11)
- A src/Alveara.Api/Migrations/20261027221734_AddImportBatches.Designer.cs
- A src/Alveara.Api/Migrations/20261027221734_AddImportBatches.cs
- A src/Alveara.Api/Migrations/20261027232355_AddImportBatchRows.Designer.cs
- A src/Alveara.Api/Migrations/20261027232355_AddImportBatchRows.cs
- A src/Alveara.Api/Migrations/20261028020902_AddImportCommit.Designer.cs
- A src/Alveara.Api/Migrations/20261028020902_AddImportCommit.cs
- A src/Alveara.Api/Migrations/20261028034413_GeneralizeImportRowResult.Designer.cs
- A src/Alveara.Api/Migrations/20261028034413_GeneralizeImportRowResult.cs
- A src/Alveara.Api/Migrations/20261028122037_AddImportBatchAmounts.Designer.cs
- A src/Alveara.Api/Migrations/20261028122037_AddImportBatchAmounts.cs
- M src/Alveara.Api/Migrations/AlveraDbContextModelSnapshot.cs

## Backend tests (19)
- A src/Alveara.Api.Tests/AppointmentImportFieldsTests.cs
- A src/Alveara.Api.Tests/BulkImportPermissionTests.cs
- A src/Alveara.Api.Tests/CsvParserTests.cs
- A src/Alveara.Api.Tests/ImportAppointmentApiTests.cs
- A src/Alveara.Api.Tests/ImportAppointmentCommitTests.cs
- A src/Alveara.Api.Tests/ImportAppointmentDryRunTests.cs
- A src/Alveara.Api.Tests/ImportBalanceCommitTests.cs
- A src/Alveara.Api.Tests/ImportBalanceDryRunTests.cs
- A src/Alveara.Api.Tests/ImportBatchRowSchemaTests.cs
- A src/Alveara.Api.Tests/ImportBatchSchemaTests.cs
- A src/Alveara.Api.Tests/ImportBatchesApiTests.cs
- A src/Alveara.Api.Tests/ImportCommitApiTests.cs
- A src/Alveara.Api.Tests/ImportCommitSchemaTests.cs
- A src/Alveara.Api.Tests/ImportCommitTests.cs
- A src/Alveara.Api.Tests/ImportIntakeServiceTests.cs
- A src/Alveara.Api.Tests/ImportMappingApiTests.cs
- A src/Alveara.Api.Tests/ImportPatientDryRunTests.cs
- A src/Alveara.Api.Tests/ImportReportsRetryAndSweepTests.cs
- A src/Alveara.Api.Tests/PatientImportFieldsTests.cs

## Client source and tests (20)
- M src/alveara-client/src/App.tsx
- M src/alveara-client/src/app/moduleRegistry.ts
- A src/alveara-client/src/pages/imports/CommitStep.tsx
- A src/alveara-client/src/pages/imports/DecisionQueue.tsx
- A src/alveara-client/src/pages/imports/DryRunStep.tsx
- A src/alveara-client/src/pages/imports/ErrorReportButton.tsx
- A src/alveara-client/src/pages/imports/ImportBatchPage.test.tsx
- A src/alveara-client/src/pages/imports/ImportBatchPage.tsx
- A src/alveara-client/src/pages/imports/ImportBatchesPage.test.tsx
- A src/alveara-client/src/pages/imports/ImportBatchesPage.tsx
- A src/alveara-client/src/pages/imports/ImportRoutes.test.tsx
- A src/alveara-client/src/pages/imports/ImportRowsTable.tsx
- A src/alveara-client/src/pages/imports/MappingStep.tsx
- A src/alveara-client/src/pages/imports/ReconciliationPanel.tsx
- A src/alveara-client/src/pages/imports/importFormat.ts
- A src/alveara-client/src/pages/imports/importTestServer.ts
- A src/alveara-client/src/pages/imports/imports.css
- A src/alveara-client/src/pages/imports/useImportBatch.ts
- A src/alveara-client/src/services/bulkImportApi.test.ts
- A src/alveara-client/src/services/bulkImportApi.ts

## Real-backend walkthrough and Playwright config (3)
- A src/alveara-client/e2e/bulk-import-real-backend.spec.ts
- A src/alveara-client/playwright.bulk-import.config.ts
- M src/alveara-client/playwright.config.ts

## Docs and walkthrough evidence (24)
- A docs/BULK_IMPORT.md
- M docs/PATIENT_PORTABILITY.md
- M docs/testing/REAL_BACKEND_E2E.md
- A docs/testing/bulk-import-walkthrough/01-dentist-refused.png
- A docs/testing/bulk-import-walkthrough/02-malformed-file-refused.png
- A docs/testing/bulk-import-walkthrough/03-patients-mapping.png
- A docs/testing/bulk-import-walkthrough/04-patients-dry-run-rejected-rows.png
- A docs/testing/bulk-import-walkthrough/05-patients-duplicate-queue.png
- A docs/testing/bulk-import-walkthrough/06-patients-commit-step.png
- A docs/testing/bulk-import-walkthrough/07-patients-reconciled.png
- A docs/testing/bulk-import-walkthrough/08-patients-again-nothing-to-commit.png
- A docs/testing/bulk-import-walkthrough/09-appointments-dry-run-rejected.png
- A docs/testing/bulk-import-walkthrough/10-appointments-reconciled.png
- A docs/testing/bulk-import-walkthrough/11-balances-dry-run.png
- A docs/testing/bulk-import-walkthrough/12-balances-reconciled.png
- A docs/testing/bulk-import-walkthrough/13-balance-account-not-empty.png
- A docs/testing/bulk-import-walkthrough/14-front-desk-cannot-commit-balances.png
- A docs/testing/bulk-import-walkthrough/15-commit-stopped.png
- A docs/testing/bulk-import-walkthrough/16-retried-and-reconciled.png
- A docs/testing/bulk-import-walkthrough/17-sweep.png
- A docs/testing/bulk-import-walkthrough/18-audit-log.png
- A docs/testing/bulk-import-walkthrough/bulk-import-evidence.json
- A docs/testing/bulk-import-walkthrough/patients-error-report.csv
- A docs/testing/bulk-import-walkthrough/run-result.txt

## Other (1)
- M PROGRESS.md
