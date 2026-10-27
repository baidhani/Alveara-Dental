# ALV-018-C01 R01 - Changed files

Range `7f8a3d4` (the STORY-018 completion) to the implementation commit `1de9de5`, excluding the handoff folder itself. 92 files changed, 16547 insertions(+), 21 deletions(-).

## Backend source (32)
- M src/Alveara.Api/Architecture/Identity/AuditLogEntry.cs
- M src/Alveara.Api/Architecture/Identity/Permission.cs
- M src/Alveara.Api/Architecture/Identity/PermissionRegistry.json
- A src/Alveara.Api/Architecture/Patients/Portability/Package/DataExportHistory.cs
- A src/Alveara.Api/Architecture/Patients/Portability/Package/DataExportOptions.cs
- A src/Alveara.Api/Architecture/Patients/Portability/Package/DataPackageFormat.cs
- A src/Alveara.Api/Architecture/Patients/Portability/Package/ExportAuthorization.cs
- A src/Alveara.Api/Architecture/Patients/Portability/Package/ExportDomainCatalog.cs
- A src/Alveara.Api/Architecture/Patients/Portability/Package/ExportFailureRecorder.cs
- A src/Alveara.Api/Architecture/Patients/Portability/Package/ExportHistoryService.cs
- A src/Alveara.Api/Architecture/Patients/Portability/Package/ExportReadSession.cs
- A src/Alveara.Api/Architecture/Patients/Portability/Package/ExportRowWriter.cs
- A src/Alveara.Api/Architecture/Patients/Portability/Package/ExportTableFilters.cs
- A src/Alveara.Api/Architecture/Patients/Portability/Package/PackageDocumentWriter.cs
- A src/Alveara.Api/Architecture/Patients/Portability/Package/PackagePaths.cs
- A src/Alveara.Api/Architecture/Patients/Portability/Package/PackageReadme.cs
- A src/Alveara.Api/Architecture/Patients/Portability/Package/PatientDataPackageBuilder.cs
- A src/Alveara.Api/Architecture/Patients/Portability/Package/PatientDataPackageService.cs
- A src/Alveara.Api/Architecture/Patients/Portability/Package/PatientReadableRecordService.cs
- A src/Alveara.Api/Architecture/Patients/Portability/Package/PracticeTimeText.cs
- A src/Alveara.Api/Architecture/Patients/Portability/Package/ReadableRecordHtml.cs
- A src/Alveara.Api/Architecture/Patients/Portability/Package/ReadableRecordLoader.Administrative.cs
- A src/Alveara.Api/Architecture/Patients/Portability/Package/ReadableRecordLoader.Clinical.cs
- A src/Alveara.Api/Architecture/Patients/Portability/Package/ReadableRecordLoader.cs
- A src/Alveara.Api/Architecture/Patients/Portability/Package/ReadableRecordModel.cs
- A src/Alveara.Api/Controllers/PatientExportsController.cs
- M src/Alveara.Api/Data/AlveraDbContext.cs
- A src/Alveara.Api/Data/DataExportHistoryModel.cs
- A src/Alveara.Api/Migrations/20261026223451_AddDataExportHistory.Designer.cs
- A src/Alveara.Api/Migrations/20261026223451_AddDataExportHistory.cs
- M src/Alveara.Api/Migrations/AlveraDbContextModelSnapshot.cs
- M src/Alveara.Api/Program.cs

## Backend tests (14)
- A src/Alveara.Api.Tests/AllPatientsPackageTests.cs
- A src/Alveara.Api.Tests/DataExportHistorySchemaTests.cs
- A src/Alveara.Api.Tests/ExportDomainCatalogTests.cs
- A src/Alveara.Api.Tests/ExportHistoryServiceTests.cs
- A src/Alveara.Api.Tests/ExportPackagePermissionTests.cs
- A src/Alveara.Api.Tests/ExportTableFiltersTests.cs
- A src/Alveara.Api.Tests/PackagePathsTests.cs
- A src/Alveara.Api.Tests/PatientDataPackageDocumentTests.cs
- A src/Alveara.Api.Tests/PatientDataPackageServiceTests.cs
- A src/Alveara.Api.Tests/PatientExportsApiTests.cs
- A src/Alveara.Api.Tests/PatientReadableRecordServiceTests.cs
- A src/Alveara.Api.Tests/PracticeTimeTextTests.cs
- A src/Alveara.Api.Tests/ReadableRecordHtmlTests.cs
- A src/Alveara.Api.Tests/ReadableRecordLoaderTests.cs

