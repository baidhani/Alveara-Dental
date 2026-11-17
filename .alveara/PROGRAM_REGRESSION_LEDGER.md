# Program Regression Ledger (post-Gate-E stabilization program)

Authority: the execution-plan exception of 2026-11-11 and `.alveara/HANDOFF_SCHEMA.md` ("Post-Gate-E stabilization-program deferred-regression closure"). Packages `ALV-N019` through `ALV-N027`, and the owner-approved plan items `ALV-N020-C01` and `ALV-N029` (amendment of 2026-11-12), close on focused stability proof with `fullRegression: "DEFERRED_TO_ALV-N028"`. **None of them is release-certified.** `ALV-N028` owns the one frozen integrated release-candidate broad regression; nothing here unlocks release engineering, Gate F, deployment or a release decision.

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
| Accumulated chain now includes stage 1 + minimal stage 2 (`chain/stage-1.spec.ts`, about 6-7 min, real 5-minute session in 1.12) and step 13.05 with a real owned-API stop/restart (runner control channel, API-STOP/API-START records) | ALV-N020 | R02 re-run: 20 of 20, builder 24 PASS / 0 FAIL / 0 tracked; runner self-test 8 of 8 |
| Migration `20261112140957_AddStaffInactivationFailedNotification`: upgrade from the pre-ALV-N020 schema on a copy of a populated database; the empty-table down path; the bell and list with a `staff.inactivation_failed` notification present | ALV-N020 | focused only: schema/migration tests on a clone (R01 G9-G10) |
| Full backend suite: also the new ALV-N020-C01 classes `LoginBackgroundPipelineTests`, `LoginNoticeRulesTests`, `LoginPresentationApiTests`, `LoginPresentationSchemaTests`, `LoginBackgroundBackupRestoreTests`, `LoginBackgroundCatalogTests` and the changed `BackupServiceTests`, `RealMigrationInventoryTests` (inventory 24) | ALV-N020-C01 | focused only (R01: 34, 7, 11, 51/20, 3, 103, 29); never in a broad run |
| Client vitest/build/lint/`tsc -b`: new `LoginLayout.test`, `SignInScreenTab.test`, `accountPermissionGating.test`; changed `App.tsx`, `AuthContext`, `authApi`, configuration hub, admin user page, backup labels | ALV-N020-C01 | focused only: 18 of 18 new, 125 of 125 affected; lint 41 (unchanged) |
| Mocked Playwright: the whole suite, now including `e2e/login-presentation.spec.ts` (every sign-in page renders inside `LoginLayout`) | ALV-N020-C01 | focused only: login-presentation 12 of 12, shell + login-banner 28 of 28 |
| Mocked Playwright: the whole suite, now including `e2e/login-layout-polish.spec.ts` (equal-width column, logo spacing, 12 px notice, SVG logo ratio); client unit suite includes `loginLogo.test.ts` | ALV-N020-C02 | focused only: browser group 39 passed (1 capture skipped; run with the dev API stopped), client unit suite 1953 of 1953 |
| Real-backend walkthroughs: auth 12 (backup history now lists "Sign-in backgrounds"), workspace, permission-policy-admin, plus every walkthrough that signs in (new layout) | ALV-N020-C01 | auth 12 of 12 only |
| Accumulated chain stage 0 + stage 1 on the new sign-in page | ALV-N020-C01 | R01: 20 of 20, 24 PASS on `94bcee0`; not re-run after the additive bootstrap-response commit `65c234b` |
| Migration `20261112210440_AddLoginPresentation`: upgrade from the post-ALV-N020 schema on a copy of a populated database; triggers 51276-51279; the down path refused once a grant or added image exists; a real backup and restore of the `loginBackgrounds` asset class with an added image | ALV-N020-C01 | focused only: schema tests on a clone, backup/restore drill on a test database |
| SkiaSharp 4.156.0 native assets on the deployment targets (Windows installer, ALV-N013) | ALV-N020-C01 | dependency audit clean; only the development machine exercised |
| Full backend suite: also the new ALV-N021 A1 classes `FinancialDocumentTemplateVersionTests`, `BillingDocumentTemplateVersionServiceTests` and the added cases in `BillingDocumentBuilderTests`, `BillingDocumentServiceTests`, `BillingTimelineAgingTests`, `DiagnosisStructureServiceTests`, `TreatmentPlanStateServiceTests`, `PrescriptionServiceTests`, `ProcedureCatalogLifecycleTests` | ALV-N021 | focused only (A1-R01 runs 2, 3, 5); never in a broad run |
| Client vitest/build/lint/`tsc`: `dateOnly` (new), `PatientFieldsForm`, `PrintPreview`, `documentText`, `TreatmentPlanItemStateLine`, `treatmentPlansApi` and every consumer of `documentText.formatDate` | ALV-N021 | focused only: dateOnly 12/12, treatment-plan answers; tsc exit 0; lint not run |
| Real-backend walkthroughs that print a prescription, show billing documents/timeline or treatment-plan answers; statement reprint of documents issued before the upgrade on a populated database | ALV-N021 | not run |
| Full backend suite: also every ALV-N021 A2 class (42 changed or new, incl. `VisitMovePolicyTests`, `VisitPathTableTests`, `VisitActionsTests`, `CheckoutTests`, `CollectPaymentsApiTests`, `ProviderReassignmentTests`, `BlockedTimeOverlapTests`, `CalendarBlockedTimeApiTests`, `AppointmentMoveHistoryTests`, `CheckInDstDayRuleTests`, `VisitWorkflowSettingsTests`, the six migration classes) and every class A2 never ran (about 2,800 of the 3,765 test methods are neither in the A2 classes nor in `*ApiTests`) | ALV-N021 | A2 final run: the 42 classes + `PatientMergeSchemaTests` 747/747 and every `*ApiTests` class 1020/1021 (one pre-A2 stale count, fixed, 1/1) on `215436c`; never a full serial run |
| Real-backend walkthroughs other than board, flow, calendar, schedule, reports (35 of 40), especially those that book, cancel, take payments or read appointment history (billing, billing-ledger, follow-ups, work-queue, external-cases, notifications, permission-policy, workspace) | ALV-N021 | not run on A2 (the five A2 walkthroughs and the chain stages 0-4 passed on `215436c`) |
| Migrations M1-M6 (`20261114162213` .. `20261116145816`): upgrade from the post-A1 schema on a copy of a populated database; rollback guards 51280, 51281, 51284, 51285, 51286; merge-guard trigger re-created by M6 | ALV-N021 | each rolled back and forward on a test clone only |
| Browsers other than Chromium for the time fields (F-068) | ALV-N021 | Chromium only |
| Full backend suite: also the ALV-N021 A3 classes `PracticeTimeZoneSessionTests`, `PracticeLocalInputTests`, `PracticeLocalInputApiTests` and every class that reads `GET /api/auth/permissions` | ALV-N021 | A3 focused only (14 + 116 + 51) |
| Every real-backend walkthrough and the chain after A3 (every screen now renders instants in the session zone; four forms send `...Local`): especially follow-ups, completion, completion-adoption, clinical (vitals), reports, billing statements, backup | ALV-N021 | only `practice-time-zones-real-backend.spec.ts` run (2/2) |
| Full backend suite: also the ALV-N022 A1 classes `EncounterChartRoutingTests`, `EncounterChartApiTests` and the rewritten `EncounterServiceTests`, `EncountersApiTests`, `ClinicalRecordServiceTests`, plus every class that reads an encounter or the clinical record | ALV-N022 | focused only: 9 classes 232 of 232 (A1-R01) |
| Client vitest/build/lint/`tsc -b` and the mocked Playwright suite over the changed encounter screen, `ChartSectionPanel`, `clinicalApi`, `clinicalRecordApi`, `safetyApi` | ALV-N022 | focused only: 2 vitest files 12 of 12, `tsc -b` clean; mocked Playwright not run |
| Real-backend walkthroughs that open an encounter or read the clinical record or the banner (clinical, clinical-companion, safety, encounter-chart run; completion, completion-adoption, diagnoses, prescriptions, patient workspace and others not run) | ALV-N022 | 4 of the walkthroughs run on `6414ffa`: 1, 9, 11, 9 passed |

