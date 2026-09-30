# ALV-001-C01 R04 — Acceptance Evidence

All 7 items of the original acceptance grouping remained PASS at R03 per the R03 reviewer's own "R02 correction assessment" and independent verification tables — this attempt's two findings were newly-discovered concurrency/atomicity gaps in the R02-01/R02-02 corrections, not regressions of already-PASS items. This document maps each R03 finding directly to what closes it.

## Finding 01 (correct step-up can proceed after a concurrent lockout race) → CLOSED

**Evidence:**
- `AccountServiceMfaTests.A_correct_step_up_password_cannot_proceed_past_a_concurrent_failure_that_crosses_the_lockout_threshold` — a real two-`DbContext` concurrency test that races a correct-password step-up against the wrong-password login that crosses the shared lockout threshold. Asserts only the two permitted outcomes: the step-up completed before the lock (and a pending MFA secret now exists), or the lock won the race (`AccountLockedOutException`, and no pending MFA secret was created).
- Code change: `BeginMfaEnrollmentAsync`'s correct-step-up-password branch now checks the affected-row count of its conditional lockout-counter reset and rejects with `AccountLockedOutException` on zero rows affected, exactly mirroring `LoginAsync`'s own success path.

## Finding 02 (challenge consumption not atomic with audit; replay rejection unaudited) → CLOSED

**Evidence:**
- `Replaying_a_consumed_TOTP_challenge_writes_a_privacy_safe_rejection_audit_entry_with_no_extra_success_audit` — sequential replay; asserts exactly one success audit pair and exactly one `MfaChallengeReplayRejected` entry, and that the rejection's audit details never contain the token or code.
- `Two_concurrent_submissions_of_the_same_successful_challenge_leave_exactly_one_success_audit_and_one_rejection_audit` — the same assertions against two real racing `DbContext`s (concurrent replay).
- `Replaying_a_challenge_already_completed_via_a_recovery_code_writes_a_rejection_audit_entry` — recovery-code path; also confirms the second, unused recovery code is never consumed by the rejected replay.
- Code change: `CompleteMfaChallengeAsync` now opens its transaction before calling `TryConsumeChallengeAsync`, so conditional challenge consumption participates in the same transaction as the subsequent audit write (success or rejection) on both the TOTP and recovery-code paths. A rejected consumption now writes a new, privacy-safe `MfaChallengeReplayRejected` audit event.

## Finding 03 (altered review artifact) → CLOSED

**Evidence:** `.alveara/reviews/ALV-001-C01/R02.md` restored byte-for-byte (SHA-256 `F2CA40BAC763C7CA73736A5A7B8B9308BBB1B3E59C0036A5E80902428357E9E4`, matching the reviewer's stated hash) via a raw file copy, recorded in commit `4a4c18c` preceding this attempt's implementation commit. See `GIT_STATE.md`.

## Summary

**3 of 3 R03 findings CLOSED**, each with specific, currently-passing automated tests (the two P1 findings) or a verified byte-for-byte restoration (the P2 finding). Combined with R01/R02/R03's already-PASS items, all 7 of the original acceptance grouping continue to PASS without qualification.
