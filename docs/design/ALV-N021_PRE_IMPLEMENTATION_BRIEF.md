# ALV-N021 pre-implementation briefing: date, scheduling and visit-transition integrity

- **Story:** ALV-N021 (execution item 58), consolidating R05 **PKG-04** (date-only and time correctness) and **PKG-05** (visit transitions, role enforcement, scheduling integrity).
- **Date:** 2026-11-12. **Session:** CC-20261109-8w3d.
- **Status:** **PROPOSED, awaiting independent review and the owner's decisions in section 6.** Planning evidence only. Nothing in the repository other than this file and its control notes was changed; no test, build or suite was run. Two-stage review applies (the plan names permission, audit and concurrency risk).
- **Binding inputs:** `GPT Docs/Alveara_Dental_Execution_Plan_v4.md` item 58 (and the stabilization pre-implementation-briefing and focused-evidence rules); `docs/design/MANUAL_E2E_STABILIZATION_PLAN_R05.md` PKG-04, PKG-05, stage 3 and 4 chain cases 3.01-3.14, decisions PD-05 and PD-06; `docs/stabilization/DISPOSITION_REGISTER.json` (the raw excerpts quoted below).
- **Limit of this draft:** section 3 records what was read in the code so far (the state machine, the flow service, the booking guards, one date-formatting site). Everything else is an explicit "verify first" item, as R05 requires. Nothing below claims a defect is fixed or that an unread area is correct.

## 1. Intended result

### 1.1 User-visible
1. A date of birth or any other date-only value is the same day everywhere it is shown (screens, prints, statements, exports), in every time zone; the printed prescription no longer says 3/13 when the header says 3/14 (F-107), and a performed date is never a day late (F-118).
2. A recorded time is the real time of the action and is not replaced by a later action or shown as a made-up 8:00:00 (F-030, F-100, F-184).
3. Calendar panel and Visit board offer exactly the same, role-correct actions for an appointment; a front-desk user cannot do clinical steps, a hygienist cannot complete another provider's visit, and no role ends at a dead end (F-056 to F-066, subject to the owner's matrix).
4. Booking, blocked time, room and provider changes and completion obey one set of overlap and state rules; a future appointment cannot become completed; check-in is offered on the appointment day (F-047, F-053, F-061, F-071).
5. Appointment history says who did what and from-to values; Week view draws blocked and non-working time; cancelled and no-show appointments offer Rebook and Open patient; a closed day says so; wording and status items are corrected (Group C).

### 1.2 Technical
1. One shared date-only helper on the client (and the equivalent rule on the server) replaces ad-hoc `new Date(...)`/`toLocaleDateString()` on date-only fields.
2. The written visit-transition matrix becomes the single source: the server enforces it and the client's action lists are derived from it (UI parity test).
3. Overlap and state invariants enforced server-side for blocked time, provider/operatory changes, future appointments, with the conflicting-record payload.
4. The chain stage 3 and 4 cases (3.01-3.14) run in the accumulated chain, with real two-context browser coverage for the booking race (3.10) and the stale-banner case (F-066).

## 2. Scope boundary

- In scope: the 7 PKG-04 raw findings (F-030, F-100, F-107, F-118, F-184, plus the withdrawn/duplicate F-133 and F-218 kept as register rows) and the PKG-05 findings listed in R05 (F-044b, F-045 to F-077 as listed in section 4).
- Not in this story: the user-selectable date-format preference (PKG-14 / PD-37); treatment-plan scheduling, follow-up integrity and financial dates (PKG-08, PKG-09, which depend on this story); allergy/medication authority beyond the decision PD-06 recorded here; any new external dependency, schema redesign or infrastructure change.
- Governance: the plan marks "permission model change: escalate" and "cancel reasons change report inputs: reviewer to confirm". Both are called out in section 6 and 7.

## 3. Current state read from the code so far

