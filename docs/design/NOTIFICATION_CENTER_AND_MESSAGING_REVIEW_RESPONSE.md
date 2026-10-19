# Notification Center and Internal Messaging — Design Review Response

**Decision:** APPROVED WITH RECORDED DESIGN CHANGES  
**Date:** 2026-10-19  
**Story:** `ALV-N015` — Notification Center and Internal Messaging

## Authorized execution position

Insert `ALV-N015` after `ALV-010-C01` and before `STORY-009`. It depends on the immutable `DocumentEvents` seam delivered by `STORY-010` and must not alter the document story or make document import depend on notification delivery.

## Binding decisions

1. Messages are immutable operational communications, not clinical documentation. Clinical instructions and care decisions belong in the chart.
2. A recipient may hide only their own notification entry. The stored message, sender copy, and notification record remain preserved until a formal retention policy exists.
3. Any staff member with `SendMessages` may choose Normal, Important, or Urgent. Priority use is accountable through immutable audit records and a sender rate limit; do not introduce `SendUrgentMessages`.
4. Sender-visible read receipts are required.
5. Recipients are named active individuals only. Do not add groups in this story.
6. System notification delivery is asynchronous through the durable cursor consumer. Producers remain independent and the consumer is idempotent.
7. Poll every 60 seconds and on browser focus. Defer push delivery.
8. Start each source cursor at the current high-water mark. Historical back-fill and its administrative controls are deferred.
9. A confirmed duplicate-document import creates one Important notification per eligible Admin and Office Manager, including the initiating user when eligible.
10. An Admin may view notification-history metadata, including recipient-hidden entries. This view excludes message subject/body and unrelated patient identifiers.
11. Message content is available to Admin only through a break-glass action for one selected message or thread. Require a dedicated Admin permission, written reason, confirmation, and immutable attempted/successful access audit. Do not provide bulk access, a default read-all mailbox, content export, or message content in audit/diagnostic logs.
12. Every related-patient notification/message offers an authorization-checked, notification/message-scoped link. Selecting it opens the patient record only when the current user has patient-view permission; otherwise it returns a safe refusal with no patient details. Do not expose a raw patient identifier in list payloads solely to construct the link.

## Required implementation boundary

`STORY-010` supplies immutable, ordered `DocumentEvents` only. `ALV-N015` owns notifications, messages, permissions, consumer, routes, bell, banner, history, message UI, and the duplicate-document recipient rule. No notification UI, notification table, or notification permission belongs in `STORY-010`.

The authoritative implementation prompt is now in `Alveara_Dental_Execution_Plan_v4.md`. Do not start implementation until its dependencies are complete and independently closed.
