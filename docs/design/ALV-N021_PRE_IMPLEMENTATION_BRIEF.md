# ALV-N021 pre-implementation briefing (revision 2): date, scheduling and visit-transition integrity

- **Story:** ALV-N021 (execution item 58), consolidating R05 **PKG-04** (date-only and time correctness) and **PKG-05** (visit transitions, role enforcement, scheduling integrity).
- **Revision:** 2, answering `.alveara/reviews/ALV-N021/BRIEFING-R01.md` (CHANGES_REQUIRED). Revision 1 is in git at `9552ea5`.
- **Date:** 2026-11-13. **Session:** CC-20261109-8w3d.
- **Status:** **PROPOSED, awaiting a new independent briefing review.** Planning evidence only. No code, test, schema, migration, setting or permission has changed; no test, build or suite was run.
- **Binding inputs:** `GPT Docs/Alveara_Dental_Execution_Plan_v4.md` item 58 and its stabilization pre-implementation-briefing and focused-evidence rules; `docs/design/MANUAL_E2E_STABILIZATION_PLAN_R05.md` PKG-04, PKG-05, chain stages 3 and 4 (cases 3.01-3.14), PD-05, PD-06; `docs/stabilization/DISPOSITION_REGISTER.json`; the owner's decisions of 2026-11-12/13 (section 6).
- **Companion files in this submission:**
  - `docs/design/ALV-N021_SOURCE_EVIDENCE_APPENDIX.md`: the complete raw-log entries for every in-scope identifier, copied by script with line numbers (blocking correction 1).
  - `docs/design/ALV-N021_PLAN_CHANGE_PROPOSAL.md`: two owner-approved deferred enhancements (ALV-N030, ALV-N031) for the reviewer to place in the plan. Not entered by the agent.

## 0. Answers to the R01 review

| R01 item | Where answered |
|---|---|
| Blocking 1: verbatim source evidence | `ALV-N021_SOURCE_EVIDENCE_APPENDIX.md` (50 register rows of PKG-04/PKG-05, raw log SHA-256 `715b1f84...1c6a`, each with heading, primary entry and every other line naming the id) |
| Blocking 2: explicit, safer visit workflow | Section 5 (path, labels, ownership, settings, corrections, appointment-day rule) and section 5.6 (complete state x action x role table with permissions, grants, API, UI, denials, history, concurrency) |
| Blocking 3: owner decisions | Section 6 records every decision; PD-06 unchanged and escalated; PKG-04 marked independently implementable (section 2.1) |
| Required proof additions | Section 8 (each review bullet mapped to a test) |

## 1. Intended result

