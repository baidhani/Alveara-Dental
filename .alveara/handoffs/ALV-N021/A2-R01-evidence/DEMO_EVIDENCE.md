# ALV-N021 A2-R01: demonstration evidence

Real-backend walkthroughs of the final evidence run (F9, tree `215436c`), each on a fresh database that was dropped afterwards; Chromium; axe accessibility scans in light and dark themes where the spec runs them. Screenshots and each spec's evidence JSON are in `artifacts/walkthroughs/<name>/`; the runner logs are in `artifacts/final-evidence/4-part1/walkthroughs/`.

| Walkthrough | Clock | Result | What it shows |
|---|---|---|---|
| board (`visit-board-real-backend.spec.ts`) | injected `2030-01-14T13:30:00Z` | 16 / 16 | columns per state; front office vs chairside buttons; occupied room refused in words; whole path with the booked dentist and the checkout resolved; provider/room change with a reason and two notifications; stale press reported; live update; cancelled/no-show apart; carried-over visit; permissions; keyboard; tablet/phone; **PARITY** (front desk and assistant in Confirmed, Checked in, Seated: the calendar drawer and the board card offer the same actions, blocked the same way, with the same next-actor text); audit without patient details |
| flow (`patient-flow-real-backend.spec.ts`) | injected | 10 / 10 | confirm and check in from the calendar; the checkout resolved in the drawer and completed; STORY-011's shortcuts refused on every route; repeats, stale, race of six; permissions incl. the legacy route; keyboard; audit |
| calendar (`calendar-real-backend.spec.ts`) | real | 12 / 12 | day view with labelled blocked time; provider and same-patient conflicts in one refusal format; reschedule with "From ... to ..., by ..."; cancel with a category; no-show with Rebook and Open patient; week view shading, two-line headers, narrow blocks |
| schedule (`schedule-real-backend.spec.ts`) | real | 10 / 10 | "Appointment booked."; status column and links to the appointment and the patient; refusals; duration from the type |
| reports (`reports-real-backend.spec.ts`) | real | 7 / 7 | cancellations by category and short notice; other reports unchanged |

Accumulated chain (F10): 40 / 40, stages 0-4 including R05 cases 3.01-3.14 (scheduling and visit flow under the A2 rules; case 3.09 uses the test-only clock-advance route, one record in its `clock-advances.jsonl`).

Not demonstrated in a browser: Firefox/WebKit (F-068 is a Firefox report; the fix is a typing hint that does not depend on the browser).
