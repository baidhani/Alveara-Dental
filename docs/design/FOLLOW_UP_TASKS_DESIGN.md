# STORY-009 - Follow-up tasks and reminders: design note (for review before any code)

Story: `STORY-009` (REQ-017: follow-up tasks with recall intervals, reminders and completion history). Release r4. Status of this note: **approved by the owner on 2026-10-23** (dedicated permissions with the defaults below; in-app reminders only now; the office-manager fallback and the 60 second poll accepted; email later, designed for but not built).

## Acceptance criteria this design must satisfy
1. A follow-up task is created; when it is due, a reminder reaches the responsible party.
2. A task is completed; the completion history is updated.
3. Every follow-up action is logged with user and timestamp.
Failure paths named by the story: missed reminder, task completion error, data entry error, audit failure, concurrency issues.

## Permissions
`ViewFollowUps` and `ManageFollowUps`, appended to `Permission` (ordinals undisturbed), added to `PermissionRegistry.json` as Active. Defaults (Admin holds both as always): Dentist, Hygienist, Assistant, FrontDesk, OfficeManager hold both; Billing and Unassigned hold neither. Neither is protected, assignable-only or high impact, so both are grantable and delegable under the existing policy rules. Manage implies view in the defaults but the two are separate permissions. The responsible party of a task must be an enabled account whose role holds `ViewFollowUps`.

## Data (one migration)
- `FollowUpTasks`: `Id`, `PatientId`, `AssigneeUserId`, `Title` (bounded, plain text), `Note` (bounded, optional), `DueAtUtc`, `Status` (`Open`, `Completed`, `Cancelled`), `RecallIntervalDays` (nullable, 1 to 3650), `CompletedAtUtc`, `CompletedByUserId`, `CreatedByUserId`, `CreatedAtUtc`, `RowVersion`. Database constraints: status and interval ranges, title not blank, completion fields set exactly when completed.
- `FollowUpReminderDeliveries`: `Id`, `TaskId`, `DueAtUtc`, `Channel` (`InApp`), `RecipientUserId`, `Outcome`, `CreatedAtUtc`, unique on (`TaskId`, `DueAtUtc`, `Channel`): the dedupe record of a reminder per channel.
- `FollowUpTaskEvents` (append-only, immutable by trigger like the policy history): `Id`, `TaskId`, `Kind` (`Created`, `Reassigned`, `DueChanged`, `Edited`, `Reminded`, `Completed`, `Cancelled`, `RecallCreated`), `ActorUserId` (null for the system reminder), `OccurredAtUtc`, `Detail` (bounded, no free text from the note). This is both the completion history and the user-and-timestamp log.
- `Notification` gains the kind `FollowUpDue` and a nullable `RelatedFollowUpTaskId` so the notification can link to its task. The reminder carries the task id and patient id only, never the title or note.
- Task and event tables are per-patient clinical data: reading them requires `ViewFollowUps` and `ViewPatientRecords` is not enough on its own.

