# ALV-N025 Session-Lifecycle Clarification R01 — Independent Review

**Decision:** APPROVED

**Reviewed package:** `Alveara_Briefing_ALV-N025_efc8358_SESSION-CLARIFICATION-R01.zip`  
**Package SHA-256:** `B2DDDD038E2FDF3E6024831A449946D4EEFC26178A1BBD4A19D727829DA0EB6D`  
**Scope:** audit-session sign-in write clarification (SES-1) only.

## Decision

The clarification resolves the former contradiction without weakening the fixed cookie deadline or making audit-session state an authorization dependency:

- Compute one `sid` and one fixed expiry deadline once, and place those same values in the cookie and the `AuditSessions` row.
- Write `LoginSucceeded` through the existing successful-login path, then write the lifecycle row in its own audit-only save before issuing the cookie.
- If that save fails, log `audit_session_write_failed` without PHI and issue the cookie anyway. The missing lifecycle row deliberately means no lifecycle sign-out, expiry, or revocation event for that one session; the help text must disclose this limitation.
- Preserve the specified focused proofs for exact sid/deadline equality, non-blocking lifecycle-write failure, and MFA cookie re-issue/revocation behavior.

The recorded export, document-download, and legacy-cookie decisions are consistent with the approved PD-20 scope: existing export events are not duplicated; patient-level document-download events occur for downloads/attachments only; and pre-release cookies without `sid` record each patient open without deduplication.

## Authorization

Implementation of this clarification may proceed within ALV-N025 using the planned focused tests only. No broad/full regression is authorized.
