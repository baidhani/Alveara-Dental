# ALV-001-C01 R03 — Acceptance Evidence

All 7 items of the original acceptance grouping remained PASS at R02 per the R02 reviewer's own "R01 correction assessment" and independent verification tables — this attempt's two findings were newly-discovered gaps in the R01-02 correction and the R01-05 UI addition, not regressions of already-PASS items. This document maps each R02 finding directly to what closes it.

## Finding 01 (MFA challenge/TOTP replay) → CLOSED

**Evidence:**
- `AccountServiceMfaTests.Replaying_the_exact_same_successful_TOTP_challenge_submission_is_rejected` — completes a challenge successfully, then replays the identical challenge-token/code and asserts `InvalidOrExpiredMfaChallengeException`.
- `Two_concurrent_submissions_of_the_same_successful_challenge_produce_exactly_one_success` — two real, concurrent `CompleteMfaChallengeAsync` calls (separate `DbContext`s) against the same challenge; asserts exactly one succeeds and the other is rejected.
- `Replaying_a_challenge_already_completed_via_a_recovery_code_is_rejected` — proves the *challenge* is spent, not just the individual recovery code: a second, still-unused recovery code cannot complete an already-consumed challenge.
- `An_expired_challenge_cannot_be_completed_even_with_the_correct_code` / `A_revoked_challenge_row_cannot_be_completed` — prove the durable `MfaChallenge` row's own expiry/consumption gates independently of the token's embedded claims.
- **Real-backend Playwright:** `replaying the exact same successful MFA challenge submission is rejected, not accepted twice` — reproduces the R02 reviewer's own live HTTP-level reproduction verbatim (identical `{challengeToken, code}` body submitted twice to the real running API) and asserts the second submission is not `200`.

## Finding 02 (step-up password not throttled/lockout-coupled) → CLOSED

**Evidence:**
- `Repeated_wrong_current_passwords_during_MFA_replacement_lock_the_account_like_a_failed_login_would` — 5 wrong step-up passwords lock the account exactly as 5 wrong login passwords would; a subsequent correct-password *login* is then also rejected, proving the lockout is real and shared, not endpoint-local.
- `Failed_login_attempts_and_failed_MFA_step_up_attempts_share_the_same_lockout_counter` — mixes 2 wrong logins and 3 wrong step-ups; the 5th failure (from either endpoint) crosses the shared threshold.
- `A_currently_locked_account_cannot_use_MFA_replacement_step_up_either` — a locked account is rejected from the step-up endpoint even with the *correct* password.
- `A_correct_step_up_password_resets_the_shared_failed_attempt_counter` — mirrors login's own reset-on-success behavior.
- `AuthRateLimitTests.Repeated_MFA_replacement_step_up_attempts_past_the_limit_are_also_throttled` — an HTTP-level test proving `POST /api/auth/mfa/enroll` is now covered by the same IP-based `AuthAttempts` rate limiter as every other auth-attempt endpoint.

## Summary

**2 of 2 R02 findings CLOSED**, each with specific, currently-passing automated tests at both the service level and (for finding 01) genuine real-backend/real-browser evidence directly reproducing the reviewer's own reported reproduction. Combined with R01/R02's already-PASS items, all 7 of the original acceptance grouping now PASS without qualification.
