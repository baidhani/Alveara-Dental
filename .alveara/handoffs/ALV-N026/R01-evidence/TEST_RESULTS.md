# ALV-N026 R01: test results

Route: focused proof only. `fullRegression: DEFERRED_TO_ALV-N028`. The owner approved the package sweep (about 30 minutes in total, more than 5 minutes) on 2026-11-22. Times are measured. Session `CC-20261115-m7x2`.

## D: final focused sweep on `77caf04`
- **Script:** `artifacts/gates/sweep_n026.sh`.
- **Logs and trx:** `artifacts/gates/n026-sweep-77caf04/`.
- **Per-test outcomes:** `artifacts/backend-focused-tests.csv`, 753 rows derived from the trx files, every one Passed. That covers the 660 sweep tests here plus the 93 of run F below.
- **Filters:** every group uses class-exact filters (`FullyQualifiedName~Tests.<Class>.`), and the sweep stops after a failing group.
- **Tree:** at the start the tree had 1 changed file, the new evidence folder itself; nothing under `src/` or `tests/` was changed.

| Group | Covers | Classes | Result | Time |
|---|---|---|---|---|
| G1 documents | S1, S2 (PD-24, U-008, F-130/131/125/171, Q-5bk/bs/bj/cx) | DocumentApiTests, DocumentBackupRestoreTests, DocumentClassificationAccessApiTests, DocumentContextServiceTests, DocumentImportServiceTests, DocumentIntegrityServiceTests, DocumentLibraryApiTests, DocumentLibraryFilterTests, DocumentLibraryServiceTests, DocumentOrphanServiceTests, DocumentPermissionTests, DocumentSettingsServiceTests, DocumentStorageApiTests, ExternalCaseControllerPermissionTests, ExternalCaseServiceTests, ExternalCaseApiTests | 354/354 | 369 s |
| G2 backup and status | S3 (PD-25, D-06, F-153, F-154) | BackupAlertApiTests, BackupAlertTests, BackupApiTests, BackupCryptoTests, SystemStatusTests | 59/59 | 78 s |
| G3 PDF engine | S4 | PdfEngineTests, InstallPdfEngineScriptTests | 16/16 | 13 s |
| G4 financial | S5 (D5, OQ-2) | BillingDocumentTemplateVersionServiceTests, BillingStatementsApiTests, BillingDocumentServiceTests | 52/52 | 101 s |
| G5 prescriptions | S6 (F-108/109/110/164/165, Q-5am/ao, D1/D2) | PrescriptionIssueOwnershipTests, PrescriptionPdfTextTests, PrescriptionWorkflowApiTests, PrescriptionWorkflowServiceTests | 74/74 | 89 s |
| G6 plan copy | S7 (D3) | TreatmentPlanCopyApiTests, TreatmentPlanCopyIssueOwnershipTests, TreatmentPlanCopyDiagnosisLabelTests | 19/19 | 47 s |
| G7 record copy and merge | S8 and its repair (D4, merge scope, U-020, U-022) | ClinicalRecordCopyApiTests, ClinicalRecordCopyOwnershipTests, ExportDomainCatalogTests, PatientOwnedDataTests, PatientIdExceptionTests, TreatmentPlanItemAppointmentLinkMergeTests, PatientMergeSchemaTests, RetiredRecordGuardTests, MergeTriggerIntegrityTests, PatientMergeApiTests, PatientUnmergeServiceTests | 86/86 | 212 s |
| **Server total** | | **45 classes** | **660/660** | about 15 min |

Client and repository gates on `77caf04`:

| Gate | Result |
|---|---|
| `tsc --noEmit -p tsconfig.app.json` | exit 0 (10 s) |
| `tsc --noEmit -p tsconfig.node.json` | exit 0 (2 s) |
| `npm run lint` | exit 0 (2 s) |
| `vite build` | exit 0 (3 s) |
| `vitest run` (focused: `src/pages/documents`, `src/pages/billing`, `src/pages/exports`, `src/pages/prescription`, the backup banner, AppShell navigation, `dateOnly`, BackupRecoveryPage, SystemStatusPage, TreatmentPlanPatientCopy, TreatmentPlanN023, `pdfApi`) | **355/357** (204 s). The 2 failures are the two `BillingStatementsPanel.test.tsx` tests recorded as **U-018**: a pre-existing, intermittent ALV-N024 test that also failed 7/10 at ALV-N024's own closure. |
| `node --test tests/*.test.mjs` | **1 failure**: `allergy-wording-guard`. It is a real defect caused by S8 and was fixed in run E. |
| Leak count (`AlveraTest_` databases) | 1 before, 1 after: unchanged. The one database left over from before is disclosed in earlier stories. |