## ALV-N019 (item 56): PKG-01, PKG-15, DentalKeep brand migration

- **Final implementation SHA:** `3cf91fa9833ff1a683d566a1edef432c4e520399` (R06 correction; R07 carries it unchanged).
- **Changed subsystems over the story:** package references (`System.Security.Cryptography.Pkcs` 9.0.21 pin, R03), brand migration (R01/R02), the shared backend test fixture (`TestDatabaseFixture` pool warm-up and owner-side failure cleanup, ~100 owner test files converted, R04), source-scanning inventory test (`RealMigrationInventoryTests`, R05/R06), one login-cleanup test (R05), one client mocked-browser spec (`e2e/architecture-map.spec.ts`, R05).
- **Last broad evidence:** R04 frozen Stage 2 on `115821d` (2026-11-11): no pool timeout, 4,354 clones, 0 fallbacks, no leak; backend 5,712 of 5,715; mocked Playwright 137 of 138; every other criterion passed. Disposition of each failure: see the R07 handoff.
- **Focused proof (R07):** 2026-11-11, HEAD `2e35858` (implementation `3cf91fa` + records): test-project build 0 errors; all 5 `RealMigrationInventoryTests` in 20 independent runs, 20 of 20 passed (includes the composite-source omission proof); source-scanning/TestDatabase sweep (26 source-scanning classes + `TestDatabase*` + 3 fixture classes) 375 of 375 in 15 m 29 s; leftover counts identical before and after. Earlier focused proof kept as history: R04 forced-pruning preflight (control 800 cycles, 154 authentic timeouts; treatment 900 of 900, 0 leftovers); R05 login-cleanup 12 of 12, Architecture Map 8 of 8. Exact commands: `.alveara/handoffs/ALV-N019/R07-evidence/TEST_RESULTS.md`.
- **Deferred to ALV-N028:** every suite in the table above.

