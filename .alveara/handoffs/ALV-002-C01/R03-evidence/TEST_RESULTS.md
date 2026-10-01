# ALV-002-C01 R03 — Test Results

All commands run from the repository at implementation commit `cc053dae1a37d0404d45682387301fe1f344b558`.

## Backend — `dotnet test`

```
cd src/Alveara.Api.Tests
dotnet test
```

**Result: 199 of 199 passing, 0 failed, 0 skipped** (duration 7 m 36 s).

New this attempt (both in `AuthControllerPermissionMatrixTests.cs`, against the real `WebApplicationFactory` host and a real SQL Server LocalDB database):

- `GET_audit_log_round_trips_entity_type_reason_and_correlation_id` — stages events through the real `AuditService.Record` path and proves the authenticated read endpoint returns `entityType`, `reason`, `correlationId` exactly, and `null` where none was supplied (finding R02-01).
- `GET_audit_log_defaults_to_100_rows_and_honors_take_up_to_500` — with 130 staged events: default request returns exactly 100; `take=500` returns all 130; `take=9999` is clamped to at most 500 (finding R02-02).

Baseline (184 before R01) + 11 R01 + 2 R02 + 2 R03 = 199.

Note on one transient result: the first targeted run of `AuthControllerPermissionMatrixTests` after the build failed 4 of 8 tests with a LocalDB connection error raised from `TestDatabaseFixture.InitializeAsync` (the LocalDB instance was cold-starting). Re-running the identical class passed 8 of 8, and the full-suite run above passed 199 of 199. No code was changed between those runs.

## Backend — `dotnet build`

Clean, 0 warnings, 0 errors.

## Frontend — `npx vitest run`

**Result: 21 test files, 70 tests, all passing** (69 carried forward + 1 new).

New this attempt:

- `pages/AuditLogPage.test.tsx` — "requests exactly the window its description claims (take=500), not the server's smaller default": asserts the fetch URL contains `/api/auth/audit-log` and `take=500`, and the visible description names the same number.

## Frontend — `npx tsc --noEmit`, `npm run build`, `npm run lint`

`tsc --noEmit` clean. Production build clean. Lint exit 0 with informational `react(set-state-in-effect)` warnings only (same class as before; none new in kind).

## Real-backend, real-browser — `npx playwright test --config=playwright.auth.config.ts`

```
cd src/Alveara.Api
sqlcmd -S "(localdb)\MSSQLLocalDB" -Q "IF DB_ID('AlveraE2E') IS NOT NULL BEGIN ALTER DATABASE AlveraE2E SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE AlveraE2E; END"
dotnet ef database update --connection "Server=(localdb)\MSSQLLocalDB;Database=AlveraE2E;..."
dotnet build
ConnectionStrings__Alveara="...AlveraE2E..." AdminBootstrapSecret="e2e-real-backend-secret" ASPNETCORE_URLS="http://localhost:5072" dotnet run --no-build

# second terminal, from src/alveara-client
E2E_BOOTSTRAP_SECRET="e2e-real-backend-secret" npx playwright test --config=playwright.auth.config.ts
```

**Result: 9 of 9 passing**, against a genuinely fresh, migrated database and a real running API process built from this attempt's implementation commit. Test 7 ("the audit log page shows real audit entries this session produced, as the admin") now additionally asserts that the real `SessionTimeoutChanged` row visibly contains `UserAccount`. Raw output: `R03-evidence/artifacts/playwright-real-backend/run-output.txt`.
