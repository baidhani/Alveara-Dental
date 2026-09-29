# ALV-001-C01 R01 — Acceptance Evidence

Grouped into the 7 items recorded on `.alveara/EXECUTION_STATUS.json`'s ledger entry (`acceptance.total = 7`), covering every bullet in the Execution Plan prompt's "Acceptance / stop condition" section.

## 1. STORY-001 regression + full baseline green

**Requirement:** The three original STORY-001 acceptance criteria remain true word for word; the verified 102-of-102 API-test baseline remains green, with all new companion tests also passing.

**Evidence:** `dotnet test` — 148 of 148 passing (102 original + 46 new/revised this attempt). See `TEST_RESULTS.md`. The only STORY-001 tests revised are the ones that encoded the insecure caller-selected-role-assignment behavior (`AccountServiceRegistrationTests`, `AccountServiceRoleChangeTests`, `AccountServiceLoginTests`, `AuthControllerTests`, `AuthControllerRbacTests` — all rewritten to obtain a privileged account via the new bootstrap/admin-administration path instead of role-at-registration), exactly as the prompt's own instruction permits. See `PARENT_REGRESSION.md` for the criterion-by-criterion mapping.

**Status: PASS.**

## 2. Registration role-lockdown + first-admin bootstrap

**Requirement:** An unauthenticated caller cannot select a role, create an `Admin`, or invoke the authorized account/role-administration path. A first administrator can be provisioned exactly once using the protected offline-capable bootstrap flow; invalid, repeated, and concurrent bootstrap attempts fail safely.