## ALV-N020 (item 57): PKG-02, PKG-03 (identity, session, staff lifecycle)

- **Implementation SHA:** `e28282f67e70478529d2c6774e7c83693578a387` (R02; R01 was `17a47758`, returned CHANGES_REQUIRED for four items).
- **Changed subsystems:** client session/auth state (`AuthContext`, `RouteGuards`, `LoginPage`, `loginOutcome`, `MfaChallengePage`, `AppShell`, `PatientContext`, `DisconnectedBanner`, `useConnectionStatus`, theme hook), sign-in API client (timeout, unreachable mapping), username normalization in `AccountService`, staff inactivation and unlink (`StaffInactivationService`, `StaffProviderService`, `ConfigurationController`), notifications (new kind, `InAppStaffInactivationAlert`, `NotificationsPage` label), one migration (two check constraints), chain stage 1.
- **Focused proof (R01):** see `.alveara/handoffs/ALV-N020/R01-evidence/TEST_RESULTS.md`: vitest 150 of 150 + later groups; xUnit 97 of 97, 24 of 24, 13 of 13; mocked Playwright 28 of 28; walkthroughs 34 of 34; chain run 3 20 of 20 (22 PASS); negative controls NC-1 to NC-5 effective, files restored byte for byte; leftover counts identical in every server run.
- **R02 focused proof:** lint 41 warnings (no new one); client 64 of 64; notification schema 8 of 8 (document-link refusals added); AuthorizationArchitectureTests 5 of 5; chain 20 of 20 with 13.05 through the real owned-API stop/restart and 1.12 on a dirty form; runner self-test 8 of 8; leftover counts identical. Changed subsystems in R02: the chain runner (`scripts/Invoke-AccumulatedChain.ps1`), the chain stage-1 spec, the auth context module split, one schema test class.
- **Deferred to ALV-N028:** the ALV-N020 rows in the table above, plus every ALV-N019 row (unchanged).
- **Not release-certified.**

