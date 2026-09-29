# ALV-N002 R08 — Acceptance Evidence

Updates R07's mapping. R07 was 8/9 PASS with item 7 `PARTIAL` (the positive service-write check and the API-to-SQL-login linkage were not independently established). This attempt closes that gap with genuine, unedited, script-verified evidence.

| # | Acceptance item | R07 review assessment | R08 evidence | R08 status |
|---|---|---|---|---|
| 1 | Local Windows server / LAN client / no public internet | PASS for the recorded R07 client run | Unchanged — reused from R07 (`artifacts/client/`), already accepted. | **PASS** |
| 2 | Schema migration initializes and upgrades safely | PASS | Unchanged. | **PASS** |
| 3 | Time/timezone and money invariants | PASS | Unchanged. | **PASS** |
| 4 | Explicit disconnected-server behavior | PASS | Unchanged. | **PASS** |
| 5 | Persisted job survives restart without duplicate effects | PASS | Unchanged. | **PASS** |
| 6 | Versioned privacy-minimized measurement event | PASS | Unchanged. | **PASS** |
| 7 | LAN client uses API rather than direct database/storage access | PARTIAL — positive service-write check and API-to-login linkage not established | `artifacts/server/20260929-065256-sql-session-correlation.txt` (live SQL session under `alveara_app_login` with `host_process_id=7172`, matching the API's own PID) + `20260929-065256-systemstatus-response.txt` (full response body, `database.reachable: true`) + `20260929-065256-svc-write-result.txt` (genuine, script-executed, self-authenticated write/read success) + `20260929-065256-ordinary-denial-result.txt` (genuine, self-authenticated denial) + unchanged client-side bypass checks. | **PASS** |
| 8 | Truthful service/database/runner System Status | PASS | Reconfirmed via the retained live `/api/systemstatus` response body. | **PASS** |
| 9 | Failure paths do not silently corrupt authoritative data | PASS | Unchanged. | **PASS** |

**Total: 9 of 9 PASS**, with item 7 now backed by evidence that directly answers all three specific gaps the R07 review identified.

## Correcting N002-R07-01 (implementation identity)

This attempt has exactly one implementation commit: `02a8c98533cc604fce4ac9f27c46f119f5b9065f`. No follow-up implementation commit exists for this attempt, so there is no possibility of the ledger/manifest/ZIP-filename identity diverging from the tree that produced the evidence.

## Correcting N002-R07-02 (manual verdict editing)

No file in this attempt's evidence was opened, edited, or completed by a human after the harness ran. `DEMO_EVIDENCE.md` walks through the timestamp ordering proving the summary was computed after every check it reports, and that the "positive write" and "negative denial" checks each embed the actual Windows identity that performed them (captured by the child process itself via `WindowsIdentity.GetCurrent()`), not a string supplied by the parent script.

## Correcting N002-R07-03 (API-to-SQL-login linkage)

`sql-session-correlation.txt` ties a live `sys.dm_exec_sessions` row under `alveara_app_login` to the exact PID of the running `Alveara.Api.exe` process, captured immediately after forcing a real database-backed request — not an inference from the login's static role membership alone.
