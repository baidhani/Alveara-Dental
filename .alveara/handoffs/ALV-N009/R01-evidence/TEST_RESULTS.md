# ALV-N009 R01 — Test Results

All commands run from the repository at implementation commit `1127f01bf36fcc4e60cd83627d51d5878d854314`.

## Backend — `dotnet test`

```
dotnet test src/Alveara.Api.Tests/Alveara.Api.Tests.csproj
```

**Result: 178 of 178 passing, 0 failed, 0 skipped.**

New/extended this attempt:

- `AuthControllerMfaConfirmSessionTests.cs` (new, 2 tests): `Confirming_MFA_enrollment_keeps_the_confirming_caller_s_own_session_valid`, `Confirming_an_MFA_factor_replacement_also_keeps_the_confirming_caller_s_own_session_valid` — real-HTTP proof of the session-continuity fix.
- `AuthControllerPermissionMatrixTests.cs` (extended, 1 new test): `GET_permissions_also_returns_the_caller_s_username_and_a_future_session_expiry`.

Every test from before this attempt (176) continues to pass, including the full carried-forward `STORY-001`/`ALV-001-C01` regression suite.

## Frontend — `npx vitest run`

**Result: 19 test files, 54 tests, all passing** (16 files / 40 tests carried forward + 3 new files / 14 new tests).

New this attempt:

- `app/AppShell.permissionAwareNav.test.tsx` (3 tests): nav hides/shows by permission, account identity + sign-out.
- `App.routeGuards.test.tsx` (4 tests): signed-out denial, permission-denied, permitted access, post-login return-to-intended-page.
- `contexts/AuthContext.test.tsx` (7 tests): defensive `hasPermission`, session-expiry-soon timing (×2), reactive 401 handling, logout clearing, 401-from-permissions-check-itself, `ApiError` shape sanity.

Updated (not new) this attempt: `App.disconnected.test.tsx` (tolerant of the relocated banner co-occurring with the login page's own session-expired alert under the same outage), `app/AppShell.emptyRegistry.test.tsx` (wraps `AuthProvider`, `AppShell`'s new dependency).

## Frontend — `npx tsc -b`

Clean, zero errors.

## Frontend — `npx vite build`

Clean production build, 62 modules transformed.

## Frontend — `npx oxlint`

Exit code 0. Two new informational `react(set-state-in-effect)` warnings in `AuthContext.tsx` (lines 67, 87), consistent in kind with four pre-existing warnings of the same class already present in `PermissionMatrixPage.tsx`/`AdminUsersPage.tsx`/`AdminUserDetailPage.tsx`; no new warning kind introduced.

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

**Result: 8 of 8 passing** (6 carried forward + 2 new: permission-aware nav/direct-URL denial for a limited-permission role, and the account-menu identity + sign-out flow) against a genuinely fresh, migrated database and a real running API process. Raw output: `R01-evidence/artifacts/playwright-real-backend/run-output.txt`.

The first run against this attempt's code caught a genuine regression in test 3 (MFA enrollment confirm silently signing the caller out - see `R01.md`'s "genuine bug" section and `DEMO_EVIDENCE.md`); fixed in the same implementation commit, and this final run reflects the fix with all 8 tests passing cleanly.
