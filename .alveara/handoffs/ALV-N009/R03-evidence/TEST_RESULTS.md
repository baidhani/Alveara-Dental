# ALV-N009 R03 — Test Results

All commands run from the repository at implementation commit `47a80c3c1f2d2a8bc4fec008ae601c140516991e`.

## Backend — `dotnet test`

```
dotnet test src/Alveara.Api.Tests/Alveara.Api.Tests.csproj
```

**Result: 179 of 179 passing, 0 failed, 0 skipped.**

New this attempt, in `AuthControllerPermissionMatrixTests.cs`:

- `GET_permissions_does_not_extend_the_session_expiry_on_repeated_calls_SlidingExpiration_is_off` — calls the permissions endpoint four times in a row and asserts the server-reported `sessionExpiresAtUtc` never changes, proving `ALV-N009-R02-01`'s fix (`SlidingExpiration = false` in `Program.cs`).

## Frontend — `npx vitest run`

**Result: 19 test files, 59 tests, all passing** (57 carried forward + 2 new this attempt).

New this attempt, in `contexts/AuthContext.test.tsx`:

- `R03: an older in-flight refresh cannot restore signed-in state after a newer 401 already cleared it` — forces the exact out-of-order completion the R02 reviewer's probe used and proves the `requestSeqRef` guard discards the stale response.
- `R03: logout() invalidates an older in-flight refresh so it cannot restore state after sign-out` — same guarantee, against an explicit sign-out instead of a reactive 401.

## Frontend — `npx tsc -b`

Clean, exit code 0.

## Frontend — `npx vite build`

Clean production build, 62 modules transformed.

## Frontend — `npx oxlint`

Exit code 0. Same informational `react(set-state-in-effect)` warning classes as R01/R02, no new warning kind.

## Real-backend, real-browser — `npx playwright test --config=playwright.auth.config.ts`

**Result: 8 of 8 passing**, unchanged in assertion from R01/R02, rerun against a genuinely fresh, migrated database and a real running API process built from this attempt's implementation commit. Raw output: `R03-evidence/artifacts/playwright-real-backend/run-output.txt`.

No new real-backend Playwright tests were added this attempt. Both corrected defects (sliding-cookie renewal, out-of-order async response ordering) are proven deterministically instead — a fast, repeatable backend integration test for the former, and forced-ordering unit tests for the latter — consistent with how the review's own reproduction worked (a runtime configuration inspection plus a temporary deterministic probe, not a multi-minute real-browser idle wait).
