# ALV-001-C01 R06 — Acceptance Evidence

All 7 items of the original acceptance grouping remained PASS at R05 per the R05 reviewer's own "R04 correction assessment" and independent verification tables — this attempt's two findings were evidence gaps in R04's/R05's own concurrency tests, explicitly noted by the reviewer as not implicating the production code itself ("the production changes for both findings are consistent with the required design on source inspection"). This document maps each R05 finding directly to what closes it.

## Finding 01 (step-up race test relies on uncontrolled scheduling) → CLOSED

**Evidence:**
- `AccountServiceMfaTests.A_correct_step_up_deterministically_loses_when_the_fifth_failure_locks_the_account_first` — uses the new `TestHook_BeforeStepUpConditionalReset` seam to pause the correct step-up immediately before its conditional reset, let the fifth failing login run to completion and lock the account, then resume and prove the stale-success rejection branch is genuinely reached (zero rows affected, `AccountLockedOutException`, no pending MFA material, `FailedLoginAttempts == 5`).
- `AccountServiceMfaTests.A_correct_step_up_that_completes_first_resets_the_counter_and_a_later_failure_is_recorded_fresh` — proves the other ordering purely sequentially (deterministic by construction, no seam needed), asserting the pending set exists and the later failure is recorded fresh on the reset counter.
- No production behavior change was needed - the R04 row-count check was already correct; the gap was purely in test coordination.

## Finding 02 (recovery-code rollback tests can pass without executing the rollback branch) → CLOSED

**Evidence:**
- `Two_distinct_challenges_racing_the_same_recovery_code_deterministically_leave_one_success_and_the_loser_rolled_back` — uses the new `TestHook_AfterRecoveryCodeVerifiedBeforeConsumption` seam to pause the loser immediately after it verifies the shared code (before either request has consumed anything), let the winner complete fully, then resume the loser and prove its recovery-code conditional update provably returns zero rows, entering the rollback branch. Confirms exactly one success, one `MfaChallengeFailed`, one consumed recovery code, and that the loser's challenge remains genuinely completable afterward with a different code.
- `A_failure_between_both_conditional_updates_and_commit_rolls_back_both_challenge_and_recovery_state` — uses the new `TestHook_AfterRecoveryCodeConsumedBeforeCommit` seam to inject a controlled failure after both conditional updates succeed but before commit, proving the rollback restores both the challenge and the recovery code together, and that the challenge remains functional afterward.
- The R05 production fix (rollback on zero rows) required no further change - the gap was purely in test coordination.

## Summary

**2 of 2 R05 findings CLOSED**, both by adding deterministic test coordination (three narrow, test-only, no-op-in-production seams) that provably forces each critical branch to execute, rather than by changing production behavior. Combined with R01–R05's already-PASS items, all 7 of the original acceptance grouping continue to PASS without qualification.