**Evidence:**
- `RegisterRequest` has no `Role` field at all (type-level proof: `AuthControllerTests.An_unauthenticated_caller_cannot_select_a_role_at_registration_the_request_shape_has_no_role_field` reflects over the record's properties and asserts `Role` is absent).
- `AccountServiceRegistrationTests.An_unauthenticated_registration_request_cannot_select_a_role_or_produce_an_enabled_account` — every self-registered account is `Role.Unassigned` + `IsDisabled = true`.
- Every admin/role-administration endpoint (`users`, `{userId}/role`, `{userId}/enabled`, `{userId}/reset-password`, `{userId}/revoke-sessions`, `permission-matrix`) is `[Authorize]` + `[RequirePermission(...)]`; `AuthControllerRbacTests.PUT_role_as_a_non_admin_returns_403` and `AuthControllerPermissionMatrixTests` prove both the allowed and denied paths at the HTTP level.
- `AccountServiceBootstrapTests` (correct-secret success / wrong-secret rejection-creates-no-account / second-use-rejected / concurrent-race-leaves-exactly-one-admin) and `AuthControllerTests` (`POST_bootstrap_admin_with_the_correct_secret_creates_an_enabled_admin`, `..._with_the_wrong_secret_returns_401`, `..._a_second_time_returns_409`) — bootstrap is genuinely one-time, secret-protected, and safe under a real concurrent race (two simultaneous `BootstrapFirstAdminAsync` calls against separate `DbContext`s, proven to leave exactly one admin row via the database's own PK uniqueness on `BootstrapState`, not an application check-then-act).

**Status: PASS.**

## 3. MFA enrollment/challenge with no public internet

**Requirement:** A configured MFA path can be enrolled and challenged. At least one MFA/recovery path is demonstrated successfully with public internet unavailable.

**Evidence:** `AccountServiceMfaTests` — full enrollment→confirm→login-pauses-for-challenge→TOTP-completes-login flow, and a parallel recovery-code flow, all driven entirely from the Base32 secret `BeginMfaEnrollmentAsync` returns (exactly what a real authenticator app would hold) via `Totp.GenerateCodeForTests`/`FromBase32` — no network call anywhere in `Totp.cs`, `AccountService`'s MFA methods, or the test path itself. This is a structural "no public internet needed" demonstration: the entire flow is provably local-only by inspection of `Totp.cs` (HMAC-SHA1 computed against the .NET BCL only) and the absence of any `HttpClient`/external call in the MFA code path, not merely an assertion that it happened to work while online. `AccountServiceMfaTests.A_recovery_code_completes_the_MFA_challenge_exactly_once_no_public_internet_needed` additionally proves the recovery-code path is one-time.

**Status: PASS.**

## 4. Disabled account + lockout lifecycle correctness

**Requirement:** Disabled user cannot authenticate. Lockout triggers, expires, and rearms correctly; concurrent threshold/rearm races cannot admit a stale successful login or lose failed-attempt state.

**Evidence:**
- `AccountServiceLoginTests.A_disabled_account_cannot_log_in_even_with_the_correct_password` / `AuthControllerRbacTests` disabled-login-returns-403 coverage.
- `Five_consecutive_failed_logins_lock_the_account_and_a_sixth_attempt_with_the_correct_password_is_rejected` — threshold trigger.
- `A_lockout_rearms_after_it_expires_new_failures_trigger_a_new_lockout` — this is the direct fix and proof for the STORY-001 reviewer's P1 finding: an expired-but-still-present lockout timestamp is now treated as "not currently locked," and a fresh failure streak can trigger a genuinely new lockout (simulated by directly setting `LockedOutUntilUtc` to the past rather than sleeping 15 real minutes).
- `Concurrent_failed_logins_against_the_same_account_do_not_lose_increments_to_a_race` — two real concurrent `LoginAsync` calls against separate `DbContext`s, proven via `Task.WhenAll`, leave `FailedLoginAttempts == 2` (SQL Server's own atomic `UPDATE ... SET x = x + 1` under row locking, not a lost read-modify-write).
- `A_correct_password_login_racing_the_failure_that_triggers_lockout_cannot_be_admitted` — the other STORY-001 reviewer P1 finding's direct fix and proof: races the exact failing request that crosses the lockout threshold against a correct-password login and asserts the safety invariant matching whichever legitimate database-serialized ordering occurred (either the correct login genuinely landed first, or it was correctly rejected because the account was already locked — never a stale success silently clearing a real lock).

**Status: PASS.**

## 5. Timing-safe unknown-user login + permission matrix

**Requirement:** Unknown-user and wrong-password attempts produce the same public result and perform equivalent password-KDF work. At least one role-allowed and role-denied action is proven at API/service level.

**Evidence:**
- `AccountServiceLoginTests.Unknown_username_and_wrong_password_perform_the_same_number_of_password_KDF_verifications` — structural proof via `Pbkdf2PasswordHasher.VerifyCallCount` (both paths call `Verify` exactly once), not a flaky wall-clock timing assertion, satisfying the required-tests list's explicit instruction. `AuthControllerTests.POST_login_with_an_unknown_username_and_a_wrong_password_produce_identical_responses_no_enumeration` proves the HTTP response bodies are byte-identical.
- `PermissionMatrixTests` (unit-level, per role) + `AuthControllerPermissionMatrixTests.PUT_role_is_allowed_for_a_role_holding_ManageRoles_and_denied_for_one_that_does_not` (HTTP-level, both branches) — the required "at least one role-allowed and role-denied action... at API/service level," now driven by the named permission matrix rather than a single hardcoded role check.

**Status: PASS.**

## 6. Session timeout, CSRF, cookie security

**Requirement:** Session timeout is enforced. Cookie-authenticated state changes reject missing/invalid CSRF protection, and issued authentication cookies carry the required security attributes.

**Evidence:**
- `AuthControllerRbacTests.A_session_whose_configured_timeout_has_already_elapsed_is_genuinely_rejected` — a session with `SessionTimeoutMinutes = -1` logs in successfully but is rejected on the very next authenticated request.
- `AuthControllerTests.A_state_changing_admin_request_without_a_CSRF_token_is_rejected` — a state-changing admin request with no `X-CSRF-Token` header returns 400.
- `POST_revoke_sessions_invalidates_the_target_s_existing_session_cookie` / `POST_logout_ends_the_session...` — explicit sign-out and server-side revocation both genuinely invalidate the session (SecurityStamp mismatch → `OnValidatePrincipal` rejects the cookie).
- `AuthCookieAttributesTests.A_successful_login_issues_a_session_cookie_with_HttpOnly_and_SameSite_Strict` — inspects the real `Set-Cookie` header from a live login response and asserts both attributes are present (added specifically for this handoff, since the prior attempt had the configuration in `Program.cs` but no test proving it reaches the wire).

**Status: PASS.**

## 7. Audit coverage + STORY-001 blocking-issue closure

**Requirement:** Security administration changes are audited. The three deferred STORY-001 security `blockingIssues` are cleared only after the corresponding tests and demo evidence pass.

**Evidence:**
- Audited event types and their proving tests: `AccountRegistered`/`RoleChanged` (`AccountServiceRoleChangeTests`), `MfaEnabled` (`AccountServiceMfaTests`), `PasswordResetIssued`/`PasswordReset` (`AccountServicePasswordResetTests`), `AccountEnabled`/`AccountDisabled`/`SessionsRevoked` (`AccountServiceAuditTests`, added specifically for this handoff). Every audited write happens in the same database transaction as the change it documents (`AccountService.cs`'s established convention from STORY-001, continued).
- The three `STORY-001` `blockingIssues` map directly to items 2, 4, and 5 above, each closed with the tests cited there. `EXECUTION_STATUS.json`'s `STORY-001` record (`num: 4`) has its `blockingIssues` array cleared to `[]` as part of this evidence commit, with a note recording which `ALV-001-C01` attempt/tests closed each one.

**Status: PASS.**

## Summary

**7 of 7 acceptance items PASS.** No item is marked passed without a specific, named, currently-passing test.