## API (all under `/api`, CSRF on writes, `Idempotency-Key` on creates and completions, request bodies validated)
- `POST /patients/{id}/follow-ups` (Manage): create; due date must be present and not absurdly far (bounded); assignee validated; title and note length bounds; `400` with a stable code on bad input.
- `GET /patients/{id}/follow-ups`, `GET /follow-ups/mine` (the caller's open tasks, due first), `GET /follow-ups/{id}` with its events (View).
- `PUT /follow-ups/{id}` edit, reassign, change due date (Manage; `expectedRowVersion`, stale write `409`).
- `POST /follow-ups/{id}/complete` and `/cancel` (Manage; `expectedRowVersion`; a task already completed answers the same result idempotently, a stale version is `409`, a cancelled task cannot be completed).
- Audit: each write records the event row and an `AuditService` entry in the same transaction (new audit types `FollowUpCreated`, `FollowUpChanged`, `FollowUpCompleted`, `FollowUpCancelled`, `FollowUpReminderSent`). If either write fails, nothing is stored (audit failure test).

## Reminders (acceptance criterion 1)
A hosted service, `FollowUpReminderService`, modeled on `NotificationConsumerHostedService`, polls every N seconds (configuration `FollowUps:ReminderPollSeconds`, default 60, 0 turns it off for tests). One pass, in one transaction per batch:
1. Select open tasks with `DueAtUtc <= now` that have no `Reminded` event for the current due date (a left-anti-join on events), oldest due first, up to a batch size.
2. For each, add a `Notification` for the assignee with `SourceKey = followup-due:{taskId}:{dueAtUtc ticks}` (the existing unique source key makes a repeat harmless), add a `Reminded` event and an audit entry, and commit the batch.
Why a scan and not a scheduled job per task: a missed run, a restart or a clock jump only delays the reminder, never loses it; the next pass sends every reminder that is due and not yet sent (the **missed reminder** path). If the assignee is disabled or no longer holds `ViewFollowUps`, the reminder goes to the practice's office managers instead and the event records the fallback (an unowned reminder must not vanish). A failure of a pass is logged with its class only, backs off (capped) and carries on, like the notification consumer. Changing a task's due date to a later time re-arms the reminder for the new date (the key includes the due time); completing or cancelling stops it.

## Completion and recall (acceptance criterion 2)
Completing a task, in one transaction: check the row version, set the status and completion fields, append `Completed`, audit, and if `RecallIntervalDays` is set create the next task (due = completion time + interval, same assignee, same interval, `RecallCreated` event on the old task, `Created` event on the new) exactly once: the new task's idempotency is the completed task's id, enforced by a unique index. Two simultaneous completions: one wins, the other gets `409` (**concurrency**). A failure inside the transaction leaves the task open and nothing stored (**task completion error**).

## Front end
A "Follow-ups" page in the patient chart (list, create, complete with an optional note, history) and "My follow-ups" for the assignee, gated by the permissions through the existing `moduleRegistry`; light/dark, loading and error states, keyboard and WCAG 2.1 AA as elsewhere; stale-write message like the policy editor.

## Tests (planned; happy, failure, boundary, idempotency)
Unit: due-date and interval validation, recall date arithmetic, key construction. API (real SQL): create/read/edit/complete/cancel, authorization per role including Billing denied and the delegation default, stale 409, double complete, completion rollback on audit failure (trigger), idempotent create and complete replay, event immutability by raw SQL, reminder sent once when due, reminder not sent before due or after completion, missed-run catch-up after the service is stopped, re-armed reminder after a due-date change, fallback to office managers when the assignee is disabled, notification carries no title or note. Client: screens tests and an axe scan. Real-backend walkthrough: create a task due now, see the reminder in the notification center, complete it and see the history. Negative controls on the reminder dedupe, the version check and the audit write.

## Later channels (email), designed for and not built
The owner expects email reminders later. This story builds only the in-app channel, but leaves the seam:
- **Deliveries are recorded per channel.** A reminder is a delivery row keyed by (task, due time, channel); the only channel now is `InApp`. A second channel can therefore never double-send the first, and a failed email never blocks the in-app reminder.
- **One interface.** The reminder pass sends through `IReminderChannel` (`Channel` name, `SendAsync(reminder)` returning delivered, retry later or failed permanently). Only the in-app channel is registered. An email channel later is one new class and one registration; the pass, the keys and the tests do not change.
- **No patient details in a reminder.** A reminder carries the task id and patient id only, which already suits email: a later email would say a follow-up is due and ask the person to sign in. Putting patient details in an email is a privacy decision for the owner, not made here.
- **What email will need, not built now:** an address on the account or a contact record, an email provider and its credential held in configuration (not in source), a per-user opt-in, an explicit timeout, capped retries with backoff, a dead-letter state for permanent failures, and bounce handling. Each external call will follow the project's timeout and capped-retry rule.

## Out of scope
Email or SMS reminders now (no email sender, provider or account address exists), appointment-driven recall scheduling and work queues (ALV-009-C01 builds on this), per-user permissions, and reminder escalation chains.

## Decisions taken with the owner
1. In-app reminders only in this story; email later (see "Later channels").
2. The fallback when the assignee cannot be reminded goes to office managers: accepted.
3. Default poll interval 60 seconds: accepted.

## What changed from this note while building (recorded after the build)
- A closed task carries `ClosedAtUtc`, `ClosedByUserId` and an optional `ClosureNote` (the note on completion, the reason on cancellation) instead of completion-only fields.
- The reminder notification links through the patient and its source key; no new notification column was added. The key uses a dot (`followup.due:`) because the existing notification source-key check refuses a hyphen in practice.
- Complete and cancel are idempotent by state (the same person doing the same thing again is a quiet replay), so they take no Idempotency-Key; only a create does, and its task id is derived from the person and the key.
- A task nobody can be told about is not recorded: it is retried on every pass and counted in the pass log, instead of being given a dead-letter state.
- Added: `GET /api/follow-ups/assignees` (who a task can be assigned to) and `GET /api/follow-ups/mine`.
- Reminders may go through several channels; each channel's delivery is its own record, and a channel that defers or throws is asked again without repeating the others.

## Risk and size
Medium blast radius (new tables, one migration, a new hosted service, one new notification kind). I expect about 70 new tests. No existing behavior changes except the permission list, the registry, and the notification kind list.
