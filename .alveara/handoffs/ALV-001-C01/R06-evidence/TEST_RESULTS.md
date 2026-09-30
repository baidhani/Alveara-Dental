# ALV-001-C01 R06 — Test Results

All commands run from the repository at implementation commit `d6fd2810b42e871d690e4032ab27c2603348a47b`.

## Backend — `dotnet test`

```
dotnet test src/Alveara.Api.Tests/Alveara.Api.Tests.csproj
```

**Result: 175 of 175 passing, 0 failed, 0 skipped.**

3 superseded uncontrolled-race tests removed, 4 new deterministic tests added, all in `AccountServiceMfaTests.cs`:

**Removed** (relied on uncontrolled scheduling, per review findings `ALV-001-C01-R05-01`/`-02`):
- `A_correct_step_up_password_cannot_proceed_past_a_concurrent_failure_that_crosses_the_lockout_threshold`
- `Two_distinct_challenges_racing_the_same_recovery_code_leave_exactly_one_success_and_the_losing_challenge_unconsumed`
- `When_a_recovery_code_race_is_lost_the_losing_challenge_can_still_be_completed_afterward_with_a_different_code`

**Added:**
- `A_correct_step_up_deterministically_loses_when_the_fifth_failure_locks_the_account_first` — seam-paused, provably exercises the stale-success rejection branch (closes `ALV-001-C01-R05-01`).
- `A_correct_step_up_that_completes_first_resets_the_counter_and_a_later_failure_is_recorded_fresh` — purely sequential, proves the other permitted ordering (closes `ALV-001-C01-R05-01`).
- `Two_distinct_challenges_racing_the_same_recovery_code_deterministically_leave_one_success_and_the_loser_rolled_back` — seam-paused, provably enters the zero-rows/rollback branch (closes `ALV-001-C01-R05-02`).
- `A_failure_between_both_conditional_updates_and_commit_rolls_back_both_challenge_and_recovery_state` — controlled mid-transaction failure, proves both pieces of state roll back together (closes `ALV-001-C01-R05-02`).

Each of the four new tests was run 3 consecutive times in isolation to confirm no flake before inclusion in the full suite run reported here.

Every test from R05 not superseded above continues to pass, including the full carried-forward STORY-001 regression suite and R01–R04's own new coverage.

## Frontend — `npx vitest run`

**Result: 16 test files, 40 tests, all passing.** Unchanged from R05 — no frontend production code was modified this attempt.

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

**Result: 6 of 6 passing** (unchanged from R05 — see `DEMO_EVIDENCE.md` for why no new browser test was added this attempt) against a genuinely fresh, migrated database and a real running API process. Raw output: `R06-evidence/artifacts/playwright-real-backend/run-output.txt`.
