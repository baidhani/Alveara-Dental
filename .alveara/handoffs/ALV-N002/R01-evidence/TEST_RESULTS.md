# ALV-N002 R01 — Test Results

## API (`src/Alveara.Api.Tests`) — `dotnet test Alveara.slnx`, against a real migrated SQL Server (LocalDB) database

```
Passed!  - Failed: 0, Passed: 39, Skipped: 0, Total: 39, Duration: ~3 s
```

Confirmed stable across 4 consecutive runs (no flakiness) after fixing two real test-isolation bugs found during development (see "Bugs found and fixed during this attempt" below).

| File | Tests | Maps to required test |
|---|---|---|
| `MigrationTests.cs` | 3 | Migration tests |
| `TransactionRollbackTests.cs` | 2 | Transaction rollback test |
| `PracticeClockTests.cs` | 7 | Timezone/DST conversion tests |
| `MoneyTests.cs` | 8 (one `[Theory]` expands to 4 cases) | Money/rounding tests |
| `BackgroundJobTests.cs` | 4 | Durable background-job restart/idempotency test |
| `MeasurementEventTests.cs` | 5 | Measurement-event version/privacy-minimization test |
| `NoPublicInternetDependencyTests.cs` | 1 | No-public-internet smoke test |
| `SystemStatusTests.cs` | 2 | Health/status tests + server-disconnect API test |
| `BlobStorageTests.cs` | 3 | (storage-seam integrity, supporting evidence) |
| `BackupSnapshotTests.cs` | 1 | (backup-seam evidence, supporting `ALV-N004`) |
| `PhiSafeLogTests.cs` | 2 | (PHI-safe logging convention) |
| `HealthEndpointTests.cs` (from `ALV-N001`, unchanged) | 1 | — |
| Existing API build | — | `dotnet build Alveara.slnx` — 0 warnings, 0 errors |

## Client (`src/alveara-client`) — `npm run test` (vitest, jsdom)

```
Test Files  10 passed (10)
     Tests  23 passed (23)
```

3 new tests in `SystemStatusPage.test.tsx` (healthy, DB-unreachable, server-unreachable states); the other 20 are `ALV-N001`'s existing suite, unaffected.

## Client (`src/alveara-client`) — `npm run test:e2e` (Playwright, real Chromium)

```
40 passed
```

4 new tests in `e2e/system-status.spec.ts` (healthy, DB-unreachable, server-unreachable, reachable from sidebar nav), run at both desktop and tablet viewports; the other 36 are `ALV-N001`'s existing suite, unaffected.

## Client — `npm run build`

```
✓ 47 modules transformed.
✓ built in ~120ms
```

Zero TypeScript errors.

## Repo-root (`tests/`) — `node --test`

```
tests/no-direct-db-access.test.mjs: 3 passed
tests/story-000-coexistence.test.mjs: 4 passed (unchanged from ALV-N001)
```

## Manual end-to-end runtime verification against the real dev API + LocalDB

1. Started `dotnet run --urls http://localhost:5072` for `Alveara.Api` (pointed at the real dev LocalDB database via its default connection string).
2. `curl http://localhost:5072/api/health` → `200 {"status":"ok"}`.
3. `curl http://localhost:5072/api/systemstatus` → `200 {"appVersion":"1.0.0.0","localServerReachable":true,"database":{"reachable":true},"backgroundRunner":{"status":"healthy","lastPollUtc":"2026-09-28T01:49:22...Z"}}` — the background runner had already completed its first poll cycle, confirming the hosted service actually runs and updates its heartbeat.
4. Server was stopped after verification; no background process left running.

## Bugs found and fixed during this attempt (not shipped)

While writing `BackgroundJobTests.cs`, two real test-isolation bugs surfaced and were fixed before this handoff:

1. A test that intentionally leaves a job permanently `Pending` (to prove idempotent-enqueue) was leaking that row into the shared class-level test database, where a *different* test's `ProcessOnceAsync` call (which does not filter by job type) would also count it as "processed" — inflating an unrelated assertion. Fixed by having that test clean up its own row, and by making every test's `JobType` unique (a fresh Guid suffix) so cross-test interference is structurally impossible regardless of ordering.
2. `MoneyTests`' addition/subtraction test had an arithmetically wrong expected value (assumed `10.005 + 0.005 = 10.01`, but banker's rounding of `10.005` alone is `10.00`, and of `0.005` alone is `0.00`) — fixed by correcting the expected values, not the rounding behavior (which was already correct).
3. `SystemStatusTests`' "database unreachable" scenario initially used `ConfigureAppConfiguration` to override the connection string, which does not reliably apply before the minimal-hosting `Program.cs`'s top-level statements read configuration; switched to `WebApplicationBuilder.UseSetting`, which does.

None of these were shipped-code defects — all three were test-harness bugs, caught and fixed by actually running the suite repeatedly rather than trusting it would pass on the first write.
