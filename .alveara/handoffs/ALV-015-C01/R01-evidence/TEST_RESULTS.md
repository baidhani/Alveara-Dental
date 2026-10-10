# ALV-015-C01 R01 - Test results

All runs are on the implementation commit `3ec1215044916ed728ac643f98c876a3466fb3d3`'s tree, 2026-10-10, on one Windows machine with SQL Server LocalDB. Raw outputs are under `artifacts/`.

| Layer | Command | Result |
|---|---|---|
| Backend, full, serial | `dotnet test --no-build --logger trx` in `src/Alveara.Api.Tests` (runner `parallelizeTestCollections:false`, unchanged) | **2640 of 2640 passed, 0 failed, 0 skipped** (1 h 17 m; started 18:32:53Z). The previous full run was 2534; this attempt adds **106** in six classes (`artifacts/test-runs/backend-full-summary.txt`) |
| Frontend type check | `npx tsc --noEmit -p tsconfig.app.json` | exit 0 |
| Frontend production build | `npm run build` | built |
| Frontend lint | `npm run lint` (oxlint) | exit 0, 0 errors, 16 warnings, none in this attempt's files |
| Frontend unit | `npx vitest run` (85 files) | **1308 of 1308 passed** on the final run; the previous count was 1240, so this attempt adds **68** (7 new test files and 1 extended). The first two full runs had 5 and 1 timeouts, disclosed below |
| Mocked browser suite | `npx playwright test` | **136 of 136 passed** |
| Repository checks | `node --test tests/*.test.mjs` | **8 of 8 passed** |
| Real-backend walkthrough (new) | `playwright.treatment-plans-complete.config.ts` on a fresh database | **10 of 10 passed**; axe light and dark, 0 critical/serious on 7 views |
| Real-backend walkthroughs, full sweep | each of 20 on its own fresh database (`alveara_c01_*`) | the 17 existing: **187 of 187**; procedures **7 of 7**; STORY-015 **11 of 11**; new **10 of 10**: **215 of 215** (`PARENT_REGRESSION.md`) |
| Leak check | databases and logins counted before and after the full backend run and after the sweep | unchanged: AlveraTest 54, AlveraRestore 59, logins 273, `alveara_` 248 (`artifacts/test-runs/leak-check.txt`); the 20 sweep databases were deleted after their output was saved |
| Migration reversibility | `dotnet ef database update AddTreatmentPlans`, then the latest, on a scratch database | after Down the five tables, the thirteen triggers and the two plan columns are gone and `ChangeType` is 13 characters again; after Up all are back; scratch database dropped. In the suite: `TreatmentPlanRevisionMigrationTests` (rolling back keeps the plan; rolling forward gives it exactly one revision holding its procedures) |
| Negative controls | five mutants, each restored byte-identical (SHA-256) | all five caught: service same-patient filter 2 tests, partial-acceptance conflict 3, trigger 51109 1, trigger 51114 1, withdrawn flag in revisions 2 (`artifacts/test-runs/negative-controls.txt`) |

## The 106 new backend tests (counted from the trx file)
| Class | Tests | What they prove |
|---|---|---|
| `TreatmentPlanRevisionSchemaTests` | 53 | Bypassing the service: every new check constraint and unique index; every trigger 51099-51115 and 51117 with its exact error number; append-only on all five tables; the foreign-key phase placement; Completed final and a completed procedure cannot be withdrawn; resolution same-patient and same-text |
| `TreatmentPlanRevisionMigrationTests` | 1 | Rolling back keeps the plan, its procedures and its history; rolling forward backfills exactly one revision with the procedures (the withdrawn one marked) |
| `TreatmentPlanRevisionServiceTests` | 9 | Revisions on every change; explicit revise; phases and placement; null keeps, blank clears; every wrong field named at once; stale revision; comparison of two revisions |
| `TreatmentPlanStateServiceTests` | 12 | Answers with attribution; a decline needs a reason; repeats quiet; two people at once; whole-plan answers and the partial-acceptance conflict; scheduling and completion; the failed-audit path; a completed procedure cannot be withdrawn |
| `DiagnosisReconciliationTests` | 10 | Reference codes; preview and apply; same-patient success; idempotency (including two runs at once); history preservation; no-match and ambiguous retention; cross-patient denial; the reference lock; the failed-audit path |
| `TreatmentPlanCompletionApiTests` | 21 | 401 on every new route; who can read and who can change; CSRF; per-field refusals; 409s and 404s; the whole workflow; the estimate label; reconciliation through the API |

## The 68 new frontend tests
`treatmentPlanLayout.test.ts` (grouping, the revise draft, words), `TreatmentPlanRevise.test.tsx` (the builder and the revise form), `TreatmentPlanAnswers.test.tsx` (answers, scheduling, completion), `TreatmentPlanRevisions.test.tsx` (history and comparison), `TreatmentPlanPatientCopy.test.tsx`, `TreatmentPlanReferences.test.tsx`, `DiagnosisPlanReference.test.tsx`, and additions to `treatmentPlanRules.test.ts`. Every form or page that opens has an axe check.

## Disclosures
- **vitest timeouts.** The first full run had 5 failures and the second had 1, every one "Test timed out in 5000ms" in `Perio.test.tsx` and `PerioCompare.test.tsx` (existing periodontal grid tests, untouched here); the third run passed 1308 of 1308. Investigated, not assumed: alone, both files pass (52 of 52) with the slowest at 1.2 to 2.6 s; the same files measured three times on a clean copy of the previous commit and on this tree gave the same timings; a clean copy of the previous commit passed the whole suite twice (1240 of 1240). In the full parallel run, directly after a 77-minute backend run and a build, with other applications using about 35% of the CPU, the same tests took 5 to 7 s. This is a thin margin against the 5 s default (and a larger suite adds parallel load), not a defect in this attempt; the remedy changes files outside this story and is recommended to the owner. Run 1's output is kept as `artifacts/test-runs/vitest-run1-transient-timeouts.txt`.
- The backend run took 1 h 17 m against 54 m 48 s for the previous full run: 106 more tests, and it was not a timing-measurement session.
- The full backend run started from the working tree that was committed as `3ec1215`; the differences between that tree and the commit are `PROGRESS.md` (the entry written after the run) and the files under `.alveara/` (this evidence).
- The first, standalone browser run of the new spec failed once on a selector in the spec and once on the contrast defect described in `HANDOFF.md`; both were fixed and the spec then passed on a fresh database (10 of 10), and again in the sweep.
- Mutation checks cover five rules, not the whole feature.
- Databases created by the sweep were deleted after their output was saved; the databases the backend run creates are left in place per the owner's rule (the counts above show none leaked).
