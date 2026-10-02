# Appointment scheduling with conflict prevention (STORY-004)

How an appointment is booked without conflicts, for the stories that build on it (notably `ALV-004-C01`, the full scheduler with reschedule, cancel, no-show, patient-overlap and the calendar view). STORY-004 is the base; the companion extends it without changing its contract.

## What exists
- **`Appointment`** - a patient seen by a provider in an operatory for a period, stored as UTC instants **[StartUtc, EndUtc)** (half-open: one appointment may end at the exact moment the next begins), the booked `DurationMinutes`, a `Status`, the caller's idempotency key (`ScheduleKey`, unique) and who/when created. Only `Scheduled` exists in this story; **only a Scheduled appointment ever blocks a provider or operatory**, so the companion can add Cancelled / NoShow / etc. without touching the conflict rule.
- **`AppointmentScheduler.ScheduleAsync`** - the one way to book. **`ListAsync`** / **`GetAsync`** read.
- **API** `POST /api/appointments` (`ManageAppointments`: front desk, office manager, admin; needs CSRF and an `Idempotency-Key`), `GET /api/appointments?from&to&providerId&operatoryId` and `GET /api/appointments/{id}` (`ViewSchedule`).
- **UI** `/schedule` (a booking form and the day's list - not the calendar).

## The rules, in the order they are checked
1. The request is well formed: the start is a real practice-local time (not in a daylight-saving gap or repeated hour), the duration is valid (5-480 minutes in steps of 5; the appointment type's default unless the caller gives one), the start is in the future.
2. The patient, provider, operatory and appointment type exist and are active (an inactive patient cannot be booked).
3. The provider is available - **ALV-N003's rule, reused unchanged**: inside one weekly window on one practice-local day, not in blocked time, not spanning a daylight-saving change.
4. The provider has no overlapping `Scheduled` appointment -> `provider_double_booked` (409, names the appointment already holding the time).
5. The operatory has no overlapping `Scheduled` appointment -> `operatory_conflict` (409, likewise).
Not in this story: patient-overlap (the same patient in two places at once), reschedule, cancel, no-show, notes, a calendar view - all `ALV-004-C01`.  Patient flow (check-in to completed) is STORY-011, below.

## No double booking under a race
A plain "check, then insert" lets two simultaneous requests both pass the check. So steps 4-5 and the insert run in **one transaction that first takes an exclusive SQL Server application lock (`sp_getapplock`, transaction-scoped) on the operatory and on the provider**, always in the same sorted order so two requests can never deadlock. The second request waits, then sees the first one's appointment and is refused. Locks are released when the transaction ends, committed or not. A request that cannot get the lock in 10 seconds gets `503 schedule_busy` (retry). The race tests were checked by removing the lock: they then fail.

## Idempotency, audit, measurement
- **Idempotency**: `Idempotency-Key` (8-100 chars) is required. The same key and the same request returns the first appointment (200, nothing new); the same key for a *different* request is `409 idempotency_key_reused`; a unique index backs this even when the same request races itself.
- **Audit** (shared `AuditService`, PHI-free, in the same save as the booking): `AppointmentScheduled` with the user and time. If the audit write fails the appointment is not stored either. **A refused conflict/unavailability attempt is audited too** (`AppointmentRejected`, with the code and reason, and the id of the appointment in the way) - "who tried to book what, and why was it refused". A malformed request (bad duration, past start...) is not a scheduling attempt and is not audited. If a refusal cannot be audited the refusal still stands (nothing was booked).
- **Measurement**: `appointment.scheduled` (`outcome`) and `appointment.rejected` (`category` = the refusal code, `outcome`), through the shared privacy-safe sink; failure to record never changes a decision.

## Stable error codes
400: `idempotency_key_required`, `invalid_duration`, `invalid_local_time`, `start_in_past`, `validation_failed`, `invalid_range`. 404: `patient_not_found`, `provider_not_found`, `operatory_not_found`, `appointment_type_not_found`, `appointment_not_found`. 409: `provider_double_booked`, `operatory_conflict`, `provider_unavailable` (+ `reason`: `outside_working_hours`, `blocked_time`, `provider_inactive`, `crosses_dst_transition`), `patient_inactive`, `operatory_inactive`, `appointment_type_inactive`, `idempotency_key_reused`. 503: `schedule_busy`.

## UI behaviour worth knowing
Each distinct request gets one idempotency key, kept while the request is unchanged. If the connection drops after the request was sent the page says it cannot tell whether it was booked, keeps the key, and "Book again" cannot book twice. After a confirmation Book is disabled until something changes. Every refusal is explained in words and ends "Nothing was booked."

## Known limits (this story)
- No reschedule/cancel/no-show, patient-overlap, calendar, drag-and-drop, recurring or multi-resource appointments.
- A start in the past is refused (entering historical appointments is not supported).
- Availability is read before the lock is taken: blocked time added in the same instant as a booking may not be seen by that booking.
- The day list shows at most 500 appointments for a range.


---

# The calendar and the life of a booked appointment (ALV-004-C01)

ALV-004-C01 extends STORY-004 without changing its contract: STORY-004's own tests (`AppointmentSchedulerTests`, `AppointmentsApiTests`, `Schedule.test.tsx`,
`schedule-real-backend.spec.ts`) are unchanged and pass, and `/schedule` (the booking form and day list) is untouched. The new work is at `/calendar`.

## What changed in the model
- **Statuses**: `Scheduled` (the only one STORY-004 created), `Cancelled`, `NoShow`. **Only a `Scheduled` appointment holds time** - for the provider, the operatory
  and the patient - so a cancelled or no-show appointment stays on the record and in the calendar without blocking anything.
- **Appointment** gained `Notes` (<= 1000 characters, never copied into the audit log), `CancelReason`, `StatusChangedAtUtc/By`.
- **`AppointmentEvent`** (append-only history): `Scheduled`, `Rescheduled` (with where it WAS: previous start, provider and operatory), `Cancelled` (with the reason),
  `NoShow`, `NotesChanged`. Migration `AddAppointmentLifecycle`.

## The rules
- **Patient overlap** (new, also applied to booking): one patient cannot hold two overlapping scheduled appointments, with any provider or operatory.
  Conflicts are reported in the order provider, operatory, patient (`provider_double_booked`, `operatory_conflict`, `patient_double_booked`), each naming the
  appointment in the way. There is **no override**: the plan says patient overlap is refused "unless an explicitly authorized future policy says otherwise", and no
  such policy exists, so there are no manual overrides to audit.
- **Reschedule** (`PUT /api/appointments/{id}/reschedule`) applies exactly STORY-004's rules to the new place and time (real future time, valid duration, available
  provider - hours and blocked time - and no overlap with any OTHER scheduled appointment; an appointment never conflicts with itself). It keeps the appointment's id and
  records where it was. Only a `Scheduled` appointment can be rescheduled. A request that changes nothing changes nothing.
