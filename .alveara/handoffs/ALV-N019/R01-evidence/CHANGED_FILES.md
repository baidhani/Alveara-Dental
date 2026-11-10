# ALV-N019 R01: changed files

`git diff --name-status 3d70b69 HEAD` (baseline to final implementation commit `6b083cd`): 66 files. A = added, M = modified. Nothing deleted or renamed. Evidence files under `.alveara/handoffs/ALV-N019/` are added by the later evidence commit and are not listed.

## Backend source (src/Alveara.Api) (14)
- M `src/Alveara.Api/Architecture/Backup/BackupCrypto.cs`
- M `src/Alveara.Api/Architecture/Backup/BackupRestoreService.cs`
- M `src/Alveara.Api/Architecture/Backup/BackupService.cs`
- M `src/Alveara.Api/Architecture/Backup/BackupSupport.cs`
- M `src/Alveara.Api/Architecture/Imports/ImportCommitEngine.cs`
- M `src/Alveara.Api/Architecture/Imports/ImportPatientRows.cs`
- M `src/Alveara.Api/Architecture/Patients/Merge/PatientMergeService.cs`
- M `src/Alveara.Api/Architecture/Patients/Merge/PatientUnmergeService.cs`
- M `src/Alveara.Api/Architecture/Patients/Portability/Package/PackageReadme.cs`
- M `src/Alveara.Api/Architecture/Patients/Portability/Package/PatientDataPackageService.cs`
- M `src/Alveara.Api/Architecture/Patients/Portability/Package/PatientReadableRecordService.cs`
- M `src/Alveara.Api/Architecture/Patients/Portability/PatientExportFormat.cs`
- M `src/Alveara.Api/Architecture/Patients/Portability/PatientExportService.cs`
- M `src/Alveara.Api/Architecture/Patients/Portability/PatientImportValidator.cs`

## Backend tests (src/Alveara.Api.Tests) (9)
- M `src/Alveara.Api.Tests/AllPatientsPackageTests.cs`
- M `src/Alveara.Api.Tests/BackupRecoveryHardeningTests.cs`
- M `src/Alveara.Api.Tests/ImportCommitTests.cs`
- M `src/Alveara.Api.Tests/PatientExportApiTests.cs`
- M `src/Alveara.Api.Tests/PatientExportServiceTests.cs`
- M `src/Alveara.Api.Tests/PatientExportsApiTests.cs`
- M `src/Alveara.Api.Tests/PatientImportValidatorTests.cs`
- A `src/Alveara.Api.Tests/PatientMergeSqlBoundaryTests.cs`
- M `src/Alveara.Api.Tests/PatientReadableRecordServiceTests.cs`

## Client source and config (src/alveara-client/src, index.html, vitest.config.ts, chain config) (12)
- M `src/alveara-client/index.html`
- A `src/alveara-client/playwright.chain.config.ts`
- M `src/alveara-client/src/app/AppShell.tsx`
- M `src/alveara-client/src/pages/LoginPage.tsx`
- M `src/alveara-client/src/pages/backup/RecoveryKeyPanel.tsx`
- M `src/alveara-client/src/pages/backup/backupMessages.ts`
- M `src/alveara-client/src/pages/exports/ExportCenterPage.tsx`
- M `src/alveara-client/src/pages/portability/PatientImportPage.tsx`
- M `src/alveara-client/src/services/patientExportsApi.test.ts`
- M `src/alveara-client/src/services/patientExportsApi.ts`
- M `src/alveara-client/src/services/patientPortabilityApi.ts`
- M `src/alveara-client/vitest.config.ts`

## Client browser specs (e2e, chain) (4)
- A `src/alveara-client/chain/stage-0.spec.ts`
- M `src/alveara-client/e2e/auth-real-backend.spec.ts`
- M `src/alveara-client/e2e/patient-export-package-real-backend.spec.ts`
- M `src/alveara-client/e2e/patient-portability-real-backend.spec.ts`

## Scripts (runner, safety gate, evidence builder, checks, dev scripts) (8)
- M `scripts/DevEnvironment.ps1`
- A `scripts/Invoke-AccumulatedChain.ps1`
- M `scripts/Start-DevApi.ps1`
- M `scripts/Start-DevClient.ps1`
- A `scripts/chain/Build-ChainEvidence.mjs`
- A `scripts/chain/ChainSafety.ps1`
- A `scripts/stabilization/Test-DispositionCompleteness.mjs`
- A `scripts/stabilization/trackedExceptions.mjs`

## Repository tests (8)
- A `tests/brand-inventory.test.mjs`
- A `tests/stabilization/chain-evidence.test.mjs`
- A `tests/stabilization/chain-safety.test.mjs`
- A `tests/stabilization/dev-script-refusals.test.mjs`
- A `tests/stabilization/disposition-completeness.test.mjs`
- A `tests/stabilization/docs-hygiene.test.mjs`
- A `tests/stabilization/tracked-exceptions.test.mjs`
- M `tests/story-000-coexistence.test.mjs`

## Stabilization registers and inventory (3)
- A `docs/stabilization/BRAND_INVENTORY.json`
- A `docs/stabilization/DISPOSITION_REGISTER.json`
- A `docs/stabilization/TRACKED_EXCEPTIONS.json`

## Design records (this story) (4)
- A `docs/design/ALV-N019_DEPENDENCY_ADVISORY_REPORT.md`
- A `docs/design/ALV-N019_PRE_IMPLEMENTATION_BRIEF.md`
- A `docs/design/ALV-N019_PRE_IMPLEMENTATION_BRIEF_REVIEW.md`
- A `docs/design/OWNER_REQUEST_FIRST_RUN_SETUP.md`

## Operator docs (3)
- M `docs/PATIENT_PORTABILITY.md`
- M `docs/RUNNING_LOCALLY.md`
- M `docs/operations/BACKUP_AND_RECOVERY.md`

## Progress log (1)
- M `PROGRESS.md`
