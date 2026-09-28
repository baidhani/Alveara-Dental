# ALV-N002 R01 — Acceptance Evidence

Maps each of the 9 acceptance items to the evidence proving it.

| # | Acceptance item | Evidence |
|---|---|---|
| 1 | Application runs on the local Windows-server model and a LAN client can use the production shell with no public internet | `dotnet run` on `http://localhost:5072` (the real, non-loopback-only bindable port) + client Vite proxy → real end-to-end request/response verified manually. `NoPublicInternetDependencyTests` proves no API code path references a non-localhost endpoint. |
| 2 | Schema migration initializes and upgrades a test database with safe failure behavior | `MigrationTests.cs`: fresh-database migration succeeds; re-running migration is a safe no-op; a real unique-index constraint is actually enforced by the database (not just modeled in C#). |
| 3 | Time/timezone and money invariants are documented in code/tests and exercised | `PracticeClockTests.cs` (7 tests: ordinary conversion, DST spring-forward throws, DST fall-back rejected/resolved, round-trip preservation, date-only unaffected) + `MoneyTests.cs` (8 tests: no floating point, banker's-rounding at all four midpoint cases, addition/subtraction, non-biasing aggregation, fixed USD currency). |
| 4 | Disconnected-server behavior is explicit and does not pretend unsynchronized edits succeeded | `SystemStatusTests.Reports_database_reachable_false_without_crashing...` proves the status endpoint degrades honestly (reports `reachable: false`) rather than throwing or silently reporting healthy. `ALV-N001`'s `useConnectionStatus`/`DisconnectedBanner` (unchanged, still passing) is the client-visible half of this. |
| 5 | A persisted test background job survives restart without duplicate side effects | `BackgroundJobTests.Restart_recovery_resumes_a_job_stuck_InProgress_from_a_crashed_process_without_duplicate_side_effects` — simulates a crashed process (a job left `InProgress` with a stale timestamp), a *new* `BackgroundJobRunner` instance recovers and completes it, and the test handler's execution count is asserted to be exactly 1. |
| 6 | A versioned sample measurement event can be recorded without duplicating the underlying clinical/financial record or requiring unnecessary PHI | `MeasurementEventTests.cs`: records an event with `SchemaVersion`; asserts it never writes to `BackgroundJobs`/`UserAccounts` (or any other table); asserts a PHI-shaped property key (`patientName`) is rejected by the allow-list validator. |
| 7 | LAN-client integration proves application/API access rather than direct database-file access | `tests/no-direct-db-access.test.mjs`: the client project has zero SQL/database-driver dependencies; a production build's bundled JS (when one exists) contains no connection-string shape; no client `.ts`/`.tsx` source file contains a connection-string literal. |
| 8 | System Status reports truthful local service/database/background-runner state | `SystemStatusTests.cs` (both healthy and unreachable-database scenarios) + manual end-to-end verification against the real dev API showing `backgroundRunner.status: "healthy"` after the hosted service's first real poll cycle. Client `SystemStatusPage` renders all three states, verified by 3 vitest + 4 Playwright real-browser tests. |
| 9 | Failure paths do not silently corrupt authoritative data | `TransactionRollbackTests.cs`: a failed multi-row transaction (forced by a real foreign-key violation) leaves zero partial writes, verified via a fresh DB context after rollback; a committed transaction persists all its writes. |

## Required tests (from the story prompt) — mapped

| Required test | Evidence |
|---|---|
| Migration tests | `MigrationTests.cs` |
| No-public-internet smoke test | `NoPublicInternetDependencyTests.cs` |
| Server-disconnect UI/API test | `SystemStatusTests.cs` (API) + `ALV-N001`'s existing disconnected-banner tests (UI, unchanged) |
| Transaction rollback test | `TransactionRollbackTests.cs` |
| Timezone/DST conversion tests | `PracticeClockTests.cs` |
| Money/rounding tests | `MoneyTests.cs` |
| Durable background-job restart/idempotency test | `BackgroundJobTests.cs` |
| Measurement-event version/privacy-minimization test | `MeasurementEventTests.cs` |
| Direct-database-access topology check/integration test | `tests/no-direct-db-access.test.mjs` |
| Health/status tests | `SystemStatusTests.cs` + `HealthEndpointTests.cs` (unchanged from `ALV-N001`) |
