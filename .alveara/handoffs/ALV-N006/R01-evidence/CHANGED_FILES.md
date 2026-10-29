# Changed files - ALV-N006 R01

`git diff --name-status 6899818 HEAD` at the implementation commit: 104 paths (the last pushed commit is `6899818`). `A` added, `M` modified. Nothing was deleted or renamed. The `.alveara/` evidence files of this attempt come in the evidence commit and are not listed.

## Backend: reporting services and the export (`src/Alveara.Api/Architecture/Reporting/`) (29)
29 added, 0 modified.
- A `src/Alveara.Api/Architecture/Reporting/BalancesDrillService.cs`
- A `src/Alveara.Api/Architecture/Reporting/BalancesReportService.cs`
- A `src/Alveara.Api/Architecture/Reporting/CancellationDrillService.cs`
- A `src/Alveara.Api/Architecture/Reporting/CancellationReportService.cs`
- A `src/Alveara.Api/Architecture/Reporting/ClinicalDrillService.cs`
- A `src/Alveara.Api/Architecture/Reporting/ClinicalQueries.cs`
- A `src/Alveara.Api/Architecture/Reporting/CollectionsReportService.cs`
- A `src/Alveara.Api/Architecture/Reporting/CompletedProceduresReportService.cs`
- A `src/Alveara.Api/Architecture/Reporting/FinancialDrillService.cs`
- A `src/Alveara.Api/Architecture/Reporting/FinancialLedger.cs`
- A `src/Alveara.Api/Architecture/Reporting/FollowUpDrillService.cs`
- A `src/Alveara.Api/Architecture/Reporting/FollowUpQueries.cs`
- A `src/Alveara.Api/Architecture/Reporting/FollowUpReportService.cs`
- A `src/Alveara.Api/Architecture/Reporting/MeasurementBaselineService.cs`
- A `src/Alveara.Api/Architecture/Reporting/MeasurementComparisonService.cs`
- A `src/Alveara.Api/Architecture/Reporting/MetricDefinitions.cs`
- A `src/Alveara.Api/Architecture/Reporting/MetricsService.cs`
- A `src/Alveara.Api/Architecture/Reporting/PatientFlowReportService.cs`
- A `src/Alveara.Api/Architecture/Reporting/ProductionReportService.cs`
- A `src/Alveara.Api/Architecture/Reporting/ProviderActivityDrillService.cs`
- A `src/Alveara.Api/Architecture/Reporting/ProviderActivityReportService.cs`
- A `src/Alveara.Api/Architecture/Reporting/ReportBaseline.cs`
- A `src/Alveara.Api/Architecture/Reporting/ReportCsv.cs`
- A `src/Alveara.Api/Architecture/Reporting/ReportDrillService.cs`
- A `src/Alveara.Api/Architecture/Reporting/ReportExportService.cs`
- A `src/Alveara.Api/Architecture/Reporting/ReportFilter.cs`
- A `src/Alveara.Api/Architecture/Reporting/ReportResult.cs`
- A `src/Alveara.Api/Architecture/Reporting/ScheduleReportService.cs`
- A `src/Alveara.Api/Architecture/Reporting/TreatmentPlanStatusReportService.cs`

## Backend: controllers (5)
5 added, 0 modified.
- A `src/Alveara.Api/Controllers/ClinicalReportsController.cs`
- A `src/Alveara.Api/Controllers/FinancialReportsController.cs`
- A `src/Alveara.Api/Controllers/MeasurementBaselinesController.cs`
- A `src/Alveara.Api/Controllers/MeasurementController.cs`
- A `src/Alveara.Api/Controllers/ReportsController.cs`

## Backend: schema (model, migration, snapshot) (5)
3 added, 2 modified.
- M `src/Alveara.Api/Data/AlveraDbContext.cs`
- A `src/Alveara.Api/Data/ReportBaselineModel.cs`
- A `src/Alveara.Api/Migrations/20261029043602_AddReportBaselines.Designer.cs`
- A `src/Alveara.Api/Migrations/20261029043602_AddReportBaselines.cs`
- M `src/Alveara.Api/Migrations/AlveraDbContextModelSnapshot.cs`

## Backend: shared components changed (permissions, audit, export catalog, start-up) (6)
0 added, 6 modified.
- M `src/Alveara.Api/Architecture/Identity/AuditLogEntry.cs`
- M `src/Alveara.Api/Architecture/Identity/Permission.cs`
- M `src/Alveara.Api/Architecture/Identity/PermissionMatrix.cs`
- M `src/Alveara.Api/Architecture/Identity/PermissionRegistry.json`
- M `src/Alveara.Api/Architecture/Patients/Portability/Package/ExportDomainCatalog.cs`
- M `src/Alveara.Api/Program.cs`