- **Cancel** (`POST .../cancel`) needs a reason (<= 400 characters; kept in the appointment's own record and history, never in the audit log). Cancelling twice is a no-op.
- **No-show** (`POST .../no-show`) is only possible once the start time has passed (`no_show_too_early` otherwise). Marking twice is a no-op.
- **Notes** (`PUT .../notes`) work in any status; blank clears. `GET .../history` lists the events; `GET /api/appointments?...&includeAll=true` includes cancelled and no-show
  appointments (the plain list is still "what is booked", exactly as STORY-004 defined it).
- Only `ManageAppointments` holders can change anything; `ViewSchedule` holders can read. Every change needs CSRF.

## Concurrency
- **Every change carries the `rowVersion` the caller read.** A stale one is the shared 409 `concurrency_conflict` - checked first, so the user hears "someone changed this",
  not a confusing consequence of it - and is also pinned for the save, so a change landing between the check and the write is caught as well.
- **Conflict checks run under locks.** Booking and rescheduling take an exclusive SQL Server application lock on the provider, the operatory AND the patient (always in the
  same sorted order, so two requests cannot deadlock) inside their transaction, then check, then write. Six appointments moved into one slot at once give one winner; so do
  a booking racing a reschedule, and six bookings for one patient at one time with different providers. Removing the locks makes those tests fail.
- A reschedule, its history entry and its PHI-free audit entry commit in ONE save: if the audit write fails the appointment does not move. A refused reschedule is audited
  and measured like a refused booking (`AppointmentRejected`).

## The calendar (`/calendar`, `ViewSchedule`)
- **Day view**: one column per provider (the practice's real assignments), time on the left (one pixel per minute from 07:00; widened for anything outside 07:00-19:00),
  each provider's weekly working hours drawn on the plain surface with the rest hatched. **Week view**: Monday to Sunday, overlapping appointments (different providers at
  the same time) placed side by side in lanes. Filters by provider and operatory; previous/next/today and a date picker.
- Cancelled appointments have a dashed outline and a struck-through name, no-shows a dotted outline, and **both write the status in the block's first line**
  ("10:00-11:00 - Cancelled"): never colour alone. Short appointments render on one line so nothing is cropped.
- **The drawer** (a non-modal dialog; focus moves into it, Escape closes it, and focus returns to its heading after each action so keyboard users are never stranded)
  books a new appointment (fast patient lookup; the type's default duration shown and used when the duration is blank; the same retry-safe idempotency key as
  STORY-004), or shows an appointment with its details, note and history and - for staff who may manage appointments - Reschedule, Cancel appointment (with a reason),
  Mark no-show (offered once the start time has passed; the server decides too) and Save note. Every refusal is explained in words, naming what is in the way and when.
- **Reschedule is done from the drawer, not by dragging.** Drag-and-drop was left out on purpose ("only if safely validated"): a drag would need the same server
  validation and explanation before it could be trusted, and the drawer already provides both, with a keyboard path.
- All times are practice-local strings from the server; the browser's own time zone never enters the calendar.

## Known limits (this story)
- No drag-and-drop; no recurring or multi-resource appointments; no manual overrides (none is authorized); no blocked-time display on the grid (blocked time is
  enforced and explained when a booking hits it, but not drawn - the read model the grid uses lists weekly hours only); no printing or export.
- A past start cannot be booked or rescheduled to (so a no-show on a long-past appointment is demonstrated by moving its times in the database in the real-browser
  walkthrough; the service-level tests use a controllable clock).
- The shared conflict banner's title colour (finding F2 on ALV-002-C01, 3.46:1) is worked around inside the calendar drawer exactly as ALV-003-C01 did inside the patient
  workspace; the shared component itself is not edited here.
- At most 500 appointments are returned for a range; the week view's side-by-side lanes get narrow when many providers overlap (the full details are in each block's
  label and in the drawer).


