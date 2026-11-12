# Program Regression Ledger (post-Gate-E stabilization program)

Authority: the execution-plan exception of 2026-11-11 and `.alveara/HANDOFF_SCHEMA.md` ("Post-Gate-E stabilization-program deferred-regression closure"). Packages `ALV-N019` through `ALV-N027` close on focused stability proof with `fullRegression: "DEFERRED_TO_ALV-N028"`. **None of them is release-certified.** `ALV-N028` owns the one frozen integrated release-candidate broad regression; nothing here unlocks release engineering, Gate F, deployment or a release decision.

Each package appends one section: implementation SHA, changed subsystems, the exact focused proof, and the broad obligations it hands to `ALV-N028`.

## Accumulated obligations for ALV-N028 (the consolidated run must cover all of these)

| Suite | Owed by | Notes |
|---|---|---|
| Full backend suite (`dotnet test src/Alveara.Api.Tests`, serial) | ALV-N019 | last broad result: R04 Stage 2, 5,712 of 5,715 (the 3 failures are fixed and focused-proven, see below); identity baseline R02 `backend-tests.csv` (5,697) + 18 R04 ids = 5,715 |
| Client: vitest, build, lint, `tsc -b` | ALV-N019 | last broad result: R04 Stage 2, all passed |
| Mocked Playwright suite | ALV-N019 | last broad result: R04 Stage 2, 137 of 138 + 2 skipped (the 1 failure is fixed and focused-proven in R05) |
| Repository checks (`node --test tests/*.test.mjs`) | ALV-N019 | last broad result: R04 Stage 2, passed |
| Real-backend walkthroughs (auth 12, patient portability 7, export package 10, merge 9, bulk import 13) | ALV-N019 | last broad result: R04 Stage 2, all passed |
| Stage-0 accumulated chain, injected clock (9 PASS records) | ALV-N019 | last broad result: R04 Stage 2, passed |
| Leak accounting (databases, logins, files, template backups identical before and after) | ALV-N019 | last broad result: R04 Stage 2, identical |
| Full backend suite: also the new ALV-N020 classes `StaffInactivationServiceTests`, `StaffInactivationAlertTests`, `StaffInactivationNotificationSchemaTests` and the added cases in `AccountServiceLoginTests`, `ConfigurationApiTests`, `RealMigrationInventoryTests` | ALV-N020 | focused only (R01 G2, G9-G11); never in a broad run |
| Client vitest/build/lint/`tsc -b` over the changed session, sign-in, patient-context, configuration and notification files | ALV-N020 | focused only (R01 re-run B 150 of 150, G9, G11, G12); lint 42 warnings (one new) |
| Mocked Playwright: the whole suite (ALV-N020 changed `e2e/auth-real-backend.spec.ts` and the shell/login behavior) | ALV-N020 | focused only: shell + login-banner 28 of 28 (R01 G3) |
| Real-backend walkthroughs: auth 12, workspace 14, permission-policy-admin 8, plus notifications (new notification kind) | ALV-N020 | first three focused (R01 G4); notifications walkthrough not run |
| Accumulated chain now includes stage 1 + minimal stage 2 (`chain/stage-1.spec.ts`, about 6-7 min, real 5-minute session in 1.12) and the browser-level outage step 13.05 | ALV-N020 | R01 G5 run 3: 20 of 20, builder 22 PASS / 0 FAIL / 0 tracked |
| Migration `20261112140957_AddStaffInactivationFailedNotification`: upgrade from the pre-ALV-N020 schema on a copy of a populated database; the empty-table down path; the bell and list with a `staff.inactivation_failed` notification present | ALV-N020 | focused only: schema/migration tests on a clone (R01 G9-G10) |

## ALV-N019 (item 56): PKG-01, PKG-15, DentalKeep brand migration

- **Final implementation SHA:** `3cf91fa9833ff1a683d566a1edef432c4e520399` (R06 correction; R07 carries it unchanged).
- **Changed subsystems over the story:** package references (`System.Security.Cryptography.Pkcs` 9.0.21 pin, R03), brand migration (R01/R02), the shared backend test fixture (`TestDatabaseFixture` pool warm-up and owner-side failure cleanup, ~100 owner test files converted, R04), source-scanning inventory test (`RealMigrationInventoryTests`, R05/R06), one login-cleanup test (R05), one client mocked-browser spec (`e2e/architecture-map.spec.ts`, R05).
- **Last broad evidence:** R04 frozen Stage 2 on `115821d` (2026-11-11): no pool timeout, 4,354 clones, 0 fallbacks, no leak; backend 5,712 of 5,715; mocked Playwright 137 of 138; every other criterion passed. Disposition of each failure: see the R07 handoff.
- **Focused proof (R07):** 2026-11-11, HEAD `2e35858` (implementation `3cf91fa` + records): test-project build 0 errors; all 5 `RealMigrationInventoryTests` in 20 independent runs, 20 of 20 passed (includes the composite-source omission proof); source-scanning/TestDatabase sweep (26 source-scanning classes + `TestDatabase*` + 3 fixture classes) 375 of 375 in 15 m 29 s; leftover counts identical before and after. Earlier focused proof kept as history: R04 forced-pruning preflight (control 800 cycles, 154 authentic timeouts; treatment 900 of 900, 0 leftovers); R05 login-cleanup 12 of 12, Architecture Map 8 of 8. Exact commands: `.alveara/handoffs/ALV-N019/R07-evidence/TEST_RESULTS.md`.
- **Deferred to ALV-N028:** every suite in the table above.

## ALV-N020 (item 57): PKG-02, PKG-03 (identity, session, staff lifecycle)

- **Implementation SHA:** `17a47758b2064de381698abf65d6d003f45b9c92` (R01).
- **Changed subsystems:** client session/auth state (`AuthContext`, `RouteGuards`, `LoginPage`, `loginOutcome`, `MfaChallengePage`, `AppShell`, `PatientContext`, `DisconnectedBanner`, `useConnectionStatus`, theme hook), sign-in API client (timeout, unreachable mapping), username normalization in `AccountService`, staff inactivation and unlink (`StaffInactivationService`, `StaffProviderService`, `ConfigurationController`), notifications (new kind, `InAppStaffInactivationAlert`, `NotificationsPage` label), one migration (two check constraints), chain stage 1.
- **Focused proof (R01):** see `.alveara/handoffs/ALV-N020/R01-evidence/TEST_RESULTS.md`: vitest 150 of 150 + later groups; xUnit 97 of 97, 24 of 24, 13 of 13; mocked Playwright 28 of 28; walkthroughs 34 of 34; chain run 3 20 of 20 (22 PASS); negative controls NC-1 to NC-5 effective, files restored byte for byte; leftover counts identical in every server run.
- **Deferred to ALV-N028:** the ALV-N020 rows in the table above, plus every ALV-N019 row (unchanged).
- **Not release-certified.**
