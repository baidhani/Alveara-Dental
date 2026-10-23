# ALV-N015 — Independent Review R01, Stage 1

**Decision:** CHANGES_REQUIRED  
**Reviewed:** 2026-10-23  
**Handoff:** `Alveara_Handoff_ALV-N015_263fb66_R01.zip`  
**Package SHA-256:** `CE3C77B2BBD45D8C61C02984876CA363C3C47B931765095220DFBBE675A15744`  
**Implementation reviewed:** `263fb66adfaac722ced34804dee5f2b1b123411f`  
**Evidence reviewed:** `64422661c31f4ccf91bc8b0a2827246efc31eaaf`

## Review basis

The ZIP is structurally valid and identifies itself as an R01 Stage 1 package with the required full-regression state `PENDING`. I independently inspected the package, the implementation commits, and the live repository. The focused evidence is substantial: 156 focused backend tests, 1,553 Vitest tests, 140 mocked Playwright tests, 13 repository checks, and the real-backend walkthroughs. Those results do not cure the privacy defect below because the implementation and its test explicitly permit it.

## Required correction

### ALV-N015-R01-01 — Notification-list payload exposes the message subject

**Severity:** blocking privacy/acceptance failure

The authoritative Execution Plan item 42 requires: “No message text, **subject**, patient name, filename, or raw patient identifier appears in audit entries, diagnostic logs, banners, **or notification-list payloads**.”

The submitted `GET /api/notifications` response violates that contract:

- `src/Alveara.Api/Architecture/Notifications/NotificationViews.cs` declares `NotificationView(..., string? Subject, ...)`.
- `src/Alveara.Api/Architecture/Notifications/NotificationService.cs` reads `m.Subject` and emits it in every message-notification list item.
- `src/alveara-client/src/pages/notifications/NotificationsPage.tsx` renders `n.subject` in the notification list.
- `NotificationApiTests.A_message_reaches_its_named_recipient_and_the_sender_sees_when_it_was_read` positively asserts that `/api/notifications?status=unread` returns `"Room 3"` as `subject`.

A message subject is message content and can easily contain patient or other sensitive information. A recipient may see it after opening the message; it must not be delivered in the notification-list API response.

## R02 scope and proof required

1. Remove `Subject` from the notification-list DTO and from every `GET /api/notifications` payload. Do not substitute `null`; the property itself must be absent.
2. Change the notification list UI to use the existing generic kind label (for example, “New message”), optionally retaining the allowed sender name, priority, timestamp, and the Open message action.
3. Preserve full subject/body delivery only on the authorized message-detail/thread routes and the audited break-glass route.
4. Replace the test that expects the subject in the notification list with a privacy test that sends a distinctive subject and asserts the raw JSON notification-list response contains neither the `subject` property nor that value. Cover unread and history if the same DTO serves both. Keep the existing audit/log/banner privacy assertions.
5. Update `docs/NOTIFICATIONS.md`, acceptance evidence, changed-files list, and the Stage 1 handoff to describe the corrected boundary. The older design proposal’s panel wording is superseded by the binding item-42 acceptance condition.
6. Run the changed focused backend/API and frontend tests, the relevant notification real-backend walkthrough, and the normal Stage 1 client/repository gates needed for the changed surface. Package a new Stage 1 attempt with `fullRegression: "PENDING"`.

Do **not** run the frozen full Stage 2 regression for R01. It is not an approved implementation. A new Stage 2 contract may be submitted only after an `APPROVED_PENDING_REGRESSION` decision on the corrected Stage 1 attempt.
