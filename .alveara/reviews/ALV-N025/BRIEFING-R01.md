# ALV-N025 Briefing R01 — Independent Review

**Decision:** CHANGES_REQUIRED

**Reviewed package:** `Alveara_Briefing_ALV-N025_0f96874_R01.zip`  
**Package SHA-256:** `B3ABCC7BD40C4D0EDEB29A63D0FD27AA61A6F0B31C4F32A08168E6F2FD123C16`  
**Scope:** pre-implementation briefing only. No code, schema, configuration, or test run is authorized by this decision.

## What is approved in principle

- Retaining the GUID as the internal key and adding an immutable, never-reused bigint MRN generated from UTC whole seconds, with database uniqueness and increment-and-retry collision handling.
- Exact MRN search before the telephone-number search path; MRN display in permitted headers, search results, summaries, and exports; printed document versioning deferred to ALV-N026.
- The selected demographics model, single-file import wording boundary, one-member-household dissolution, actor-name fallback, deliberate duplicate-override reason, and no audit entry for routine blocked duplicate attempts.
- Logging patient access once per patient per session, exports/downloads, explicit sign-out, and in-application print requests—not claims of physical printing and not Ctrl+P/browser-menu printing.

## Required corrections

### BR-01 — Session-expiry event is not feasible as drafted

The current application uses a non-persistent cookie containing a ticket with `ExpiresUtc`, with no server-side session store. Once that ticket has expired, normal cookie authentication fails before the application has a reliable authenticated principal or event hook from which to recover and trust the proposed `sid`. A browser may also stop sending an expired persistent cookie. The proposed wording, “the authentication handler's failure ... with a readable sid,” is therefore not an implementable evidence guarantee.

Revise section 4 to use a durable, audit-only session lifecycle record created at sign-in: session id, user id, issued-at, expires-at, and terminal-state/event fields. A server-owned processor must create `SessionExpired` at the known deadline for sessions that have not ended earlier; explicit sign-out must close that record and prevent an expiry event. This tracking record must not become an authorization/session store or renew the fixed authentication deadline. Specify the handling of an already-closed record, retries/idempotency, and retention consistent with the audit log. Add focused proofs for expiration at the deadline, sign-out suppressing expiry, and exactly-once behavior.

If the implementation cannot safely support this audit-only lifecycle record within ALV-N025, remove the claimed `SessionExpired` event from this story and disclose it for later design. Do not leave an unobservable promise in the audit-page help text.

### BR-02 — Patient-history link requires a permission boundary and proof

F-041 correctly keeps field values out of the PHI-free audit record, but the briefing merely says that an audit row “links” to a patient's History row. It must explicitly state that the audit API returns no old/new values, patient name, or contact detail; it may return an opaque history-entry reference only. Following the link, and any API call that resolves it, must independently require the normal patient-record/history permission. A holder of `ViewAuditLog` without access to that patient must not obtain PHI through the link; the UI should withhold or mark the target unavailable in that case.

Add focused server and client proof for both the authorized path and a `ViewAuditLog`-only user denied the history content. This must cover direct navigation/API attempts, not only the visible link.

## Resubmission boundary

Return a revised **documents-only** briefing addressing BR-01 and BR-02. Do not begin implementation, create migrations, modify source, or run tests before the revised briefing is independently approved.
