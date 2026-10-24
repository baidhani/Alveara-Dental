# Follow-up tasks and reminders (STORY-009, REQ-017)

Follow-up tasks keep a patient's care continuous: a task has a title, an optional note, a due time, a responsible person and an optional recall interval. When it is due the responsible person is reminded; when it is completed the history records who did it and when, and a task with a recall interval creates the next one. The approved design and its reasons are in `docs/design/FOLLOW_UP_TASKS_DESIGN.md`; this page says what the system does now.

## Who may do what

| Permission | Holds it by default | Allows |
|---|---|---|
| `ViewFollowUps` | Dentist, Hygienist, Assistant, FrontDesk, OfficeManager, Admin | reading a patient's tasks, "My follow-ups", a task's history, the list of people a task can be assigned to |
| `ManageFollowUps` | the same | creating, editing, completing and cancelling tasks |

Billing and unassigned accounts hold neither. Neither permission is protected, so an administrator can change them in the permission editor (the change applies to the next request) and delegate them under the usual rules. The responsible person of a task must be an enabled account whose role holds `ViewFollowUps`.

## The API (`FollowUpsController`)

Reads need `ViewFollowUps`; writes need `ManageFollowUps` and a CSRF token. Errors carry a stable `error` code.

* `POST /api/patients/{id}/follow-ups`: create (`title`, `note`, `dueAtUtc`, `assigneeUserId`, `recallIntervalDays`). Needs an `Idempotency-Key` (up to 64 characters). The task's id is made from the person and the key, so a repeat answers with the task already made (`replayed: true`) and the same key with a different request is `idempotency_key_reused` (409).
* `GET /api/patients/{id}/follow-ups?status=open|closed|all`, `GET /api/follow-ups/mine?status=...` (soonest due first, at most 200, `more` says if there are others), `GET /api/follow-ups/{id}` (the task and its events), `GET /api/follow-ups/assignees`.
* `PUT /api/follow-ups/{id}`: edit title, note, due time, assignee and recall interval as a whole, with the `rowVersion` the caller was shown. `POST .../complete` (optional `note`) and `POST .../cancel` (a `note` is required) also carry `rowVersion`.

Input rules (checked before anything is stored, and again by the database): a title is one line of 1 to 120 characters; a note is up to 500 characters of plain text; a due time is at most 366 days in the past and 3,660 days in the future; a recall interval is 1 to 3,650 days; names of states are exact. Refusals: `title_invalid`, `note_invalid`, `note_required`, `due_required`, `due_out_of_range`, `interval_invalid`, `assignee_invalid`, `patient_not_found`, `task_not_found`, `idempotency_key_required`, `row_version_required` (400/404); `task_changed`, `task_closed`, `no_change`, `idempotency_key_reused` (409).

Complete and cancel are idempotent by state instead of by key: the same person doing the same thing again is answered quietly (`replayed: true`) and changes nothing; anyone else gets `task_closed`.

## Reminders

> ALV-009-C01: reminders are now delivered through the durable job queue (one job per task, due time and channel), and tasks can be unassigned, prioritised and made by the system; see [RECALL_AND_WORK_QUEUE.md](RECALL_AND_WORK_QUEUE.md).

`FollowUpReminderService` finds every open task that is due and has no recorded delivery for a channel and its current due time, oldest first, and tells the responsible person. Because the rule is "due and not yet reminded" and not "due in this window", a missed run, a restart, a clock jump or an outage only delays a reminder: the next pass sends everything owed. The notification (kind `followup.due`, in the notification center), the delivery record, a `Reminded` event and an audit entry are saved together, so a failure leaves none of them and the next pass repeats the batch; the unique keys make a repeat, or a second server running the same pass, harmless. A changed due time is a new reminder (the delivery is unique per task, due time and channel). A completed or cancelled task is never reminded. A reminder carries the task id, the patient and the due time; never the title or the note. Opening it takes the person to the patient's follow-ups.

