# Test results - ALV-006-C01 R01

All runs on the final implementation commit `90f6ebd6cefc2bbb99149c452a9d13ae4f3cb9a0` (real SQL Server LocalDB for backend and real-backend runs).

| Suite | Command | Result |
|---|---|---|
| Backend, full | `dotnet test` in `src/Alveara.Api.Tests` | **1,711 of 1,711 passed**, 0 failed, 0 skipped (1 h 34 m). New: 133. |
| Frontend, full | `npx vitest run` | **899 of 899 passed** (64 files). Up from 873; 26 new: `OdontogramLongitudinal.test.tsx` 22 and 4 more contrast checks. |
| Type check / build / lint | `npx tsc --noEmit -p tsconfig.app.json`, `npm run build`, `npm run lint` | tsc exit 0, build ok, no lint finding in the new or changed odontogram files |
| Mocked browser | `npx playwright test` | **136 passed (17.0s)** |
| Repository checks | `node --test tests/*.mjs` | **8 of 8 passed** |
| Real-backend walkthrough (new) | `playwright.odontogram-longitudinal.config.ts` | **11 of 11 passed**; axe scans of the mixed chart, a tooth's history with its links, the catalogue with its add form and the 768 px tablet layout, each in light and dark, **0 critical/serious** |
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

## Frontend: the parent's tests and the new ones
The STORY-006 files (`Odontogram.test.tsx`, `OdontogramWrite.test.tsx`, `toothNumbering.test.ts`, `odontogramContrast.test.ts`) pass. Their only edits: three lookups scoped to the findings list (the new catalogue and tooth history also print "Caries" and "History"), one test now picks the Permanent view explicitly (a patient with a primary-tooth finding opens as Mixed, so the old premise "the chart does not draw primary teeth" became "the permanent view does not"), and the contrast test gained the new controls. New: `OdontogramLongitudinal.test.tsx` (22).

## Mutation checks
Backend: removing the dentition check failed the 2 targeted tests, removing the absent-tooth check failed 3 of 5, recording a link under the wrong history type failed its test. Frontend: removing the dentition filter, offering retired conditions, drawing catalogue controls for a reader and reversing ArrowLeft each failed one targeted test. All were reverted.

## Disclosures
- Failed tests in the full backend run: 0.
- One frontend test (`BackupRecoveryPage.test.tsx`, code this attempt does not touch) failed once during a full frontend run under load and passed on all 8 isolated reruns and on the next full run (899 passed (899)); recorded, not fixed.
- The earlier STORY-006 backend run recorded one intermittent failure in `BackupCryptoTests` (ALV-N004); it is unrelated and is carried forward as a recommended follow-up.
- Earlier attempts of the new walkthrough stopped at test-side mistakes (a strict-mode locator, an asynchronous list counted too early) and at one scenario the system correctly answered differently than the test expected (retiring an already-retired condition is a quiet repeat, so the stale-change scenario was rebuilt to produce a true conflict). The result above is the run on the final code.

Raw outputs are in `artifacts/` (`test-runs/`, `odontogram-longitudinal-walkthrough/`, `parent-regression-odontogram-walkthrough/`, `regression/`).
