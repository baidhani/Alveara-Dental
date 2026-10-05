# Test results - ALV-012-C01 R01

All runs on the final implementation commit `c4797b255570194ef4812ca3bded77f6d8a4d6c4` (real SQL Server LocalDB for backend and real-backend runs).

| Suite | Command | Result |
|---|---|---|
| Backend, full | `dotnet test` in `src/Alveara.Api.Tests` | **2,019 of 2,020 passed**, 1 failed, 0 skipped (1 h 49 m). New: 194. |
| Frontend, full | `npx vitest run` | **1034 of 1034 passed** (69 files). |
| Type check / build / lint | `npx tsc -b`, `npm run build`, `npm run lint` | tsc exit 0, build ok, no lint finding in the new or changed periodontal files |
| Mocked browser | `npx playwright test` | **136 of 136 passed** |
| Repository checks | `node --test tests/*.mjs` | **8 of 8 passed** |
| Real-backend walkthrough (new) | `playwright.perio-sessions.config.ts` | **12 of 12 passed**; axe scans of the entry screen, with a problem shown, the comparison with every site listed and the 768 px tablet entry layout, each in light and dark, **0 critical/serious** |
| Parent regression: original STORY-012 walkthrough, **unchanged** | `playwright.perio.config.ts` | **10 of 10 passed** |
| Regression walkthroughs on the same build | auth 12, patients 7, workspace 14, forms 10, schedule 10, calendar 12, flow 10, board 15, clinical 9, companion 9, safety 11, odontogram 11, odontogram_longitudinal 11 | all passed |

## Backend: the parent's tests and the new ones
The STORY-012 classes (`PerioRulesTests`, `PerioSchemaTests`, `PerioServiceTests`, `PerioApiTests`) pass **90** tests with **no test file edited**.

| New class | Passed |
|---|---:|
| `PerioSiteModelTests` | 38 |
| `PerioChartValidatorTests` | 25 |
| `PerioSessionSchemaTests` | 28 |
| `PerioMigrationTests` | 1 |
| `PerioSessionServiceTests` | 36 |
| `PerioComparisonTests` | 32 |
| `PerioComparisonServiceTests` | 6 |
| `PerioSessionApiTests` | 28 |

## Frontend: the parent's tests and the new ones
`Perio.test.tsx` (STORY-012, 33 tests) passes with no edit. New: `perioSiteModel.test.ts` (48), `PerioSessionApi.test.ts` (12), `PerioSteps.test.tsx` (20) and `PerioCompare.test.tsx` (19), one of the new tests being a whole mouth of 192 sites typed from the keyboard. `App.patientWorkspaceRoutes.test.tsx` was changed once in STORY-012 for the tab list and not again here. The shared fake API (`fakePerioStore.ts`, routed by `fakeClinicalServer.ts`) was extended with sessions, comparison and links; the STORY-012 tests that use it are unchanged.

## Mutation checks
Eleven deliberate breakages of the new rules, each built (the build was checked) and run against the rule, site-model, comparison and session-service tests, then reverted. **Every one made at least one test fail.**

| Breakage | Tests that failed |
|---|---:|
| M1 readings on an excluded tooth are accepted | 4 |
| M2 readings on an odontogram-missing tooth are accepted | 4 |
| M3 a furcation grade is accepted on a single-rooted tooth | 3 |
| M4 the sweep visits the right-hand teeth in the left-hand direction | 6 |
| M5 a stale edit is accepted (the row version is not checked) | 2 |
| M6 finalize does not re-check teeth the odontogram records as missing | 1 |
| M7 a 1 mm change counts as better or worse | 3 |
| M8 sites in only one chart are not counted | 3 |
| M9 the chart's audit entry is not written when a session is finalized | 4 |
| M10 a save ignores the sites it was asked to clear | 2 |
| M11 the comparison takes the previous chart from any patient | 1 |

## Disclosures
- Failed tests in the full backend run: 1 - Alveara.Api.Tests.BackupCryptoTests.A_file_that_is_not_a_backup_is_rejected_with_its_own_reason. That one is the intermittent ALV-N004 `BackupCryptoTests` case already on record as a follow-up in earlier handoffs (a test that feeds random bytes, some of which look like a backup header); it passed 22 of 22 on six isolated reruns (`artifacts/test-runs/backupcrypto-reruns.txt`) and is in code this story did not touch.
- Test-side mistakes found and corrected while writing the tests (not product defects): a tuple-type error, wrong expectations about which tooth a sorted list starts with, an unticked-measure letter typed into a number box in one test, FDI 47 being Universal 31 not 30, and the history table naming sites by their codes.
- Defects found by the tests and fixed in the product: a number box accepted letters (it now ignores them); the dark-mode colour of the inline error text was a fallback that failed contrast (the periodontal styles now use the shared theme tokens, which also corrects STORY-012's borders in dark mode); a heading level skipped (h3 to h5); the comparison read "0 sites were charted only in the earlier chart" (it now names only the side that has sites).
- A mutation script left a stale compiled library once (a restored file kept its old timestamp, so the build skipped it); noticed because one test failed on the restored tree, fixed by touching the files, and the final runs use a clean build.
- The six `architecture-map.spec.ts` tests that assumed no portal story is mid-build were made data-driven during STORY-012 (already committed and portal-verified); no change here.

Raw outputs are in `artifacts/` (`test-runs/`, `walkthrough/`, `parent-regression-walkthrough/`, `regression/`).
