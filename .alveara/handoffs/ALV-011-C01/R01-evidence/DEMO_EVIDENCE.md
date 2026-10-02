# ALV-011-C01 R01 — Demo evidence

The visible workflow was demonstrated **un-mocked**: a real Chromium against the real Vite proxy → real `Alveara.Api` → real SQL Server LocalDB, by `src/alveara-client/e2e/visit-board-real-backend.spec.ts` (config `playwright.board.config.ts`; how to run it: `docs/testing/REAL_BACKEND_E2E.md`).
Result: **15 of 15**. Output, JSON evidence and screenshots: `artifacts/playwright-real-backend/board-walkthrough/`.

## What the demo shows (in the order it runs)
1. **SETUP** — a practice (two operatories, two providers, an exam type), six staff with real roles (front desk ×2, assistant, dentist, office manager, billing), five patients, two form templates marked "required at check-in" through the real endpoint, and real form states: Ann signed both, Bo signed one and *started* the other, Cy none.
2. **BOARD** (`01-board-scheduled.png`) — a column per state with counts; each card shows patient, time, where the patient is, elapsed time and the check-in form cue — "Required forms complete (2 of 2)", "Forms: 1 of 2 complete · Financial policy — Started, not signed", "Forms: 0 of 2 complete". axe: 0 serious/critical in light and dark. Viewing a patient's forms page changes nothing.
3. **FRONT OFFICE** (`02-checked-in.png`) — the front desk confirms and checks in from the board; chairside buttons are not offered to them; a patient with an open form is still checked in (the cue never blocks).
4. **CHAIRSIDE** — the assistant marks patients ready and seats one; the front-office buttons are not offered to the assistant.
5. **ROOM** (`03-room-occupied.png`) — seating a second patient into the occupied room is refused in words ("Op 1 already has a patient who is seated or in treatment. Nothing was changed. Use another room, or move on the patient who is there first."); the API gives 409 `operatory_occupied` naming the occupier. axe clean.
6. **WHOLE CHAIN** (`04-checked-out.png`, `05-completed.png`) — treatment (dentist), check-out (desk), the room is freed and the second patient is seated (assistant), completion (office manager, who is asked first; "Keep it open" changes nothing). The history has all eight events with `from -> to` and **four distinct people**.
7. **ASSIGNMENT** (`06-assigned.png`) — the provider and operatory a patient is actually with change without moving the booking ("Booked: Dr. Patel · Op 2" stays visible; the calendar API still places it by the booking); a seated patient cannot be moved into an occupied room; a patient moved into a held room is refused at seating.
8. **CONFLICT** (`07-conflict.png`) — a second desk moves a patient first; the first desk's stale press shows the shared banner, nothing is applied on top, Reload shows the real state. axe clean in both themes.
9. **LIVE** — another workstation checks a patient in; the open board shows it by itself within its 15-second refresh (the run waits for it, ~15.5 s).
10. **CANCELLED AND NO-SHOW** (`08-cancelled-apart.png`) — a cancelled appointment sits apart from the chain and is labelled in words; the completed visit is elsewhere.
11. **LEFT OPEN** (`09-carried-over.png`) — a visit made to look left open from days ago (one declared SQL UPDATE; the API correctly refuses to book in the past) appears on today's board as "Carried over from … and still open", and its room still blocks another patient. axe clean in both themes.
12. **PERMISSIONS** — the dentist sees chairside buttons only; billing has no nav link, a permission message at `/flow`, and 403 from the API; the front desk cannot do chairside moves (403 naming `UpdateChairsideFlow`); STORY-011's own endpoints keep `ManageAppointments`.
13. **KEYBOARD** — a move made with the keyboard; focus stays on the visit.
14. **RESPONSIVE** (`10-phone.png`) — at 768 px and 390 px the page does not scroll sideways; on a phone the columns stack and a card's actions are reachable; axe clean on the phone layout in both themes.
15. **ACCEPTANCE** — the audit log holds every confirm / check-in / ready / seat / treatment / check-out / completion / assignment / requirement change with user and time, once per move, with no patient details.

## Honest notes on the demo
- The practice's "today" is the real date and the demo's appointments are in 2030, so elapsed cues for waiting patients read in days ("Starts in 1199 d 23 h"); the in-step times ("Just moved to this step") are real.
- Looking at the first screenshots found two things the assertions could not: waiting cards read "Starts in 28794 h 50 min" (long durations now read in days) and four empty columns took as much room as the busy ones (empty columns now shrink). Both fixed and re-verified; only the final complete run is counted.
- One arrangement uses the database directly (the "left open" visit), declared in the spec header and in `docs/testing/REAL_BACKEND_E2E.md`.
- At desktop width the eight columns still scroll sideways when many states are busy (see Limitations).
