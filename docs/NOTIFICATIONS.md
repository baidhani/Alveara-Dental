# Notification center and internal messaging (ALV-N015)

Operational notices and plain-text messages between staff. This is not a clinical record and not a place for patient details.

## What a person sees

| Surface | Route | Who |
|---|---|---|
| Bell and Important/Urgent banner | every screen (sidebar) | everyone signed in |
| Notifications (Unread, History) | `/notifications` | everyone signed in |
| Messages (Inbox, Sent, thread, reply) | `/messages`, `/messages/:id` | read: everyone signed in; write: `SendMessages` |
| Notification history (metadata) and break-glass | `/admin/notifications` | `ViewNotificationAuditHistory`; break-glass needs `ViewMessageAuditContent` |

The bell asks for counts every 60 seconds and when the window regains focus. If the check fails it says "could not check"; it never shows zero. The banner appears only for unread **Important** or **Urgent** notices and its text is generic (no patient, file or message text).

## Rules

* A message is immutable plain text: subject up to 120, body up to 2000 characters, 1 to 25 named, enabled recipients, priority Normal / Important / Urgent (any sender with `SendMessages`).
* A notification belongs to its recipient. Only `ReadAtUtc` and `DeletedAtUtc` change, once each. An unread notification cannot be hidden. Nothing is physically deleted (the database refuses it).
* Read receipts are shown to the sender only.
* **A notification list never carries a message subject, body or patient identifier** (item 42, ALV-N015 R02). `GET notifications` returns the kind, priority, times, the sender's name for a message, `messageId` and `hasPatientLink`; the client shows a generic label ("New message", "A duplicate document was added"). The subject and body are delivered only on the authorized message routes (`messages/{id}`, `messages/threads/{id}`, the inbox and sent lists for a participant) and the audited break-glass route.
* A message may link one patient. Lists never carry a patient id; the client asks the server to resolve the link, which answers with a path only to a caller who may view patients, and with one `patient_access_denied` refusal otherwise.
* Anyone who is not the sender or a recipient gets the same `message_not_found` (404) as for a missing message.
* Sending needs an `Idempotency-Key`. The same key and body replays the first result (200, `replayed: true`); the same key with a different body is `idempotency_key_reused` (409). Limit: 30 messages a minute per sender (`rate_limited`, 429, `Retry-After`).

## Administrator

* **History** shows recipient, kind, priority, status (Unread / Read / Hidden), times and a safe source id. Never a subject, body or patient id.
* **Break-glass** opens ONE message or ONE thread (at most 100 messages). It needs a written reason (up to 500 characters) and an explicit confirmation. The attempt is audited first; refusals are audited with the cause; the success is audited before any content is returned. If an audit entry cannot be written the request fails with `audit_unavailable` (503) and no content is returned. Audit entries hold ids and the reason, never message text.

## Document duplicate notices (the consumer)

A durable, cursor-driven consumer reads `DocumentEvents` after its stored cursor (`NotificationCursors`, source `DocumentEvents`) in the API process (`Notifications:ConsumerPollSeconds`, default 5; 0 turns the loop off). For each `ImportedAsDuplicate` event it creates one **Important** `document.duplicate_added` notice for every enabled account whose role holds `ReceiveDocumentNotifications` *when the event is processed* (Admin, OfficeManager). The notice carries ids only (patient, document, the duplicated document), never a file name.

* Cursor starts at the current high-water mark when the migration runs: no back-fill of earlier events.
* Delivery is eventually consistent. The producer never calls the center and is unaffected by it.
* Idempotent: a unique `(RecipientUserId, SourceKey)` (`DocumentEvents:<n>`) means a replay after a crash between creating notices and moving the cursor creates nothing new. The cursor moves in the same transaction as the notices.
* Failure: an error is logged with its class only, the loop backs off (capped at 60 s) and tries again; nothing is lost because the cursor did not move.

## API

`GET notifications/summary`, `GET notifications?status=unread|history`, `POST notifications/{id}/read`, `POST notifications/read-all`, `DELETE notifications/{id}` (409 `notification_unread`), `DELETE notifications/history`, `GET notifications/{id}/patient-link`, `POST messages`, `GET messages?box=inbox|sent`, `GET messages/recipients`, `GET messages/{id}`, `GET messages/threads/{threadId}`, `GET messages/{id}/patient-link`, `GET admin/notifications/history`, `POST admin/messages/break-glass`. Every write needs a CSRF token. Refusals carry `error`, `message` and, for wrong fields, `problems`.

## Database

Tables `Messages`, `MessageRecipients`, `Notifications`, `NotificationCursors` with check constraints and triggers (error codes 51195 to 51203) that refuse updates to a message, any change other than the one-time read / hide of a notification, deletes, links across threads or to disabled recipients, and a cursor that moves backwards. Rolling the migration back is refused when messages or notifications exist.

## Verification

Backend: `NotificationSchemaTests`, `NotificationPermissionTests`, `NotificationApiTests`. Client: `Notifications.test.tsx`.