### 1.1 User-visible
1. A date of birth or other date-only value is the same day everywhere (screens, prints, statements, exports) in every time zone; the printed prescription no longer says 3/13 when the header says 3/14 (F-107); a performed date is never a day late (F-118).
2. A recorded time is the real time of the action: an answer's entered time is not replaced by a later change (F-184, F-218), and no made-up 8:00:00 or zero-second time is shown as a click time (F-100, Q-5an).
3. One visit workflow with one vocabulary: Calendar and Visit board show the same actions for the same appointment and role, each role sees only its own next step, and when another role must act the card says who ("Awaiting front desk checkout"). No shortcuts, no dead ends (F-056, F-058, F-059, F-063, F-065, Q-065a).
4. Check-in only on the appointment's practice-local day; a future appointment cannot be completed (F-047, F-071).
5. Corrections with a reason and full history: undo check-in before seating; correct a no-show; cancel with a required category (F-050, F-057, F-073).
6. Provider reassignment by a grantable permission, with a required reason, the same availability checks as booking, and urgent notifications to both providers (owner decision; replaces the board's "Change room or provider" that bypassed the booking rules: F-060, F-061, F-062).
7. History says who did what, from what to what (F-046, F-049, F-062, F-074); blocked time cannot silently cover an appointment (F-053); Week view shows blocked and non-working time (F-052); cancelled and no-show appointments offer Rebook and Open patient (F-045, F-051, F-073); wording and status items corrected (Group C).

### 1.2 Technical
1. A shared date-only helper (client) and the equivalent server rule replace ad-hoc `new Date(...)`/`toLocaleDateString()` on date-only fields; each of about 94 client sites is classified date-only or instant first, not bulk-replaced.
2. One server-side **visit transition policy** (state x action x actor x appointment x settings) used by every API path; an "available actions" result per appointment that Calendar and Board both render; legacy endpoints routed through the same policy.
3. Overlap and state invariants enforced server-side for blocked time, reassignment and completion, through `SchedulingGuards`.
4. Chain stages 3 and 4 (3.01-3.14) added to the accumulated chain, with real two-context browser coverage for the booking race (3.10) and the stale view (F-066). The chain run exceeds five minutes and is requested from the owner before running.

## 2. Scope boundary

- **In scope:** the 50 PKG-04/PKG-05 register rows in the appendix, and the owner's decisions in section 6 (configurable Confirm and Mark ready, reassignment permission and notification, dentist close rule).
- **Not in this story:** date-format preference (PKG-14/PD-37); treatment-plan scheduling, follow-up and financial dates (PKG-08/PKG-09, which depend on this story); allergy/medication authority (PD-06, see 6.2); **Replace appointment (proposed ALV-N030)** and **confirmation reminders (proposed ALV-N031)**, both in the plan-change proposal; any new external dependency or infrastructure change.
- **Governance flags (escalated in this briefing):** permission-model change (new permissions, changed default grants, appointment-specific rules); cancel category changes report inputs; two additive migrations (section 7).

### 2.1 Independent implementability
PKG-04 (section 4, Group A) needs no owner decision and touches no permission or schema. **It may be implemented and reviewed as its own attempt** ahead of PKG-05 if the reviewer prefers smaller packages. Recommendation: two attempts, PKG-04 first (R01), PKG-05 second, under one story.

## 3. Current state read from the code

| Area | What is there | Consequence |
|---|---|---|
| State machine | `Architecture/Scheduling/VisitStateMachine.cs`: Scheduled, Confirmed, CheckedIn, Ready, Seated, InTreatment, CheckedOut, Completed; forward only; legal shortcuts CheckedIn->InTreatment, CheckedIn->Completed, InTreatment->Completed; `DutyFor`: Confirmed, CheckedIn, CheckedOut are front office | Shortcuts are removed (5.2); corrections become explicit, separately authorized moves |
| Two API paths | `AppointmentsController` POST `{id}/check-in`, `{id}/start-treatment`, `{id}/complete` need only `ManageAppointments` (calendar panel). `VisitsController` POST `{id}/state` checks `UpdateVisitFlow` or `UpdateChairsideFlow` by duty (board). `VisitsController` PUT `{id}/assignment` ("Seen by") needs either flow permission | Root cause of F-058/F-059 (two rule sets). Both routed through one policy (5.6) |
| Default grants | `PermissionMatrix`: Dentist, Hygienist, Assistant hold `UpdateChairsideFlow`; FrontDesk holds `UpdateVisitFlow`; OfficeManager holds both; Admin holds every permission; Unassigned none | Changed in 5.5 (escalated) |
| Flow service | `AppointmentFlowService.cs`: one transaction per move, row-version check, `AppointmentEvent` ("from -> to") plus PHI-free audit in the same save, operatory lock on seating, repeat of the current state is a no-op | Reused; the event gains actor display, reason and category (F-046, F-074) |
| Booking guards | `SchedulingGuards.cs`: `sp_getapplock` on provider, operatory, patient in sorted order, 10 s, then 503 `schedule_busy`; half-open intervals; only `Scheduled` holds time | Reassignment and blocked-time creation use it (F-053, F-061) |
| Notifications | `NotificationKinds` with priorities Normal, Important, Urgent; `CK_Notifications_Kind` and `CK_Notifications_Shape` constraints list the allowed kinds; ALV-N020 added `staff.inactivation_failed` by migration | A new kind for reassignment needs one additive migration (7) |
| Date-only | `PrintPreview.tsx` line 7 `new Date(iso).toLocaleDateString()` on `patientDateOfBirth` | F-107 root cause confirmed; about 94 similar client sites to classify |
| Practice clock | `Architecture/Time/PracticeClock.cs`: practice zone, DST gap handling, injectable clock | Basis for the appointment-day rule and its DST tests |

**Verify first (read in the first implementation step and reported, not assumed):** `VisitBoardService`, `VisitAssignmentService` (how "Seen by" rows are stored and shown), `AppointmentManager` (no-show, cancel, reschedule), `AppointmentViews`, the Calendar and Board components, blocked-time creation, how a user account is linked to a provider record (needed for "own visit"), where practice settings live (for the two new settings), whether provider-to-appointment-type capability data exists, F-118's performed-date path, the source of F-100's 8:00:00, and why a Completed appointment held its slot in F-071.

## 4. Finding-by-finding plan

Each item is reproduced first and recorded CONFIRMED, NOT_REPRODUCED, DUPLICATE, WITHDRAWN or DESIGN_DECISION_REQUIRED with the test that proves it. Full raw entries: appendix.

**PKG-04 (Group A, independently implementable):** F-107 (S1) and F-118 (S3) reproduced in a zone behind UTC and across the date line before any change; F-184 and F-218 (duplicate of F-184): the answer's entered time and the last-change time are stored and shown separately; F-100 and Q-5an: trace the zero-second times (form default vs click time); F-030 kept as the clock/time-zone observation it records (display verified correct) with a regression test; F-133 WITHDRAWN (register row kept). Fix rule: no date-only value passes through a time-zone conversion; instants stay UTC with practice-zone display.

**PKG-05 Group A (workflow and roles):** F-056, F-058, F-059, F-063, F-065, F-066, Q-065a, F-057, F-073, F-047, F-071, F-060, F-061, F-062: answered by section 5. F-170 and D-01a WITHDRAWN (F-170's delegation model kept with a boundary and audit test; D-01a: FrontDesk keeps read-only fees). Permission questions Q-5a, Q-5q, Q-5ar, Q-5au, Q-5be, Q-5bh: each checked against the Permission Matrix and recorded with its answer; **no grant changes beyond section 5.5** without a separate owner decision (Q-5ar touches prescriptions and stays with PD-06's escalation).

**PKG-05 Group B (overlap and state):** F-053 (blocked time lists overlapped appointments and requires confirmation; overlapped appointments flagged), F-061 (reassignment through `SchedulingGuards`), F-071 (day rule; completed slot behaviour verified), F-047.

**PKG-05 Group C (history and usability):** F-044b (blank duration: section 6), F-045, F-046, F-048, F-049, F-050 (section 6), F-051, F-052, F-054, F-055, F-062, F-064, F-068 (NOT_REPRODUCED as an app defect: Firefox-only, re-check), F-069, F-070, F-072, F-074, F-076, F-077. F-044 (raw, the 30-minute entry) stays WITHDRAWN.

## 5. Visit workflow (owner-confirmed, for review)

### 5.1 Normal path and labels
`Scheduled -> Confirmed -> Checked in -> Ready -> Seated -> In treatment -> Ready for checkout -> Completed`

| Step | Screen action label | Persisted state | History wording |
|---|---|---|---|
| 1 | **Confirm** | Confirmed | "Confirmed by <user>" |
| 2 | **Check in** | CheckedIn | "Checked in by <user>" |
| 3 | **Mark ready** (room ready) | Ready | "Marked ready by <user>" |
| 4 | **Seat patient** | Seated | "Seated in <room> by <user>" |
| 5 | **Start treatment** | InTreatment | "Treatment started by <user>" |
| 6 | **End treatment / ready for checkout** | CheckedOut (kept for compatibility) | "Treatment ended by <user>; awaiting checkout" |
| 7 | **Complete visit** (administrative close) | Completed | "Visit completed by <user>" |

The screen never shows "Check out" for step 6; the CheckedOut state is displayed as **"Ready for checkout - awaiting front desk"**.

### 5.2 Settings and no shortcuts
Two new practice settings (Admin only; change audited; applies to the next move, never to a move already made):

| Setting | Default | Effect when required | Effect when optional |
|---|---|---|---|
| Confirmation required | Required | Check in is refused until the visit is Confirmed; Confirm is available any time before check-in | Check in allowed from Scheduled or Confirmed |
| Mark ready required | Required | Seat patient only from Ready | Seat patient from CheckedIn or Ready |

Seat patient is **always required** (it takes the operatory lock and refuses an occupied room). There is no other shortcut: CheckedIn->InTreatment, CheckedIn->Completed and InTreatment->Completed are removed; the only route to Completed is CheckedOut->Completed. The UI never renders an unavailable action as enabled; it renders only actions in the server's available-actions result.

### 5.3 Who may act
- **Front desk** (`UpdateVisitFlow`): Confirm, Check in, Undo check-in, Cancel, Mark no-show, Correct no-show, Complete visit.
- **Chairside** (`UpdateChairsideFlow`): Mark ready, Seat patient.
- **Visit's own provider** (`UpdateChairsideFlow` **and** the actor is the appointment's booked provider): Start treatment, End treatment / ready for checkout. No covering rule: if someone else treats, the visit is reassigned first (5.4). A broad chairside permission never advances another provider's visit.
- **Complete visit** also by the clinician who started treatment on that visit (`CompleteOwnStartedVisit`, default Dentist; the server checks the actor recorded the visit's InTreatment event).
- **Reassign provider / room** (`ReassignAppointmentProvider`, new, grantable): before treatment starts.

### 5.4 Corrections, cancel, no-show, reassignment
| Action | From | To | Who | Rule |
|---|---|---|---|---|
| Undo check-in | CheckedIn, Ready | the state held before check-in (Scheduled or Confirmed) | `UpdateVisitFlow` | before seating; reason required; history keeps the original check-in event and adds the undo with actor, from, to, reason, time |
| Cancel | Scheduled, Confirmed | Cancelled | `UpdateVisitFlow` (and today's `ManageAppointments` holders, verified) | category required (controlled list: patient, office, provider, weather, other) plus optional note; "short notice" derived when cancelled less than 24 hours before start (assumption, constant named in code); Rebook and Open patient offered |
| Mark no-show | Scheduled, Confirmed (verify Confirmed today) | NoShow | `UpdateVisitFlow` | only after the start time (unchanged); Rebook and Open patient offered |
| Correct no-show | NoShow | the state held before the no-show | `UpdateVisitFlow` | reason required; the original no-show event is never altered; a new correction event is added |
| Reassign provider (optionally room) | Scheduled, Confirmed, CheckedIn, Ready, Seated | same state, new provider/room | `ReassignAppointmentProvider` | reason always required for a provider change; choices limited to available providers and rooms (working at that time, not blocked, not double-booked, room free) through `SchedulingGuards`; the booking itself changes (no separate "Seen by"); if Seated, the operatory lock moves with the room; both providers notified, Urgent |
| Room-only change | Scheduled ... Seated | same state, new room | `ReassignAppointmentProvider` **or** `UpdateChairsideFlow` (assumption, see 6.3) | same room-availability check; reason optional |

### 5.5 Permissions and default grants (escalated: permission-model change)
| Permission | Status | Default roles after this story | Change |
|---|---|---|---|
| `UpdateVisitFlow` | existing | FrontDesk, OfficeManager, Admin | unchanged |
| `UpdateChairsideFlow` | existing | Dentist, Hygienist, Assistant, Admin | **OfficeManager no longer by default** (owner: office manager does front-office steps, not clinical ones); grantable |
| `ReassignAppointmentProvider` | **new** | OfficeManager, Admin | grantable by an admin to any role/account (e.g. FrontDesk) |
| `CompleteOwnStartedVisit` | **new** | Dentist | grantable; limited server-side to visits the actor started |
| `ManagePracticeConfiguration` | existing | Admin | governs the two new settings |
| `ManageAppointments` | existing | unchanged | no longer sufficient on its own for any visit-state move |

Admin holds every permission by design but is still bound by the appointment rules (Admin can start/end treatment only on a visit where Admin's linked provider is the booked provider). Policy-health and the Permission Matrix screen are updated with the new rows.

### 5.6 Complete transition table
Roles: FD FrontDesk, DE Dentist, HY Hygienist, AS Assistant, OM OfficeManager, AD Admin, BI Billing, UN Unassigned. "Y" = allowed by default grant; "own" = allowed only on the visit whose booked provider is the actor; "started" = allowed only if the actor started treatment on it; "-" = not offered and refused by the server (403 `forbidden_transition`).

| Action | From state(s) | To | Settings | API | FD | DE | HY | AS | OM | AD | BI | UN |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Confirm | Scheduled | Confirmed | - | POST `visits/{id}/state` | Y | - | - | - | Y | Y | - | - |
| Check in | Confirmed; Scheduled only if confirmation optional | CheckedIn | Confirmation required | POST `visits/{id}/state` (and legacy `appointments/{id}/check-in`, same policy) | Y | - | - | - | Y | Y | - | - |
| Undo check-in | CheckedIn, Ready | pre-check-in state | - | POST `visits/{id}/undo-check-in` (reason) | Y | - | - | - | Y | Y | - | - |
| Mark ready | CheckedIn | Ready | - | POST `visits/{id}/state` | - | Y | Y | Y | - | Y | - | - |
| Seat patient | Ready; CheckedIn only if ready optional | Seated | Mark ready required | POST `visits/{id}/state` | - | Y | Y | Y | - | Y | - | - |
| Start treatment | Seated | InTreatment | - | POST `visits/{id}/state` (legacy `start-treatment`, same policy) | - | own | own | - | - | own | - | - |
| End treatment / ready for checkout | InTreatment | CheckedOut | - | POST `visits/{id}/state` | - | own | own | - | - | own | - | - |
| Complete visit | CheckedOut | Completed | - | POST `visits/{id}/state` (legacy `complete`, same policy) | Y | started | - | - | Y | Y | - | - |
| Cancel | Scheduled, Confirmed | Cancelled | - | existing cancel endpoint (category) | Y | - | - | - | Y | Y | - | - |
| Mark no-show | Scheduled, Confirmed, after start | NoShow | - | existing no-show endpoint | Y | - | - | - | Y | Y | - | - |
| Correct no-show | NoShow | pre-no-show state | - | POST `appointments/{id}/correct-no-show` (reason) | Y | - | - | - | Y | Y | - | - |
| Reassign provider/room | Scheduled ... Seated | same | - | PUT `visits/{id}/assignment` (reworked: changes the booking; reason) | grantable | - | - | - | Y | Y | - | - |
| Room-only change | Scheduled ... Seated | same | - | PUT `visits/{id}/assignment` | grantable | Y | Y | Y | Y | Y | - | - |
| Any action | Completed, Cancelled (except Rebook), NoShow (except Correct/Rebook) | - | - | - | refused 409 `terminal_state` | | | | | | | |

Every other (state, action) pair is refused with 409 `illegal_transition` and no change; every role/appointment mismatch above is refused with 403 and no change. The table is generated into a test fixture so a test fails if the code and the table disagree.

**Common behaviour of every move:**
- **History/audit:** one `AppointmentEvent` with actor, from, to, reason/category where applicable, and time, plus the PHI-free audit entry, in the same transaction (existing pattern). Corrections add events; nothing is overwritten.
- **Concurrency/stale view:** the request carries the row version; a mismatch returns 409 `stale_appointment` with the current state and the fresh available-actions list; nothing is written and no history row is added. A repeat of a move already made is a no-op with no second history row (existing).
- **Day rule:** Check in only when the appointment's start, converted to the practice zone, falls on today's practice-local date (no early/late exception; any future exception is a separate permission, reason and test). Steps after check-in follow the visit, so the visit cannot be completed before its day (F-071).
- **Available actions:** GET `appointments/{id}/actions` (and the same list embedded in board cards) returns, for the signed-in user, the allowed actions and an "awaiting" text naming the next role and action (for example "In treatment with Dr. Dana - awaiting Dr. Dana to end treatment"; "Ready for checkout - awaiting front desk"). Calendar panel and Board render only this list; the awaiting text is announced through the existing live region.

## 6. Owner decisions (recorded 2026-11-12/13)

### 6.1 Decided
1. **PD-05 workflow:** sections 5.1 to 5.6 as written. Confirm and Mark ready configurable (both default required); Seat always required; no shortcuts; front desk closes; the visit's provider starts and ends treatment; office manager does front-office steps only.
2. **Dentist close:** a dentist may complete only a visit they started treatment on.
3. **Covering:** no "Seen by" covering rule; provider changes go through the grantable reassignment permission (owner: more flexible for the practice later).
4. **Reassignment:** permission `ReassignAppointmentProvider`, default Admin and OfficeManager, grantable to FrontDesk or other roles; before treatment starts; from available providers and rooms; reason always required for a provider change; Urgent notification to both providers.
5. **F-050 cancel reason:** required controlled category plus optional note; short notice derived from timing; one added column (report input change: reviewer to confirm).
6. **F-057 / F-073:** undo check-in before seating and no-show correction, front desk/office manager/admin, each with a required reason and full history.
7. **F-044b blank duration:** prefilled from the appointment type's length, shown and editable.
8. **Appointment type change:** not edited in place; a linked "Replace appointment" action is proposed as ALV-N030 (plan-change proposal), not built here.
9. **Confirmation reminders** for front desk: proposed as ALV-N031 (plan-change proposal), not built here.

### 6.2 Not changed: PD-06
Hygienist authority over allergies and medications is **not changed in this story** and is escalated as a separate decision (it changes the clinical-record permission model). Q-5ar (hygienist printing a prescription) is recorded with PD-06.

### 6.3 Assumptions logged (implementation-level, reversible)
1. Short-notice threshold 24 hours (named constant).
2. Room-only change allowed to chairside holders as well as reassignment holders (today's board behaviour kept, but now conflict-checked); reason optional.
3. Existing "Seen by" assignment rows are kept read-only as history; no new ones are written; the calendar shows the booking only.
4. Provider-to-appointment-type capability is checked only if that data already exists (verify first); no new capability schema in this story.
5. The two settings are stored in the existing practice-configuration store if it can hold them; if it needs a column, that is added to migration M1 below and disclosed.

## 7. Impact, permission, audit, migration

- **Permissions (escalated):** two new permissions, one default grant removed (OfficeManager `UpdateChairsideFlow`), appointment-specific rules. Permission Matrix, policy-health and delegation rules updated; seeding is idempotent.
- **Migrations (additive, each with the usual trigger/inventory/constraint tests and a down path):**
  - **M1:** `Appointments.CancelCategory` (nullable for existing rows, required by the API for new cancellations); reason/category columns on `AppointmentEvents` if not already present; the two settings if needed (6.3.5).
  - **M2:** notification kind `appointment.provider_reassigned` added to `CK_Notifications_Kind` and `CK_Notifications_Shape` (source key names the appointment event; no message or document), following ALV-N020's pattern.
- **Notifications:** one Urgent notification per provider (old and new) per reassignment event, PHI-free (time and link only); idempotent by source key.
- **Reports:** Cancellations and no-shows report gains category and short-notice; legacy cancellations show "Not categorised".
- **Behaviour change:** existing STORY-011/ALV-011-C01 tests that rely on the shortcuts or on OfficeManager chairside moves change with the matrix in the same diff and are listed in the handoff.
- **Rollout effect:** with confirmation required by default, today's unconfirmed appointments must be confirmed before check-in (disclosed to the owner).

## 8. Proof plan (focused; runs over five minutes asked first)

| Review requirement | Proof |
|---|---|
| Full normal path; every prohibited shortcut; terminal states; corrections | Unit: the state machine over every (state, action, settings) combination against the generated table; API: the path end to end under each setting combination; each removed shortcut refused 409; Completed/Cancelled/NoShow refuse everything except Rebook/Correct |
| Server denial per role and cross-provider | API 403 matrix from the table for all eight roles, including hygienist on Dr. Dana's visit (Q-065a), assistant starting treatment, OfficeManager seating, dentist completing a visit someone else started, legacy calendar endpoints with only `ManageAppointments` |
| Concurrent transition | Two parallel requests on the same appointment/version: one succeeds, one gets 409 `stale_appointment`, exactly one history row; real two-context browser case for the stale view (F-066) |
| Calendar/Board parity | Browser (mocked API): both screens render the same `actions` response; a parity test per role and state |
| Accessible next-role explanation | Browser: the awaiting text is visible, in the live region, and axe-clean |
| Practice-local day boundaries | Unit/API with the injected clock: 23:59/00:00 practice time, a zone behind UTC, the spring-forward and fall-back days; check-in refused the day before and the day after |
| Mandatory reasons and immutable history | API: undo, no-show correction, provider change and cancel without reason/category refused 400; with it, events carry actor/from/to/reason; the original check-in/no-show event is unchanged (row compare) |
| Reassignment | API: unavailable provider/room refused with the conflict payload; reason required; exactly two Urgent notifications, none on a retry; operatory lock moves when seated |
| PKG-04 | Unit date-only helper (zones behind/ahead of UTC, DST, year boundary, date line); print DOB equals header; performed date round trip; answer time kept after scheduling |
| Chain | Stages 3 and 4 (3.01-3.14) added; the run is requested from the owner first |

Negative controls: each new rule reverted to show its test fails. No broad regression (`DEFERRED_TO_ALV-N028`). The Stage 1 review approves the code and evidence plan before any frozen Stage 2 run.

## 9. Risks

- **Size:** about 45 findings plus the owner's additions; two attempts recommended (2.1).
- **Client date sites:** each classified before change; wrong replacement would break correct instant displays.
- **User-to-provider link:** "own visit" depends on it; if an account has no linked provider record, it can never start treatment (verified first; reported if missing).
- **Rollout of required confirmation:** existing appointments need confirming on the first day.

## 10. Requested from the reviewer

1. Approve this revision, or ask for changes.
2. Confirm two attempts (PKG-04 first) or one.
3. Rule on the escalations: the permission-model change (5.5), the report-input change (F-050) and migrations M1/M2.
4. Act on `docs/design/ALV-N021_PLAN_CHANGE_PROPOSAL.md` (ALV-N030, ALV-N031).
