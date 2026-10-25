# ALV-017-C01 R01 - Changed files

Implementation range: `1aa4891..5f0efb5` (10 commits, 67 files, +31,960 / -81; most of the volume is the three generated migration designers and the screenshots). Evidence and status commits follow and change only `.alveara/`, `PROGRESS.md` and `docs/`.

## Backend (`src/Alveara.Api/`)
- `Architecture/Patients/Merge/`: `PatientMergeService.cs` (confirmation, identity check, audit detail), `PatientMergeViews.cs`, `PatientMergeEntities.cs`, `PatientMergeCatalog.cs`, `PatientMergeFields.cs`, `PatientOwnedData.cs` (merge flag, scope trigger, bookkeeping), `PatientDuplicateScanner.cs`, **new** `PatientDuplicateReview.cs` (dismissals), **new** `PatientUnmergeService.cs`.
- `Controllers/`: `PatientMergesController.cs` (nine routes), **new** `PatientMergeHistoryController.cs`, `PatientsController.cs`.
- `Architecture/Patients/PatientWrite.cs`, `Architecture/Logging/SafeExceptionHandlingMiddleware.cs` (merged-record refusals as 409), `Architecture/Identity/AuditLogEntry.cs` (event types), `Program.cs`.
- `Data/`: `AlveraDbContext.cs`, `PatientMergeModel.cs`, **new** `PatientDuplicateDismissalModel.cs`.
- Migrations: `20261025195735_AddPatientDuplicateDismissals`, `20261025200612_GuardRetiredPatientRecords`, `20261025202141_AddPatientUnmerge` (+ designers, snapshot). (`20261025052956_AddPatientMerges` is STORY-017's.)

## Tests (`src/Alveara.Api.Tests/`)
New: `PatientDuplicateReviewTests`, `RetiredRecordGuardTests`, `PatientUnmergeSchemaTests`, `PatientUnmergeServiceTests`, `PatientMergeRecoveryTests`, `PatientMergeChartPreservationTests`, `MergeChartTestBase`. Extended: `PatientMergeServiceTests`, `PatientMergeApiTests`, `PatientMergeSchemaTests`, `PatientMergeBackupRestoreTests`, `RealMigrationInventory(+Tests)`.

## Client (`src/alveara-client/`)
`services/patientMergesApi.ts`, `services/patientsApi.ts`, `pages/patientmerge/{PatientMergePage,MergeReview,MergeHistoryBanner,UnmergeReview}.tsx`, `merge.css`, `pages/PatientWorkspacePage.tsx`, `pages/PatientDetailsPanel.tsx`, `test/fakePatientServer.ts`, tests `PatientMergeScreens.test.tsx`, `MergeHistoryBanner.test.tsx`, `PatientWorkspace.test.tsx`, `e2e/patient-merge-real-backend.spec.ts`.

## Docs
`docs/PATIENT_MERGE.md`, `docs/testing/patient-merge-walkthrough/`, `docs/testing/STORY-017_TEST_RESULTS.md`, `docs/testing/STORY-017_NEGATIVE_CONTROLS.md` (lint wording corrected), `PROGRESS.md`.

## Size
Every new or changed hand-written production file is under 500 lines; the largest is `PatientMergeService.cs` (329). The migrations and designers are generated plus hand-written triggers.
