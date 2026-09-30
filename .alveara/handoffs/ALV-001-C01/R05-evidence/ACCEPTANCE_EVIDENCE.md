# ALV-001-C01 R05 — Acceptance Evidence

All 7 items of the original acceptance grouping remained PASS at R04 per the R04 reviewer's own "R03 correction assessment" and independent verification tables — this attempt's two findings were an evidence gap in R04's own test (not a production-code defect) and a narrower recreation of R03's unaudited-consumption window for a specific race shape R04's fix didn't cover. This document maps each R04 finding directly to what closes it.

## Finding 01 (step-up race test never reaches lockout threshold) → CLOSED

**Evidence:**
- `AccountServiceMfaTests.A_correct_step_up_password_cannot_proceed_past_a_concurrent_failure_that_crosses_the_lockout_threshold` — corrected in place: primes exactly 4 failed attempts (asserted directly) and races the correct step-up against the genuine 5th failure. Asserts the full outcome-specific invariants for both permitted orderings: exact `FailedLoginAttempts`, `LockedOutUntilUtc`, pending-secret presence, and pending-recovery-code count. Ran 3 consecutive times locally with no flake.
- No production code change was needed for this finding — the R04 row-count check itself was already correct; only the test's threshold math was wrong.

## Finding 02 (two-challenges-one-recovery-code race leaves an unaudited consumed challenge) → CLOSED

**Evidence:**
- `Two_distinct_challenges_racing_the_same_recovery_code_leave_exactly_one_success_and_the_losing_challenge_unconsumed` — a real two-`DbContext` race between two independently-issued challenges submitting the same recovery code. Asserts exactly one success (`MfaRecoveryCodeUsed`/`LoginSucceeded`), exactly one `MfaChallengeFailed`, exactly one consumed recovery code, and exactly one consumed / one unconsumed `MfaChallenge` row afterward.
- `When_a_recovery_code_race_is_lost_the_losing_challenge_can_still_be_completed_afterward_with_a_different_code` — a deterministic reproduction of the losing branch, proving the rolled-back challenge is genuinely still completable with a different valid code.
- Code change: `CompleteMfaChallengeAsync`'s recovery-code branch now rolls back (rather than commits) the transaction when the recovery-code conditional update affects zero rows, undoing that attempt's own challenge consumption along with the no-op code update.

## Summary

**2 of 2 R04 findings CLOSED** — one by correcting a test's threshold arithmetic (no production defect), one by a genuine production fix (transactional rollback) with a real two-`DbContext` race test plus a deterministic reproduction. Combined with R01–R04's already-PASS items, all 7 of the original acceptance grouping continue to PASS without qualification.
