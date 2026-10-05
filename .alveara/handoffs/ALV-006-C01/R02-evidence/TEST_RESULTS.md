# Test results - ALV-006-C01 R02

All runs on the final implementation commit `0b2aceb080c5432ec5a1a5e48cab76f4f9522400` (real SQL Server LocalDB for backend and real-backend runs).

| Suite | Command | Result |
|---|---|---|
| Backend, full | `dotnet test` in `src/Alveara.Api.Tests` | **1,736 of 1,736 passed**, 0 failed, 0 skipped (1 h 36 m). New in R01: 133; new in R02: 25. |
| Frontend, full | `npx vitest run` | **902 of 902 passed** (64 files). |
| Type check / build / lint | `npx tsc --noEmit -p tsconfig.app.json`, `npm run build`, `npm run lint` | tsc exit 0, build ok, no lint finding in the new or changed odontogram files |
| Mocked browser | `npx playwright test` | **136 of 136 passed** |
| Repository checks | `node --test tests/*.mjs` | **8 of 8 passed** |
| Real-backend walkthrough (the new one, updated in R02) | `playwright.odontogram-longitudinal.config.ts` | **11 of 11 passed**; axe scans of the mixed chart, a tooth's history with its links, the catalogue with its add form and the 768 px tablet layout, each in light and dark, **0 critical/serious** |
| Parent regression: original STORY-006 walkthrough, **unchanged** | `playwright.odontogram.config.ts` | **11 of 11 passed** |
| Regression walkthroughs on the same build | auth 12, patients 7, workspace 14, forms 10, schedule 10, calendar 12, flow 10, board 15, clinical 9, companion 9, safety 11 | all passed |

## Backend: the parent's tests and the new ones
The STORY-006 classes (`ToothNumberingTests`, `OdontogramSchemaTests`, `OdontogramServiceTests`, `OdontogramApiTests`) pass **209** tests unchanged in meaning (the only test-code change was giving the schema test's helper the finding's stored scope).

| New class | Passed |
|---|---:|
| `OdontogramCatalogueSchemaTests` | 35 |
| `OdontogramCatalogueMigrationTests` | 1 |
| `ConditionTypeServiceTests` | 38 |
| `OdontogramHistoryAndLinksTests` | 33 |
| `OdontogramCatalogueApiTests` | 26 |
| `ToothPresenceInvariantTests` | 21 |
| `ToothPresenceApiTests` | 4 |

## Frontend: the parent's tests and the new ones
The STORY-006 files (`Odontogram.test.tsx`, `OdontogramWrite.test.tsx`, `toothNumbering.test.ts`, `odontogramContrast.test.ts`) pass. Their only edits: three lookups scoped to the findings list (the new catalogue and tooth history also print "Caries" and "History"), one test now picks the Permanent view explicitly (a patient with a primary-tooth finding opens as Mixed, so the old premise "the chart does not draw primary teeth" became "the permanent view does not"), and the contrast test gained the new controls. New: `OdontogramLongitudinal.test.tsx` (25, three added in R02).

## Mutation checks (R02: the review finding)
Against `ToothPresenceInvariantTests` (21 tests): **disabling only the database trigger** failed the trigger tests and the concurrency tests (10 of 21, seven distinct tests); **removing only the service's two new checks** left all 21 passing, which shows the database alone enforces the rule (the service gives the clear message first); **removing both, which is the R01 behaviour**, failed 16 of 21 - the reviewer's exact sequence, the direct refusal, the way-out test, the sixteen simultaneous pairs, the simultaneous move-versus-record and the raw-SQL queueing tests. Earlier attempt's checks (dentition, absent-tooth and link rules; the frontend filters and arrow keys) are in the R01 package. All mutations were reverted.

## Disclosures
- Failed tests in the full backend run: 0.
- While developing R02, the test fixture failed once with a SQL collation error: the new trigger's staging table took the database's default collation, which conflicts with the case-sensitive condition code. Found by the migration being applied in the fixture and fixed (the column is declared with the matching collation).
- A run of the odontogram backend tests was interrupted while LocalDB was down (the instance had stopped); it was restarted, 48 orphaned `AlveraTest_*` fixture databases from the interrupted run were dropped, and the tests were rerun in full.
- An earlier mutation script ran against stale binaries after one mutation failed to compile; it was rewritten to check each build, and the numbers above come from the corrected run.
- The earlier STORY-006 backend run recorded one intermittent failure in `BackupCryptoTests` (ALV-N004); it is unrelated and remains a recommended follow-up. The R01 package also disclosed one intermittent frontend test (`BackupRecoveryPage.test.tsx`); it passed in every run of this attempt.

Raw outputs are in `artifacts/` (`test-runs/`, `odontogram-longitudinal-walkthrough/`, `parent-regression-odontogram-walkthrough/`, `regression/`). The R01 package and its review (`.alveara/reviews/ALV-006-C01/R01.md`) are preserved unchanged.
