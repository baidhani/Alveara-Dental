# ALV-N023 R01: test results (Stage 1 focused proof; 2026-11-18)

Swept tree **`f9c0d2a`** (final implementation commit). One sweep, 15:05:48-15:15:51 (-06:00, 10 m 3 s), working tree clean before and after (`artifacts/summary.txt`). Implementation commit of this package **`2ade215`** = `f9c0d2a` + the handoff note + one test-only commit (see the end). Windows 11, local SQL Server Express, Chromium. Route: focused proof only; `fullRegression: DEFERRED_TO_ALV-N028`.

| Run | Result | Duration | Output |
|---|---|---|---|
| `dotnet build Alveara.Api.Tests` | ok | 26 s | `artifacts/gates/backend_build.txt` |
| Server group A (7 classes) | **77 / 77** | 102 s | `artifacts/gates/groupA.txt`, `artifacts/backend-focused-tests.csv` |
| Server group B (7 classes) | **173 / 173** | 199 s | `artifacts/gates/groupB.txt` |
| Server group C (6 classes) | **118 / 118** | 157 s | `artifacts/gates/groupC.txt` |
| `npx tsc -b` | exit 0 | 9 s | `artifacts/gates/tsc.txt` |
| `npm run lint` (oxlint) | exit 0 (0 errors; 2 warnings in untouched pre-existing specs) | 1 s | `artifacts/gates/lint.txt` |
| `npm run build` | exit 0 | 9 s | `artifacts/gates/build.txt` |
| `npx vitest run` focused selection (20 files) | **238 / 238** | 31 s | `artifacts/gates/vitest_focused.txt` |
| `node --test tests/*.test.mjs` (repo root) | **58 / 58** | 3 s | `artifacts/gates/repo_checks.txt` |
| `treatment-plan-integrity-real-backend` alone, fresh database (`-SkipBuild`) | **7 / 7** | 31 s | `artifacts/gates/walk_integrity.txt`, `artifacts/walkthrough/` |
| Leak counts before / after | identical | - | `artifacts/leak_before.txt`, `artifacts/leak_after.txt` |
| Negative controls (a)-(d) (run before the final commit, on the same code) | as designed | 54-84 s each | `artifacts/negative-controls/control_*.log`; NEGATIVE_CONTROLS below |

Filters were class-exact: `FullyQualifiedName~Tests.<Class>.` per class, joined with `|`.

**Group A:** TreatmentPlanAppointmentLinkTests 13, TreatmentPlanAppointmentLinkMigrationTests 4, TreatmentPlanStateTests 22, TreatmentPlanAuditTests 1, UnscheduledTreatmentJobTests 26, RealMigrationInventoryTests 5, TreatmentPlanBookingApiTests 6.
**Group B:** TreatmentPlanServiceTests 32, TreatmentPlanApiTests 24, TreatmentPlanLifecycleTests 16, TreatmentPlanRevisionServiceTests 9, TreatmentPlanStateServiceTests 14, TreatmentPlanCompletionApiTests 21, TreatmentPlanRevisionSchemaTests 57.
**Group C:** AppointmentSchedulerTests 41, AppointmentsApiTests 30, CompletedProcedureServiceTests 19, CompletedProcedureAdoptionHardeningTests 8, DiagnosisReconciliationTests 10, ClinicalReportsTests 10.
(The brief named `RecallJobTests` for the section 4.4 job tests; they live in `UnscheduledTreatmentJobTests`.)

**Focused vitest selection:** `src/pages/treatmentplan`, `src/pages/calendar`, `src/Calendar*`, `src/CalendarBookTreatment`, `src/Schedule*`, `src/pages/A3Display`.

## Negative controls (one at a time; byte backup, SHA-256 equal after restore)
| Control | Change | Tests run | Result |
|---|---|---|---|
| (a) | same-patient check removed from `TreatmentPlanService.Links.cs` `LinkAppointmentAsync` | TreatmentPlanAppointmentLinkTests | **13 / 13 green** - expected: the database trigger 51291 still refuses (layering proof) |
| (b) | `Scheduling == Scheduled` shortcut restored in `UnscheduledTreatmentRules.JudgeAsync` | UnscheduledTreatmentJobTests | **2 of 26 fail** (`A_target_date_never_settles_it_only_a_live_linked_appointment_does`, `Path1_legacy_scheduled_item_gets_a_new_v2_task_...`) |
| (c) | DR-4 `partial_acceptance_conflict` refusal restored in `DecidePlanAsync` | TreatmentPlanStateServiceTests | **2 of 14 fail**, both with that exception (`A_plan_decision_applies_only_to_unanswered_procedures_...`, `Declining_a_scheduled_procedure_...`) |
| (d) | lock guard removed from `WithdrawAsync` | TreatmentPlanStateTests | **2 of 22 fail** (`A_completed_plan_refuses_every_change_including_withdraw...`, `A_closed_plan_completed_and_declined_also_refuses_withdraw`) |

## Test-only commit `2ade215` (after the sweep)
Mapping the acceptance evidence found three brief section 6 rows whose client proof was missing (code present, no client assertion): F-102, DR-8 (Q-5af), D-03. Owner-approved, `TreatmentPlanN023Proof.test.tsx` (4 tests) was added. On `2ade215`: `tsc -b` exit 0, oxlint clean, the file 4/4, focused vitest **21 files 242 / 242** (30 s, `artifacts/gates/vitest_focused_proof_commit.txt`). Each test was shown to catch a planted break and the file restored (SHA-256 equal): link removed -> F-102 test fails; note removed -> DR-8 test fails; group role removed -> D-03 test fails (1 failed / 3 passed each; `artifacts/negative-controls/client_break_*.log`). `git diff --stat f9c0d2a 2ade215`: the test file, PROGRESS.md, the handoff note - no production change.

## New or changed tests (whole story)
- Server: `TreatmentPlanAppointmentLinkTests`, `TreatmentPlanAppointmentLinkMigrationTests`, `TreatmentPlanStateTests`, `TreatmentPlanAuditTests`, `TreatmentPlanBookingApiTests` (new); `UnscheduledTreatmentJobTests`, `TreatmentPlanStateServiceTests`, `TreatmentPlanCompletionApiTests`, `TreatmentPlanRevisionSchemaTests`, `RealMigrationInventory*` (changed).
- Client: `TreatmentPlanN023.test.tsx` (12), `TreatmentPlanN023Proof.test.tsx` (4), `CalendarBookTreatment.test.tsx` (4) new; `TreatmentPlanAnswers.test.tsx`, `TreatmentPlansPanel.test.tsx`, `treatmentPlanFixtures.ts`, AdoptCompletion, ClinicalHistory, PatientCopy, Revisions tests updated to the approved behaviour (DR-4, PD-15, F-186, DR-5, Q-5ae, F-183) - never loosened.
- Browser: `treatment-plan-integrity-real-backend.spec.ts` (new, 7); `treatment-plans-complete-real-backend.spec.ts` updated in source to DR-4, PD-15 wording and the `TreatmentPlanItemTargetDateSet` audit event (not run - only if the reviewer names it).
