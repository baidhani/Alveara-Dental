# ALV-N002 R06 — Acceptance Evidence

Updates R05's mapping. The R05 review found items 1 and 7 still `PARTIAL`: the client's own internet was never blocked (only the server host's was), and the service-identity/ACL demo used a stand-in folder/accounts on the client VM rather than the actual server process. Both are closed in this attempt through a corrected topology — see `DEMO_EVIDENCE.md`.

| # | Acceptance item | R05 review assessment | R06 evidence | R06 status |
|---|---|---|---|---|
| 1 | Local Windows server / LAN client / no public internet | PARTIAL — client's own internet remained available | A dedicated server VM and a genuinely separate cloned client VM; the **client's** public internet was blocked via a real firewall rule (not the server's), confirmed failing (`ping 8.8.8.8` → `General failure`), while the same client simultaneously exercised the full shell/API surface over the LAN with genuine TLS validation (no bypass). | **PASS** |
| 2 | Schema migration initializes and upgrades safely | PASS | Unchanged; also re-exercised for real on the server VM via `dotnet ef database update` against a real SQL Server 2025 Express instance. | **PASS** |
| 3 | Time/timezone and money invariants | PASS | Unchanged. | **PASS** |
| 4 | Explicit disconnected-server behavior | PASS | Unchanged. | **PASS** |
| 5 | Persisted job survives restart without duplicate effects | PASS | Unchanged. | **PASS** |
| 6 | Versioned privacy-minimized measurement event | PASS | Unchanged. | **PASS** |
| 7 | LAN client uses API rather than direct database/storage access | PARTIAL — ACL demo used a stand-in on the client VM, not the actual server | The real API process runs on the real server VM under `svc-alveara-api`, connected via the real least-privilege SQL login (`alveara_app_login`, `db_datareader`+`db_datawriter` only), protecting the real storage path (`C:\AlveaaraServer\App_Data\blobs`). An unrelated local account is genuinely denied access to that real path. The separate client VM cannot reach the database port (`Test-NetConnection ... -Port 1433` → `TcpTestSucceeded: False`) or the storage filesystem (`Test-Path \\...\blobs` → `False`) directly — it can only reach the application through the HTTP(S) API. | **PASS** |
| 8 | Truthful service/database/runner System Status | PASS | Unchanged; also verified `database.reachable: true` for real against the least-privilege SQL connection from the real server. | **PASS** |
| 9 | Failure paths do not silently corrupt authoritative data | PASS | Unchanged. | **PASS** |

**Total: 9 of 9 PASS.** Unlike R05, both closures this time are against the actual running server process, its real storage path, and a genuinely separate client — not a stand-in.

## Correcting R05's inaccurate test-summary claim (N002-R05-04)

R05's package claimed the full 142-test suite was rerun for that attempt when only the 67 API tests actually were. For this attempt, all four suites were genuinely rerun and are reported in `TEST_RESULTS.md` with their actual commands and counts — no suite is claimed as current-attempt validation unless it was actually executed this attempt.