## Client (22)
- M src/alveara-client/e2e/command-center.spec.ts
- A src/alveara-client/e2e/patient-export-package-real-backend.spec.ts
- M src/alveara-client/playwright.config.ts
- A src/alveara-client/playwright.patient-export-package.config.ts
- M src/alveara-client/src/App.tsx
- M src/alveara-client/src/app/moduleRegistry.ts
- M src/alveara-client/src/pages/PatientWorkspacePage.tsx
- A src/alveara-client/src/pages/exports/DownloadReadableRecordButton.test.tsx
- A src/alveara-client/src/pages/exports/DownloadReadableRecordButton.tsx
- A src/alveara-client/src/pages/exports/ExportCenterPage.test.tsx
- A src/alveara-client/src/pages/exports/ExportCenterPage.tsx
- A src/alveara-client/src/pages/exports/ExportCenterRoutes.test.tsx
- A src/alveara-client/src/pages/exports/ExportHistoryTable.tsx
- A src/alveara-client/src/pages/exports/ExportOverviewPanel.tsx
- A src/alveara-client/src/pages/exports/exportFormat.ts
- A src/alveara-client/src/pages/exports/exports.css
- A src/alveara-client/src/pages/exports/useExportHistory.ts
- M src/alveara-client/src/pages/portability/ExportPatientButton.tsx
- A src/alveara-client/src/services/patientExportsApi.test.ts
- A src/alveara-client/src/services/patientExportsApi.ts
- M src/alveara-client/src/services/patientPortabilityApi.ts
- A src/alveara-client/src/services/saveFile.ts

## Scripts (2)
- M scripts/Initialize-DevDatabase.ps1
- M scripts/Invoke-RealBackendWalkthrough.ps1

## Docs and evidence (21)
- M docs/PATIENT_PORTABILITY.md
- M docs/RUNNING_LOCALLY.md
- A docs/operations/DATABASE_OPTIONS.md
- M docs/testing/REAL_BACKEND_E2E.md
- A docs/testing/patient-export-package-walkthrough/01-export-screen-name-folders-warning.png
- A docs/testing/patient-export-package-walkthrough/02-export-screen-all-patients.png
- A docs/testing/patient-export-package-walkthrough/03-one-patient-package-saved.png
- A docs/testing/patient-export-package-walkthrough/04-patient-page-readable-record-button.png
- A docs/testing/patient-export-package-walkthrough/05-readable-record-opened.png
- A docs/testing/patient-export-package-walkthrough/06-all-patients-package-saved.png
- A docs/testing/patient-export-package-walkthrough/07-dentist-readable-record-only.png
- A docs/testing/patient-export-package-walkthrough/08-audit-log.png
- A docs/testing/patient-export-package-walkthrough/all-patients-package.zip
- A docs/testing/patient-export-package-walkthrough/negative-controls.txt
- A docs/testing/patient-export-package-walkthrough/one-patient-package.zip
- A docs/testing/patient-export-package-walkthrough/original-story-018-export.json
- A docs/testing/patient-export-package-walkthrough/package-verification.json
- A docs/testing/patient-export-package-walkthrough/patient-export-package-evidence.json
- A docs/testing/patient-export-package-walkthrough/readable-record-by-dentist.html
- A docs/testing/patient-export-package-walkthrough/readable-record-from-patient-page.html
- A docs/testing/patient-export-package-walkthrough/readable-record-from-screen.html

## Other (1)
- M PROGRESS.md