## Backend tests (`src/Alveara.Api.Tests/`) (16)
16 added, 0 modified.
- A `src/Alveara.Api.Tests/BalancesReportTests.cs`
- A `src/Alveara.Api.Tests/CancellationReportTests.cs`
- A `src/Alveara.Api.Tests/ClinicalReportsApiTests.cs`
- A `src/Alveara.Api.Tests/ClinicalReportsTests.cs`
- A `src/Alveara.Api.Tests/FinancialReportsApiTests.cs`
- A `src/Alveara.Api.Tests/FinancialReportsTests.cs`
- A `src/Alveara.Api.Tests/FollowUpReportTests.cs`
- A `src/Alveara.Api.Tests/MeasurementApiTests.cs`
- A `src/Alveara.Api.Tests/MeasurementBaselineTests.cs`
- A `src/Alveara.Api.Tests/MetricsTests.cs`
- A `src/Alveara.Api.Tests/OperationalReportsApiTests.cs`
- A `src/Alveara.Api.Tests/OperationalReportsTests.cs`
- A `src/Alveara.Api.Tests/ProviderActivityTests.cs`
- A `src/Alveara.Api.Tests/ReportCsvTests.cs`
- A `src/Alveara.Api.Tests/ReportExportApiTests.cs`
- A `src/Alveara.Api.Tests/ReportScaleTests.cs`

## Client: reports pages, components and tests (`src/alveara-client/src/pages/reports/`) (32)
32 added, 0 modified.
- A `src/alveara-client/src/pages/reports/BalancesView.tsx`
- A `src/alveara-client/src/pages/reports/BaselineSection.tsx`
- A `src/alveara-client/src/pages/reports/CancellationsView.tsx`
- A `src/alveara-client/src/pages/reports/CollectionsView.tsx`
- A `src/alveara-client/src/pages/reports/ComparisonSection.tsx`
- A `src/alveara-client/src/pages/reports/CompletedProceduresView.tsx`
- A `src/alveara-client/src/pages/reports/FinancialClinicalViews.test.tsx`
- A `src/alveara-client/src/pages/reports/FollowUpsView.tsx`
- A `src/alveara-client/src/pages/reports/LedgerProof.tsx`
- A `src/alveara-client/src/pages/reports/MeasurementView.test.tsx`
- A `src/alveara-client/src/pages/reports/MeasurementView.tsx`
- A `src/alveara-client/src/pages/reports/MetricsSection.tsx`
- A `src/alveara-client/src/pages/reports/MoreRecords.tsx`
- A `src/alveara-client/src/pages/reports/NumberButton.tsx`
- A `src/alveara-client/src/pages/reports/OperationalViews.test.tsx`
- A `src/alveara-client/src/pages/reports/PatientFlowView.tsx`
- A `src/alveara-client/src/pages/reports/ProductionView.tsx`
- A `src/alveara-client/src/pages/reports/ProviderActivityView.tsx`
- A `src/alveara-client/src/pages/reports/RecordsPanel.tsx`
- A `src/alveara-client/src/pages/reports/ReportActions.test.tsx`
- A `src/alveara-client/src/pages/reports/ReportActions.tsx`
- A `src/alveara-client/src/pages/reports/ReportFilterBar.tsx`
- A `src/alveara-client/src/pages/reports/ReportFrame.tsx`
- A `src/alveara-client/src/pages/reports/ReportsHubPage.test.tsx`
- A `src/alveara-client/src/pages/reports/ReportsHubPage.tsx`
- A `src/alveara-client/src/pages/reports/ScheduleView.tsx`
- A `src/alveara-client/src/pages/reports/TreatmentPlansView.tsx`
- A `src/alveara-client/src/pages/reports/reportCatalog.ts`
- A `src/alveara-client/src/pages/reports/reportFormat.ts`
- A `src/alveara-client/src/pages/reports/reportTestServer.ts`
- A `src/alveara-client/src/pages/reports/reports.css`
- A `src/alveara-client/src/pages/reports/useReport.ts`

## Client: services, app shell, routes (4)
2 added, 2 modified.
- M `src/alveara-client/src/App.tsx`
- M `src/alveara-client/src/app/moduleRegistry.ts`
- A `src/alveara-client/src/services/reportsApi.test.ts`
- A `src/alveara-client/src/services/reportsApi.ts`

## Client: browser tests and configuration (3)
2 added, 1 modified.
- A `src/alveara-client/e2e/reports-real-backend.spec.ts`
- M `src/alveara-client/playwright.config.ts`
- A `src/alveara-client/playwright.reports.config.ts`

## Scripts (1)
0 added, 1 modified.
- M `scripts/Invoke-RealBackendWalkthrough.ps1`

## Documentation (2)
1 added, 1 modified.
- A `docs/REPORTING.md`
- M `docs/testing/REAL_BACKEND_E2E.md`

## Progress record (1)
0 added, 1 modified.
- M `PROGRESS.md`
