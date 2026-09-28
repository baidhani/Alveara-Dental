# ALV-N002 R02 — Test Results

## API (`src/Alveara.Api.Tests`) — `dotnet test Alveara.slnx`, against real SQL Server (LocalDB) databases

```
Passed!  - Failed: 0, Passed: 61, Skipped: 0, Total: 61, Duration: ~30 s
```

Up from 39 in R01. New/changed files:

| File | Tests | Purpose |
|---|---|---|
| `BackgroundJobTests.cs` (+5) | 9 | atomic claim, transactional effect+receipt, concurrent-claim race, MaxAttempts-on-recovery, recovery-on-every-poll |
| `MeasurementEventTests.cs` (+11) | 16 | nested-object bypass, array bypass, oversized string, negative count, out-of-range outcome, schemaVersion 0, 4 malformed-event-name cases, well-formed-name accept |
| `BlobStorageTests.cs` (+1) | 4 | interrupted-write leaves no partial/orphaned file |
| `MigrationUpgradeTests.cs` (new) | 1 | genuine upgrade of a populated database from the first to the second migration |
| `MigrationFailureTests.cs` (new, in `MigrationUpgradeTests.cs`) | 1 | a failing migration is not recorded as applied and does not destroy existing data |
| `LeastPrivilegeAccessTests.cs` (new) | 2 | a `db_datareader`+`db_datawriter`-only login can do CRUD but not DDL/BACKUP |
| `RealProcessDatabaseOutageTests.cs` (new) | 1 | a real separate OS process survives a missing-database startup and reports sustained degraded status |

Plus all pre-existing files unchanged in count: `MigrationTests.cs` (3), `TransactionRollbackTests.cs` (2), `PracticeClockTests.cs` (7), `MoneyTests.cs` (8), `NoPublicInternetDependencyTests.cs` (1), `SystemStatusTests.cs` (2), `PhiSafeLogTests.cs` (2), `HealthEndpointTests.cs` (1).

Run 4 times consecutively (full suite) with zero flakiness, including the new concurrent-claim race test.

## Client (`src/alveara-client`) — `npm run test` (vitest, jsdom)

```
Test Files  11 passed (11)
     Tests  28 passed (28)
```

Up from 23 in R01. New: `useSystemStatus.test.ts` (5 tests: loaded, error-on-first-failure, shape-rejection, stale-after-prior-success, timeout).

## Client (`src/alveara-client`) — `npm run test:e2e` (Playwright, real Chromium)

```
40 passed
```

Unchanged from R01 (no shell/System-Status-page structural changes this attempt beyond the stale-state banner, which the existing `system-status.spec.ts` tests don't specifically exercise — see "Review-relevant limitations").

## Repo-root (`tests/`) — `node --test`

```
7 passed (3 no-direct-db-access + 4 story-000-coexistence, unchanged from R01)
```

## Manual real-process/real-network verification

1. **N002-R01-03 reproduction and fix confirmation:** started the real built API (`dotnet run`) with `ConnectionStrings__Alveara` pointed at a nonexistent LocalDB database. `curl /api/health` → `200` (unaffected). `curl /api/systemstatus` → `200 {"database":{"reachable":false},...}` — checked twice, several seconds apart, both times consistent. The process log shows the underlying SQL error logged (`SqlException`, database not found) but **no** "Application is shutting down" message — confirming the host survived, unlike the reviewer's R01 reproduction against the old code.
2. **N002-R01-05 LAN/HTTPS reproduction:** started the API bound to `0.0.0.0` on both `http://:5072` and `https://:7180`. `curl http://192.168.1.180:5072/api/health` → `200 {"status":"ok"}` (this machine's real, non-loopback LAN IP). `curl -k https://192.168.1.180:7180/api/health` → `200`, with `curl -v` output showing a genuine `schannel: SSL/TLS connection renegotiated` handshake against the ASP.NET Core dev certificate.
3. All manual dev-server processes were stopped after verification; no background processes were left running.

## Overall

**136 automated tests** (61 API + 28 client vitest + 40 Playwright + 7 repo-root), up from 57 in R01. All passing, rerun repeatedly to confirm no flakiness. Plus the manual real-process and real-network reproductions above, directly targeting the two findings (N002-R01-03, N002-R01-05) that most needed evidence beyond unit tests.