## ALV-N020-C01 (item 57a): DentalKeep login presentation, managed background catalog, footer notice

- **Implementation SHA (R01):** `65c234bf89b2318878c407f1ad057d04e2bedcda` (base `d446184`, briefing revision 3 approved).
- **Changed subsystems:** a new account-scoped permission type (`AccountPermission`, `AccountPermissionGrants`, `RequireAccountPermission`, bootstrap grant, `/api/auth/permissions`), the login-presentation module (catalog, SkiaSharp re-encoding pipeline, derivative store, public anonymous endpoints with a rate policy, notice rules), one migration with four triggers, a new backup asset class with a restore check, the sign-in layout on three client routes, the configuration hub tab and the account page control.
- **Focused proof (R01):** `.alveara/handoffs/ALV-N020-C01/R01-evidence/TEST_RESULTS.md`: xUnit 34, 7, 11, 51 (then 20), 3, 103, 29; vitest 18 + 125; mocked Playwright 12 + 28; negative controls NC-1 to NC-5 effective, restored byte for byte; chain 20 of 20 (24 PASS); auth walkthrough 12 of 12; leftover counts identical in every server run.
- **Deferred to ALV-N028:** the ALV-N020-C01 rows in the table above, plus every earlier row (unchanged).
- **R01 review (CHANGES_REQUIRED) and R02:** R02 (implementation `8d4f3fa67f86c290442568af087debfe17762b7d`) adds a catalog lock for add/reactivate/inactivate/select, forced-colours suppression of the sign-in background, and the tests `LoginBackgroundCatalogConcurrencyTests` (3) and `e2e/login-presentation-access.spec.ts` (6). Owed to ALV-N028 in addition to the R01 rows: the whole mocked Playwright suite now includes `login-presentation-access.spec.ts`; the full backend suite includes `LoginBackgroundCatalogConcurrencyTests`. Focused only: concurrency 3 of 3, browser group 32 of 32, server group 46 of 46.

## ALV-N020-C02 (item 57b): DentalKeep login layout visual polish

- **Implementation SHA (R01):** `7ed42a5082f24249654c5a4523f8f0a0b74755fd`. Presentation only (CSS, one image import, the owner-supplied static SVG logo replacing the PNG, two test files); no server, schema, dependency or shared-component change.
- **Focused proof (R01):** `.alveara/handoffs/ALV-N020-C02/R01-evidence/TEST_RESULTS.md`.
- **Deferred to ALV-N028:** the ALV-N020-C02 row in the table above, plus every earlier row (unchanged).
- **Not release-certified.**

## ALV-N021 (item 58): PKG-04/PKG-05 - attempt A1 (PKG-04 date/time integrity)

- **Implementation SHA (A1-R01):** `807014c05dc93896a69b022bca5fa2032b584081` (base `76bd422`). Statement template version 2 (practice-day aging, frozen basis in the existing envelope, version dispatch; no migration), date-only formatting (F-107, DOB limit), practice-day labels (F-118, perio), answer time apart from later changes (F-184/F-218).
- **Focused proof (A1-R01):** `.alveara/handoffs/ALV-N021/A1-R01-evidence/TEST_RESULTS.md`.
- **Deferred to ALV-N028:** the ALV-N021 rows in the table above, plus every earlier row (unchanged).
- **Not release-certified.**

- **Not release-certified.**