---

# Patient flow: from scheduled to completed (STORY-011)

STORY-011 tracks where a patient is in the visit. It extends ALV-004-C01 without changing any of its contracts: STORY-004's and ALV-004-C01's own tests are
unchanged and pass.

## What exists
- **`Appointment.FlowState`** (with `FlowChangedAtUtc` / `FlowChangedByUserId`): `Scheduled` -> `CheckedIn` -> `InTreatment` -> `Completed`. It is **separate from
  `Status`** (Scheduled / Cancelled / NoShow). Status says whether the appointment still holds time and drives every conflict rule; flow says how far the visit has
  got. Only an appointment whose Status is `Scheduled` has a flow, so a checked-in, in-treatment or completed visit **still holds its provider, operatory and
  patient time** and the conflict rules and calendar are unchanged. Migration `AddPatientFlow` (existing rows start at `Scheduled`).
- **`PatientFlowRules.Decide`** - one pure function, tested on every pair of states. Forward only; `InTreatment` may be skipped (`CheckedIn` -> `Completed`);
  check-in may not be skipped; nothing moves backwards (a mistaken check-in is a correction for a later story, not a silent undo); asking for the current state is a no-op.
- **`AppointmentFlowService`** - `CheckInAsync`, `StartTreatmentAsync`, `CompleteAsync`.
- **API** `POST api/appointments/{id}/check-in`, `.../start-treatment`, `.../complete`, each with `{ rowVersion }`; `ManageAppointments` and a CSRF token. The appointment
  view (list, detail, every response) carries `flowState` and `flowChangedAtUtc`.
- **UI** the calendar drawer offers the next step (Check in; Start treatment / Complete treatment; nothing once completed), a badge and the calendar block say it in words
  ("09:00-10:00 - Checked in") and the history shows it.

## The rules
- **Every status change is logged**, in the same save as the change: an `AppointmentEvent` history entry (`CheckedIn`, `TreatmentStarted`, `Completed`; detail
  `"<from> -> <to>"`, who, when) and a PHI-free audit entry (`PatientCheckedIn`, `PatientTreatmentStarted`, `PatientTreatmentCompleted`, with the user and a timestamp, the
  appointment id as the target, and no names, times or notes). **If the log cannot be written the status does not change either**, so a state can never change without a record.
- **Repeats are harmless.** Asking for the state the appointment is already in changes nothing and writes nothing, whatever version the caller holds (a retried request, a
  dropped connection, two people pressing Check in at once end in one check-in).
- **Every real move carries the row version the caller read**; a stale one is the shared 409 `concurrency_conflict`, never applied on top of someone else's change.
- **Once the patient has checked in** the appointment can no longer be rescheduled, cancelled or marked no-show (409 `appointment_in_progress`). A cancelled or no-show appointment has no flow (409 `appointment_not_scheduled`).
- **The database backs the service**: `CK_Appointments_FlowState` refuses an unknown state and `CK_Appointments_FlowNeedsScheduled` refuses any flow past Scheduled on a Cancelled or NoShow appointment.
- Stable codes added: 409 `invalid_flow_transition`, 409 `appointment_in_progress`. Measurement: `appointment.flow` (`category` = the target state or the refusal code, `outcome`).

## Failure paths (the story's three) and what happens
| Failure | Behaviour |
|---|---|
| Status fails to update on check-in / on completion | Nothing is stored; the caller gets a clear refusal or error and the appointment is exactly where it was. The same request can be retried safely. |
| The status change cannot be logged | The change is not stored either (one save). The caller gets an error; retrying works once logging is back. |
| Two people move the patient at once | One wins; the other gets the shared conflict (or, for the same move, a quiet no-op). One history and audit entry per move. |

## Known limits (this story)
- **No "too early to check in" rule**: booking requires a future start, so a patient can be checked in days before the appointment. The story asks for none; a same-day rule can be added later.
- **Only roles with `ManageAppointments`** (front desk, office manager, admin) can move a patient. Dentists, hygienists and assistants can see the flow but cannot mark treatment complete from their own login; that needs a clinical-completion permission, which no story has defined yet.
- **No undo**: a mistaken check-in cannot be reversed in this story.
- **"Completed" means the visit's treatment is marked complete**; it does not create a clinical encounter or charge (those are later stories).
