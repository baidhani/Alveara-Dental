# ALV-N021 A2-R01: test results

All runs 2026-11-14 to 2026-11-16, Windows 11, SQL Server (each test on its own database cloned from a template; real-migration classes create and migrate their own), Chromium (Playwright). Every run had the product owner's explicit Go after the code was shown; runs over five minutes were asked first. Server: `dotnet test src/Alveara.Api.Tests --filter ...`; client: `npx vitest run ...`, `npx playwright test`; walkthroughs: `scripts/Invoke-RealBackendWalkthrough.ps1`; chain: `scripts/Invoke-AccumulatedChain.ps1`.

## 1. Final evidence run (on the final tree)

Implementation commit `2ef0817`. Parts ran on the tree named in each row; the commits after a row's tree changed only the files named in the "later commits" column, each re-proven as stated.

| # | Tree | Run | Expected | Actual | Result | Raw (`artifacts/final-evidence/`) |
|---|---|---|---|---|---|---|
| F1 | `7207b88` | `tsc --noEmit`, `npm run build`, `npm run lint` | < 1 min | 15 s | exit 0 each; lint 0 errors, 43 warnings (41 recorded at ALV-N020 R02; see Disclosures) | `1-client-gates/` |
| F2 | `7207b88` | full client unit suite `npx vitest run` | ~3 min | 164 s | **2018 / 2018**, 133 files | `1-client-gates/vitest.txt` |
| F3 | `7207b88` | mocked Playwright `npx playwright test` | ~1 min | 106 s | **14 failed**, 149 passed: 12 = Component Showcase specs against the production build (consequence of F-064), 2 = ALV-N020-C02 missing from `.alveara/story_catalog.json` (pre-A2). Run stopped here | `2-mocked-playwright/mocked-playwright-first-14-failed.txt` |
| F4 | working tree, then `0006779` | mocked Playwright after the two fixes | ~2 min | 126 s | **165 passed, 29 skipped, 0 failed** (my first config attempt broke the real-backend ignore list - 106 failed, stopped; one retry hit a 30 s web-server start timeout; one project-name check fixed; all in Disclosures) | `2-mocked-playwright/mocked-playwright-green.txt` |
| F5 | `0006779` | repo checks `node --test tests/*.test.mjs` | < 1 min | 6 s | **48 / 49**: brand inventory names the old file of the theme key (pre-A2). Run stopped | `3-stopped-runs/run2-repo-checks-48of49.txt` |
| F6 | `265331c` | repo checks | < 1 min | 4 s | **49 / 49** | `3-stopped-runs/run3-summary.txt` |
| F7 | `265331c` | backend focused, broad name filter (`Visit|Appointment|...|Migration|Schema`) | estimated 6 min (wrong) | stopped by the owner after ~18 min | 976 tests selected; 6 failures seen before the stop: `PatientMergeSchemaTests` (real A2 gap, fixed by M6) and 5 `VisitStateMachineTests` cases encoding STORY-011 rules (updated) | `3-stopped-runs/run3-broad-filter-stopped.txt` |
| F8 | `215436c` | the 42 test classes changed by A2 commits + `PatientMergeSchemaTests` (filter in `a2_final_p1.sh`) | ~10 min | 8 min 44 s | **747 / 747** | `4-part1/backend_focused.txt` |
| F9 | `215436c` | real-backend walkthroughs: board and flow on the injected clock `2030-01-14T13:30:00Z`, calendar, schedule, reports | ~8 min | 241 s | board **16/16**, flow **10/10**, calendar **12/12**, schedule **10/10**, reports **7/7** | `4-part1/walkthroughs/` |
| F10 | `215436c` | accumulated chain, stages 0-4 | ~6.5 min | 399 s | **40 / 40** | `4-part1/chain/` |
| F11 | `215436c` | every `*ApiTests` class | ~20 min | 21 min | **1020 / 1021**: the one failure `BackupApiTests.Manual_backup_verification_...` hard-coded 4 asset classes; ALV-N020-C01 added a fifth (pre-A2) | `5-part2/apitests.txt` |
| F12 | `24417e1` | that test alone | < 1 min | 24 s | **1 / 1** | console (PROGRESS.md) |
| F13 | `2ef0817` | after removing the one lint warning A2 added (`VisitWorkflowPanel.tsx`): `npm run lint`, `tsc --noEmit`, `vitest ConfigurationHubPage` | < 1 min | ~40 s | lint exit 0, 0 errors, **42** warnings; tsc clean; **30 / 30** | `1-client-gates/lint-after-panel-fix.txt` |

