# ALV-011-C01 R01 — Parent and dependency regression

## Parent: `STORY-011`
`STORY-011` — "Track patient flow states from scheduled to completed" — is `COMPLETE`, portal-verified at `207791d` (3 of 3 criteria; `.colaberry/progress.json`, ledger commit `0fcbe1c`). This companion must keep its completion contract passing. It did,
**without touching any STORY-011 test**.

### Proof the parent's files were not modified
`git diff 207791d <implementation commit> -- <file>` is empty for all seven:
- `src/Alveara.Api.Tests/PatientFlowRulesTests.cs` (20 tests)
- `src/Alveara.Api.Tests/PatientFlowLifecycleTests.cs` (13)
- `src/Alveara.Api.Tests/PatientFlowApiTests.cs` (13)
- `src/alveara-client/src/PatientFlow.test.tsx` (10)
- `src/alveara-client/e2e/patient-flow-real-backend.spec.ts` (10)
- `src/alveara-client/playwright.flow.config.ts`
- `src/Alveara.Api/Architecture/Scheduling/PatientFlow.cs` (`PatientFlowStates`, `PatientFlowRules` — the parent's rule is untouched and still compared against)

Parent *test-support* files changed **additively or by widening visibility only** (nothing removed that a parent test uses): `SchedulingTestSupport.cs` (+26 lines: assignment-service factory, a pause-before-save context for race tests),
`src/test/fakeCalendarServer.ts` (three helpers `private` → `protected` so the new board fake can reuse them), `src/test/fakeFormsServer.ts` (an optional `required` field and one route).

### How the parent's *production* code changed, and why that is safe
`AppointmentFlowService` (STORY-011's service) was generalised: its `CheckInAsync`, `StartTreatmentAsync` and `CompleteAsync` still exist with the same signatures and now call `TransitionAsync`, which decides with `VisitStateMachine` instead of `PatientFlowRules`.
That is safe because `VisitStateMachine.Decide` gives STORY-011's exact answer for every pair of the original four states (`VisitStateMachineTests.For_the_four_original_states_it_gives_exactly_STORY_011s_answer`), the original values are reused from `PatientFlowStates` (not copied), and
the parent's 46 backend tests exercise the new code. `AppointmentManager` now refuses cancel/no-show/reschedule once the patient has **arrived** (`VisitStates.HasArrived`, true from check-in on) instead of when the flow is not `Scheduled`: identical for the four original states, and a Confirmed appointment (new) correctly stays movable.
`AppointmentView` gained fields only; the migration widened the flow-state check constraint (the original four values are still accepted). The legacy endpoints on `api/appointments` still require `ManageAppointments` (`VisitsApiTests.STORY_011s_endpoints_keep_their_rules…` and the parent's own role theory).

### Results on the final code
| Parent suite | Result |
|---|---|
| Backend `PatientFlowRulesTests` + `PatientFlowLifecycleTests` + `PatientFlowApiTests` | **46 / 46** (inside the full run) |
| Frontend `PatientFlow.test.tsx` | **10 / 10** (inside 484/484) |
| Real-backend walkthrough `patient-flow-real-backend.spec.ts` | **10 / 10** (`artifacts/playwright-real-backend/regression/run-output-s011.txt`) |

### STORY-011 Done-means, mapped
| STORY-011 acceptance | Still proven by (unchanged tests) |
|---|---|
| A scheduled patient checks in → status becomes "checked-in" | `PatientFlowLifecycleTests.A_scheduled_patient_who_checks_in_becomes_CheckedIn`; `PatientFlowApiTests` check-in; `PatientFlow.test.tsx` "offers Check in…"; its walkthrough CHECK-IN |
| A checked-in patient's treatment is completed → status becomes "completed" | `A_checked_in_patient_whose_treatment_is_completed_becomes_Completed…`; API; its frontend test; walkthrough COMPLETE |
| Trust: every status change logged with timestamp and user | `Every_flow_move_is_in_the_history_and_the_audit_log_with_user_and_time…`; walkthrough ACCEPTANCE |
| Failure paths (status fails to update, log fails) | `If_the_log_cannot_be_written_the_status_does_not_change_either`; refused-move and stale-version tests |

## Dependency regression
| Dependency | Result |
|---|---|
| `ALV-004-C01` (calendar; approved R01) | Backend `AppointmentLifecycleTests` 25 + `AppointmentLifecycleApiTests` 23 pass (inside the full run); frontend `Calendar.test.tsx` 30 pass; real-backend calendar walkthrough **12 / 12** (`…/regression/run-output-cal.txt`). Its tests were not modified. |
| `STORY-004` (booking; via the calendar) | `AppointmentSchedulerTests` 41 + `AppointmentsApiTests` 30; `Schedule.test.tsx` 19; real-backend scheduling walkthrough **10 / 10** (`…/run-output-sched.txt`) |
| `ALV-N010` (forms) | All form test classes pass unchanged (inside the full run); `FormTemplates.test.tsx` gained 2 tests, none removed or renamed; real-backend forms walkthrough **10 / 10** (`…/run-output-forms.txt`). `ALV-N010`'s template row gained one column (`RequiredAtCheckIn`, default false) and one endpoint; no form, version or signed copy is touched. |
| `ALV-003-C01` / `STORY-003` | Real-backend patient workspace **14 / 14** and patient registration **7 / 7** (`…/run-output-ws.txt`, `…/run-output-s3.txt`) |
| Gate A evidence | route scans (A2 axe on every shell route, A6 seven-role navigation matrix) **3 / 3** — the new nav entry did not disturb them (`…/run-output-gatea.txt`) |
| `ALV-N004` auth | **12 / 12** on the 3rd and 4th runs and with longer timeouts (`…/run-output-auth-passing-rerun.txt`, `…/run-output-auth-extended-timeouts.txt`); the first two runs failed only the recovery-key step (a 5 s wait; RSA key generation measured 1.3–4.2 s), as disclosed for ALV-004-C01 — ALV-N004's test, unchanged, nothing in scheduling is involved |
