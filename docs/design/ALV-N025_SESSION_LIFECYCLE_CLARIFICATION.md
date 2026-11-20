# ALV-N025 — Session-lifecycle clarification (sign-in write; for independent review)

**Status:** proposed, documents only. **The sign-in write of the session lifecycle is not implemented before an independent review approves this clarification** (BRIEFING-R03 stop rule: "Any design change affecting the ... audit-session lifecycle ... requires independent review before implementation of that change"; owner decision SES-1, 2026-11-20).
**Amends:** `ALV-N025_PRE_IMPLEMENTATION_BRIEF.md` (revision 3, approved in BRIEFING-R03), section 4, "Session lifecycle record (BR-01; audit-only)", first bullet. Nothing else in the lifecycle changes.

## 1. The contradiction

Section 4 says two things about the sign-in write that cannot both hold:

1. "At sign-in a row is written to a new table `AuditSessions` ... It is written **in the same save as `LoginSucceeded`**."
2. "A missing or failed lifecycle write at sign-in is logged (`audit_session_write_failed`) and **does not block sign-in**."

If the row is in the same save as `LoginSucceeded`, a failure of that save also fails the `LoginSucceeded` write, and so fails the sign-in. Also, in the code `LoginSucceeded` is saved inside `AccountService.LoginAsync` (and its MFA variants), before the controller issues the cookie. The cookie's fixed deadline (`ExpiresUtc = now + SessionTimeoutMinutes`) is computed only when the cookie is issued, so the row could not carry "the same fixed deadline the cookie ticket carries" from that earlier save.

## 2. The clarification (owner decision SES-1, quoted)

> Separate save, non-blocking. This preserves the approved behavior that audit-lifecycle tracking must not prevent a clinician from signing in. Calculate one fixed deadline and one sid; use those exact values for both the cookie and the AuditSessions row. If the lifecycle-row save fails, log audit_session_write_failed without PHI and let sign-in continue. That session has no lifecycle/sign-out event, as disclosed.

**Replacement for the first bullet of section 4's lifecycle record:**

- **At sign-in** (every place a cookie is issued, which today is `AuthController.SignInAsync`: password sign-in, the MFA challenge, the MFA recovery code and the cookie re-issue after MFA enrollment):
  - one session id (`sid`, a random GUID) and one fixed deadline (`ExpiresAtUtc = now + SessionTimeoutMinutes`) are computed **once**;
  - the same two values go into the cookie (the `sid` claim and the ticket's `ExpiresUtc`) and into a new `AuditSessions` row (`SessionId`, `UserId`, `IssuedAtUtc`, `ExpiresAtUtc`; `EndedAtUtc`, `EndKind` null; `EndAuditWritten` false);
  - the row is written in **its own save, after `LoginSucceeded`** has been saved by the account service, and before the cookie is issued.
- **If that save fails,** the failure is logged as `audit_session_write_failed`, with the error class, the user id and the correlation id, and no PHI. Sign-in continues and the cookie is still issued with its `sid`. That session has no lifecycle row, so its sign-out, expiry or revocation writes no lifecycle event (the conditional update finds no row). The audit page's help text discloses this.
- **Order and consistency:** `LoginSucceeded` is always written, for every successful sign-in, as today. A successful sign-in with a failed lifecycle row is the only case without a row. A lifecycle row without a successful sign-in cannot happen, because the row is written after the account service has accepted the credentials.

## 3. What stays as approved

Everything else in section 4 is unchanged:
- **Sign-out:** an explicit sign-out makes one conditional update `WHERE SessionId = @sid AND EndedAtUtc IS NULL`, and writes `UserSignedOut` only if a row changed.
- **Expiry:** a processor runs every minute, in batches of at most 500; it sets `EndedAtUtc = ExpiresAtUtc`, `EndKind = 'Expired'`, and writes `SessionExpired` only if a row changed.
- **Revocation:** when the security stamp rotates, `Revoked` plus one `SessionRevoked` per open row, in the same save.
- **Guard 51301:** a closed row and the identifying columns never change, and no row is deleted.
- **Not a session store:** nothing reads `AuditSessions` to accept or refuse a request.
- **Retention:** the same as the audit log.
- **The cookie-reissue case:** confirming MFA enrollment rotates the security stamp. This is today's behaviour, and it invalidates every other session. By the approved revocation rule, it also closes the caller's current lifecycle row as `Revoked`. The re-issued cookie then starts a new row with a new `sid`, so a session is never reported as expired while its holder is still signed in under a re-issued cookie.

## 4. Tests added (to section 8's `AuditSessionLifecycleTests`)

1. **One sid, one deadline:** after sign-in, the cookie's `sid` equals the row's `SessionId`, and the ticket's `ExpiresUtc` equals the row's `ExpiresAtUtc` (to the tick).
2. **A failed lifecycle write does not block sign-in:**
   - with the `AuditSessions` insert made to fail (a test-only fault in the save), sign-in still succeeds;
   - `LoginSucceeded` is written;
   - `audit_session_write_failed` is logged without PHI;
   - no row exists, and a later sign-out with that cookie writes no `UserSignedOut`.
3. **The MFA re-issue:** the old row is closed as `Revoked`, with one `SessionRevoked`, and a new row starts with a new `sid`.

## 5. Owner decisions recorded with this clarification (not part of the reviewed change)

These follow the approved scope and are listed for completeness (decision record, 2026-11-20):

- **EXP-2:** exports keep the three existing per-patient events (`PatientExported`, `PatientDataPackageExported`, `PatientReadableRecordExported`). "PatientRecordExported" is the conceptual category, not a new stored name, and there are no duplicate rows. A focused test proves each path writes exactly one event.
- **DOC-1:** `PatientDocumentDownloaded` (entity = the patient) is written **only when a document is served as a download or attachment**. Inline viewing keeps the existing `DocumentOpened`.
- **SES-2:** a cookie issued before this release (no `sid`) records each record open as `PatientRecordOpened`, without a `PatientRecordOpens` row (so no once-per-session dedupe), and its sign-out writes nothing. This is temporary, because such cookies expire within the session timeout. A focused test proves the path.
