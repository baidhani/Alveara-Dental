# STORY-017 test results (duplicate detection and controlled merge)

**Risk declaration: HIGH.** The story moves patient-owned data between records in production-shaped tables, adds a migration that touches 54 tables' triggers and a database exception to append-only records, appends a permission to the shared matrix, and writes audit entries. Per the testing rules: unit, integration (real SQL Server) and end-to-end, plus restore proof and negative controls.

## Gates, exact commands and results

| Gate | Command | Result |
|---|---|---|
| Story backend tests | `dotnet test --filter "FullyQualifiedName~PatientMerge\|FullyQualifiedName~PatientOwnedData\|FullyQualifiedName~PatientDuplicate"` | all passed: registry 6, scan 17, schema 12, service 15, API 6, restore proof 1 |
| Backend, full serial | `dotnet build --nologo` then, in `src/Alveara.Api.Tests`, `dotnet test --nologo --no-build --logger "trx;LogFileName=story017_full2.trx"` on the committed final tree (f7270da) | **5080 passed / 0 failed / 0 skipped** (43 min). The first full run, one commit earlier, was 5078 of 5080: it found a trigger-count test that counted the merge-scope triggers (fixed in f7270da) and a teardown error "cannot drop database ... in use" in `PermissionPolicyPipelineTests` that did not recur (3 of 3 isolated re-runs and the clean full run); a flake, not proven pre-existing |
| Related backend suites before the full run | the story tests plus the migration-rollback, inventory, architecture, backup/restore, patient, permission, authorization, audit, safety and treatment-plan classes | **1234 passed / 0 failed / 0 skipped** (23 min) |
| Frontend unit | `npx vitest run` | 104 files, **1659 passed** before the last screen fixes; the story's screen tests (12) re-run after them and pass |
| Type check and build | `npx tsc --noEmit`, `npm run build` | exit 0 each |
| Mocked browser | `npx playwright test` | **140 passed / 0 failed** (the new real-backend spec is in `testIgnore`) |
| Repository checks | `node --test tests/gate-tooling-coexistence.test.mjs tests/no-direct-db-access.test.mjs tests/status-labels.test.mjs tests/story-000-coexistence.test.mjs` | 13 passed / 0 failed |
| Real-backend walkthrough | `scripts/Invoke-RealBackendWalkthrough.ps1 -Config playwright.patient-merge.config.ts -OutputVariable PATIENT_MERGE_E2E_OUT` | **6 passed / 0 failed**; screenshots and `patient-merge-evidence.json` in `docs/testing/patient-merge-walkthrough/`; the accessibility scans (WCAG 2 A and AA, light and dark) found no serious or critical violation on the list, the review and the log |
| Restore proof | `PatientMergeBackupRestoreTests` (real backup, real restore into an isolated database) | passed: the retired patient in place and marked merged, the lineage with its moves and answers, the chart on the surviving patient, the two audit entries with the user and the time, the guards still refusing; a merge made after the backup is not in the restore; the live database is untouched |
| Negative controls | `docs/testing/STORY-017_NEGATIVE_CONTROLS.md` | 56 of 56 killed in the end |

## What was not run

* Lint: the project lint is oxlint (`npm run lint`); it was not recorded for this story, and a later run on the same code exits 0 with warnings only (see `ALV-017-C01` R01). An earlier version of this line wrongly said there is no ESLint configuration and that type-check is the lint gate.

## Defects found by the story's own verification

* Running a real merge showed that 61 of the append-only triggers are `INSTEAD OF` triggers, so the first version of the database exception made the move a silent no-op; the migration now performs the change inside them.
* Some of those triggers refuse every UPDATE, even one that changes no row, so a merge failed on a table with nothing to move; the exception now covers an empty update.
* The rows-affected number of an UPDATE is unreliable through those triggers; the lineage's move counts come from a count taken before, and a conservation test proves them.
* The permission registry file did not list the new permission; two registry tests failed until it was added.
* The real browser walkthrough showed the merged pair still listed as a possible duplicate, and the merge log showing an id instead of who; both fixed with tests.
* The first full serial run found a test that counted triggers by name prefix and so counted the 54 new merge-scope triggers; it now excludes them.
* Editing a merged-away record through the existing screens was a raw 500; it is now a 409 `patient_merged`.

## Housekeeping

Scratch databases dropped; the walkthrough drops its own database. Generated build output is not committed.
