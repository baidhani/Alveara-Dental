# ALV-N020 - Migration request: staff-inactivation failure notification

- **Story:** ALV-N020 (item 57), R05 PKG-03, finding F-021
- **Session:** CC-20261109-8w3d
- **Date:** 2026-11-12
- **Status:** REQUEST - document only. No code, migration or test has been written or run for it.
- **Decision requested:** authorize one small schema migration inside ALV-N020, or direct otherwise.

## 1. Why this request exists

F-021's acceptance line in PKG-03 (`MANUAL_E2E_STABILIZATION_PLAN_R05.md`, PKG-03 Group A) requires:

> A failure returns a clear result and a durable operational record; the highest-urgency notification goes to the administrator and the office manager but is never the only evidence of a failure.

The approved brief (section 1.1 item 4, section 7.5, test table row "Atomic failure") promised it, and the brief review fixed it as **approved decision 2**: `Urgent` priority, only to enabled Admin and OfficeManager accounts, best effort, never the only evidence.

**What the brief missed:** the in-app notification table accepts only a fixed list of kinds, enforced by two database check constraints. None of the existing kinds can carry this alert. Adding one is a schema change. PKG-03 authorizes only "a version column on the link only if none exists", and the standing rule for this story is to stop for review if any other migration is needed. This is that stop. The omission is mine, from the brief; it was found during implementation.

## 2. Current state (implemented in 21b40fc, proven)

On a failed account step, `StaffInactivationService.InactivateAsync`:

1. rolls everything back (profile stays active, login unchanged);
2. writes a durable `StaffInactivationFailed` audit entry in its own short transaction after the rollback;
3. calls `IStaffInactivationAlert.SendAsync` with a 10-second timeout; a failure there is logged and never changes the result;
4. returns a clear refusal (409 `staff_login_last_admin` / `staff_login_busy`, 403 `staff_login_not_allowed`) or the original exception (500 with correlation id).

The alert implementation registered today is `LogOnlyStaffInactivationAlert`: an error-level server log entry, no in-app notification. **So the "Urgent notification to administrator and office manager" part of the acceptance line is not met.** Everything else in F-021 is built and proven (focused xUnit 10/10, negative control NC-3, chain step F-021 PASS).

## 3. Why no existing kind fits

`Notifications` constraints today (`NotificationModel.cs:100-108`):

| Kind | Shape the database requires |
|---|---|
| `message.received` | `MessageId` NOT NULL, `SourceKey` NULL, no documents |
| `document.duplicate_added` | `MessageId` NULL, `SourceKey`, `RelatedPatientId` and two different document ids NOT NULL |
| `followup.due` | `MessageId` NULL, `SourceKey` and `RelatedPatientId` NOT NULL, no documents |

The staff-inactivation failure concerns a staff profile and an account. It has no patient, no document and no message. Reusing a kind either violates the shape constraint or fabricates a patient or message link that is not true.

**Alternative considered and not recommended:** send it as an internal `Message` (which supports `Urgent`). `Messages.SenderUserId` must be a real account, so the message would appear to be written by the administrator who attempted the inactivation. That misattributes authorship, consumes that person's per-minute message limit, and puts a system alert in the human message thread.

## 4. Proposed change

### 4.1 Migration (one, additive): `AddStaffInactivationFailedNotification`

Same pattern as STORY-009's `AddFollowUps` (`20261024005717`), which added `followup.due` by dropping and re-adding the two constraints.

- `NotificationKinds.StaffInactivationFailed = "staff.inactivation_failed"` added to `NotificationKinds.All`.
- `CK_Notifications_Kind`: dropped and re-added with the four kinds.
- `CK_Notifications_Shape`: dropped and re-added with one more alternative:
  `[Kind] = 'staff.inactivation_failed' AND [MessageId] IS NULL AND [SourceKey] IS NOT NULL AND [RelatedPatientId] IS NULL AND [RelatedDocumentId] IS NULL AND [RelatedOtherDocumentId] IS NULL`
