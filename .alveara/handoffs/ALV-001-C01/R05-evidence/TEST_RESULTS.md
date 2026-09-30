# ALV-001-C01 R05 — Test Results

All commands run from the repository at implementation commit `9fd920abc551942089807d10c598695d76635d4b`.

## Backend — `dotnet test`

```
dotnet test src/Alveara.Api.Tests/Alveara.Api.Tests.csproj
```

**Result: 174 of 174 passing, 0 failed, 0 skipped.**

1 test corrected in place, 2 new tests this attempt, all in `AccountServiceMfaTests.cs`:

- `A_correct_step_up_password_cannot_proceed_past_a_concurrent_failure_that_crosses_the_lockout_threshold` (corrected) — now primes exactly 4 failed attempts and races the genuine 5th, with expanded outcome-specific assertions (closes `ALV-001-C01-R04-01`).
- `Two_distinct_challenges_racing_the_same_recovery_code_leave_exactly_one_success_and_the_losing_challenge_unconsumed` (new) — real two-`DbContext` race between two distinct challenges on one recovery code (closes `ALV-001-C01-R04-02`).
- `When_a_recovery_code_race_is_lost_the_losing_challenge_can_still_be_completed_afterward_with_a_different_code` (new) — deterministic reproduction of the losing branch, proving genuine rollback.

The two race-condition tests (the corrected step-up test and the new recovery-code race test) were each run 3 consecutive times in isolation to confirm no flake before inclusion in the full suite run reported here.

Every test from R04 (172) continues to pass, including the full carried-forward STORY-001 regression suite and R01–R04's own new coverage.

## Frontend — `npx vitest run`

**Result: 16 test files, 40 tests, all passing.** Unchanged from R04 — no frontend production code was modified this attempt.

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

**Result: 6 of 6 passing** (unchanged from R04 — see `DEMO_EVIDENCE.md` for why no new browser test was added this attempt) against a genuinely fresh, migrated database and a real running API process. Raw output: `R05-evidence/artifacts/playwright-real-backend/run-output.txt`.
