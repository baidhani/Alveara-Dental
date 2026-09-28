# ALV-N002 R04 — Test Results

## API (`src/Alveara.Api.Tests`) — `dotnet test Alveara.slnx`, against real SQL Server (LocalDB) databases

```
Build succeeded. 0 Warning(s), 0 Error(s).
Passed!  - Failed: 0, Passed: 67, Skipped: 0, Total: 67, Duration: 30 s
```

Up from 66 in R03. New: `FrameworkLoggingPhiSafetyTests.cs` — `A_synthetic_patient_like_value_in_a_real_unique_constraint_violation_never_appears_in_any_captured_log_across_any_category`, which registers a capturing `ILoggerProvider` into the real, fully-configured `WebApplicationFactory` host (appsettings.json's category filters included, not bypassed), forces a genuine SQL unique-constraint violation with a synthetic patient-like username through the real EF pipeline, and asserts the value is absent from every captured log record across every category — the exact reviewer reproduction (which previously found the value present via EF's `Database.Command`/`Update` logging), now with the opposite result.

## Client (`src/alveara-client`) — `npm run test` (vitest, jsdom)

```
Test Files  11 passed (11)
     Tests  28 passed (28)
```

Unchanged from R03 — no client-side behavioral changes this attempt.

## Clean `dotnet publish` reproduction (N002-R03-01)

Reproduces the reviewer's exact independent check:

1. `rm -rf /tmp/alveara-publish-test && dotnet publish Alveara.Api/Alveara.Api.csproj -c Release -o /tmp/alveara-publish-test` — the new `BuildClientForPublish`/`CopyClientBuildToPublishOutput` MSBuild targets ran `npm ci && npm run build` for the client and copied `dist/` into `/tmp/alveara-publish-test/wwwroot/`, confirmed present (`index.html`, `assets/`, `favicon.svg`, `icons.svg`).
2. Ran the published executable directly (`./Alveara.Api.exe`, no repository context, `ConnectionStrings__Alveara` pointed at a fresh LocalDB database) bound to `http://127.0.0.1:5099`.
3. `curl -o /dev/null -w "%{http_code}" http://127.0.0.1:5099/api/health` → `200` (reviewer's prior result: `200`, unchanged).
4. `curl -o /dev/null -w "%{http_code}" http://127.0.0.1:5099/` → `200` (reviewer's prior result: `404` — now fixed).
5. `curl -o /dev/null -w "%{http_code}" http://127.0.0.1:5099/system-status` → `200` (reviewer's prior result: `404` — now fixed).
6. `curl http://127.0.0.1:5099/ | grep -o '<title>[^<]*</title>'` → `<title>Alveara Dental</title>` — confirms the real served shell, not a stub.
7. Stopped the test process after verification.

## Client (`src/alveara-client`) — `npm run test:e2e` (Playwright, real Chromium)

```
40 passed (7.3s)
```

Unchanged from R03 (rebuilt `dist/` first, since it was removed after the publish reproduction above).

## Repo-root (`tests/`) — `node --test`

```
tests 7
pass 7
fail 0
```

Unchanged from R03.

## Overall

**142 automated tests** (67 API + 28 client vitest + 40 Playwright + 7 repo-root), up from 141 in R03. All passing. Plus the clean-publish reproduction above, directly targeting N002-R03-01's specific technical finding (client missing from `dotnet publish` output).

## What this attempt does NOT resolve (see `ACCEPTANCE_EVIDENCE.md` and `HANDOFF.md`)

N002-R03-01's remaining requirements — a real LAN hostname with a CA/root-trusted certificate a genuinely separate client validates (not `localhost` resolved to another machine), disabling this machine's public internet access while preserving LAN reachability, and a provisioned least-privilege OS service identity with filesystem ACLs on the storage root — require physical/VM infrastructure and system-level configuration outside this sandboxed repository environment. The user is provisioning a VirtualBox Windows VM for this; the runbook for that work is tracked separately and is not part of this code-only attempt's evidence.