| Area | What is there (file) | Consequence for this story |
|---|---|---|
| Visit state machine | `Architecture/Scheduling/VisitStateMachine.cs`: states Scheduled, Confirmed, CheckedIn, Ready, Seated, InTreatment, CheckedOut, Completed; forward only; **legal shortcuts** CheckedIn to InTreatment, CheckedIn to Completed, InTreatment to Completed; no undo; Completed final | F-056 is true by design today (Complete skips In treatment). Whether the shortcuts stay is the owner's decision (PD-05). |
| Authority | `Controllers/VisitsController.cs` line 82: a move is `UpdateVisitFlow` (front office: Confirmed, CheckedIn, CheckedOut) or `UpdateChairsideFlow` (everything else), by `VisitStateMachine.DutyFor` | Authority is by permission, not provider: nothing stops a hygienist completing another provider's visit (Q-065a). Needs the owner's rule. |
| Flow service | `AppointmentFlowService.cs`: one transaction, row-version check, history entry plus PHI-free audit in the same save, operatory lock when seating, idempotent repeat | The machinery for actor-on-every-transition and concurrency exists; the matrix and who may do what are the gaps. |
| Booking guards | `SchedulingGuards.cs`: applock on provider, operatory, patient in sorted order; only `Scheduled` holds time; half-open periods | Race 3.10 is already designed for; F-053/F-061/F-071 are about paths that do not go through it (blocked time, board room change, completion of a future appointment). To be reproduced. |
| Date-only (F-107) | `src/alveara-client/src/pages/prescription/workflow/PrintPreview.tsx` line 7: `const day = (iso) => new Date(iso).toLocaleDateString()` applied to `patientDateOfBirth` | Root cause confirmed in code: a date-only string parsed as UTC midnight then shown in the local zone shows the previous day west of UTC. About 94 other `new Date(`/`toLocaleDateString` sites exist in the client (not yet classified as date-only or instant). |
| Practice clock | `Architecture/Time/PracticeClock.cs`: practice zone conversion, DST gap handling, injectable clock | The injectable clock needed for cases 3.06/3.09 exists; the runner must drive it. |

Not yet read (to be done in the first implementation step and reported, not assumed): `VisitBoardService`, `VisitAssignmentService`, `AppointmentManager`, `AppointmentViews`, the Calendar and Board components, blocked-time creation, the server-side date-only handling for performed date (F-118) and the sources of the 8:00:00 times (F-100).

## 4. Finding-by-finding plan (verify first, then record the disposition)

PKG-04 (R05 Group A): F-107 and F-118 reproduced with a test in a zone behind UTC and across the date line before any change; F-030 and F-184 reproduced against the history/answer code; F-100's 8:00:00 traced to its source (defaulted time stored as an action time). Fix: no date-only value passes through a time-zone conversion; recorded times keep their instant; defaulted times are not shown as click times; audit and history stay UTC with practice-zone display.

