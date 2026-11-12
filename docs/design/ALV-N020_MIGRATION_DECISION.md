# ALV-N020 Migration Request — Review Decision

**Decision: APPROVED — Option A**

The requested in-app alert is part of the accepted F-021 outcome. A server log
alone is not notification to the responsible operational roles, and none of the
three existing notification kinds can represent this event without fabricating a
patient, message, or document relationship. The additive migration is therefore
authorized inside ALV-N020.

## Authorized scope

- Add exactly one notification kind:
  `staff.inactivation_failed`.
- Update `CK_Notifications_Kind` and `CK_Notifications_Shape` so that this
  kind has no message, patient, or document link and requires a non-null
  `SourceKey`.
- Add no table, column, index, trigger, public notification API, or unrelated
  notification behavior.
- Replace the log-only alert seam with the in-app alert implementation.
- Use `Urgent` priority and send to every enabled Admin and OfficeManager
  account, including the acting administrator when they are in either recipient
  role. The action result does not replace the team notification.

## Required safety conditions

1. **Record before notify.** The alert may be created only after the separate,
   committed `StaffInactivationFailed` operational/audit record exists. Its
   `SourceKey` must contain that durable record's identifier.
2. **No correlation-id fallback notification.** Do not create an in-app alert
   using a correlation ID when the durable failure-record write itself fails.
   That would make the notification the only persisted evidence, contrary to
   the acceptance criterion. Log and surface that infrastructure fault with the
   correlation ID instead; it is a distinct operational failure, not evidence
   that the staff-inactivation failure was durably recorded.
3. **Idempotency.** Repeated delivery attempts for one recorded failure create
   at most one notification per recipient, using the existing
   `(RecipientUserId, SourceKey)` uniqueness rule.
4. **Privacy and truthfulness.** Content contains only the staff/profile or
   account identifiers needed by authorized recipients and the safe error
   class—never an exception message, patient data, fabricated link, or claimed
   successful deactivation.
5. **Best effort only.** Alert failure is logged and never changes the original
   refusal/rollback result or hides the durable record.
6. **Rollback disclosure.** The migration `Down` method may refuse to restore
   the prior three-kind constraint after one of these immutable notifications
   exists. State this explicitly in the migration/operator evidence and test
   the empty-table down path; do not delete immutable notification history to
   make rollback succeed.

## Required proof before handoff

In addition to the request's listed tests, prove the record-before-notify order:

- failure record succeeds, alert send fails: refusal and durable record remain;
- failure-record write fails: no in-app alert is created and the returned/logged
  correlation ID identifies the infrastructure failure;
- the database accepts the new kind only with its permitted shape and rejects
  unknown/invalid combinations;
- upgrade and empty-table down migration behave as declared; and
- the notification page renders the label with no patient link.

These are focused tests. Request the owner's permission with exact commands and
estimated duration before running them. Do not run a broad regression; record
the migration and notification/UI obligations in
`.alveara/PROGRAM_REGRESSION_LEDGER.md` for ALV-N028.
