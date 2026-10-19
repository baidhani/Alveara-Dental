# Notification Center and Internal Messaging — Story Proposal for Independent Review

**Proposed story:** ALV-N015 — Notification Center and Internal Messaging (the identifier and its position in the plan are the reviewer's to set, decision D1)
**Type:** New production story (no course parent). It builds on `STORY-010` through a read-only event seam and must not require any change to it.
**Document status:** APPROVED DESIGN. No code has been written for this story. The owner approved the decisions recorded in section 8 on 2026-10-19; the implementation prompt is controlled by the Execution Plan.
**Prepared:** 2026-10-19, session CC-20261008-k4q9
**Repository state when written:** `main` at `4463f2f` (pushed). Gates A–D are PASS and approved; items 1–38 are `COMPLETE`. `STORY-010` (item 39) is in design; its design note is `docs/DOCUMENTS.md`.

**How to respond.** Section 10 is a response template. For each decision D1–D12 reply APPROVE, APPROVE WITH CHANGE (say what), or REJECT (say what to do instead). Anything not answered is treated as "recommendation not yet accepted".

---

## 1. What the project owner asked for

The request grew over one design conversation about `STORY-010` (document import). It is recorded here in full so nothing is lost.

1. **A notification system available application-wide and to every signed-in user.** Not only the office manager and the admin.
2. **A bell icon with an unread-count badge** at the top (right or left) of every screen. Selecting it shows the unread notifications.
3. **A notification history.** Notifications that have been read move to a history that lists them; the person can **delete** individual ones and **clear all** of them at once.
4. **A banner** that can be **dismissed** (otherwise it becomes noise). It appears on sign-in and while the person is already signed in, but **only for notifications marked Important or Urgent**. A normal notification reaches the bell and its badge quietly.
5. **User-to-user notes and messages.** One person can send a note or message to another person (the owner's example: a dentist sends a note to an assistant). It appears in the recipient's bell, and shows the banner only if the sender marked it Important or Urgent.
6. **System notifications** use the same center. The first one: when a document is imported again as a duplicate (`STORY-010`), notify **both** the office manager and the admin.
7. A condition from the owner: whatever is built for `STORY-010` now **must not need to change** to accommodate this story; otherwise `STORY-010` waits until this story is specified.

## 2. Verified current state (what exists today)

- **There is no notification feature.** No bell, inbox, history, message, e-mail or SMS. The one "notifier" in the code is backup-specific: it records a `BackupNotificationRecord` and drops a file into a local folder (`Backup:NotificationDirectory`), which is not visible to staff in the application.
- **The application shell** (`src/alveara-client/src/app/AppShell.tsx`) has a header with account controls, a permission-aware navigation (`moduleRegistry.ts`), a session-expiry warning banner and a shared `Notification` banner component. None of it persists or addresses a person.
- **Permissions are fixed in code** (`PermissionMatrix`, one role-to-permission table). The permission-matrix screen is read-only; nothing lets an administrator grant a permission at run time. (A separate proposal for that is recorded in `docs/DOCUMENTS.md`, "Deferred: admin-grantable permissions".)
- **Building blocks that exist and are reused:** the shared audit service (same `SaveChanges`), the idempotency guard, optimistic row versions, durable background work (`BackgroundJobRunner`), the measurement events, backup coverage of the whole database, and the unit and real-backend test harness.
- **The system is designed to work without public internet access** (a local Windows server and a LAN). There is no push channel (WebSocket, server-sent events) and no mail relay.

## 3. Proposed design

### 3.1 Two concepts: a message and a notification

- A **message** is content a person wrote: sender, subject (optional), body, priority, an optional patient link, an optional reply-to. It is immutable once sent.
- A **notification** is one person's inbox entry: recipient, kind, priority, related ids, created, read, deleted. Every message produces one notification per recipient; the system produces notifications with no message at all.

This separation is what lets the sender keep a sent list, lets each recipient keep their own read and delete state, and lets system notifications exist without any text body.

### 3.2 Data (new tables)

| Table | Content | Rules |
|---|---|---|
| `Messages` | `Id`, `SenderUserId`, `Subject` (up to 120), `Body` (up to 2000, plain text), `Priority`, `RelatedPatientId` (optional), `InReplyToMessageId` (optional), `ThreadId`, `CreatedAtUtc` | Immutable (trigger and guard). Never physically deleted by the application. |
| `MessageRecipients` | `MessageId`, `RecipientUserId` | One row per recipient; unique together. |
| `Notifications` | `Id`, `RecipientUserId`, `Kind` (stable code, for example `document.duplicate_added`, `message.received`), `Priority` (`Normal`, `Important`, `Urgent`), `MessageId` (optional), `RelatedPatientId`, `RelatedDocumentId`, `RelatedOtherDocumentId`, `SourceKey` (for system notifications), `CreatedAtUtc`, `ReadAtUtc`, `DeletedAtUtc`, `RowVersion` | Only `ReadAtUtc` and `DeletedAtUtc` can change (trigger and guard). `SourceKey` is unique per recipient so a system event produces at most one notification per person however many times it is processed. Holds ids and a kind only: no text, no patient name. |
| `NotificationCursors` | `Source` (for example `DocumentEvents`), `LastEventNumber`, `RowVersion` | One row per system event source; the position the consumer has reached (see 3.3). |

### 3.3 How system notifications are produced without changing the producing story

A producing story does not call the notification center. It records an **immutable, ordered event** in its own history, which it already does for its own reasons. The center reads those events.

- Each producing event table has a monotonic `EventNumber` (a never-reused identity value), a `Kind`, the ids involved and a timestamp, and holds ids and codes only.
- A background job (the existing durable job runner) keeps a **cursor** per source in `NotificationCursors`, reads events with `EventNumber` greater than the cursor, turns the ones it knows into notifications, and advances the cursor in the same save. A crash between reading and advancing is safe: the unique `SourceKey` makes a repeat harmless.
- A new event kind or a new producer is registered in the center (a small mapping: source, kind, recipient rule, priority, text key). The producer's code does not change.
- Delivery is therefore **eventually consistent**: a notification appears a few seconds after the event, not inside the producer's own transaction. A producer's transaction never depends on the notification center and cannot be broken or slowed by it.
- On first run the consumer can **back-fill** from the start of a source's history, so events recorded before this story existed still produce notifications if the owner wants them (decision D8).

**First producer: `STORY-010`.** Its `DocumentEvents` table (design in `docs/DOCUMENTS.md`) already records `ImportedAsDuplicate` with the new document id, the duplicated document id, the patient, the user and the time. The rule registered here: one notification per enabled user holding `ReceiveDocumentNotifications` (admin and office manager), priority Important, kind `document.duplicate_added`, linking to the patient's Documents tab.

Other natural producers, listed for the reviewer's planning (not built by this story): a failed or overdue backup (today the local-folder notifier), follow-up tasks and reminders (`STORY-009`, `ALV-009-C01`), laboratory and referral updates (`ALV-009-C02`), a medical clearance that is received (`ALV-N011`).

### 3.4 Person-to-person messages

- **Compose:** to one or more named users (an active account), a priority (Normal, Important, Urgent), an optional subject, a plain-text body of up to 2000 characters, and an optional link to a patient. The client receives a message-scoped navigation link rather than a raw patient id; selecting it opens the patient only after the server confirms the recipient may view that patient's record.
- **Inbox and sent list**, newest first, paged. A **reply** keeps the thread together.
- **Read receipts:** the sender sees whether and when each recipient read the message (decision D4).
- **Rules:** plain text only (rendered escaped, never as markup); no attachments; a rate limit per sender (for example 30 messages a minute); a disabled account cannot be addressed; sending to oneself is allowed.
- **Delete:** a recipient deleting a notification removes only their own entry; the message, the sender's copy and the audit entry remain. What may be deleted, and how long messages are kept, is the owner's decision (D2, D3).

### 3.5 Priority and the banner

| Priority | Bell and badge | Banner |
|---|---|---|
| Normal | yes | never |
| Important | yes | yes, until dismissed or read |
| Urgent | yes | yes, visibly stronger, until dismissed or read |

The banner is a status banner (not a popup), announced to screen readers, using the existing banner styling and colours. It names no patient, no file and no message text (for example "1 urgent notification"). It appears when the application shell loads (so at sign-in) and again every 60 seconds and whenever the browser tab regains focus (so while the person is already signed in). It can be **dismissed for the current session**; it returns at the next sign-in, or at once when a newer Important or Urgent notification arrives. Who may send Urgent is decision D3, so that Urgent does not become noise.

### 3.6 API (all under the existing conventions: CSRF on writes, row versions, stable `error` codes)

| Route | Purpose |
|---|---|
| `GET /api/notifications/summary` | Own unread count, unread Important-or-Urgent count, newest such time (bell and banner). |
| `GET /api/notifications?status=unread` or `?status=history` | Own notifications, newest first, paged. |
| `POST /api/notifications/{id}/read`, `POST /api/notifications/read-all` | Mark read; repeating changes nothing. |
| `DELETE /api/notifications/{id}`, `DELETE /api/notifications/history` | Delete one read notification or clear the history; an unread one cannot be deleted (`notification_unread`). Another person's id is a `404`. |
| `POST /api/messages` | Send (needs an `Idempotency-Key`). |
| `GET /api/messages/{id}`, `GET /api/messages?box=sent` | Read a message (sender or recipient only); the sent list with read receipts. |
| `GET /api/messages/recipients?search=` | Active users who can be addressed (name and role only). |

### 3.7 Screens

- **Bell** in the top right of the header, next to the account controls, for every signed-in person: badge ("9+" above nine), labelled for screen readers, keyboard operable, opening a panel of unread notifications with **Mark all as read** and a link to the history and to Messages.
- **Notification history** page: read notifications with **Delete** on each and **Clear all** (with a confirmation that says only the person's own list is cleared).
- **Messages** page: Inbox, Sent, Compose and the thread view; each refusal is shown beside its field; the compose screen carries a notice that clinical instructions belong in the chart and that messages are not for emergencies.
- **Banner** as in 3.5.

### 3.8 Permissions

| Permission | Proposed holders | Purpose |
|---|---|---|
| (none) | every signed-in person | Read, mark read and delete **own** notifications and messages. |
| `SendMessages` | all staff roles | Compose a message. |
| `SendUrgentMessages` | Dentist, Office manager, Admin (decision D3) | Mark a message Urgent. |
| `ReceiveDocumentNotifications` | Admin, Office manager | Recipient rule for the `STORY-010` duplicate notification. |

These are added to the existing fixed role table. If the admin-grantable permissions proposal is accepted later, these permissions can move into it without a change to this story's contracts.

### 3.9 Security, privacy and audit

- **Messages can carry patient information.** Access is limited to the sender and the recipients; admins do not get a read-all by default (a break-glass reader would be a separate decision, D12).
- **Audit entries and diagnostic logs carry ids and kinds only.** Never a message subject or body, a patient name or a filename. Audit names: `MessageSent`, `MessageRead`, `NotificationsCleared` (count only), and for the producing stories their own.
- **Banners, the bell panel and notification text never contain a patient name or message text.** The panel shows the sender's name and the subject only for a message; a system notification's text is composed on screen from its kind.
- Plain-text bodies, escaped on display; CSRF on every write; per-sender rate limit; length limits; no HTML, no links other than the patient link. Every patient link resolves server-side from message/notification context and opens only after the current user's patient-view permission passes; a refusal discloses no patient details. An unauthenticated or wrong-person request is refused and tested.
- Covered by the existing database backup and restore; no new file storage.

### 3.10 Failure-first answers

- **The consumer fails or stops:** the producer is unaffected; events wait in the producer's history; the cursor resumes where it was; a crash between creating notifications and advancing the cursor creates no duplicate (unique `SourceKey`).
- **The polling request fails:** the bell shows an unobtrusive "could not check" state and keeps the last known count; it never shows a false zero.
- **A recipient account is disabled or deleted:** no notification is created for it; existing ones remain for audit.
- **Sending fails midway:** the message, its recipient rows, its notifications and the audit entry are saved in one transaction, or none are. The same key and request is replayed safely.
- **A very large history:** lists are paged; the summary reads an index, not the list.
- **Concurrency:** read and delete are idempotent; a stale row version is a conflict that changes nothing.
- Not handled: real-time delivery (the center is polled), attachments, mail or SMS, message search, recall of a sent message.

## 4. What `STORY-010` provides, and why it needs no change

`STORY-010` (design note `docs/DOCUMENTS.md`, revised to match this proposal) builds none of the notification center. For the duplicate-document case it provides, and guarantees, only this:

1. `DocumentEvents` rows of kind `ImportedAsDuplicate` carrying `EventNumber` (monotonic, never reused), the patient id, the new document id, the duplicated document id, the user id and the time.
2. Those rows are **immutable** (trigger and guard) and hold **ids and codes only**, so the center can read them without knowing anything else about documents.
3. The `DuplicateOfDocumentId` column on `Documents` and the `DocumentDuplicateImported` audit entry.
4. The permission name `ReceiveDocumentNotifications` is **not** defined by `STORY-010`; this story defines it. `STORY-010` defines only `ImportDocuments`, `ViewDocuments` and `ManageDocumentSettings`.

Everything this story needs from `STORY-010` therefore already exists in `STORY-010`'s own history. Nothing in `STORY-010` is built for the center's sake except a monotonic event number, which is useful for any consumer, including the admin audit view.

## 5. Acceptance mapping and test plan

| Requirement | Evidence |
|---|---|
| Bell and badge for every signed-in user | Component and real-backend tests for each role: badge equals the unread count; keyboard operation; screen-reader label. |
| Unread list; read moves to history | Service and API tests; the panel and history page in a browser walkthrough. |
| Delete one and clear all (own copies only) | Tests that another person's list and the underlying message and audit entry are untouched; an unread item cannot be deleted. |
| Banner only for Important and Urgent, dismissible | Tests: Normal never shows it; Important and Urgent do; present at sign-in and within a minute while signed in; dismissed for the session and back on a newer item; no patient or message text in it. |
| One person writes to another | Send, receive, read, reply, sent list and read receipt, through the real API as two users (dentist to assistant). |
| Duplicate document notifies admin and office manager | With `STORY-010`'s history: a duplicate import produces exactly one Important notification for each of those two people and none for others; running the consumer twice adds none; back-fill behaves as decided. |
| Privacy | No patient name or message text in any audit entry, log, banner or notification record (test searches stored rows and captured logs); a non-participant cannot read a message (404). |
| Atomic send; consumer safety | Fault-injection tests: failing audit write, failing recipient write, consumer killed between creating notifications and advancing the cursor. |

Required regression: the full serial backend run, the full frontend run, the real-backend walkthroughs of the shell (bell, banner, history, messages), `STORY-010`'s own tests unchanged, and the shell's accessibility scans in both themes.

## 6. Alternatives considered and rejected

- **Building the notification center inside `STORY-010`.** Rejected: it more than doubles that story and ties the document feature to messaging policy decisions that are the owner's.
- **A shared "notice" table with one acknowledgement for everyone.** Rejected: the owner wants a per-person inbox and history.
- **E-mail or SMS delivery.** Rejected for now: it adds an external dependency and an outbound-PHI risk to a system designed to run without internet access. It can be added later as another delivery channel on the same notifications.
- **A push channel (WebSocket or server-sent events).** Deferred: polling every 60 seconds meets the stated need with no new infrastructure; push can replace the poll later without changing the data model.
- **Reusing the backup notifier.** Rejected: it writes to a local folder no staff member sees.
- **Creating the notification inside the producer's transaction.** Rejected: it would make every producer depend on the center and force a change to `STORY-010`.

## 7. Risks and open questions

- **Clinical content in free-text messages** is a privacy and record-keeping question (retention, whether it is part of the patient record, whether staff may delete it). Technical controls cannot settle it (D2, D3, D12).
- **Alert fatigue:** Urgent and the banner lose meaning if overused (D3).
- **Eventually consistent system notifications** arrive seconds after the event, not instantly.
- **Volume:** a busy practice could generate many notifications; paging, the history clear and a possible retention rule (D2) address it.
- **Messages are not an emergency channel;** the compose screen says so.

## 8. Decisions requested from the reviewer and owner

- **D1.** Story identifier and position. **Approved:** `ALV-N015`, after `ALV-010-C01` and **before `STORY-009`**, because it consumes the document event seam and follow-up tasks/reminders become natural later producers.
- **D2.** Retention and deletion of messages. **Approved:** a recipient may hide only their own notification entry; the message and sender copy remain immutable until a formal retention policy is adopted. Admin may inspect notification-history metadata, including recipient-hidden entries.
- **D3.** Who may send Urgent. **Approved:** every staff member holding `SendMessages` may select Urgent. Immutable audit accountability and the per-sender rate limit address misuse; no separate `SendUrgentMessages` permission exists.
- **D4.** Read receipts visible to the sender. Recommended: yes.
- **D5.** Recipients: named individuals only, or also "all assistants" groups. Recommended: named individuals in this story.
- **D6.** Delivery of system notifications by a background consumer (eventually consistent) rather than inside the producer's transaction. Recommended: yes.
- **D7.** Polling every 60 seconds as the first delivery mechanism, with push deferred. Recommended: yes.
- **D8.** Back-fill historical events on the first run, or start from "now". **Approved:** start from now. Back-fill is deferred; no administrative back-fill action is part of this story.
- **D9.** Priority of the duplicate-document notification. Recommended: Important.
- **D10.** Whether the person who caused an event also receives its notification. Recommended: yes for system notifications (the rule is "everyone holding the permission"); not applicable to messages.
- **D11.** Relationship to the admin-grantable permissions proposal: schedule before, after or independently. Recommended: independently; this story's permissions are defined to move into it later.
- **D12.** Whether an administrator may read other people's messages (break-glass) for compliance. **Approved with change:** an Admin may inspect one selected message or thread only through a reason-required, confirmation-required, fully audited break-glass action. There is no default read-all mailbox, bulk export, or message content in audit/diagnostic logs.

## 8A. Approved privacy boundary for administration

An Admin may review notification-history metadata, including entries a recipient hid, without seeing a message subject/body or an unrelated patient identifier. For a content investigation, Admin-only break-glass access is limited to one chosen message or thread. It requires a written reason and confirmation and records attempted and successful access immutably. The audit records identities, object identifiers, reason, time and outcome only; it never reproduces the message content. This is an operational-record control, not a clinical-chart reader.

## 9. Work plan and packaging

Small, reversible steps: schema and triggers with their tests; the notification service and consumer with the first producer (`STORY-010` duplicates); the summary and list routes; the bell and panel; the history page; the banner; messages (send, inbox, sent, reply); permissions and audit; accessibility and walkthroughs; the full regression. Packaged for review under `.alveara/HANDOFF_SCHEMA.md` like every `ALV-*` story (`AWAITING_REVIEW`, one ZIP).

## 10. Reviewer response template

| Decision | Reply (APPROVE / APPROVE WITH CHANGE / REJECT) | Notes |
|---|---|---|
| D1 | APPROVE WITH CHANGE | Insert after `ALV-010-C01`, before `STORY-009`; it cannot precede the document producer it consumes. |
| D2 | APPROVE WITH CHANGE | A recipient hides only their own notification entry; messages/sender copies remain immutable pending a formal retention policy. Admin may view notification metadata, including recipient-hidden entries. |
| D3 | APPROVE WITH CHANGE | Any staff member with `SendMessages` may choose Urgent; sender accountability is immutable and audited. |
| D4 | APPROVE | Read receipts are required. |
| D5 | APPROVE | Named active users only. |
| D6 | APPROVE | Durable, eventually consistent consumer; producers remain independent. |
| D7 | APPROVE | 60-second polling and focus refresh; defer push. |
| D8 | APPROVE WITH CHANGE | Start from current high-water mark; back-fill is deferred entirely. |
| D9 | APPROVE | Important. |
| D10 | APPROVE | Evaluate current eligible recipients at consumer processing time; initiating user receives it when eligible. |
| D11 | APPROVE | Independently scheduled; fixed permissions remain valid contracts. |
| D12 | APPROVE WITH CHANGE | Admin-only, one-message/thread break-glass access needs permission, reason, confirmation, and immutable audit; no default mailbox or bulk access. |

---

## Appendix A — Story text in the execution plan's format

**Type:** New production
**Parent:** None
**Dependencies:** `STORY-010` (first producer, through its `DocumentEvents` history), `ALV-N001` (application shell), `ALV-N009` (identity and session UX), `ALV-002-C01` (audit), `ALV-N002` (durable background work), `ALV-N004` (backup coverage)

**Story:** ALV-N015 — Notification Center and Internal Messaging

**User story:** As staff, I want a notification bell and inbox for everything that needs my attention, and the ability to send a note to a colleague, so that important events and messages reach the right person without anyone having to look for them.

**Why this story exists:** The application has no way to tell a person something. Document duplicates, failed backups, follow-up tasks and laboratory updates all need to reach named people, and staff need a quick way to pass a note to one another. A single, privacy-safe center avoids each feature inventing its own.

**Implementation scope**

- Per-person notifications with a kind, a priority (Normal, Important, Urgent), read and deleted state, and related ids; an immutable message store with a sent list, replies and read receipts
- A background consumer that turns immutable, ordered producer events into notifications idempotently, with a cursor per source; the first producer is `STORY-010`'s duplicate-document event, delivered to the admin and the office manager
- Summary, list, read, delete, clear-history and send routes; per-sender rate limit; plain-text bodies with length limits
- Permissions (`SendMessages`, `SendUrgentMessages`, `ReceiveDocumentNotifications`) in the existing role table
- Audit of sends and reads with ids and kinds only

**UI/UX deliverables**

- A bell with an unread badge in the application header for every signed-in person; a panel of unread notifications with Mark all as read
- A notification history page with delete and clear all
- A Messages page: inbox, sent, compose, thread, reply, read receipts
- A dismissible banner for Important and Urgent notifications only, at sign-in and while signed in, naming no patient or message text
- Accessible in both themes: keyboard operable, screen-reader labels, status announcements

**Security, audit, and data-integrity requirements**

- Messages visible only to the sender and recipients; no patient name or message text in audit entries, logs, banners or notification rows; CSRF; rate limits
- Notifications and messages are never physically deleted by the application; deleting removes only the person's own entry
- Message, recipients, notifications and audit saved together or not at all; the consumer is idempotent and resumable

**Failure paths to handle**

- Consumer stopped or crashed; duplicate processing; polling failure; recipient disabled; send failure midway; unauthorized read; very large history; stale concurrent edit; priority misuse

**Acceptance / stop condition**

- Every signed-in role sees its own bell and unread count; read items move to history; delete and clear-all affect only the person's own list
- The banner appears only for Important or Urgent, is dismissible for the session and returns on a newer item
- A dentist can send a note to an assistant who sees it in the bell; the sender sees the read receipt; Urgent is limited to the permitted roles
- A duplicate document import produces exactly one Important notification for each admin and office manager, never twice
- No private content appears in any log, audit entry or banner
- `STORY-010`'s tests pass unchanged

**Required tests**

- Service, API and authorization tests for every route and role; privacy tests that search stored rows and captured logs; fault-injection tests for atomic send and the consumer; real-backend browser walkthroughs of the bell, banner, history and messages; accessibility scans in both themes; the full serial backend run

**Out of scope / future extension**

E-mail, SMS and push delivery; attachments; message search; recall of a sent message; group and real-time chat; an administrator break-glass reader (needs a privacy review).