If the responsible person is disabled or can no longer see follow-ups, every enabled office manager who can is told instead and the delivery is recorded as a fallback. If nobody can be told, nothing is recorded and the next pass tries again (it is counted in the pass log, not lost).

`FollowUpReminderHostedService` runs the pass every `FollowUps:ReminderPollSeconds` seconds (default 60; 0 turns it off, as in tests that drive the pass themselves). A failed pass is logged with its error class only, backs off (capped) and carries on. Each pass that did anything logs one structured line: `followup_reminders_pass outcome=... sent= fallback= undeliverable= deferred= duration_ms=`.

### Email later

Only the in-app channel exists. The pass sends through `IReminderChannel`; an email channel would be one new class and one registration, with its own timeout, capped retries and dead-letter handling, and the pass, the keys and the tests would not change. A channel that returns `RetryLater` or throws records nothing and is asked again on the next pass (tests prove a second channel neither blocks nor is blocked by the in-app one). What email needs and is not built: an address on the account or a contact record, a provider and its credential in configuration, a per-person opt-in, bounce handling, a permanent-failure result with a dead-letter store, and the owner's decision on what a reminder email may say about a patient.

## Completion, recall and history

Completing a task, in one save: checks the row version, closes the task with who and when and the optional note, appends the `Completed` event and the audit entry, and, if the task has a recall interval, creates the next task (due: now plus the interval; same patient, title, note and interval) with its own `Created` event, and a `RecallCreated` event on the completed one. The recall's id is made from the completed task's id and the database allows one recall per task, so it is made exactly once however many times or from however many places completion is attempted. The recall goes to the same person, or to the person completing when the original can no longer receive it. A cancelled task creates no recall.

The history is `FollowUpTaskEvents`, append-only (the database refuses any change or delete): `Created`, `Edited`, `Reassigned`, `DueChanged`, `Reminded`, `Completed`, `Cancelled`, `RecallCreated`, each with who (none for the system's reminder) and when. Every write also records an audit entry (`FollowUpCreated`, `FollowUpChanged`, `FollowUpCompleted`, `FollowUpCancelled`, `FollowUpReminderSent`) naming ids only.

## Failure and recovery (the four questions)

1. **What happens if this fails?** A write is one save: the task change, its events, its audit entry and (for a completion) the recall commit together or not at all, so an audit failure, a stale version or a database error stores nothing and the person is told. A reminder pass is the same: a failure leaves no notification, delivery, event or audit entry.
2. **Will it retry?** A reminder is owed until it is recorded, so every pass retries it (fixed interval, capped backoff after consecutive failed passes, at most 20 passes per wake-up). A write is retried by the person; the idempotency key (create) or the state (complete, cancel) makes the retry safe.
3. **When retries are exhausted?** There is no point at which a due reminder is given up on: it is sent by the first pass that can send it. A task nobody can be told about is counted in the pass log (`undeliverable`) so it can be found; a dead-letter store belongs to a future external channel.
4. **Handled and not handled.** Handled: a missed reminder, a task completion error, a data entry error, an audit failure, concurrent changes, a second server, a person who has left. Not handled: a reminder to a person who never opens the notification center (there is no second channel yet), reminders by email or text, a recall that should skip weekends or holidays, a task assigned to a group.

## Verification

`FollowUpPermissionTests`, `FollowUpSchemaTests` (rows written around any service), `FollowUpValidationTests`, `FollowUpApiTests`, `FollowUpReminderTests` and `FollowUpReminderApiTests`, `FollowUpBackupRestoreTests`, `FollowUpScreens.test.tsx`; the real-backend walkthrough `e2e/follow-ups-real-backend.spec.ts` (`playwright.follow-ups.config.ts`; the walkthrough runner sets the reminder pass to 3 seconds); the negative controls and the full-run results are in `docs/testing/STORY-009_TEST_RESULTS.md`.
