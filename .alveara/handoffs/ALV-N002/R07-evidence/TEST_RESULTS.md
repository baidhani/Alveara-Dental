# ALV-N002 R07 — Test Results

All four suites genuinely rerun for this attempt.

## API (`src/Alveara.Api.Tests`) — `dotnet test Alveara.slnx`

```
Passed!  - Failed: 0, Passed: 67, Skipped: 0, Total: 67, Duration: 31 s
```

## Client (`src/alveara-client`) — `npm run test` (vitest, jsdom)

```
Test Files  11 passed (11)
     Tests  28 passed (28)
```

## Client (`src/alveara-client`) — `npm run build` then `npm run test:e2e` (Playwright, real Chromium)

```
40 passed (7.7s)
```

## Repo-root (`tests/`) — `node --test tests/*.test.mjs`

```
tests 7
pass 7
fail 0
```

## Overall

**142 automated tests, all genuinely rerun this attempt, all passing** (67 API + 28 client vitest + 40 Playwright + 7 repo-root). No application source changed in this attempt (the R07 implementation commit is the deployment-verification harness under `ops/deployment-verification/`, not a change to `src/`).

## Deployment verification harness — the substance of this attempt

`ops/deployment-verification/Invoke-ServerVerification.ps1` and `Invoke-ClientVerification.ps1` (added in this attempt's implementation commit, `967b1e8`, fixed for a mangled-character parsing issue in `c780277`) were run against the real server VM and a genuinely separate client VM. Their raw, unedited, timestamped output is committed at `artifacts/server/` and `artifacts/client/` in this evidence directory — see `DEMO_EVIDENCE.md` for what each file proves and `ACCEPTANCE_EVIDENCE.md` for how they map to acceptance items 1 and 7.
