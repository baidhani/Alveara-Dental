# ALV-004-C01 R01 — Test results

Implementation commit `4179fa4aa1249a35b8f9d714216c7994292d7e39`; baseline HEAD `99dcc4e` (`STORY-004: record course completion`, plus the portal sync and Command Center commits). Raw output is in `artifacts/`.

| Suite | Result | Output |
|---|---|---|
| Backend `dotnet test` (real SQL Server LocalDB, one database per test class) | **721 of 721**, 0 failed, 0 skipped, 38 m 17 s (up from 673: **48 new**) | `artifacts/test-runs/backend-full.txt` |
| Frontend `npx vitest run` | **434 of 434** (43 files; up from 390: **44 new**) | `artifacts/test-runs/frontend-vitest.txt` |
| Mocked browser `npx playwright test` | **68 of 68** | `artifacts/test-runs/mocked-playwright.txt` |
| Real-backend/real-browser **ALV-004-C01 calendar walkthrough** | **12 of 12**; 12 axe scans (6 states × light/dark), **0 violations of any impact** | `artifacts/playwright-real-backend/calendar-walkthrough/` (JSON, run output, 12 screenshots) |
| Regression: STORY-004's own real-backend walkthrough | **10 of 10** | `artifacts/playwright-real-backend/regression/run-output-sched.txt` |
| Regression: ALV-N010 forms walkthrough | **10 of 10** | `…/run-output-forms.txt` |
| Regression: ALV-003-C01 patient-workspace walkthrough | **14 of 14** | `…/run-output-ws.txt` |
| Regression: STORY-003 original walkthrough | **7 of 7** | `…/run-output-s3.txt` |
| Regression: `auth-real-backend` | **12 of 12** on the retry (see the note below) | `…/run-output-auth-passing-rerun.txt` |
| Regression: Gate A route scans (A2 axe on every shell route, A6 seven-role navigation matrix) | **3 of 3** | `…/run-output-gatea.txt`, `a2-axe-results.json`, `a6-role-navigation.json` |
| Repository checks `node --test tests/*.test.mjs` | **7 of 7** | `artifacts/test-runs/repo-checks.txt` |

**Total: 1298 passing, 0 failing** (721 + 434 + 68 + 12 + 10 + 10 + 14 + 7 + 12 + 3 + 7). Also clean: `tsc -b`, `npm run build`, `oxlint` (no warnings in files this story touched).

## The 48 new backend tests (real SQL Server)
- `AppointmentLifecycleTests` (25) — reschedule keeps the appointment's identity, moves provider/operatory/time and remembers where it was; frees the old slot; keeps the duration unless one is given; may overlap its own old time; a repeated move changes nothing; refused into a busy provider, busy operatory or the patient's other appointment (naming the one in the way); respects working hours, blocked time, valid durations and a future start; a refused reschedule is audited and a malformed one is not; stale/missing/invalid row versions; only a scheduled appointment can be rescheduled; **audit failure leaves the appointment unmoved**; cancel needs a reason, keeps the appointment on record, frees the slot, audits without the reason text; cancelled appointments are in the calendar list and not in the booked list; cancelling twice and a stale cancel; no-show only after the start (controllable clock), keeps the record, idempotent, not for a cancelled appointment; notes (set at booking, changed, cleared, left alone, too long, on a cancelled appointment, never in the audit); **patient overlap at booking** (refused, back-to-back fine, a cancelled appointment does not block, audited); **races**: two users rescheduling the same appointment (one change), six different appointments into one slot (one winner, no overlap anywhere), a booking racing a reschedule, six same-patient bookings with different providers; privacy-safe measurement events.
- `AppointmentLifecycleApiTests` (23) — anonymous 401s; per-role access for reschedule/cancel/notes (theory over eight roles) and history (theory over five roles); CSRF on every change; the HTTP reschedule flow with the new version and a stale 409; every conflict shape with the code and the appointment in the way, plus 400/404 shapes; `patient_double_booked` for a new booking; cancel with its reason requirement and idempotent repeat; calendar list (`includeAll`) versus booked list; `no_show_too_early`; notes at booking and later and the 400 for a long note; history order with the previous place; audit with the signed-in user.

## The 44 new frontend tests
`Calendar.test.tsx` (30, through the real `<App />` against an in-memory fake of the lifecycle API) — day view columns and pixel positions, working-hours shading, provider and operatory filters (and the same filters sent to the server), navigation, all-status requests, week view (Monday–Sunday, side-by-side overlaps), cancelled/no-show presentation in words, read-only role, permission gating, load errors and Retry, drawer details/history/focus/Escape, booking (fast lookup, default duration, notes, key), clicking an empty slot, every conflict explanation (provider, operatory, patient, unavailable), a dropped connection on booking (Book again reuses the key), reschedule with version and history, a refused reschedule, a stale edit with Reload, double-click sends one request, a dropped connection on reschedule, cancel with reason, no-show enabled/disabled by the start time, notes, and axe on the day view, week view and both drawers. `calendarLayout.test.ts` (14) — date arithmetic, weekday/week start, practice-zone "now", axis range, block placement, lane assignment, determinism, midnight clipping, slot snapping.

## Mutation check (race tests detect a race)
Disabling the application locks in `SchedulingGuards.AcquireLocksAsync` made the three new race tests and both of STORY-004's race tests fail; the locks were restored (see the handoff).

## Run notes (honest account)
- **`auth-real-backend` is timing-sensitive on this machine.** Its backup test waits 5 seconds for a PGP recovery key to be generated (ALV-N004's test; Gate A has a separate timing spec for that latency). In this session it failed that one step on two of four runs (`…/run-output-auth-first-attempt-recovery-key-timeout.txt` is one such run: 9 passed, that step failed, 2 not run) and passed 12 of 12 on the others, including the immediate retry on a fresh database and the previous round. Nothing in scheduling is involved; the failing step is before any scheduling code is used.
- The first run of the new walkthrough failed in four places that were mine (a wrong column order, the known contrast finding F2 appearing in the drawer, two data mistakes in the race step, and `sqlcmd` needing `-I`) and one real product flaw found by the keyboard test (focus was lost after a reschedule, so Escape did nothing) - all fixed; only the final complete run is counted.
- Not re-run (unaffected by this story): Gate A A1/A9 specs and the recovery-key timing spec.
