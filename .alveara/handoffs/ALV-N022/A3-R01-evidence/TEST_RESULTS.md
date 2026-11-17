# ALV-N022 A3-R01: test results (Stage 1 focused proof; 2026-11-17)

Swept tree `ecfb7f4` (implementation `63f74ff` = `ecfb7f4` + one test-only commit, see the end); one sweep, 13:51:04-14:09:47 (-06:00, 18 m 43 s), tree clean before and after (`artifacts/summary.txt`). Windows 11, local SQL Server Express, Chromium.

| Run | Result | Duration | Output |
|---|---|---|---|
| `dotnet build Alveara.Api.Tests` | ok | 69 s | `artifacts/gates/backend_build.txt` |
| `dotnet test --filter` 12 classes (below) | **297 / 297** | 4 m 7 s | `artifacts/backend-focused-tests.csv` (from the trx) |
| `npx tsc -b` | exit 0 | 7 s | `artifacts/gates/tsc.txt` |
| `npm run lint` (oxlint) | exit 0 | 2 s | `artifacts/gates/lint.txt` |
| `npm run build` | exit 0 | 9 s | `artifacts/gates/build.txt` |
| `npx vitest run` focused selection (45 files) | **915 / 915** | 36 s | `artifacts/gates/vitest_focused.txt` |
| `npx playwright test --list` (mocked config) | a3-display listed 0 times; 194 tests in 13 files | s | `artifacts/gates/playwright_list.txt` |
| `node --test tests/*.test.mjs` | **58 / 58** | 5 s | `artifacts/gates/repo_checks.txt` |
| 11 walkthroughs, fresh database each (first with build) | **110 / 110** | 9 m 27 s | `artifacts/walkthroughs/` |
| Negative controls NC1-NC3 | as designed | 150 s | `artifacts/negative-controls/` |
| Leak counts before / after | identical | s | `artifacts/leak-before.txt`, `leak-after.txt` |

Backend per class: AllergyCheckTests 27, ClearanceServiceTests 24, DiagnosisApiTests 28, DiagnosisServiceTests 39, PerioSessionApiTests 28, PerioSessionServiceTests 38, PrescriptionServiceTests 26, PrescriptionWorkflowServiceTests 31, RealMigrationInventoryTests 5 (rollback-class count 30), SafetyApiTests 33, SafetyContextServiceTests 14, TypedLinkGuardMigrationTests 4.

vitest selection: `A3Slice4`, `OdontogramA3`, `pages/A3Display`, `ClinicalRecord`, `Clinical*`, `Diagnoses`, `DiagnosisStructure`, `Odontogram*`, `Perio*`, `Prescription*`, `Safety`, `Patient*`, `styles/patientWorkspaceContrast`, and the folders `pages/{diagnosis,treatmentplan,externalcases,perio,safety,odontogram,prescription}`, `pages/ProcedureCatalogPage`.

Walkthroughs (passed / elapsed incl. database and API start): a3-display 3 (28 s), safety 11 (54 s), perio-sessions 12 (60 s), clinical-companion 9 (65 s), external-cases 9 (71 s), diagnoses-structure 12 (48 s), completion-adoption 8 (43 s), prescription-workflow 15 (50 s), prescriptions 10 (39 s), perio 10 (55 s), odontogram-longitudinal 11 (54 s). The first eight touch an A3 surface directly; the others assert text near A3 changes (numbering, perio labels, the absent tooth).

## New or changed tests
- Server: `TypedLinkGuardMigrationTests` (new, 4: a direct clearance insert with another patient's document refused 51288 and its own accepted; a direct perio link to another patient's diagnosis refused 51289 and a DiagnosisId needs a Diagnosis link type; the migration rolls back and forward while no typed link exists and keeps legacy text; rolling back is refused once a typed link exists, nothing lost); `ClearanceServiceTests` (typed document receive/attach, free text, another patient's/unknown/inactive document, the name shown is file name and date, never the description); `PerioSessionServiceTests` (diagnosis link by id, free text, another patient's / withdrawn diagnosis); `SafetyApiTests`, `PerioSessionApiTests` (HTTP shapes); `PrescriptionWorkflowServiceTests` (list carries workflow fields and supersession); `SafetyContextServiceTests` (gap grammar); `RealMigrationInventoryTests` (count 29 -> 30, class named).
- Client: `OdontogramA3.test.tsx` (new, 5), `pages/A3Display.test.tsx` (new, 3), `A3Slice4.test.tsx` (new, 9), `PrescriptionList.test.tsx`, `CompletionResultPanel.test.tsx`, `Safety.test.tsx` (picker; the stale A1 strip test corrected), `PerioCompare.test.tsx`, `DiagnosisStructure.test.tsx`, `PerioSessionApi.test.ts`, `Clinical.test.tsx`, `ExternalCaseScreens.test.tsx`, `TreatmentPlanClinicalHistory.test.tsx`; fakes `fakeSafetyStore`, `fakePerioStore`, `fakeClinicalServer`.
- Repo: `tests/allergy-wording-guard.test.mjs` (new).
- Browser: `a3-display-real-backend.spec.ts` (new, 3); `safety`, `perio-sessions`, `external-cases`, `diagnoses-structure` specs updated.

## Negative controls (one at a time; byte backup, SHA-256 equal after restore; `artifacts/negative-controls/negative-controls.txt`)
| # | Planted break | Test | Expected | Result |
|---|---|---|---|---|
| NC1 | "so it is safe" added to the client no-match sentence | `tests/allergy-wording-guard.test.mjs` | fail | caught: exit 1, `uses the verdict word "safe"` |
| NC3 | absent mark for every state (Existing/Completed condition removed) | `OdontogramA3.test.tsx` | fail | caught: 2 failed (the rule test and the Diagnosed-tile test) |
| NC2 | the service's same-patient condition removed in `RequirePatientDocumentAsync` | `ClearanceServiceTests.Another_patients_document...`, `TypedLinkGuardMigrationTests.A_clearance_pointing...`, `SafetyApiTests` | stay green (database trigger refuses) | 35 / 35 passed: the refusal is held by trigger 51288 |

## Leak counts
Identical before and after (2 databases, 1 `AlveraTest_*`, 0 templates/restores, 0 SQL logins, folder counts equal). The one `AlveraTest_*` database and the Temp template backup existed before the sweep (left by an earlier session); disclosed, not removed.

## Re-run on the implementation commit `63f74ff` (`artifacts/gates-63f74ff/`)
| Run | Result |
|---|---|
| `npx tsc -b`, `npm run lint` | exit 0, exit 0 |
| focused vitest, run 1 | 916 / 917: `ClinicalNotes.test.tsx` "shows the conflict banner for a stale tab..." received `OriginalMy version` (pre-existing timing-sensitive test, ALV-005-C01; the saved text arrived after `user.clear()` under load); alone 5/5 passed |
| focused vitest, run 2 | **917 / 917** (the 915 of the sweep + the 2 added tests) |
| planted-break checks for the 2 added tests | withdrawn diagnosis offered -> F-098 test failed; Q-5aa paragraph removed -> Q-5aa test failed; both restored |

## Not run
Stage 2 (to be defined by the reviewer), full backend / vitest / mocked Playwright suites, other walkthroughs, chain (`DEFERRED_TO_ALV-N028`).
