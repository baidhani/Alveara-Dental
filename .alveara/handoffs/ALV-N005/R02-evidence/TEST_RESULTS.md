# ALV-N005 R02 - Test results

All runs are on the corrected tree (implementation commit `0ef1ab491ea56aa935a52a4d55fc5c6535860b6e`), 2026-10-08, on one Windows machine with SQL Server LocalDB. Raw outputs are under `artifacts/`. R01's results are in `../R01-evidence/TEST_RESULTS.md` and are preserved.

| Layer | Command | Result |
|---|---|---|
| Backend, full, serial | `dotnet test --no-build --logger trx` in `src/Alveara.Api.Tests` (runner `parallelizeTestCollections:false`, unchanged) | **2405 of 2405 passed, 0 failed, 0 skipped** (48 m 58 s). R01 was 2386; this attempt adds **19**. The catalog classes total **103**: rules 30, lifecycle 17, schema 33, API 23. `artifacts/test-runs/backend-full-summary.txt` |
| Frontend type check | `npx tsc --noEmit -p tsconfig.app.json` | exit 0 |
| Frontend production build | `npm run build` | built |
| Frontend lint | `npm run lint` (oxlint) | exit 0, 0 errors, 17 warnings; 1 in the new files (the same warning 13 existing places carry) |
| Frontend unit | `npx vitest run` | **1215 of 1215 passed** (76 files). R01 was 1214; this attempt adds **1** |
| Mocked browser suite | `npx playwright test` | **136 of 136 passed** |
| Repository checks | `node --test tests/*.test.mjs` | **8 of 8 passed** |
| Real-backend walkthrough (catalog) | `playwright.procedures.config.ts` on a fresh database, standalone | **7 of 7 passed** (R01: 6; a CDT step added); axe light and dark, 0 critical/serious |
| Real-backend walkthroughs, full sweep | each of 18 on its own fresh database (`alveara_n5r2_*`), 15:41:05Z to 15:53:05Z | the 17 existing: **187 of 187 passed**; catalog **7 of 7** (`PARENT_REGRESSION.md`) |
| Leak check | databases and logins counted before and after the full backend run | unchanged: AlveraTest 54, AlveraRestore 59, logins 273, `alveara_` 230 (`artifacts/test-runs/leak-check.txt`; the 18 sweep databases are created after this check and are intentionally left, per the standing no-delete rule) |
| Migration reversibility | `dotnet ef database update AddProcedureCatalog`, then the latest, on a scratch database | after Down the trigger and the constraint are gone (0, 0); after Up the trigger is back (1); scratch database dropped |
| Negative controls | two mutants of the new rule, each restored byte-identical | service rule removed: 3 tests fail; trigger removed: 5 tests fail; unmutated: 103 of 103 (`artifacts/test-runs/negative-controls.txt`) |

## The 19 new backend tests
| Class | New | What they prove |
|---|---|---|
| `ProcedureCatalogRulesTests` | 5 | CDT and External, with no source name or a blank one (an edition alone is not a source), are refused with `sourceName` named and nothing stored (theory, 2 cases); a local code refuses an edition alone and a source name with an edition; the edition is optional for CDT and external while the source name is not; a new version of a CDT code cannot drop its source |
| `ProcedureCatalogSchemaTests` | 12 | Bypassing the service: CDT with no source, CDT with an edition only, External with no source (trigger 51083), a local version with a source name, a local version with an edition only (trigger 51084), each leaving no orphan version; CDT with and without an edition, External with source and edition, and a plain local version are accepted; a blank or empty source name or a blank edition is refused by `CK_ProcedureVersions_SourceText` |
| `ProcedureCatalogApiTests` | 2 | CDT and External without a source: `400 validation_failed` with `sourceName`, the catalog stays empty; the same call with a source succeeds and the response carries the source name |

One existing schema test (`The_database_refuses_the_same_code_twice_in_one_code_system_but_allows_it_in_another`) was changed to give its External procedure a source, because it relied on the gap.

## The 1 new frontend test
`asks a CDT code to name its source too, and sends the source with the code`: choosing CDT and pressing Add without a source shows "Name the source of this code set." and sends nothing; with a source the request carries `codeSystem: "CDT"`, the upper-cased code, the source name and no edition.

## Disclosures
- The backend run took 48 m 58 s against 44 m 4 s for R01; the suite grew by 19 tests and the machine had just run two builds and the mutation controls. It was not a timing-measurement session.
- The full backend run started from the working tree that was committed as `0ef1ab4` after the run; the only difference between the run's tree and the commit is `PROGRESS.md` (the entry written after the run).
- Mutation checks cover only the new rule (two mutants), not the whole catalog; R01 ran none.
- The loading state and the "connection dropped" message on the catalog page remain without a test (carried from R01).
- Databases created by the sweep (`alveara_n5r2_*`, 18) and by the full run are left in place per the standing no-delete rule.
