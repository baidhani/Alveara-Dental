# ALV-015-C01 R02 - Test results

All runs are on the R02 implementation commit `3fbb79852c39a0712ecdcfed1917c687ecf3526a`'s tree, 2026-10-11 (the full backend run: 00:11:08Z start), one Windows machine, SQL Server LocalDB. Raw outputs under `artifacts/`.

| Layer | Command | Result |
|---|---|---|
| Backend, full, serial | `dotnet test --no-build --logger trx` (runner `parallelizeTestCollections:false`, unchanged) | **2649 of 2649 passed**, 0 failed, 0 skipped, 43 m 20 s. R01 was 2640; R02 adds 9 (`artifacts/test-runs/backend-full-summary.txt`) |
| Backend, affected, before the full run | `--filter` on the new classes, `TreatmentPlanRevisionServiceTests` and the edited retry test | 19 of 19 |
| Frontend | `tsc --noEmit` exit 0; `npm run lint` 0 errors; `npm run build` built | `artifacts/test-runs/frontend-gates.txt` |
| vitest | `npx vitest run` (85 files) | **1309 of 1309** (R01 1308; +1 test); `ProcedureCatalogPage.test.tsx` 22 of 22; no timeouts |
| Mocked browser suite | `npx playwright test` | **136 of 136** |
| Repository checks | `node --test tests/*.test.mjs` | **8 of 8** |
| Real-backend sweep | 20 runs, each on its own fresh database (`alveara_r02_*`) | **216 of 216** (auth 12, patients 7, workspace 14, forms 10, schedule 10, calendar 12, flow 10, board 15 (rerun, see below), clinical 9, companion 9, safety 11, odontogram 11, odontogram longitudinal 11, perio 10, perio sessions 12, diagnoses 12, diagnoses structure 12, procedures 7, STORY-015 11, complete 11) |
| Leak check | counts before and after | unchanged: AlveraTest 54, AlveraRestore 59, logins 273, alveara_ 248 |
| Negative controls | 3 mutants, each restored byte-identical (SHA-256) | all caught: registration removed 3 tests, withdrawn not counted 2, count independent of procedure 2 (`artifacts/test-runs/negative-controls-r02.txt`) |

## The 9 new backend tests
`TreatmentPlanProcedureUsageTests` (5, service level): usage lists "Treatment plans" with the count and zero when unused; counts include withdrawn plans and other patients; inactivation needs acknowledgement; an unused procedure inactivates freely; the snapshot stays readable and new work is refused. `TreatmentPlanProcedureUsageApiTests` (4, production wiring through `Program.cs`): the usage answer counts a plan procedure and lists the source with 0 when unused; withdrawn, completed and other-patient items count; inactivation is refused (409) until acknowledged, the plan still shows the item and fee, and adding the inactive procedure to new work is refused (400); the usage answer carries only numbers.

New frontend: one vitest test, one real-backend walkthrough test (screenshots 13 and 14).

## Disclosures
- First full backend run: 2 failures, both explained, in `backend-full-summary.txt` and `R02.md`; the final run passed.
- Sweep: the visit-board walkthrough failed its first test once (5 s heading wait after page load; 9 did not run), then passed 15 of 15 twice on fresh databases; the board code is untouched. The R01 sweep passed 15 of 15.
- Build: four nullability warnings (CS8631, CS8619) in R01 test files remain; one xUnit2013 warning was fixed.
- The 22 sweep databases (20 plus two board reruns) were dropped after their output was saved; the databases the backend run creates are left per the owner's rule (counts flat).
- Mutation checks cover three rules, not the whole feature.
