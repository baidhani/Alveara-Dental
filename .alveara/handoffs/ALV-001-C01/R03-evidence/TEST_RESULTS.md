# ALV-001-C01 R03 — Test Results

All commands run from the repository at implementation commit `3203c05f843f9c5b98968dba5bc9ee3da820cab7`.

## Backend — `dotnet test`

```
dotnet test src/Alveara.Api.Tests/Alveara.Api.Tests.csproj
```

**Result: 168 of 168 passing, 0 failed, 0 skipped.**

10 new tests this attempt:

`AccountServiceMfaTests.cs` (9):
- `Replaying_the_exact_same_successful_TOTP_challenge_submission_is_rejected`
- `Two_concurrent_submissions_of_the_same_successful_challenge_produce_exactly_one_success`
- `Replaying_a_challenge_already_completed_via_a_recovery_code_is_rejected`
- `An_expired_challenge_cannot_be_completed_even_with_the_correct_code`
- `A_revoked_challenge_row_cannot_be_completed`
- `Repeated_wrong_current_passwords_during_MFA_replacement_lock_the_account_like_a_failed_login_would`
- `Failed_login_attempts_and_failed_MFA_step_up_attempts_share_the_same_lockout_counter`
- `A_currently_locked_account_cannot_use_MFA_replacement_step_up_either`
- `A_correct_step_up_password_resets_the_shared_failed_attempt_counter`

`AuthRateLimitTests.cs` (1):
- `Repeated_MFA_replacement_step_up_attempts_past_the_limit_are_also_throttled`

Every test from R02 (158) continues to pass, including the full carried-forward STORY-001 regression suite and R01/R02's own new coverage.

## Frontend — `npx vitest run`

**Result: 16 test files, 40 tests, all passing.** Unchanged from R02 — no frontend production code was modified this attempt (only the real-backend Playwright spec, which vitest doesn't run).

## Frontend — `npx tsc -b`

Clean, zero errors.

## Frontend — `npx oxlint`

Exit code 0. No new warnings.

## Real-backend, real-browser — `npx playwright test --config=playwright.auth.config.ts`

```
cd src/Alveara.Api
sqlcmd -S "(localdb)\MSSQLLocalDB" -Q "ALTER DATABASE AlveraE2E SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE AlveraE2E;"  # fresh DB
dotnet ef database update --connection "Server=(localdb)\MSSQLLocalDB;Database=AlveraE2E;..."
$env:ConnectionStrings__Alveara = "...AlveraE2E..."; $env:AdminBootstrapSecret = "e2e-real-backend-secret"
dotnet run --no-build

# second terminal, from src/alveara-client
$env:E2E_BOOTSTRAP_SECRET = "e2e-real-backend-secret"
npx playwright test --config=playwright.auth.config.ts
```

**Result: 6 of 6 passing** (5 carried from R02 + 1 new: `replaying the exact same successful MFA challenge submission is rejected, not accepted twice`) against a genuinely fresh, migrated database and a real running API process. Raw output: `R03-evidence/artifacts/playwright-real-backend/run-output.txt`.

This new test directly reproduces the R02 reviewer's own live finding: it submits an identical `{challengeToken, code}` body to `POST /api/auth/mfa/challenge` twice via real HTTP requests and asserts the first returns 200 while the second does not.
