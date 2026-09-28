# ALV-N002 R03 — Test Results

## API (`src/Alveara.Api.Tests`) — `dotnet test Alveara.slnx`, against real SQL Server (LocalDB) databases

```
Build succeeded. 0 Warning(s), 0 Error(s).
Passed!  - Failed: 0, Passed: 66, Skipped: 0, Total: 66, Duration: 30 s
```

Up from 61 in R02. Changed/new files:

| File | Change |
|---|---|
| `BackgroundJobTests.cs` | +`ThrowsWithMessageJobHandler` (configurable exception message), +`CapturingLogger<T>` (fake `ILogger` capturing formatted messages); new test `Two_concurrent_enqueue_calls_for_the_same_idempotency_key_both_succeed_and_return_the_same_job` (reproduces and proves the fix for N002-R02-03); new test `A_handler_exception_containing_synthetic_patient_like_text_never_reaches_persisted_LastError` (reproduces and proves the fix for N002-R02-02, persisted side); new `BackgroundJobPhiSafeLoggingTests` class with `The_configured_logging_sink_never_receives_the_raw_exception_message_only_the_safe_summary` (proves the fix for N002-R02-02, logging side) |
| `MigrationUpgradeTests.cs` | `FailureTestBrokenMigration.Up()` now performs a real `ALTER TABLE ... ADD COLUMN` before its failing statement; test renamed to `A_partially_executed_failing_migration_is_fully_rolled_back_not_left_half_applied_and_does_not_destroy_existing_data`, with a new `ColumnExistsAsync` helper asserting the added column does not survive rollback (proves the fix for N002-R02-04) |
| `StorageIsolationTests.cs` (new) | 2 tests: a stored blob's bytes are never present in any HTTP response for plausible guessed URLs, even accounting for the SPA fallback's 200 response; the default storage root and default client build path are structurally disjoint directories |

Plus all pre-existing R02 files unchanged in count.

Run 2 times consecutively (full suite) with zero flakiness, including the new concurrent-enqueue race test.

## Client (`src/alveara-client`) — `npm run test` (vitest, jsdom)

```
Test Files  11 passed (11)
     Tests  28 passed (28)
```

Unchanged from R02 — this attempt made no client-side behavioral changes (`Program.cs` is server-only, static-file serving does not change client source).

## Client (`src/alveara-client`) — `npm run test:e2e` (Playwright, real Chromium)

```
40 passed (7.3s)
```

Unchanged from R02.

## Repo-root (`tests/`) — `node --test`

```
tests 7
pass 7
fail 0
```

Unchanged from R02.

## Manual real-process/real-network verification (N002-R02-01)

1. Rebuilt the API (`dotnet build Alveara.slnx`) and the client (`npm run build`) — both succeeded.
2. `dotnet dev-certs https --trust` — succeeded ("Successfully trusted the existing HTTPS certificate").
3. Started the API bound to `0.0.0.0` on both `http://:5072` and `https://:7180` (`dotnet run --urls "http://0.0.0.0:5072;https://0.0.0.0:7180"`), with the built client `dist/` present so static-file serving activated.
4. `curl http://192.168.1.180:5072/` → `307` redirect to HTTPS (expected: `UseHttpsRedirection()`).
5. `curl https://192.168.1.180:7180/` (no `--resolve`, raw IP) → TLS handshake fails with `SEC_E_WRONG_PRINCIPAL` (expected and correct: the ASP.NET Core dev certificate's CN is `localhost`, with no SAN entry for the IP address — a real deployment needs a CA/AD-CS-issued certificate with the LAN hostname in its SAN, which is `ALV-N014` scope).
6. `curl --resolve localhost:7180:192.168.1.180 https://localhost:7180/<path>` — this simulates a properly-configured LAN client whose DNS/hosts resolution maps a hostname matching the certificate's CN to the server's real LAN IP, achieving **genuine, non-bypassed TLS certificate validation** (directly answering the R02 finding's specific objection to `curl -k`). Verified `200` for:
   - `/` — response body contains `<title>Alveara Dental</title>`, confirming the served shell, not a stub.
   - `/api/health` → `200 {"status":"ok"}`
   - `/system-status` → `200` (SPA fallback resolves a client-side route correctly on a direct request)
   - `/assets/index-LorW5yfl.js` → `200` (a real built static asset)
   - `/api/systemstatus` → `200` with real `database.reachable: true` and `backgroundRunner.status: "healthy"` — a same-origin API call from the served shell's own origin, not cross-origin to the Vite dev server.
7. Stopped the test server (`Get-Process -Name "dotnet" | Stop-Process -Force`) after verification; no background processes were left running.

**Honestly not exercised:** a genuinely separate physical/virtual LAN client machine, a real production Windows Service account, CA/AD-CS-issued certificate deployment, and disabling this machine's public internet access. These require infrastructure or system-level changes outside this story's sandboxed scope and explicit user authorization; they are documented as open limitations in `.alveara/BUILD_STATE.md` rather than claimed as established. See `ACCEPTANCE_EVIDENCE.md` for how this affects the acceptance assessment.

## Overall

**141 automated tests** (66 API + 28 client vitest + 40 Playwright + 7 repo-root), up from 136 in R02. All passing, rerun to confirm no flakiness. Plus the manual real-process/real-network reproductions above, directly targeting all four R02 findings.
