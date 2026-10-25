# Recall, unscheduled treatment and the work queue (ALV-009-C01)

This extends [FOLLOW_UPS.md](FOLLOW_UPS.md). ALV-009-C02 adds two more system task kinds, `ReferralFollowUp` and `LabCaseDue`: see [REFERRALS_AND_LAB_CASES.md](REFERRALS_AND_LAB_CASES.md). A follow-up task now also has a **priority** (Low, Normal, High, Urgent), a **kind** (Manual, Recall, UnscheduledTreatment), an optional appointment and an optional treatment-plan item, and may be **unassigned**. Tasks the system makes have no creator and no owner until a person takes them.

## What the system makes, and when

| Kind | Made when | Never made when |
|---|---|---|
| Recall | Automatic recall is **on**, the patient's last completed visit plus their interval (default 180 days, or their own) is within the lead time (default 30 days), and the visit is not older than the max age (default 24 months) | recall is off; the patient opted out, is inactive, has an open recall, has a future appointment, was never seen, or has lapsed |
| UnscheduledTreatment | The latest state of a treatment-plan item is Accepted + Unscheduled + NotStarted, and neither the item nor its plan is withdrawn. Due 7 days after acceptance | any other state |

Recall is **off by default**, so switching the system on never fills the queue with a backlog; an office manager turns it on in *Recall settings*. "Last completed visit" is an appointment that is Scheduled with flow state Completed; the due moment is 09:00 practice time on the due date (`RecallCalculator`).

A system task is closed by the system, with the reason in its note, when the treatment is scheduled, completed, declined or withdrawn (a task a person already closed is left alone). Not done, a possible later refinement: closing a recall task when the patient books.

## How: durable background jobs

Two hosted passes (`FollowUps:WorkPollSeconds`, default 300, 0 = off) only **enqueue** jobs on the ALV-N002 job queue; the job runner does the work. The job key is the task's source key (`recall:{patient}:{yyyyMMdd}` / `unscheduled:{item}:{sequence}`), so a repeat, a restart or a second server enqueues nothing new. Each handler looks at the world again when it runs (the patient may have booked or opted out; recall may have been switched off) and does nothing if the task is no longer wanted. The task, its `SystemCreated` event and its audit entry are saved in the runner's transaction together with the job's receipt, and the unique source-key index is the last guard.

STORY-009 reminders now use the same queue: the reminder pass enqueues one job per task, due time and channel (`followup-reminder:...`); the handler delivers through the channel. A due reminder therefore survives a restart and is not duplicated by a retry. A job that fails 3 times waits for a cool-down and is then reset, so a reminder is never given up on while it is owed. A reminder for an unassigned task goes to the office managers.

## The work queue

`GET /api/work-queue` (`ViewFollowUps`): open tasks, most urgent first (priority Urgent to Low, then soonest due), filters overdue, duesoon (default 7 days), unassigned, mine, all; narrowing by priority, kind, owner or none; paged. The counts for the filter chips come with the page. Quick actions need `ManageFollowUps` and the row version the screen showed: `POST /api/follow-ups/{id}/claim` (take an unassigned task), `/assign`, `/priority`, `/appointment`; completion and cancellation are STORY-009's. A stale version is a 409 (`task_changed`) and nothing is stored; two people claiming the same task at once: exactly one wins. Every change appends a history event and an audit entry.

`GET /api/patients/{id}/follow-ups/timeline` (`ViewFollowUps`): everything that happened to the patient's tasks, newest first (at most 200), the system's part shown as the system's.

## Settings and permission

`GET/PUT /api/recall/settings` (read: `ViewFollowUps`; change: new permission `ManageRecallSettings`, Admin and Office Manager): enabled, default interval 1-3650 days, lead 0-365, max visit age 1-120 months, due-soon 1-30 days. One row, replaced with its row version; each change is audited with old and new numbers. `GET/PUT /api/patients/{id}/recall` and `POST .../recall/reset` (`ManageFollowUps`): a patient's own interval and/or opt-out with a reason; reset returns them to the practice default. Screens: *Work queue*, *Recall settings*, and a recall card and timeline on each patient's follow-ups page.

## Failure and recovery (the four questions)

1. **If this fails?** Task creation and closure commit with the job receipt or not at all; a failed job leaves no task. A stale or duplicate action stores nothing.
2. **Retry?** The runner retries a job up to 3 times; the passes repeat every poll interval with capped backoff after consecutive failures; stuck jobs are recovered after 5 minutes.
3. **Retries exhausted?** A failed recall/treatment job stays `Failed` with its error for triage and the next pass enqueues nothing new for the same key (the key exists), so an operator resets or re-keys it; failed reminder jobs are reset automatically after a cool-down.
4. **Handled / not handled.** Handled: restart, repeat, second server, concurrent edits, a patient who booked or opted out after enqueue, recall switched off after enqueue, a withdrawn plan. Not handled: sending recall by SMS or email (out of scope), auto-closing recall when the patient books, a failed recall job that is never reset by hand.

## Verification

`RecallCalculatorTests`, `WorkQueueSchemaTests`, `RecallJobTests`, `UnscheduledTreatmentJobTests`, `FollowUpReminderJobTests`, `WorkQueueApiTests`, `WorkQueueBackupRestoreTests`, the STORY-009 suites unchanged, client `WorkQueueScreens.test.tsx`, and the real-backend walkthrough `e2e/work-queue-real-backend.spec.ts` (`scripts/Invoke-RealBackendWalkthrough.ps1 -Config playwright.work-queue.config.ts`). 20 negative controls are recorded in the handoff evidence.
