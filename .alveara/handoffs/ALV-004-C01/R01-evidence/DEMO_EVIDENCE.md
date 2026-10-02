# ALV-004-C01 R01 — Demo evidence

Demo result: **verified**. A real Chromium against the real Vite proxy, the real `Alveara.Api` and a real LocalDB database — nothing mocked (`e2e/calendar-real-backend.spec.ts`, run with `playwright.calendar.config.ts`; **12 of 12**). Users: front-desk users (two), a dentist and a billing user; the practice is configured through the real configuration API. Results: `artifacts/playwright-real-backend/calendar-walkthrough/calendar-e2e.json`.

One arrangement uses the database directly and says so in the spec: the API (correctly) refuses to book a start in the past, so the no-show demonstration moves one booked appointment ten years back with a single SQL UPDATE of its times, which is exactly how a long-past appointment looks.

| Test | What it shows | Screenshot |
|---|---|---|
| CALENDAR | The day view with one column per provider; Ann Calendar 09:00–10:00 in Dr. Rivera's column (`top: 120px; height: 60px`), Bo Booker 10:30–11:00 in Dr. Patel's (`210px / 30px`); each provider's real working hours drawn (08:00–17:00) with the rest hatched; provider and operatory filters. | `01-day-view.png` |
| BOOK + CONFLICTS | Booking through the drawer. Five attempts are refused in words and book nothing: provider double-booked ("Dr. Rivera already has an appointment from 09:00 to 10:00."), operatory in use, the patient already booked elsewhere ("A patient cannot be in two places at once."), outside working hours, and inside the blocked lunch. A conflict-free booking starting exactly when the first ends then succeeds. | `02-conflict-provider.png`, `03-conflict-patient.png`, `04-booked-in-drawer.png` |
| RESCHEDULE | A move to another provider, operatory and time; the history shows "Was 2030-01-14 10:00 with Dr. Rivera in Op 1"; the block is now in Dr. Patel's column; a refused move ("Dr. Patel already has an appointment from 10:30 to 11:00.") leaves it where it was. | `05-rescheduled-with-history.png`, `06-reschedule-refused.png` |
| STALE EDIT | A second user moves the appointment first; the first user's save is refused with "Someone else changed this while you were editing", the second user's change stands, and Reload shows the real state. | `07-stale-edit.png` |
| CONCURRENT EDITS | Six appointments racing into one slot: exactly one succeeds; two users moving the same appointment: `[200, 409 concurrency_conflict]`; no overlap remains. | — |
| CANCEL | A cancellation without a reason is refused; with one, the appointment stays on the calendar with a dashed outline, a struck-through name and "· Cancelled" in its first line, and the freed slot can be booked by another patient. | `08-cancelled-drawer.png`, `09-cancelled-on-calendar.png` |
| NO-SHOW | A future appointment's no-show control is disabled and the API returns `no_show_too_early`; once the start has passed the appointment is marked no-show, shown with a dotted outline and "· No-show", and stays on the record. | `10-no-show-drawer.png`, `11-no-show-on-calendar.png` |
| PATIENT OVERLAP + WEEK VIEW | The API refuses a patient in two places (`patient_double_booked`); the week view shows Monday–Sunday with overlapping appointments side by side (`0%` and `50%` lanes). | `12-week-view.png` |
| PERMISSIONS | A dentist can open the calendar and an appointment's details but has no New appointment button or action buttons, and the API returns 403; billing has no Calendar link and is denied the page. | — |
| KEYBOARD | An appointment is rescheduled with no mouse (Tab/Enter; focus moves into the drawer and back to its heading; Escape closes it). | — |
| ACCEPTANCE (audit) | 11 scheduled, 5 rescheduled, 1 cancelled, 1 no-show and 12 rejected entries, each with a user and a recent time and none containing a patient or provider name, a time or a reason. | — |

Accessibility: **12 axe scans** (day view, conflict explained, stale edit, cancelled drawer, no-show on calendar, week view; each in light and dark) — **0 violations of any impact**.