PKG-05 Group A (matrix and roles): F-056 [DESIGN], F-058, F-059, F-060, F-063, F-064, F-065, F-066; F-170 and D-01a are withdrawn (F-170's delegated-permission model is verified and kept, with a boundary and audit test).
PKG-05 Group B (overlap and state): F-047, F-053, F-061, F-071.
PKG-05 Group C (history and usability): F-044b [DESIGN], F-045, F-046, F-048, F-049, F-050 [DESIGN], F-051, F-052, F-054, F-055, F-057 [DESIGN], F-062, F-068 [NOT_REPRODUCED, re-check], F-069, F-070, F-072, F-073 [DESIGN], F-074, F-076, F-077.

Each is recorded CONFIRMED, NOT_REPRODUCED, DUPLICATE, WITHDRAWN or DESIGN_DECISION_REQUIRED in the evidence with the test that proves it.

## 5. Proposed visit-transition matrix (for the owner's approval; PD-05)

Principle: the chain is the one the code already has; what the owner decides is who may perform each step and which shortcuts and undo moves exist. This table is a proposal that follows the current duty split, not a recorded decision.

| Move | Today | Proposed |
|---|---|---|
| Into Confirmed, CheckedIn, CheckedOut | front office (`UpdateVisitFlow`) | unchanged: front desk, plus any clinical role that holds `UpdateVisitFlow` |
| CheckedIn to Ready, Ready to Seated, Seated to InTreatment | chairside (UpdateChairsideFlow) | unchanged: clinical roles; **not** front desk (F-059) |
| CheckedIn to InTreatment shortcut | allowed | allowed only if the owner keeps "start without seating" (the board offers Seat); else removed |
| InTreatment (or CheckedIn) to Completed | allowed | **Completed only from CheckedOut**; the clinical team moves to CheckedOut, front office completes, or the owner names a single "final completion" role (F-056/F-065 dead-end) |
| Completing another provider's visit (Q-065a) | allowed for any holder | only the visit's own provider, or a dentist, or a role the owner names |
| Undo check-in before treatment (F-057) | none | owner decision: allow, front office, recorded in history |
| No-show correction (F-073) | none | owner decision |
| Check-in more than the appointment day away (F-047) | offered | offered only on the appointment day, with a confirmation otherwise |

Every transition records actor and from-to (already true in `AppointmentFlowService`); the Permission Matrix defaults and policy-health are updated if the owner changes any grant.

## 6. Decisions needed from the owner (not assumed)

1. **PD-05:** the matrix above: keep or remove the CheckedIn-to-InTreatment and to-Completed shortcuts; who performs final completion and check-out; whether a provider other than the visit's own may complete it.
2. **PD-06:** hygienist authority to add, retire or mark allergies and medications in error (R05 puts this under the same review; only the decision is taken here, any change to the clinical-record permission model is escalated).
3. **F-044b:** blank duration: keep a default (and say so in the wording) or require a value.
4. **F-050:** cancel reason categories (patient-cancelled, office-cancelled, short-notice, other) and whether a reason becomes required; this changes report inputs and may add a column (the plan asks the reviewer to confirm).
5. **F-057 / F-073:** undo of check-in; correction of a no-show.

If the owner prefers, the implementation can start with PKG-04 (no decisions needed) and the overlap/state rules (Group B), and take Groups A and C once PD-05 and the design items are answered.

## 7. Impact, permission, audit, migration

- Permission: possible new or changed grants for completion and for undo; Permission Matrix defaults and policy-health updated; **escalated** because it changes the permission model, as the plan requires.
- Audit: actor and from-to on every transition (largely present); history gains the new value and actor on reschedule and cancel.
- Migration: none expected, except a cancel-reason column if F-050 is approved (a small, additive, nullable column; separate migration with the usual trigger/inventory tests).
- Reports: the cancel-reason categories feed cancellation reporting; reviewer to confirm.

## 8. Proof plan (focused; standing five-minute permission, longer runs asked first)

- Unit: shared date-only helper (zones behind and ahead of UTC, DST, year boundary, the date line); the state machine over every pair (the existing all-pairs test extended to the approved matrix); permission-per-transition table.
- API: 403 matrix per role and transition; each overlap/state refusal with the conflicting-record payload; future appointment cannot complete; a completed appointment frees its slot; DOB and performed-date round trip in a non-UTC zone.
- Browser (real Chromium, mocked API): Calendar/Board action parity per role; DOB print equals header; history shows actor and from-to; Week view blocked time; closed-day message; filtered-empty message.
- Real two-context browser: concurrent same-slot booking (3.10) and the stale banner after check-out in another session (F-066), run through the chain runner as R05 requires.
- Chain: stage 3 (cases 3.01-3.14) and stage 4 added to the accumulated chain. A chain run is longer than five minutes; it will be requested from the owner before running, as the owner's rule requires.
- Negative controls: each new rule reverted to show its test fails. No broad regression (deferred to ALV-N028).

## 9. Risks and assumptions

- **Size.** About 45 findings in two packages; two-stage review. Proposal: land PKG-04 first as its own reviewed attempt if the reviewer prefers smaller packages.
- **Client date sites.** The 94 `new Date`/`toLocaleDateString` sites must be classified (date-only vs instant) rather than bulk-replaced; a wrong replacement would change correct instant displays.
- **Behaviour change.** Removing the legal shortcuts changes what existing STORY-011/ALV-011-C01 tests expect; those tests move with the matrix in the same change.
- **Assumption logged:** nothing above was run; the findings are the raw observations from the manual pass and are verified first.

## 10. Requested from the reviewer

1. Approve this briefing, or ask for changes.
2. Confirm whether PKG-04 and PKG-05 land as one attempt or two.
3. Confirm the escalation handling for the permission-model and cancel-reason items.
