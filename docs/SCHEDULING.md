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
Not in this story: patient-overlap (the same patient in two places at once), reschedule, cancel, no-show, notes, a calendar view - all `ALV-004-C01`.

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
