# ALV-N021 pre-implementation briefing (revision 3): date, scheduling, visit-workflow and checkout integrity

- **Story:** ALV-N021 (execution item 58): R05 **PKG-04** (date-only and time correctness) and **PKG-05** (visit transitions, roles, scheduling integrity), plus the owner's checkout-readiness gate (addendum 3).
- **Revision:** 3, answering `.alveara/reviews/ALV-N021/BRIEFING-R02.md` (CHANGES_REQUIRED) and the owner's addenda 1-3 in the same folder. Revision 1: `9552ea5`; revision 2: `cb7866b`.
- **Date:** 2026-11-13. **Session:** CC-20261109-8w3d.
- **Status:** **PROPOSED, awaiting a new independent briefing review.** Document only: no code, test, schema, migration, setting or permission has changed; nothing was run.
- **Companion files:**
  - `docs/design/ALV-N021_OWNER_DECISION_RECORD.md`: every owner decision (OD-01 to OD-37) with its acceptance criterion and attempt, with the owner's approval line.
  - `docs/design/ALV-N021_SOURCE_EVIDENCE_APPENDIX.md`: verbatim raw-log entries (accepted in R02, unchanged).
  - `docs/design/ALV-N021_PLAN_CHANGE_PROPOSAL.md`: ALV-N030, ALV-N031, ALV-N032 proposed for the reviewer; not entered in the plan or status ledger.

## 0. Answers to R02 and the addenda

| Item | Answer |
|---|---|
| R02 blocking 1 (single final-completion owner) | Superseded by **owner addendum 1**: front desk or the treating dentist who started treatment (`CompleteOwnStartedVisit`) may perform the one `CheckedOut -> Completed` transition, under one policy (OD-09). Wording per addendum 1 (OD-10). |
| R02 blocking 2 (owner authority) | Every expanded item now has an owner decision with acceptance criteria in the decision record; anything not built here is moved to a separate proposal (OD-33 to OD-36). |
| R02 blocking 3.1 (Confirm / Mark ready) | Practice settings, both default required; persistence, authorization, audit, default, rollout and all combinations in 5.2 (OD-03, OD-04). |
| R02 blocking 3.2 (blocked time) | Option 2: listed, confirmed with a reason, affected appointments flagged until resolved by one of four actions (OD-17, OD-18). |
| R02 blocking 3.3 (reassignment, notifications, cancel categories) | In this story, owner-approved with full contracts: OD-13 to OD-16 (migration M2), OD-19 (migration M1). |
| R02 blocking 3.4 (no-show correction) | **Removed** by the owner: a no-show is final and is rebooked (OD-20). Automatic late/no-show and fees are a separate proposal (ALV-N032). |
| R02 approval path | Three sequential attempts under this story: **A1** PKG-04, **A2** PKG-05 workflow, **A3** checkout gate (section 2.1). |
| Addendum 2 (no shortcuts; meaning of checkout) | 5.1, 5.2 (OD-01, OD-02, OD-10). |
| Addendum 3 (checkout-readiness gate) | Section 6 (OD-22 to OD-32), attempt A3, migration M3. |

## 1. Intended result

1. Date-only values (date of birth, performed date) are the same day everywhere and in every zone (F-107, F-118); recorded times are real action times and are not overwritten (F-100, Q-5an, F-184, F-218). **A1**
2. One visit workflow, one vocabulary, identical role-correct actions on Calendar and Board, an honest "next actor" text, no shortcuts and no dead ends (F-056, F-058, F-059, F-063, F-065, F-066, Q-065a). **A2**
3. Check-in only on the appointment's practice-local day; a visit cannot be completed before its day (F-047, F-071). **A2**
4. Undo check-in with a reason; cancel with a required category; final no-show with Rebook (F-050, F-057, F-073). **A2**
5. Provider/room reassignment through the booking rules, reason required, urgent notifications; blocked time cannot silently cover appointments (F-053, F-060, F-061, F-062). **A2**
6. History names who did what, from and to (F-046, F-049, F-062, F-074); Week view, empty states and wording fixes (Group C). **A2**
7. A visit is completed only when treatment ended, the patient left, follow-up is decided and, where the account owes money, a truthful financial decision is recorded; front desk can collect payments without broader billing powers. **A3**

## 2. Scope

