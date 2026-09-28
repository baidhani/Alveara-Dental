# ALV-N002 R02 — Acceptance Evidence

Updates R01's mapping with the review's required corrections. Cross-referenced against the reviewer's own acceptance assessment table.

| # | Acceptance item | R01 review assessment | R02 evidence |
|---|---|---|---|
| 1 | Local Windows server / LAN client / no public internet | NOT ESTABLISHED | Real HTTP+HTTPS reachability confirmed over this machine's actual LAN IP (`192.168.1.180`, not loopback), including a genuine TLS handshake. `NoPublicInternetDependencyTests` unchanged (still passing). Honest limitation: no second physical LAN client machine was available to test true cross-device reachability — recorded, not hidden. |
| 2 | Migration initializes and upgrades safely | PARTIAL (upgrade/failure missing) | `MigrationUpgradeTests`: a real second migration upgrades an already-populated database, data verified intact. `MigrationFailureTests`: a deliberately-broken migration fails without half-applying or destroying data. |
| 3 | Time/timezone and money invariants | PASS (unchanged) | Unchanged — `PracticeClockTests.cs` (7), `MoneyTests.cs` (8), still passing. |
| 4 | Explicit disconnected-server behavior | PARTIAL (startup outage, stale status) | Startup-outage host crash fixed and independently reproduced/confirmed via a real separate process. Client hook now has an explicit "stale" state instead of indefinitely showing old healthy data. |
| 5 | Persisted job survives restart without duplicate effects | FAIL | Atomic claim + transactional effect/receipt/completion closes the duplicate-effect window structurally. Concurrent-claim race, effect-atomicity, and receipt-guard all directly tested. |
| 6 | Versioned privacy-minimized measurement event | PARTIAL (bypassable) | Nested-object/array bypass closed; bounded value/length/type validation added; schemaVersion >= 1 enforced. All reproduced-then-fixed with dedicated tests. |
| 7 | LAN client uses API rather than direct database access | NOT ESTABLISHED as permissions evidence | `LeastPrivilegeAccessTests`: a real restricted SQL login proves ordinary CRUD works and DDL/BACKUP are genuinely rejected — the application identity provably does not need elevated database rights. |
| 8 | Truthful service/database/runner System Status | PARTIAL (sustained outage, staleness) | Sustained degraded status confirmed via a real separate-process test across multiple poll intervals. Client hook staleness explicitly surfaced. |
| 9 | Failures do not silently corrupt authoritative data | NOT ESTABLISHED | Transaction rollback (unchanged), plus: background-job duplicate-effect window closed, migration-failure data-preservation proven, interrupted blob writes no longer leave partial artifacts. |

## Required tests (from the story prompt) — re-mapped

| Required test | R01 gap | R02 evidence |
|---|---|---|
| Migration tests | Only fresh-init + no-op re-run | + `MigrationUpgradeTests`, `MigrationFailureTests` |
| No-public-internet smoke test | Source-scan only, not exercised against a real deployment | Unchanged source-scan test retained; real LAN/HTTPS reachability now separately verified (item 1 above) so the two claims are no longer conflated |
| Server-disconnect UI/API test | Startup-outage crash undiscovered | `RealProcessDatabaseOutageTests` + manual real-process reproduction |
| Transaction rollback test | (no gap noted) | Unchanged |
| Timezone/DST conversion tests | (no gap noted) | Unchanged |
| Money/rounding tests | (no gap noted) | Unchanged |
| Durable background-job restart/idempotency test | Duplicate-effect and quick-restart gaps | 5 new tests covering both |
| Measurement-event version/privacy-minimization test | Nested/array bypass, schemaVersion 0 | 11 new tests |
| Direct-database-access topology check/integration test | No permissions evidence | `LeastPrivilegeAccessTests` |
| Health/status tests | Sustained-degraded-status gap | `RealProcessDatabaseOutageTests` |
