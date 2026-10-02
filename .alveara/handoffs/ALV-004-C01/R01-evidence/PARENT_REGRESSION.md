# ALV-004-C01 R01 — Parent regression (STORY-004)

`STORY-004` — "Enable appointment scheduling with conflict prevention" — is `COMPLETE`, portal-verified at `d0024f3` (3 of 3 criteria; `.colaberry/progress.json`). This companion must keep its completion contract passing. It did, **without touching any STORY-004 test**.

## Proof the parent's tests were not modified
`git diff 99dcc4e 4179fa4 -- <the four files>` (baseline to implementation commit) is empty for:
- `src/Alveara.Api.Tests/AppointmentSchedulerTests.cs` (41 tests)
- `src/Alveara.Api.Tests/AppointmentsApiTests.cs` (30)
- `src/alveara-client/src/Schedule.test.tsx` (19)
- `src/alveara-client/e2e/schedule-real-backend.spec.ts` (10)

Two of STORY-004's *test-support* files gained additive members only (nothing removed or changed): `SchedulingTestSupport.cs` (a manager factory, reload/history helpers, a settable-clock `TestClock`) and `src/test/fakeScheduleServer.ts` (one protected `takeInjected` hook, with the existing canned-failure loop now calling it). STORY-004's `schedulingApi.ts` gained new exports and optional fields only.

## Results on the final code
| Parent suite | Result |
|---|---|
| Backend `AppointmentSchedulerTests` + `AppointmentsApiTests` | **71 / 71** (also run in isolation right after the refactor of the scheduler: 71 / 71) |
| Frontend `Schedule.test.tsx` | **19 / 19** (inside 434/434) |
| Real-backend walkthrough `schedule-real-backend.spec.ts` | **10 / 10** (`artifacts/playwright-real-backend/regression/run-output-sched.txt`) |
| Mocked browser suite | 68 / 68 |

## STORY-004 Done-means, mapped
| STORY-004 acceptance | Still proven by (unchanged tests) |
|---|---|
| A provider is available → the appointment is confirmed without conflicts | `AppointmentSchedulerTests.An_appointment_with_an_available_provider_is_confirmed_and_stored_in_practice_time`; `AppointmentsApiTests.A_front_desk_books_an_available_provider_and_a_retry_returns_the_same_appointment`; `Schedule.test.tsx` "books an available provider…"; its walkthrough ACCEPTANCE 1 |
| A provider is double-booked → the system rejects the new appointment | `A_provider_who_is_double_booked_rejects_the_new_appointment_and_nothing_is_stored` (4 overlap shapes), the wrap-around case, the HTTP 409, the page's plain-words refusal, its walkthrough ACCEPTANCE 2 |
| Trust: every scheduling action is logged with user and timestamp | the audit tests for a booking and for a refused attempt, the HTTP audit test, its walkthrough ACCEPTANCE 3 |
| Failure paths: double booking, unavailable provider, operatory conflict, audit failure, incorrect duration | the corresponding unchanged tests |

## What changed *around* the parent, and why it did not break it
- `AppointmentScheduler` now takes the shared locks on the **provider, operatory and patient** and checks conflicts through `SchedulingGuards` (provider, then operatory, then patient). The check order keeps every STORY-004 refusal identical; a **patient** conflict is new and was not reachable by any STORY-004 test (they book different patients when they overlap in time).
- `ScheduleAppointmentRequest` gained one optional trailing member (`Notes`), `AppointmentView` gained optional trailing members (`RowVersion`, `Notes`, `CancelReason`, `StatusChangedAtUtc`), so every STORY-004 call site compiles and every reader of the JSON still works. `ListAsync` keeps its meaning (the booked, i.e. scheduled, appointments); the calendar uses the new `CalendarAsync` / `includeAll=true`.
- A "Scheduled" history entry is written in the same save as each booking; STORY-004's counts of audit entries and appointments are unaffected.
- STORY-004's rejection and measurement helpers moved into `SchedulingRecorder` unchanged in behaviour (`appointment.scheduled` / `appointment.rejected`, refusals audited and measured, a refusal that cannot be audited still stands).
- The booking page `/schedule` is untouched; the calendar is a new page at `/calendar` (the nav lists both).

## Dependencies
`ALV-N003` (configuration read model, availability rule — reused unchanged), `ALV-003-C01` (patient picker, patient workspace — untouched; its walkthrough re-run **14 / 14**), plus `STORY-003` (walkthrough **7 / 7**), `ALV-N010` forms (**10 / 10**), auth (**12 / 12** on the retry; see `TEST_RESULTS.md` for the recovery-key timing note) and Gate A's route scans (**3 / 3**).