- **In:** the 50 PKG-04/PKG-05 register rows (appendix) and decisions OD-01 to OD-32, OD-37.
- **Out:** ALV-N030, ALV-N031, ALV-N032 (proposals only); date-format preference (PKG-14/PD-37); treatment-plan scheduling, follow-up and financial-date work in PKG-08/PKG-09; insurance claims; PD-06.
- **Escalations raised here:** permission-model change (three new permissions, one removed default grant, appointment-specific rules); report-input change (cancel category); three additive migrations (M1-M3); a checkout rule that reads the financial ledger (read-only).

### 2.1 Attempts (sequential, each separately reviewed)
| Attempt | Content | Depends on | Schema |
|---|---|---|---|
| A1 | PKG-04 date/time integrity | nothing | none |
| A2 | PKG-05 workflow, roles, scheduling, reassignment, blocked time, cancel | A1 approved | M1, M2 |
| A3 | Checkout-readiness gate and `CollectPayments` | A2 approved | M3 |

## 3. Code facts (read so far; read-only check of 2026-11-13 included)

| Area | Fact | Consequence |
|---|---|---|
| State machine | `VisitStateMachine.cs`: forward-only states with legal shortcuts CheckedIn->InTreatment, CheckedIn->Completed, InTreatment->Completed; `DutyFor` splits front office and chairside | Shortcuts removed; undo check-in added as an explicit move |
| Two API paths | `AppointmentsController` check-in/start-treatment/complete need only `ManageAppointments`; `VisitsController` `/state` checks the duty permission; `/assignment` ("Seen by") needs either flow permission | Both routed through one policy; "Seen by" replaced by reassignment |
| Appointment status vs flow | `AppointmentManager.MarkNoShowAsync`: allowed only for Status Scheduled, not arrived, after start; sets Status NoShow and resets FlowState to Scheduled; event + audit | No-show stays manual and final (OD-20) |
| Grants | `PermissionMatrix.cs`: FrontDesk has `ViewBilling` but **not** `ManageBilling`; Billing, OfficeManager, Admin have `ManageBilling`; OfficeManager has both flow permissions | `CollectPayments` (OD-27); OfficeManager chairside default removed (OD-07) |
| Billing | `BillingController`: payments, apply, reversals, adjustments, charges all need `ManageBilling`; charges are posted per treatment-plan item (`ChargeRequest(PlanItemId)`); no insurance/claim records exist | Financial outcomes are decisions or verified against real ledger rows (OD-26) |
| Visit-to-charge link | `Encounter.AppointmentId` (optional) -> `CompletedProcedure.EncounterId`, `PlanItemId` -> charge | "Completed procedure not yet charged on this visit" is computable (OD-25) |
| Follow-ups | `FollowUpEntities` carry an optional `AppointmentId` | Follow-up "booked" detected from a real future appointment (OD-24) |
| Blocked time | `ConfigurationController` blocked-time endpoints need `ManagePracticeConfiguration`; `SchedulingConfiguration` checks blocks only for new bookings | OD-17 adds the overlap check at block creation |
| Booking guards | `SchedulingGuards.cs`: applocks on provider, operatory, patient; half-open; only Scheduled holds time | Reused for reassignment and room change |
| Flow service | `AppointmentFlowService.cs`: one transaction, row version, event + audit, operatory lock on seating, idempotent repeat | Reused for every move and for completion |
| Notifications | priorities Normal/Important/Urgent; kinds fixed by `CK_Notifications_Kind`/`Shape` | M2 adds one kind |
| Date-only | `PrintPreview.tsx` line 7 (F-107 root cause); about 94 client sites to classify | A1 |
| Clock | `PracticeClock.cs`: practice zone, DST, injectable | Day rule and DST tests |

**Still verify first (start of A2/A3, reported in the handoff):** `VisitBoardService`, `VisitAssignmentService`, `AppointmentViews`, Calendar/Board components, how a user is linked to a provider record (needed by OD-08/OD-09), where the two settings are stored, F-118's performed-date path, F-100's source, and why a Completed appointment held its slot in F-071.

## 4. Finding plan

- **A1 (PKG-04):** F-107, F-118 reproduced in a zone behind UTC and across the date line before any change; F-184/F-218 (duplicate): answer time and last-change time stored and shown separately; F-100, Q-5an traced; F-030 regression test; F-133 WITHDRAWN row kept.
- **A2 (PKG-05):** Group A F-056, F-058, F-059, F-060, F-063, F-065, F-066, Q-065a, F-057, F-073; Group B F-047, F-053, F-061, F-071; Group C F-044b, F-045, F-046, F-048, F-049, F-050, F-051, F-052, F-054, F-055, F-062, F-064, F-068 (re-check; Firefox only), F-069, F-070, F-072, F-074, F-076, F-077; F-044 (raw) WITHDRAWN; F-170 and D-01a WITHDRAWN (delegation boundary and audit test kept); Q-5a, Q-5q, Q-5au, Q-5be, Q-5bh checked against the Permission Matrix and recorded, no grant change beyond 5.5; Q-5ar recorded with PD-06.

