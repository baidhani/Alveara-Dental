# ALV-N025 R01: test results

Route: focused proof only. `fullRegression: DEFERRED_TO_ALV-N028`. The owner authorised runs A to D on 2026-11-20 (a test group of 5 minutes or less needs no permission; longer runs were approved by the owner). Times are measured.

## D: final focused sweep
Script: `artifacts/gates/sweep_d.sh` and `sweep_d2.sh`. Logs: `artifacts/gates/n025-sweep-3dcaf8d/`, `artifacts/gates/n025-sweep-d2/`. Per-test outcomes: `artifacts/backend-focused-tests.csv` (487 rows, derived from the trx files). Each group stops the sweep if it fails or takes longer than 300 s.

| Group | Tree | Classes (class-exact filters) | Result | Time |
|---|---|---|---|---|
| G1 identity | `3dcaf8d` | PatientDuplicateWarningTests, PatientRegistrationServiceTests, PatientIdentityApiTests, PatientDuplicateScannerTests, PatientDuplicateReviewTests, ImportPatientDryRunTests, ImportCommitTests | 117/117 | 98 s |
| G2 MRN and merge | `3dcaf8d` | MrnTests, MrnMigrationTests, MergeTriggerIntegrityTests, PatientMergeSchemaTests, PatientUnmergeSchemaTests, PatientMergeServiceTests, RetiredRecordGuardTests, HouseholdDissolveTests | 69/69 | 190 s |
| G3 demographics and files | `3dcaf8d` | DemographicsValidationTests, DemographicsMigrationTests, PatientExportServiceTests, PatientImportValidatorTests, PatientImportServiceTests, PatientExportApiTests, PatientImportApiTests, PatientExportsApiTests | 97/97 | 101 s |
| G4 audit and access, first run | `3dcaf8d` | AuditLogApiTests, AuditSessionLifecycleTests, PatientAccessTrailTests, DocumentApiTests, AuthControllerMfaConfirmSessionTests, AccountServiceMfaTests, ObservabilityClassificationTests, PermissionPolicyWriteTests, AppointmentSchedulerTests | 203/204: my own nondeterministic assertion (`FirstAsync` without an order), so the sweep stopped | 192 s |
| G4 after the test-only fix | `f0c474b` | the same | **204/204** | 191 s |
| **Server total** | | **32 classes** | **487/487** | |

Client gates on `f0c474b` (`n025-sweep-d2/`):

| Gate | Result |
|---|---|
| `tsc --noEmit -p tsconfig.app.json` | exit 0 (9 s) |
| `tsc --noEmit -p tsconfig.node.json` | exit 0 (2 s) |
| `npm run lint` | exit 0, warnings only (1 s) |
| `vite build` | exit 0 |
| `vitest run` (full) | **2140 passed, 1 failed** (166 s). The failure is `BillingStatementsPanel.test.tsx` (ALV-N024), recorded as **U-018**: pre-existing and intermittent. Measured over 10 runs of that file alone: `f0c474b` 5/10, the ALV-N025 component change reverted 6/10, ALV-N024's closure `996dec1` in a clean worktree with its own `npm ci` 7/10. The owner kept it outside ALV-N025. |

## C: negative controls (11; owner-approved)
Driver: `artifacts/negative-controls/negative_controls.py`. Logs: `negative-controls-run1.txt` and `negative-controls-C8-rerun.txt`. For each control: the edit applied, a rebuild, only the targeted tests run, then the file restored and checked with `git diff --exit-code`. Every restore was clean, and the final rebuild exited 0. Total 538 s, plus 61 s for the C8 re-run.

| Control | Rule broken | Caught by |
|---|---|---|
| C1 | MRN retry removed (a collision is rethrown at once) | MrnTests concurrent race (20 rounds) and retry cap |
| C2 | guard 51298 disabled (`RETURN` instead of `THROW`) | MrnTests `A_number_never_changes_by_sql_or_through_ef` |
| C3 | once-per-session check removed | PatientAccessTrailTests once-per-session |
| C4 | username fallback removed | AuditLogApiTests names |
| C5 | different-birth-date rule removed | PatientDuplicateWarningTests (3 cases) |
| C6 | `WHERE EndedAtUtc IS NULL` removed from the expiry pass | AuditSessionLifecycleTests 20-round race |
| C7 | permission check removed from the History route | AuditLogApiTests BR-02 |
| C8 (MA-01) | the MRN guard given the merge stand-aside line | MrnTests merge-session test and PatientMergeSchemaTests named-exception assertion. **The first run was invalid:** the control's own SQL quoting broke the template database. The re-run failed on the assertions themselves. |
| C9 (MA-01) | the MRN guard's trigger order not set | MrnTests merge-session test (51298 expected, another guard answered) |
| C10 | the U-017 stand-aside line not added | MergeTriggerIntegrityTests U-017 merge |
| C11 | the U-016 `Clearances.DocumentId` term not added | MergeTriggerIntegrityTests U-016 and PatientMergeSchemaTests column rule |

## B: browser
`identity-audit-integrity-real-backend.spec.ts` through `scripts/Invoke-RealBackendWalkthrough.ps1` (fresh database, dropped afterwards): **6/6** (12.6 s; 30 s with the runner).
- The two earlier attempts failed in the setup: the app did not load, because of the build defect disclosed in R01.md.
- Output: `artifacts/walkthrough/` (5 screenshots, `identity-audit-e2e.json`).

## A: new tests before the final commit
AuditSessionLifecycleTests 8/8 (28 s); PatientWorkspace.test.tsx 26/26 (11 s).

## Slice runs (wip, for history)
Each slice's focused runs and first-run failures are in PROGRESS.md and the wip commit messages. The client "tsc" claims made in slices 1-6 were vacuous (see R01.md disclosure 1). Only the gates above count.
