# ALV-001-C01 R02 — Acceptance Evidence

R01's 7-item acceptance grouping (`.alveara/handoffs/ALV-001-C01/R01-evidence/ACCEPTANCE_EVIDENCE.md`) all remained PASS at R01 by the reviewer's own assessment (see the R01 review's "Acceptance assessment" table — items 1, 2, 4, 6 fully PASS; items 3, 5, 7, and the UI portion of item 6 were the FAIL/PARTIAL ones this attempt corrects). This document maps each R01 review finding directly to what closes it in R02; it does not re-litigate the items the reviewer already confirmed PASS.

## Finding 01 (MFA re-enrollment lifecycle) → CLOSED

**Evidence:** `AccountServiceMfaTests.Starting_re_enrollment_does_not_disable_the_already_established_factor` — begins a replacement enrollment on an already-MFA-enabled account and proves the ORIGINAL factor still completes a real login afterward, with `MfaSecretProtected` untouched. `Replacing_MFA_requires_the_current_password_and_the_wrong_password_is_rejected` — no password and a wrong password are both rejected before any pending secret is generated for a replacement. `Confirming_a_replacement_atomically_swaps_the_factor_and_recovery_codes_and_rotates_the_security_stamp` — after confirmation, the OLD secret and OLD recovery codes no longer work and the NEW ones do, atomically. `Two_concurrent_confirmations_of_the_same_pending_enrollment_apply_at_most_once` — a real two-`DbContext` race leaves exactly one confirmation applied. Also demonstrated live in the real-backend Playwright suite (`e2e/auth-real-backend.spec.ts`, `MfaSettingsPage`).

## Finding 02 (challenge bound to SecurityStamp) → CLOSED

**Evidence:** Three new tests each prove a specific stamp-rotating operation (explicit session revocation, password reset, role change) invalidates an MFA challenge issued beforehand: `An_MFA_challenge_issued_before_an_explicit_session_revocation_is_rejected_afterward`, `..._before_a_password_reset_is_rejected_afterward`, `..._before_a_role_change_is_rejected_afterward`. All three assert `CompleteMfaChallengeAsync` throws `InvalidOrExpiredMfaChallengeException` for a challenge that was valid at issue time but stale by completion time.

## Finding 03 (proven-cryptography rule) → CLOSED

**Evidence:** `src/Alveara.Api/Alveara.Api.csproj` now references `Otp.NET` 1.4.1; `Totp.cs` delegates secret generation, Base32 encode/decode, and TOTP compute/verify entirely to that library. Interoperability is proven three separate ways: (1) every existing `AccountServiceMfaTests` test still passes against the new wrapper; (2) the R01 handoff's own curl-based demo (independently-computed Python TOTP) is superseded by (3) the real-backend Playwright suite, which computes the confirming code independently via the browser's Web Crypto API (`crypto.subtle`) rather than calling into the app's own code at all — a genuinely independent implementation agreeing with the server proves standards compliance, not self-agreement.

## Finding 04 (MFA/recovery audit + LoginSucceeded timing) → CLOSED

**Evidence:** `Beginning_enrollment_writes_an_MfaEnrollmentStarted_audit_entry` — enrollment start is audited. `Consuming_a_recovery_code_writes_an_MfaRecoveryCodeUsed_audit_entry_and_the_final_LoginSucceeded_only_fires_after_MFA` — this single test directly proves the corrected sequencing: after a password-only login with MFA enabled, `LoginSucceeded` is NOT yet recorded (only `PasswordVerifiedMfaPending` is) — and only after the recovery-code challenge succeeds does exactly one `LoginSucceeded` and one `MfaRecoveryCodeUsed` entry appear. `A_failed_MFA_challenge_writes_an_MfaChallengeFailed_audit_entry` — a wrong code at the challenge stage is itself audited.

## Finding 05 (security-admin UI, session timeout, real-browser evidence) → CLOSED

**Evidence:**
- `MfaSettingsPage.tsx` + `MfaSettingsPage.test.tsx` — MFA enrollment/replacement UI, calling `POST /api/auth/mfa/enroll` and `/mfa/confirm`, previously entirely absent.
- `PermissionMatrixPage.tsx` + `.test.tsx` — permission-visibility UI, calling `GET /api/auth/permission-matrix`, previously entirely absent.
- `AdminUserDetailPage.tsx` + `.test.tsx` — the user-detail workflow the review found missing, including the new, validated (5–1440 minutes), audited session-timeout control (`AccountService.SetSessionTimeoutAsync`, `PUT /api/auth/{userId}/session-timeout`) — previously there was no authorized way to configure `SessionTimeoutMinutes` at all despite the field existing and being enforced.
- `e2e/auth-real-backend.spec.ts` (5 tests, all passing) — the actual rendered application, in a real Chromium browser, against a real running API and a real migrated database, with zero request mocking. Directly satisfies "exercise the actual rendered application in a real browser against the real API/database" in a way R01's component tests and the rest of `e2e/`'s mocked specs did not. See `DEMO_EVIDENCE.md` and `artifacts/playwright-real-backend/run-output.txt`.

## Summary

**5 of 5 R01 findings CLOSED**, each with a specific, currently-passing automated test (and, for finding 05, additional genuine real-browser/real-backend evidence). Combined with R01's already-PASS items, all 7 of the original 7-item acceptance grouping now PASS without qualification.
