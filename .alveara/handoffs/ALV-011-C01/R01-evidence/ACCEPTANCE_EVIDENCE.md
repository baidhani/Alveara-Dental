# ALV-011-C01 R01 — Acceptance evidence

Each acceptance line of the prompt, with the tests and walkthrough steps that prove it. Test names are the real ones; raw output is in `artifacts/`. "Walkthrough" is the un-mocked
`e2e/visit-board-real-backend.spec.ts` (real Chromium, real API, real SQL Server LocalDB); its run output, JSON and screenshots are in `artifacts/playwright-real-backend/board-walkthrough/`.

## 1. Original STORY-011 tests still pass — **PASS**
STORY-011's seven files are **byte-identical** to the portal-verified commit `207791d` (see `PARENT_REGRESSION.md`) and all pass on the final code: backend `PatientFlowRulesTests` 20,
`PatientFlowLifecycleTests` 13, `PatientFlowApiTests` 13 (inside the full run); frontend `PatientFlow.test.tsx` 10 and the `flowWord` cases (inside 484/484); its own real-backend walkthrough **10/10**.
`PatientFlowRules` / `PatientFlowStates` were not touched; `VisitStateMachine` is a superset and a test compares every pair of the original four states.

## 2. A visit can traverse the major production state chain — **PASS**
- `VisitStateMachineTests.A_visit_can_traverse_the_whole_production_chain_one_step_at_a_time` and `Every_pair_of_states_has_exactly_the_documented_outcome` (all 64 pairs).
- `VisitWorkflowTests.A_visit_traverses_the_whole_production_chain_and_each_move_is_in_the_history_and_the_audit_log_with_user_and_time` (service, real SQL).
- `VisitsApiTests` — the 20-case role-against-move table and `The_board_shows_what_the_api_has_just_changed…`.
- Walkthrough **WHOLE CHAIN**: one visit goes Scheduled → Confirmed → CheckedIn → Ready → Seated → TreatmentStarted → CheckedOut → Completed pressed by **four different people** (front desk, assistant, dentist,
  office manager); the history lists all eight with `from -> to` details and four distinct actors; the completion asked for confirmation first and declining it changed nothing.
- Frontend `FlowBoard.test.tsx` "walks a patient down the chain one press at a time".

## 3. Provider and operatory assignment are visible — **PASS**
- Every appointment view carries the booked provider/operatory **and** the effective visit-time ones (`visitProviderName`, `visitOperatoryName`); `VisitAssignmentTests` (11) cover assigning, clearing back to "as booked", history with where the patient WAS,
  audit without patient details, repeats, stale versions, concurrency, finished/cancelled visits, unknown/inactive provider or operatory, audit failure.
- `VisitAssignmentTests.Assigning_a_provider_and_operatory_is_visible_in_the_view_and_never_moves_the_booking` — the booked slot still blocks others and the calendar still places it by the booking.
- Board cards show "Dr. X · Op N" and, when different, "Booked: …" (`FlowBoard.test.tsx` "shows where each patient actually is…"). Walkthrough **ASSIGNMENT**: the board shows the new place and the booking; `/api/appointments` and the calendar still say Dr. Patel / Op 2.

## 4. Cancelled/no-show remain distinct from completed — **PASS**
- They are the booking `Status`, not visit states: `VisitStateMachineTests.Unknown_states_are_refused_and_cancelled_and_no_show_are_not_visit_states`; the database refuses them as a flow (`VisitWorkflowSchemaTests`, 5 cases) and refuses any flow past Scheduled on a cancelled/no-show appointment.
- `VisitWorkflowTests.Cancelled_and_no_show_stay_distinct_from_completed` and `VisitBoardTests.Cancelled_and_no_show_appointments_are_on_the_board_distinct_from_completed_and_offer_no_moves`.
- Board: a separate "Cancelled and no-show" group with the status written in the card ("· Cancelled", struck-through name, dashed outline); no buttons. Walkthrough **CANCELLED AND NO-SHOW**: a cancelled and a completed visit for the same patient sit in different places.
- A confirmed appointment that is then cancelled or marked no-show resets its flow and keeps the Confirmed event (`VisitWorkflowTests.A_confirmed_appointment_can_still_be_rescheduled_or_cancelled`) — found because the database rightly refused the first attempt.