Each item recorded CONFIRMED, NOT_REPRODUCED, DUPLICATE, WITHDRAWN or DESIGN_DECISION_REQUIRED with its proving test.

## 5. Visit workflow (A2)

### 5.1 Path and labels
| Step | Action label | Persisted state | Displayed state |
|---|---|---|---|
| 1 | Confirm | Confirmed | Confirmed |
| 2 | Check in | CheckedIn | Checked in |
| 3 | Mark ready | Ready | Ready |
| 4 | Seat patient | Seated | Seated in <room> |
| 5 | Start treatment | InTreatment | In treatment with <provider> |
| 6 | End treatment / ready for checkout | CheckedOut (compatibility name) | Ready for checkout |
| 7 | Complete visit | Completed | Completed |

### 5.2 Settings
| Setting | Default | Stored | Changed by | Audit | Effect |
|---|---|---|---|---|---|
| Confirmation required | On | practice configuration (verify store; if a column is needed it joins M1 and is disclosed) | `ManagePracticeConfiguration` | old/new, actor, time | On: check-in only from Confirmed. Off: from Scheduled or Confirmed |
| Mark ready required | On | same | same | same | On: seat only from Ready. Off: from CheckedIn or Ready |

A change applies to the next move only. **Rollout effect:** with confirmation required by default, appointments not yet confirmed on the release day must be confirmed before check-in (disclosed; the practice may switch the setting off).

### 5.3 Permissions and default grants (escalated)
| Permission | New? | Default holders | Notes |
|---|---|---|---|
| `UpdateVisitFlow` | existing | FrontDesk, OfficeManager, Admin | front-desk steps and completion |
| `UpdateChairsideFlow` | existing | Dentist, Hygienist, Assistant, Admin | OfficeManager default removed (OD-07); grantable |
| `CompleteOwnStartedVisit` | **new** | Dentist | completion only of visits the actor started (OD-09) |
| `ReassignAppointmentProvider` | **new** | OfficeManager, Admin | grantable (OD-13) |
| `CollectPayments` | **new (A3)** | FrontDesk | payments and allocation only (OD-27) |
| `ManagePracticeConfiguration` | existing | Admin, OfficeManager | settings and blocked time |
| `ManageAppointments` | existing | unchanged | booking/rescheduling; no longer sufficient for any visit move |

Admin holds every permission but is bound by the appointment rules (OD-08, OD-09). Permission Matrix, policy-health and delegation rules updated; seeding idempotent.

### 5.4 Complete transition table
Roles: FD FrontDesk, DE Dentist, HY Hygienist, AS Assistant, OM OfficeManager, AD Admin, BI Billing, UN Unassigned. Y = default grant; own = only the appointment's booked provider; started = only the account that recorded InTreatment; g = only if granted; - = not offered, server 403.