## E: the allergy-statement restoration (`baaeb51`, owner decision 2026-11-22)
- **What the sweep found:** the `node --test` failure. S8 had deleted the old browser print, which was the only place that printed the allergy-check statement. The p2 PDF from S6 had silently dropped that statement.
- **The fix:**
  - the outcome recorded when the prescription was written is captured into the issue envelope;
  - the p2 template prints the three established wordings;
  - the p2 pin changed to `daa31289...`;
  - the guard now checks the p2 template;
  - three new PrescriptionWorkflowApiTests were added.
- **First run** (uncommitted tree, same source as `baaeb51`): 88/90. The two failures were wordings with apostrophes: Typst markup turned them into typographic quotes, so the text no longer matched the established wording. The template now prints the wordings as plain strings.
- **Rerun:** 90/90 (1 min 36 s), and `node --test` 58/58.

## F: final-tree check on `baaeb51`
- **Logs:** `artifacts/gates/n026-final-baaeb51/`.
- **Tree:** `src/` and `tests/` clean.

| Run | Result | Time |
|---|---|---|
| Prescriptions and PDF engine: PrescriptionIssueOwnershipTests, PrescriptionPdfTextTests, PrescriptionWorkflowApiTests, PrescriptionWorkflowServiceTests, PdfEngineTests, InstallPdfEngineScriptTests | **93/93** | 101 s |
| `node --test tests/*.test.mjs` | **58/58** | about 6 s |

`baaeb51` changes, relative to `77caf04`, only the prescription issue and PDF code, the p2 template and its pin, the allergy guard, PrescriptionWorkflowApiTests and PROGRESS.md. Groups G1, G2, G4, G6 and G7 and the client gates were **not** rerun on `baaeb51`; they ran on `77caf04`.

## C: negative controls on `baaeb51` (6; owner-approved)
- **Script and log:** `artifacts/negative-controls/negative_controls.py` (ALV-N025's script with the controls replaced) and `negative-controls-run1.txt`.
- **Method:** each control breaks one rule, rebuilds, runs only the targeted tests, restores the file byte-for-byte and proves the restore with `git diff --exit-code`.

| Control | Caught by | Result |
|---|---|---|
| C1 clinical-category read rule removed (ViewDocuments alone reads clinical documents) | DocumentClassificationAccessApiTests (OfficeManager, Billing, FrontDesk rows) | CAUGHT |
| C2 plan-copy reference-code integrity check removed | TreatmentPlanCopyApiTests `A_copy_whose_recorded_sources_no_longer_match_...` | CAUGHT |
| C3 record-copy key replay broken | ClinicalRecordCopyApiTests `A_second_request_is_a_new_issue_and_a_retry_with_the_same_key_...` | CAUGHT |
| C4 backup alert reaches only one administrator | BackupAlertTests `A_failure_reaches_every_enabled_administrator_...` | CAUGHT |
| C5 named exception silently dropped | ExportDomainCatalogTests, PatientOwnedDataTests, PatientIdExceptionTests (2) | CAUGHT |
| C6 appointment-link merge stand-aside removed | TreatmentPlanItemAppointmentLinkMergeTests, PatientMergeSchemaTests | CAUGHT |

Every restore was byte-identical, and the restored tree rebuilt with exit 0. Total 328 s.

## A: pre-existing proof for U-020 and U-022 (2026-11-22)
- **Where:** a disposable worktree at `03831e5` (before S8), `C:\wt8`, since removed.
- **Result:** PatientOwnedDataTests and ExportDomainCatalogTests gave 7/11. The same 4 failures (2 registry tests, 2 catalog tests) appear there, naming `PatientRecordOpens`. That proves the failures came before S8.
- **A void first attempt:** worktree creation failed on a long path, so that run went to HEAD. It was set aside.

## Slice runs (wip, for history; from PROGRESS.md)
| Slice | Commit | Result |
|---|---|---|
| S1 | `56fe7c9` | A 243/243, B 128/128, client 93/93 |
| S2 | `95a321d` | 183/183, client 98/98 |
| S3 | `16cdc17` | 149/151 (2 = U-020, fixed in S8), client 98/98 |
| S4 | `c41520c` | engine 13/13, script 3/3 |
| S5 | `afafd76` | 76 (rerun 11/11), client 72/73 (U-018) |
| S6 | `68c926a` | 45/45, client 34/34 |
| S7 | `03831e5` | 6/6, 13/13, 16/18 (U-020), client 23/23 |
| S8 | `1ab6962` | 16/16, 78/80 (U-020), client 20/20, merge 53/56 (U-022) |
| S8 repair | `f75e33e` | 74/75 (my test setup), then 5/5 |