Later commits after each tree: `7207b88` -> `2ef0817` changed `.alveara/story_catalog.json`, `docs/stabilization/BRAND_INVENTORY.json`, the mocked Playwright configuration and two specs (re-proven by F4, F6), migration M6 and six test files (re-proven by F8, F11, F12), and in `2ef0817` two client files, `VisitWorkflowPanel.tsx` and `PracticeTab.tsx` (re-proven by F13). No other client source under `src/alveara-client/src` changed after F1/F2.

The raw `.trx` files of F8 and F11 are in the handoff ZIP only (not in git).

## 2. Focused proof per step (during the build)

Every A2 commit's PROGRESS.md entry (session `CC-20261109-8w3d` for steps 0-5b, `CC-20261115-m7x2` for 6a onward) holds its runs with real numbers. Summary:

| Step | Commit | Focused runs | Negative controls |
|---|---|---|---|
| 0 (T-2, T-3) | `f82c5ca` | new classes + VisitBoardTests 38/38; collateral 140/140 | culture removed from a parse; board back on a midnight conversion |
| 1 (M1-M3, permissions) | `ce6e9da` | 82/82; schema 59; API 121/121 | role-limit check removed: 4 fail |
| 2 (checkout) | `b16a2f5` | CheckoutTests 21/21 | live-entry check removed: 1 fails |
| 3a (move policy) | `eaaf2f5` | 63/63; visit group 160/163 -> 41/41 rerun; PatientFlowApiTests 13/13 | checkout gate, clinician role check, day rule: each fails its test |
| 3b (undo, cancel, reassign) | `df82656` | 62/62; 214/215 -> VisitsApiTests 60/60 | reason check, notification staging, too-late check |
| 3c (blocked time) | `4f73a81` | 17/17; collateral 156/157 -> 5/5, 33/33 | active-visit refusal (6 fail), check-in flag (3), keep time (3) |
| 4a (actions) | `022982c` | 24/24; 92/92 | who-may (10 fail), checkout block (2), flag block (1) |
| 4b-4e (board, calendar, checkout panel, availability) | `38e2956`..`6c3fcef` | client 43/43, 85/85, 92/92, 27/27 | each caught and restored |
| measurement fix | `2b013b6` | 104/104 | raw state sent again |
| 5a (walkthroughs) | `22bac22` | board 15, flow 10, calendar 12, schedule 10, reports 7 | - |
| 5b (chain clock, stages 3-4) | `fa7d849`, `b5980d4` | 11/11; chain 40/40 | bounds check removed |
| 6a (CollectPayments) | `19b6f07` | 99/99; client 57/57 | - |
| 6b (history, M4) | `5c475f9` | 150 (149 + 1 setup fix); client 92/92; calendar 12/12; chain 40/40 | destination read from current place: 2 fail |
| 6c (Rebook, Open patient) | `b713478` | 88/88; client 114/114; chain 40/40 | - |
| 6d-1a/1b (calendar, M5) | `cd56506`, `2dc42df` | client 91/91, 111/111; backend 172 (171 + 1 fix); calendar 12/12, schedule 10/10, chain 40/40 | route returns the reason: 2 fail |
| 6d-2 (Schedule page) | `7108d00` | client 126/126; schedule 10/10; chain 40/40 (rerun after my stale text) | - |
| 6d-3 (showcase, time hint) | `f67b9b1` | client 137/137; build searched | - |
| 6e (duration) | `41844aa` | client 105/105; schedule 10/10, calendar 12/12 | - |
| proof rows | `8969d15` | CheckInDstDayRuleTests 2/2; board 16/16 incl. PARITY | day rule on the UTC date: both fail |
| OD gaps | `7207b88` | 6/6 + ConfigurationApiTests 10/10; client 30/30 | audit removed: 2 fail |
| M6 | `215436c` | 71 + 3 (after one setup fix) | M6 leaving the column out: 4 fail |

Negative-control method: one rule broken at a time, only the class that must catch it run, the file restored (steps 0-6a: `git checkout`, `git hash-object` compared; from 6b on: restored from a copy taken before the mutation and the hash compared, after a `git checkout` restore dropped uncommitted edits once - Disclosures).

## 3. Not run

- The full serial backend suite (about 3,765 test methods) and every real-backend walkthrough other than the five A2 ones (35 of 40): `DEFERRED_TO_ALV-N028`, recorded in `.alveara/PROGRAM_REGRESSION_LEDGER.md`.
- Firefox or WebKit (Playwright runs Chromium only; F-068 is a Firefox report).
