# Referrals and dental laboratory cases (ALV-009-C02)

Builds on [FOLLOW_UPS.md](FOLLOW_UPS.md) and [RECALL_AND_WORK_QUEUE.md](RECALL_AND_WORK_QUEUE.md). Outside work (a referral to a specialist, a piece sent to a dental laboratory) is tracked as a **case** with an outside **party**, a status, dates, linked documents and a history, and an overdue case reaches the work queue as a system task. Electronic referral or lab integrations are out of scope.

## Who may do what

| Permission | Default holders | Allows |
|---|---|---|
| `ViewExternalCases` | admin, dentist, hygienist, assistant, front desk, office manager | read cases, the party list, a case's history and a patient's timeline |
| `ManageExternalCases` | admin, dentist, front desk, office manager | create, edit and move cases; link and unlink documents |
| `ManageExternalParties` | admin, office manager | add, edit and make inactive the outside parties |

None is protected, so each can be granted, revoked and delegated through the permission policy. A case's linked documents also need `ViewDocuments` (they are the documents themselves): reading, linking and unlinking are refused without it. Billing sees nothing.

## Outside parties

A **referral provider** or a **lab vendor**: a name (unique within its kind, case-insensitive) and optional contact details. A party is never deleted (the database refuses it); making it inactive stops it being offered for new cases while the cases already sent to it keep it. A case names an active party of the matching kind when it is made; a draft may be redirected to another, a sent case keeps its party.

## Cases and their statuses

| Kind | Statuses | Moves |
|---|---|---|
| Referral | Draft, Sent, Received, Completed, Cancelled | Draft to Sent or Cancelled; Sent to Received or Cancelled; Received to Completed or Cancelled |
| Lab case | Draft, Sent, InProgress, Received, Completed, Cancelled | as a referral, plus Sent to InProgress, InProgress to Received or Cancelled, and Received back to Sent (a remake) |

Completed and Cancelled are final (the database refuses any change). A cancellation and a lab remake need a written note (kept in the history, never in the audit log). Sent and Received take the date it happened (today by default, never in the future; a case cannot come back before it was sent). A remake clears the received date.

A referral has a reason, a due ("follow up by") date, an optional visit and notes. A lab case has what is being made, a due date, an optional tooth (two-digit FDI key), an optional treatment-plan item (its tooth is the default), material, shade and notes. The procedure comes through the treatment-plan item.

A create carries an `Idempotency-Key` (one case however many times it is sent; a different request under the same key is `key_reused`, 409). Every change carries the `rowVersion` the screen showed; a stale one is `case_changed` (409) and nothing is stored. Every write, its history entry and its audit entry are one save; the audit text holds ids and status names only.

## Documents are linked, not copied

`PUT/DELETE api/{referrals|lab-cases}/{id}/documents/{documentId}` links or unlinks an existing document of the case's own patient (an inactive one is refused, a finished case takes no changes, doing it twice is a quiet replay, the database refuses a second link). A link whose document later becomes inactive, loses its file or fails the integrity check is **listed with the reason**, never dropped.

## Overdue cases and the work queue

A case that has gone out (Sent, InProgress or Received), is not finished and has a due date is **overdue** the day after it; on the due date itself it already wants its follow-up. The existing follow-up pass (`FollowUps:WorkPollSeconds`, default 300) enqueues durable ALV-N002 jobs: one create job per case and due date (key = the task's source key `referral:{case}:{yyyyMMdd}` / `lab:{case}:{yyyyMMdd}`) and one close job per open chase task its case no longer wants (`externalcase-close:{task}`). The create handler re-checks the case and stages one unassigned system task (`ReferralFollowUp` / `LabCaseDue`, High priority when already past due, due 09:00 practice time, title naming the party only) with its history and audit entry in the runner's transaction. The close handler closes it as the system when the case is completed, cancelled, back to a draft or given another due date (a new due date that has come makes a new task). The work queue shows the task with "Open the case"; the case shows the open task.

## Screens

`/external-cases` (list: Open, Overdue, Finished, All with counts; kind and status; paging), `/external-cases/{referrals|lab-cases}/{id}` (detail: facts, chase task, status actions, edit, documents, history), the patient tab **Referrals & labs** (cases, new referral, new lab case, timeline), `/admin/external-parties` (parties). Statuses, kinds and OVERDUE are said in words.

## Failure and recovery (the four questions)

1. **If this fails?** A write is one save: a failing audit write, a stale version or a duplicate leaves nothing. A failed chase job leaves no task and stays pending for the runner's retry.
2. **Retry?** The runner retries a job up to 3 times; the pass repeats every poll interval with capped backoff; stuck jobs are recovered after 5 minutes. A retried or double-clicked create makes one case.
3. **Retries exhausted?** A chase job that fails all attempts stays `Failed` with its error for triage (the next pass enqueues nothing for the same key); no case data is lost and the case still shows OVERDUE in the list.
4. **Handled / not handled.** Handled: a missing, wrong-kind or inactive party, an overdue case, a broken document link, concurrent status changes, a restart. Not handled: electronic referral or lab exchange, patient-facing notices, a chase job that is never reset by hand.

## Verification

`ExternalCaseRulesTests`, `ExternalCasePermissionTests`, `ExternalCaseSchemaTests`, `ExternalCaseServiceTests`, `ExternalCaseTaskJobTests`, `ExternalCaseApiTests`, `ExternalCaseControllerPermissionTests`, `ExternalCaseBackupRestoreTests`; client `ExternalCaseScreens.test.tsx`; the real-backend walkthrough `e2e/external-cases-real-backend.spec.ts` (`scripts/Invoke-RealBackendWalkthrough.ps1 -Config playwright.external-cases.config.ts`); 33 negative controls recorded in the handoff evidence.
