# ALV-002-C01 R02 — Test Results

All commands run from the repository at implementation commit `59fd1365252e18413b594bdd1f0eda9d7dbfb292`.

## Backend — `dotnet test`

```
dotnet test src/Alveara.Api.Tests/Alveara.Api.Tests.csproj
```

**Result: 197 of 197 passing, 0 failed, 0 skipped.**

New this attempt:

- `AuditLogImmutabilityTests.A_business_change_and_its_new_required_audit_event_are_both_rolled_back_when_the_audit_INSERT_genuinely_fails` — forces a genuine SQL Server primary-key violation on a new audit event staged through the real `AuditService.Record` path, proving the audit-persistence failure policy against an actual storage-layer rejection, not the preflight guard.
- `IdempotencyGuardTests.IsDuplicateReceiptViolation_is_true_for_the_receipt_s_own_unique_index_and_false_for_an_unrelated_failure` — proves `IdempotencyGuard.IsDuplicateReceiptViolation` correctly distinguishes the receipt's own unique-index violation from an unrelated `DbUpdateException` (a duplicate-username violation on `UserAccounts`).

Baseline (184 before R01) + 11 new in R01 + 2 new in R02 = 197. (R01's manifest/handoff originally miscounted this as "15 new against a 179 baseline" — corrected here per review finding `ALV-002-C01-R01-04`.)

## Backend — `dotnet build`

Clean, 0 warnings, 0 errors.

## Frontend — `npx vitest run`

**Result: 21 test files, 69 tests, all passing** (59 carried forward + 10 new this attempt).

New this attempt:

- `pages/AuditLogPage.test.tsx` (4 tests): renders entries with actor/action/entity/time, empty state, permission-denied state, retryable error state.
- `components/ConcurrencyConflictBanner.test.tsx` (4 tests): presents the conflict in plain language without discarding the edit, invokes `onReload` on recovery, `isConcurrencyConflict` true/false cases.
- `App.routeGuards.test.tsx` (2 new tests): direct URL to `/admin/audit-log` denied for a role without `ViewAuditLog` (nav link also hidden); direct URL access granted for a role that holds it.

## Frontend — `npx tsc -b`

Clean, exit code 0.

## Frontend — `npx vite build`

Clean production build, 64 modules transformed (up from 62).

## Frontend — `npx oxlint`

Exit code 0. One new informational `react(set-state-in-effect)` warning (`AuditLogPage.tsx`), same class already present in `PermissionMatrixPage.tsx`/`AdminUsersPage.tsx`/etc., no new warning kind.

## Real-backend, real-browser — `npx playwright test --config=playwright.auth.config.ts`

```
cd src/Alveara.Api
sqlcmd -S "(localdb)\MSSQLLocalDB" -Q "IF DB_ID('AlveraE2E') IS NOT NULL BEGIN ALTER DATABASE AlveraE2E SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE AlveraE2E; END"
dotnet ef database update --connection "Server=(localdb)\MSSQLLocalDB;Database=AlveraE2E;..."
ConnectionStrings__Alveara="...AlveraE2E..." AdminBootstrapSecret="e2e-real-backend-secret" dotnet run --no-build

# second terminal, from src/alveara-client
E2E_BOOTSTRAP_SECRET="e2e-real-backend-secret" npx playwright test --config=playwright.auth.config.ts
```

**Result: 9 of 9 passing** (8 carried forward, unchanged in assertion, + 1 new: "the audit log page shows real audit entries this session produced, as the admin"), against a genuinely fresh, migrated database and a real running API process built from this attempt's implementation commit. The existing limited-permission-caller test was also extended to assert the Audit Log nav link is hidden and `/admin/audit-log` is denied directly. Raw output: `R02-evidence/artifacts/playwright-real-backend/run-output.txt`.