## 5. Two users cannot silently overwrite current state — **PASS**
- Every real change carries the row version (`VisitWorkflowTests.A_repeat_changes_nothing_and_a_stale_version_is_the_shared_conflict`, `Two_people_making_different_moves_on_the_same_visit_never_overwrite_each_other`, `VisitAssignmentTests.A_stale_version_is_the_shared_conflict_and_two_people_assigning_at_once…`).
- Rooms: `VisitOccupancyTests.Six_receptionists_seating_six_different_patients_into_one_room_at_once_seat_exactly_one` and `VisitAssignmentTests.Two_seated_patients_moved_into_the_same_free_room_at_once_leave_exactly_one_there` — **proven sensitive**: with the locks removed, six patients are seated in one room and two are moved into one room.
- HTTP: `VisitsApiTests` stale/missing version, `Two_chairside_users_seating_two_patients_into_one_room_at_once_leave_exactly_one_seated`.
- Board: `FlowBoard.test.tsx` "reports a change someone else made first as a conflict, applies nothing…"; walkthrough **CONFLICT** — a second desk confirms a patient first; the first desk's stale "Check in" shows the shared banner, nothing is applied, Reload shows the real state (axe-clean in both themes).

## 6. Live board updates to persisted truth — **PASS**
- The board is built only from stored appointments (`VisitBoardTests` — "The board follows what is stored…", "…never writes anything"); after any action it takes the server's answer and re-reads the whole board.
- It re-reads every 15 seconds and when the tab becomes visible; a failed or >10-second read keeps the last good board and says it is out of date (`FlowBoard.test.tsx` "re-reads the board on its own every 15 seconds…", "keeps the last good board…", "gives up on a read that takes longer than 10 seconds…", "never shows one day's visits under another day's heading…").
- Walkthrough **LIVE**: another workstation checks a patient in; the open board shows it by itself within the 15-second refresh with no click or reload (the "Updated hh:mm:ss" text changes). Walkthrough **LEFT OPEN**: a visit left seated from days ago appears on today's board marked "Carried over" and its room is still blocked.
- Elapsed times use the server's clock, never the browser's (`visitHelpers.test.ts`, `boardNowMs` cases).

## 7. Check-in shows required-form/consent readiness accurately and does not fabricate completion — **PASS**
- `CheckInReadinessTests` (15, real SQL): nothing required → "none required" (not complete); missing; a draft is in progress and **viewing/listing/saving a form never makes it complete**; signed on the current version → complete; **a signature on older wording is never complete** until the current version is signed; a draft on the new version shows in progress; a voided form no longer counts; **a form marked Signed without its signed copy is not complete**; another patient's signature never counts; an inactive template never blocks; several forms ordered with an honest count; unknown patient 404; batch equals single and writes nothing; marking a template required is audited, publishes no version, repeats change nothing, stale versions conflict, audit failure changes nothing.
  Mutation checks: allowing any signed version, and dropping the signed-copy requirement, each fail tests.
- `VisitBoardTests` (accurate cue on the board; absent once seated, for cancelled, and for callers who may not see form status; "no required forms" says so) and `FlowBoard.test.tsx` "shows the check-in form cue accurately…".
- Walkthrough **BOARD**: real forms — Ann signed both required forms ("Required forms complete (2 of 2)"), Bo signed one and only *started* the other ("Forms: 1 of 2 complete", "Financial policy — Started, not signed"), Cy did nothing ("0 of 2"); the readiness API agrees; looking at the forms page changes nothing. Walkthrough **FRONT OFFICE**: Bo is checked in with a form still open — the cue never blocks a check-in.

## Failure paths from the prompt
| Failure path | Where it is handled and proven |
|---|---|
| Invalid transition | `invalid_flow_transition` 409 — state machine (64 pairs), `VisitWorkflowTests` (9-case theory), `VisitsApiTests`, board explanation |
| Operatory already occupied where enforced | `operatory_occupied` 409 naming the occupier — `VisitOccupancyTests`, `VisitAssignmentTests`, `VisitsApiTests`, board/`FlowBoard.test.tsx`, walkthrough **ROOM** and **ASSIGNMENT** |
| Concurrent status change | shared `concurrency_conflict` 409 + races above; board banner and Reload |
| Missing appointment | `appointment_not_found` 404 — service, API, board message ("Refresh the board") |
