# ALV-001-C01 R04 — Test Results

All commands run from the repository at implementation commit `1d15cfd564a6b2432ca0777158e6610a4980b525`.

## Backend — `dotnet test`

```
dotnet test src/Alveara.Api.Tests/Alveara.Api.Tests.csproj
```

**Result: 172 of 172 passing, 0 failed, 0 skipped.**

4 new tests this attempt, all in `AccountServiceMfaTests.cs`:

- `A_correct_step_up_password_cannot_proceed_past_a_concurrent_failure_that_crosses_the_lockout_threshold` — real two-`DbContext` concurrency test (closes `ALV-001-C01-R03-01`).
- `Replaying_a_consumed_TOTP_challenge_writes_a_privacy_safe_rejection_audit_entry_with_no_extra_success_audit` — sequential replay audit assertion (closes `ALV-001-C01-R03-02`, TOTP path).
- `Two_concurrent_submissions_of_the_same_successful_challenge_leave_exactly_one_success_audit_and_one_rejection_audit` — concurrent replay audit assertion, real two-`DbContext` race (closes `ALV-001-C01-R03-02`, concurrent case).
- `Replaying_a_challenge_already_completed_via_a_recovery_code_writes_a_rejection_audit_entry` — recovery-code path audit assertion (closes `ALV-001-C01-R03-02`, recovery path).

Every test from R03 (168) continues to pass, including the full carried-forward STORY-001 regression suite and R01/R02/R03's own new coverage.

## Frontend — `npx vitest run`

**Result: 16 test files, 40 tests, all passing.** Unchanged from R03 — no frontend production code was modified this attempt.

## Frontend — `npx tsc -b`

Clean, zero errors.

## Frontend — `npx oxlint`

Exit code 0 (informational warnings only — `react(set-state-in-effect)` and `react(only-export-components)` on pre-existing files this attempt did not touch; none new).

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

**Result: 6 of 6 passing** (unchanged from R03 — see `DEMO_EVIDENCE.md` for why no new browser test was added this attempt) against a genuinely fresh, migrated database and a real running API process. Raw output: `R04-evidence/artifacts/playwright-real-backend/run-output.txt`.
