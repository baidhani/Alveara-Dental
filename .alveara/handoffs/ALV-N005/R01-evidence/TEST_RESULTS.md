# ALV-N005 R01 - Test results

All runs are on the implementation tree (commit `4d4cb0ee1dc05c28b15f5a36f3bd15a5ac690826`), 2026-10-08, on one Windows machine with SQL Server LocalDB. Raw outputs are under `artifacts/`.

| Layer | Command | Result |
|---|---|---|
| Backend, full, serial | `dotnet test --no-build --logger trx` in `src/Alveara.Api.Tests` (runner `parallelizeTestCollections:false`) | **2386 of 2386 passed, 0 failed, 0 skipped** (44 m 4 s). New: **84**. `artifacts/test-runs/backend-full-summary.txt` |
| Frontend type check | `npx tsc --noEmit -p tsconfig.app.json` | exit 0, no output |
| Frontend production build | `npm run build` | built; the existing large-chunk notice only |
| Frontend lint | `npm run lint` (oxlint) | exit 0, 0 errors, 17 warnings in all; exactly one is in the new files (`set-state-in-effect` on the page's load effect), the same warning that 13 existing places already carry |
| Frontend unit | `npx vitest run` | **1214 of 1214 passed** (76 files). New: **20**. (An earlier full run had 1 timing failure in an untouched Backup test; see below.) |
| Mocked browser suite | `npx playwright test` | **136 of 136 passed** (desktop and tablet) |
| Repository checks | `node --test tests/*.test.mjs` | **8 of 8 passed** |
| Real-backend walkthrough (new) | `playwright.procedures.config.ts` on a fresh database | **6 of 6 passed**; axe, light and dark, 0 critical/serious |
| Real-backend walkthroughs (existing 17) | each on its own fresh database | **187 of 187 passed** (listed in `PARENT_REGRESSION.md`) |
| Leak check | databases and logins counted before and after | unchanged: AlveraTest 54, AlveraRestore 59, logins 273, `alveara_` 211 (`artifacts/test-runs/leak-check.txt`; the `alveara_` count was taken after the new walkthrough database and before the 18 sweep databases, which are intentionally left per the standing no-delete rule) |

## The 84 new backend tests

| Class | Tests | What it proves |
|---|---|---|
| `ProcedureCatalogRulesTests` | 25 | Creating with all fields; every wrong field named in one refusal and nothing stored; code shape per code system (a local code that looks like CDT, bad CDT, bad external); fee limits and boundaries (0, 12.50, 1,000,000.00 accepted; negative, over the limit, three decimals refused); description rules; category/scope/dentition lists and date rules; dentition stored as `Both` unless tooth-level; CDT keeps source and edition, local has none; external must name its source; same code in two systems is two procedures; a new source edition is a new version of the same identity; code and system cannot change; an identical repeat is quiet, a different procedure with a used code is refused, codes compare ignoring case; four concurrent creates of one code end with one procedure and one `Created` event |
| `ProcedureCatalogLifecycleTests` | 17 | A fee change is a new version and the old one keeps its fee; the fee in effect on any date, including before a change; a scheduled fee waits for its date and a procedure shows `Scheduled`; no backdating; a reason is required; saving unchanged adds no version; a stale row version is a conflict and changes nothing; a remembered version id reads back exactly; inactivate (reason required), usage confirmation with counts, reactivate; **a finding linked to the procedure through the real odontogram service counts as usage, inactivation needs confirmation, and the link and the procedure still read as before**; list filters; planning filters by tooth, surface and dentition; audit entries and events with actor names and fee amounts |
| `ProcedureCatalogSchemaTests` | 21 | The database refuses, bypassing the service: bad code shapes and code system, fees out of range, category/scope/dentition/description/date rule breaks, a duplicate code; delete of a procedure (51077); change of code or code system (51078) while other columns can change; edit or delete of a version (51079) or an event (51080); a skipped version number (51081) and a version starting before the one it follows (51082), while the same day is allowed; an inactivate event without a reason |
| `ProcedureCatalogApiTests` | 21 | 401 for every endpoint when anonymous; read allowed for Admin, OfficeManager, Billing, Dentist, FrontDesk and 403 for Hygienist, Assistant; change allowed for Admin, OfficeManager, Billing and 403 for Dentist, FrontDesk, Hygienist, Assistant; CSRF required (400 without); a wrong entry returns `validation_failed` with a message per field; duplicate is 409 `procedure_exists` and an identical repeat is quiet; a fee change is a new version, a stale edit is 409 and changes nothing, a missing reason is 400; inactivate and reactivate with reasons and a readable history; 404 for unknown ids and 400 for bad filters |

## The 20 new frontend tests (`ProcedureCatalogPage.test.tsx`)
Fee text parsing and formatting (2); list shows code, description, fee, code system, category and applicability; read-only view for someone who cannot change; permission message and no request for someone who cannot see billing; a failed load says so with a retry; filters are sent to the server; an incomplete entry refused before sending, naming each field; an external code set asks for its source; add sends the typed fields; a server refusal shows beside the field and keeps what was typed; a fee change keeps the code fixed, requires a reason and sends the row version; a conflict shows the shared banner and keeps the open form; inactivate without references; inactivate with references shows the count and needs confirmation; inactivation is not offered when the usage check fails; reactivation instead of change for an inactive procedure; a scheduled fee reads "not started yet"; the history loads when opened; axe with the add form open.

## Disclosures
- One earlier full vitest run (before the final run above) failed one test, `BackupRecoveryPage.test.tsx > Backup settings form > keeps an edit in progress when the permission set arrives after the first load`; the file passed 35 of 35 three times alone and the full suite then passed 1214 of 1214. No file in that area was touched by this attempt.
- The first backend run of the new rules tests failed 11 of 25 from a defect in the database code-shape check; it is fixed and described in `R01.md`.
- No mutation checks were run on the new rules in this attempt.
- The backend full run started from the tree that was committed as `4d4cb0e` a few minutes later; the only differences between the run's tree and the commit are `PROGRESS.md` (the entry written after the run) and no source file.
- Test databases created by the sweep (`alveara_n5_*`, 18) and by the full run are left in place per the standing no-delete rule; cleanup waits for an ownership-based design.