## ALV-N021 (item 58): PKG-04/PKG-05 - attempt A2 (PKG-05 visit workflow and checkout gate; decision 02 T-2, T-3)

- **Implementation SHA (A2-R01):** `2ef081770e12dc880b8ee52306ea5097e2e16cfa` (base `9d6e533`). Changed subsystems: visit workflow (one path, move policy, actions list, undo check-in), checkout gate (evaluator, recorder, panel), scheduling (reassignment with notifications, blocked time over bookings, cancel category, calendar blocked-time read route, history from/to), billing (`CollectPayments`), configuration (visit-workflow settings), client calendar, board, schedule, configuration pages; migrations M1-M6; permissions `CompleteOwnStartedVisit`, `ReassignAppointmentProvider`, `CollectPayments`; test-only chain clock-advance route.
- **Focused proof and final evidence run (A2-R01):** `.alveara/handoffs/ALV-N021/A2-R01-evidence/TEST_RESULTS.md`. These runs are pre-Stage-1, unreviewed diagnostic evidence (review A2-R01).
- **Implementation SHA (A2-R02):** `b7efad658138698547d1b887cb8789942c1ee350` (review CR-1 only: chain-host clock parse on the invariant culture, guard widened). Focused proof: `.alveara/handoffs/ALV-N021/A2-R02-evidence/TEST_RESULTS.md`.
- **Deferred to ALV-N028:** the ALV-N021 A2 rows in the table above, plus every earlier row (unchanged).
- **Not release-certified.**

## ALV-N021 (item 58): attempt A3 (decision 02 T-1, T-4: practice-zone display, today and local input)

- **Implementation SHA (A3-R01):** `b8756f1` (base `3845e31`). Changed subsystems: session response (`practiceTimeZoneId`), start-up zone check, local-input parser on five routes, client time helpers and every instant display, auth state, the test session builders, a repository guard, a new real-backend spec.
- **Focused proof (A3-R01):** `.alveara/handoffs/ALV-N021/A3-R01-evidence/TEST_RESULTS.md`.
- **Implementation SHA (A3-R02):** `6f0f128` (review CR-1 and CR-2 only: Calendar and Flow Board on the session zone; statement/receipt component test). Focused proof: `.alveara/handoffs/ALV-N021/A3-R02.md`.
- **Approved (A3-R02, `.alveara/reviews/ALV-N021/A3-R02.md`):** ALV-N021 COMPLETE; the ALV-N021 rows above stay owed to ALV-N028.
- **Deferred to ALV-N028:** the ALV-N021 A3 rows in the table above, plus every earlier row (unchanged).
- **Not release-certified.**

## ALV-N022 (item 59): PKG-06/PKG-07 - attempt A1 (chart as the single source; Addendum 02)

- **Implementation SHA (A1-R01):** `6414ffa` (base `3ab3c4e`). Changed subsystems: encounter write path for allergy/medication/medical history (routed to the clinical-record service), encounter read model (chart-backed sections, provenance, contradictions), clinical-record duplicate and none-known rules, two new encounter routes, the encounter screen, the safety banner refresh, a repository guard, a new real-backend spec.
- **Focused proof (A1-R01):** `.alveara/handoffs/ALV-N022/A1-R01-evidence/TEST_RESULTS.md`.
- **Implementation SHA (A1-R02):** `38f478e` (review A1-R01 CR-1 only: FinalizeReview chart-backed count). Focused proof: `.alveara/handoffs/ALV-N022/A1-R02-evidence/TEST_RESULTS.md`.
- **Approved (A1-R02, `.alveara/reviews/ALV-N022/A1-R02.md`):** attempt A1 COMPLETE; ALV-N022 IN_PROGRESS (A2, A3 pending); the ALV-N022 rows above stay owed to ALV-N028.
- **Deferred to ALV-N028:** the ALV-N022 rows in the table above, plus every earlier row (unchanged).
- **Not release-certified.**