| Action | From | To | Setting | API | FD | DE | HY | AS | OM | AD | BI | UN |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Confirm | Scheduled | Confirmed | - | POST `visits/{id}/state` | Y | - | - | - | Y | Y | - | - |
| Check in | Confirmed; Scheduled if confirmation off | CheckedIn | Confirmation required | `visits/{id}/state`; legacy `appointments/{id}/check-in` same policy | Y | - | - | - | Y | Y | - | - |
| Undo check-in | CheckedIn, Ready | pre-check-in state | - | POST `visits/{id}/undo-check-in` (reason) | Y | - | - | - | Y | Y | - | - |
| Mark ready | CheckedIn | Ready | - | `visits/{id}/state` | - | Y | Y | Y | - | Y | - | - |
| Seat patient | Ready; CheckedIn if ready off | Seated | Mark ready required | `visits/{id}/state` | - | Y | Y | Y | - | Y | - | - |
| Start treatment | Seated | InTreatment | - | `visits/{id}/state`; legacy `start-treatment` same policy | - | own | own | - | - | own | - | - |
| End treatment / ready for checkout | InTreatment | CheckedOut | - | `visits/{id}/state` | - | own | own | - | - | own | - | - |
| Complete visit | CheckedOut (A3: and all checkout items resolved) | Completed | - | `visits/{id}/state`; legacy `complete` same policy | Y | started | g | g | Y | Y | - | - |
| Cancel | Scheduled, Confirmed | Cancelled | - | cancel endpoint (category) | Y | - | - | - | Y | Y | - | - |
| Mark no-show | Scheduled, Confirmed, after start | NoShow | - | no-show endpoint | Y | - | - | - | Y | Y | - | - |
| Reassign provider (+room) | Scheduled to Seated | same | - | PUT `visits/{id}/assignment` (reworked; reason) | g | - | - | - | Y | Y | - | - |
| Room-only change | Scheduled to Seated | same | - | PUT `visits/{id}/assignment` | g | Y | Y | Y | Y | Y | - | - |
| Resolve block flag (reschedule/reassign/cancel/keep) | flagged, not started | per action | - | existing endpoints; keep: POST `appointments/{id}/keep-despite-block` (reason) | Y | - | - | - | Y | Y | - | - |
| Rebook / Open patient | Cancelled, NoShow | new appointment | - | booking | Y | - | - | - | Y | Y | - | - |
| Any other | Completed, Cancelled, NoShow | - | - | - | 409 `terminal_state` | | | | | | | |

"g" for HY/AS on Complete means `CompleteOwnStartedVisit` granted to them, still limited to visits they started. Every pair not in the table: 409 `illegal_transition`. Every role/appointment mismatch: 403. The table is generated into a test fixture; the code and the table are compared by test.

### 5.5 Behaviour common to every move
- **History/audit:** one event (actor, from, to, reason/category where applicable, time) plus the PHI-free audit entry, same transaction; corrections add events, nothing is overwritten.
- **Concurrency:** row version on every request; mismatch 409 `stale_appointment` with the current state and fresh available actions, nothing written; a repeat of a completed move is a no-op without a second event.
- **Available actions:** GET `appointments/{id}/actions` (also embedded in board cards) returns the signed-in user's allowed actions and the next-actor text; Calendar and Board render only this; the text is announced through the live region.
- **Day rule:** check-in only when the start falls on today's practice-local date; late check-in logs minutes late.

### 5.6 Reassignment, blocked time, cancel
- **Reassignment (OD-13 to OD-16):** guards as booking; if Seated, the operatory lock moves with the room; two Urgent notifications (kind `appointment.provider_reassigned`, source key = the event id, so a retry sends nothing new); legacy "Seen by" rows read-only.
- **Blocked time (OD-17, OD-18):** POST blocked-time without `confirmOverlaps` and with overlapping not-started appointments returns 409 with the list (time, provider, room; patient name only in-app). With confirmation and reason: saved, audited, each affected appointment carries a derived flag (overlap with a block and no later resolution event), shown on Calendar, Board and the provider's day; cleared by reschedule, reassign, cancel (category office/provider) or keep (reason). No new table: the flag is derived from blocks and events.
- **Cancel (OD-19):** M1 adds `Appointments.CancelCategory` (nullable for legacy rows, required by the API); short notice derived at under 24 hours (named constant); report gains both.

## 6. Checkout-readiness gate (A3)

### 6.1 Items and outcomes
| Item | Applies | Resolved by | Who |
|---|---|---|---|
| Treatment ended | always | the provider's End-treatment event (derived) | booked provider |
| Patient departed | always | "Patient has left" with time | `UpdateVisitFlow` |
| Follow-up | always | booked (auto-detected future Scheduled/Confirmed appointment), not clinically required, patient declined, deferred (reason) | not required: treating provider; others: `UpdateVisitFlow` |
| Financial | balance due > 0, or this visit's encounter has a completed procedure not yet charged | payment recorded (verified payment on the visit's practice day), waived (verified `ManageBilling` adjustment that day), payment plan, insurance to bill, send statement; "waiver requested" recorded but not resolving | `CollectPayments`/`UpdateVisitFlow`/`ManageBilling` per OD-26 to OD-28 |

### 6.2 Data (migration M3)
`VisitCheckoutOutcomes` (append-only): `Id`, `AppointmentId`, `ItemCode` (departure, follow_up, financial), `OutcomeCode`, `Reason` (nullable, required for deferred and waiver requested), `LinkedLedgerEntryNumber` (nullable; payment or adjustment it relies on), `LinkedAppointmentId` (nullable; follow-up), `ActorUserId`, `RecordedAtUtc`, `SupersedesId` (nullable), `RowVersion`. Check constraints on codes; INSTEAD OF UPDATE/DELETE triggers refuse edits (pattern from STORY-017). The current outcome per item is the latest not superseded.