- No new table, column, index or trigger. The existing triggers already do what is needed: `TR_Notifications_Links` refuses a notification to a disabled account; `TR_Notifications_Guard` refuses a change of recipient, kind, priority or creation time, and `TR_Notifications_NoDelete` refuses a delete.
- **Down:** restores the three-kind constraints. If any `staff.inactivation_failed` row exists, re-adding the constraint fails and the down migration stops with nothing changed (rows cannot be deleted: `TR_Notifications_NoDelete`). This is stated, not hidden; a rollback after use needs a manual decision.
- No data change to existing rows. Installs and upgrades (ALV-N013 path) pick it up like any other migration.

### 4.2 Code (replaces the log-only seam; no new public contract)

- `InAppStaffInactivationAlert : IStaffInactivationAlert` replaces `LogOnlyStaffInactivationAlert` in `Program.cs`.
- **Recipients:** accounts with `Role` Admin or OfficeManager and `IsDisabled = 0`, read at send time. The acting administrator is included if they hold one of those roles (they already saw the refusal; the record is for the team).
- **Row:** `Kind = staff.inactivation_failed`, `Priority = Urgent`, `RelatedPatientId = null`, `SourceKey = staff-inactivation-failed:<failure audit entry id>`.
- **Idempotency:** the existing unique index `(RecipientUserId, SourceKey)` gives one notification per recipient per recorded failure, however many times the send is retried.
- **Content:** ids and the error class only, never an exception message (same rule as the audit entry). The failure audit entry id is passed from `RecordFailureAsync` to the alert; if that record itself failed, the alert uses the correlation id in the source key instead.
- **Failure handling unchanged:** own short `SaveChanges`, 10-second timeout, any error logged with `error_class` and swallowed into the log (not thrown), never changes the caller's result.
- **Client:** `NotificationsPage.tsx` `KIND_LABEL` gains `"staff.inactivation_failed": "A staff inactivation failed; the login was not disabled"`. The entry has no patient link (`NotificationLinkService` already refuses a link when `RelatedPatientId` is null), so it shows as plain text.

### 4.3 Tests (written with the change; run only with the owner's explicit permission)

| Test | Kind | Proves |
|---|---|---|
| Failed inactivation sends one `Urgent` `staff.inactivation_failed` notification to each enabled Admin and OfficeManager, none to other roles or disabled accounts | xUnit (extends `StaffInactivationServiceTests`) | acceptance line |
| Sending twice for the same failure leaves one row per recipient | xUnit | idempotency |
| Alert store failure (forced) is logged and the refusal and failure audit entry are unchanged | xUnit (`A_failing_alert_does_not_change_the_outcome_or_hide_the_failure_record` exists; extended to the in-app alert) | never the only evidence |
| Database refuses the new kind with a patient, message or document attached, and refuses an unknown kind | xUnit on a real database | shape constraint |
| Notifications page shows the new label without a patient link | Vitest | client |
| NC-4 negative control: alert not sent -> the first test fails | mutation, restored byte for byte | the test has teeth |

**Cost to disclose:** a new migration changes the migration count, so the first server test run afterwards rebuilds the shared test template database once (logged as `template_migration_mismatch`). The focused run above is otherwise the same size as group 2 already run for this story. The chain step F-021 is unchanged (it exercises the success path).

## 5. Ledger impact

`PROGRAM_REGRESSION_LEDGER.md` gains a migration obligation for ALV-N028: upgrade from the pre-ALV-N020 schema, apply/down on an empty table, and the notification bell and list with the new kind present.

## 6. Options for the reviewer

| Option | Result |
|---|---|
| **A. Authorize the migration in ALV-N020 (recommended)** | F-021 closes fully in this story; R01 is built after the change and its focused proof. |
| B. Keep log-only in ALV-N020 and move the in-app alert to a named later story | R01 states the acceptance line as NOT MET with the owner/reviewer deferral recorded; F-021 stays partly open. |
| C. Accept the log entry as meeting the line | Not recommended: a server log is not a notification to the administrator and office manager. |

## 7. Required decision

Choose A, B or C. Under A, confirm the kind name `staff.inactivation_failed` and that the acting administrator may be among the recipients.
