# Test results - ALV-013-C01 R01

All runs on the final implementation commit `b94eb699193b7e25a0bb236684eeb0a2c5ebda10` with a clean working tree (real SQL Server LocalDB for backend and real-backend runs). Raw outputs are under `artifacts/`.

| Suite | Command | Result |
|---|---|---|
| Backend, full | `dotnet test Alveara.slnx` | **2,265 of 2,265 passed**, 0 failed, 0 skipped (2 h 02 m). New: 100. |
| Frontend, full | `npx vitest run` | **1,194 of 1,194 passed** (75 files). New: 78. |
| Type check / build / lint | `npx tsc --noEmit`, `npm run build`, `npm run lint` | tsc exit 0, build ok, no oxlint finding in any diagnosis file |
| Mocked browser | `npm run test:e2e` | **136 of 136 passed** |
| Repository checks | `node --test tests/*.test.mjs` | **8 of 8 passed** |
| Real-backend walkthrough (new) | `playwright.diagnoses-structure.config.ts` | **12 of 12 passed**; axe scans (chips and links shown, amend, resolve and link forms open, history, 768 px tablet), each in light and dark, **0 critical or serious** |
| Parent regression: original STORY-013 walkthrough, **unchanged** | `playwright.diagnoses.config.ts` | **12 of 12 passed** |
| Regression walkthroughs on the same tree | auth 12, patients 7, workspace 14, forms 10, schedule 10, calendar 12, flow 10, board 15, clinical 9, companion 9, safety 11, odontogram 11, odontogram longitudinal 11, perio 10, perio sessions 12 | **all passed** |
| Mutation checks | scripted breakages of the new rules (below) | **10 of 10 killed** |

## Backend: the parent's tests and the new ones

The four STORY-013 backend classes (`DiagnosisRulesTests` 51, `DiagnosisSchemaTests` 28 + `DiagnosisMigrationTests` 1, `DiagnosisServiceTests` 37, `DiagnosisApiTests` 28) pass with **no test file edited** (`git diff fd7e152 b94eb69` over those files is empty).

| New class | Passed |
|---|---:|
| `DiagnosisStructureRulesTests` | 46 |
| `DiagnosisStructureSchemaTests` | 19 |
| `DiagnosisStructureMigrationTests` | 1 |
| `DiagnosisStructureServiceTests` | 19 |
| `DiagnosisStructureApiTests` | 15 |
| **Total new** | **100** |

## Frontend: the parent's tests and the new ones

`diagnosisRules.test.ts` (46), `DiagnosisApi.test.ts` (17) and `Diagnoses.test.tsx` (19) from STORY-013 pass with no edit. New: `diagnosisStructureRules.test.ts` (the client mirror of the server's structure rules, held to the same cases and codes), `DiagnosisStructureApi.test.ts` (the typed client against the fake) and `DiagnosisStructure.test.tsx` (the screen: coding, source and region on the record form, structure problems listed and linked, amend with a reason and a stale or dropped connection, resolve and reactivate, link picker offering only the patient's own records, a read-only role, axe in light and dark); 78 new in all.

## Mutation checks

Ten deliberate breakages of the new rules, each built and run against the new classes and `DiagnosisServiceTests` and `DiagnosisApiTests` (145 tests), then reverted (the build was checked each time). **Every one made at least one test fail.**

| Breakage | Tests that failed |
|---|---:|
| M1 a coding system without its code is accepted | 4 |
| M2 a source note is accepted on a manual diagnosis | 1 |
| M3 a region together with a tooth is accepted | 2 |
| M4 an amendment writes no history entry | 3 |
| M5 a withdrawn diagnosis can be amended | 2 |
| M6 a link to another patient's finding is not refused by the service | 2 |
| M7 a treatment-plan reference can be marked resolved | 3 |
| M8 resolving needs no reason | 2 |
| M9 the default list drops resolved diagnoses | 2 |
| M10 an amendment drops the treatment-plan reference | 2 |

Raw output: `artifacts/test-runs/mutation-checks.txt`.

## Defect found and fixed by this attempt's own verification

The code-character check constraint (`CK_Diagnoses_CodeChars`) rejected codes containing a hyphen, such as `P-9`. A schema test that records a code with every allowed character found it; the constraint now removes hyphens before testing the remaining characters, in the model, the migration and the snapshot.

## Disclosures (read these before relying on the numbers)

1. **The mutation filter did not include every STORY-013 class.** It covered the new classes plus `DiagnosisServiceTests` and `DiagnosisApiTests`, but **not `DiagnosisRulesTests` or `DiagnosisSchemaTests`**. All STORY-013 classes were run on the unmutated tree inside the full backend run (2,265 of 2,265), but they were not part of the per-mutant runs. This attempt does not claim the stricter standard of running the complete parent filter against every mutant. Whether to re-run is for the reviewer.
2. **No `.trx` file was captured for the full backend run**; `artifacts/test-runs/backend-full-summary.txt` holds the runner's summary line (2,265 passed, 2 h 2 m). The per-class counts above come from the runner's test list and the earlier filtered runs.
3. **Screenshots and the evidence JSON** under `artifacts/walkthrough/` come from a standalone run of the new walkthrough on the same code (12 of 12); the 12 of 12 inside the final sweep wrote its screenshots to a default folder that was cleaned, and its console output is `artifacts/walkthrough/run-output.txt`.
4. **The frontend suite failed once** (1 of 1,116) in an earlier sweep for STORY-013 while real-backend runs loaded the machine; it was not identified and has not recurred in any run since (this attempt: several full runs, all green).
5. **Test speed.** A full backend run now takes about 2 hours. A separate proposal to shorten it is with the reviewer; **nothing in this attempt depends on it and no test infrastructure was changed.**
