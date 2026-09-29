# ALV-N002 R08 — Test Results

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
40 passed (9.3s)
```

## Repo-root (`tests/`) — `node --test tests/*.test.mjs`

```
tests 7
pass 7
fail 0
```

## Overall

**142 automated tests, all genuinely rerun this attempt, all passing** (67 API + 28 client vitest + 40 Playwright + 7 repo-root). No application source changed in this attempt; the R08 implementation commit (`02a8c98533cc604fce4ac9f27c46f119f5b9065f`) is entirely within `ops/deployment-verification/Invoke-ServerVerification.ps1`.

## Deployment verification harness — the substance of this attempt

The rewritten `Invoke-ServerVerification.ps1` was run against the real server VM in a single, unattended pass — no manual step, no result file edited afterward. See `DEMO_EVIDENCE.md` for what each of its six checks proves and how the R07 findings are addressed. The client-side evidence, already accepted by the R07 review, is reused unchanged in this package (`artifacts/client/`) for a self-contained ZIP.