### 6.3 API and UI
- GET `appointments/{id}/checkout`: items, applicability, current outcome, next action, responsible role.
- POST `appointments/{id}/checkout/{item}`: records an outcome (row version, idempotency key); verified outcomes check the ledger read-only.
- Complete: the evaluator runs inside the completion transaction; open items return 409 `checkout_incomplete` with item codes and next-action text; nothing written.
- **Checkout panel** (Calendar and Board, same server data): each item with its state and the action; Complete enabled only when all resolved; text such as "Ready for checkout — financial decision required"; live-region announcement; keyboard and screen-reader accessible.
- Payments: POST `billing/payments` and `payments/apply` accept `CollectPayments` or `ManageBilling`; every other billing write keeps `ManageBilling`.
- Legacy visits in CheckedOut use the same gate.

## 7. Migrations (additive, each with constraint/trigger/inventory tests and a down path)
| Id | Attempt | Change |
|---|---|---|
| M1 | A2 | `Appointments.CancelCategory`; reason/category columns on `AppointmentEvents` if not present; settings columns only if the configuration store needs them |
| M2 | A2 | notification kind `appointment.provider_reassigned` in `CK_Notifications_Kind` and `CK_Notifications_Shape` (ALV-N020 pattern) |
| M3 | A3 | `VisitCheckoutOutcomes` table with append-only triggers |

## 8. Proof plan (focused; anything over five minutes asked first)

| Requirement | Proof | Attempt |
|---|---|---|
| Date-only and time integrity | unit helper (zones behind/ahead, DST, year boundary, date line); print DOB equals header; performed date and answer time round trips | A1 |
| Full path, every shortcut, terminal states, undo | state machine over every (state, action, settings) against the generated table; API path under all four setting combinations; each shortcut 409 | A2 |
| Server denial per role and cross-provider | 403 matrix for all eight roles, hygienist on Dr. Dana's visit, assistant starting treatment, OfficeManager seating, dentist completing a visit someone else started, legacy endpoints | A2 |
| Concurrency | parallel requests on one appointment: one succeeds, one 409, one event; dentist and front desk completing at once; real two-context browser for the stale view (F-066) | A2/A3 |
| Calendar/Board parity and next-actor text | browser: both render the same `actions` response per role and state; text in live region; axe | A2 |
| Day boundaries | injected clock: 23:59/00:00, zone behind UTC, both DST days | A2 |
| Reasons and immutable history | undo, cancel, provider change, block override, keep without reason 400; originals unchanged (row compare) | A2 |
| Reassignment and notifications | each unavailable case refused; exactly two Urgent notifications, none on retry; lock moves when seated | A2 |
| Blocked time | overlap list and refusal; confirmed save; flags shown and cleared by each of four actions | A2 |
| Checkout applicability and denial | each item applicable/not; completion refused for each open item; each permitted outcome; verified outcomes refused without a ledger row; ledger row count unchanged by checkout; dentist completion only when all resolved; legacy visit gated | A3 |
| Checkout concurrency and history | outcome vs completion race; superseding correction leaves the original row | A3 |
| `CollectPayments` | payment and apply allowed; charge, reversal, adjustment, refund, method change 403 | A3 |
| Chain | stages 3 and 4 (3.01-3.14) added; the run is requested from the owner first | A2 |

Negative controls per rule. No broad regression (`DEFERRED_TO_ALV-N028`). Each attempt's Stage 1 review approves code and evidence before any Stage 2 run.

## 9. Risks
- Size: three attempts keep each review small.
- User-to-provider link: OD-08/OD-09 depend on it; verified first.
- Rollout of required confirmation and of the checkout gate: practices must confirm and resolve checkout items from the first day; both disclosed.
- Client date sites: classified one by one.
- Ledger reads in the gate: read-only; any write would be a defect (tested by row count).

## 10. Requested from the reviewer
1. Approve revision 3 (A1 may start on approval; A2 and A3 follow their own reviews), or ask for changes.
2. Rule on the escalations: permissions (5.3), report input (OD-19), migrations M1-M3, the read-only ledger rule in the gate.
3. Act on `ALV-N021_PLAN_CHANGE_PROPOSAL.md` (ALV-N030, ALV-N031, ALV-N032).
