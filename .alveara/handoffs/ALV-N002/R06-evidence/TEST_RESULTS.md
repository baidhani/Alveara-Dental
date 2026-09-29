# ALV-N002 R06 — Test Results

All four suites were genuinely rerun for this attempt (correcting R05's inaccurate claim that all 142 were rerun when only 67 were).

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
40 passed (8.7s)
```

## Repo-root (`tests/`) — `node --test`

```
tests 7
pass 7
fail 0
```

## Overall

**142 automated tests, all genuinely rerun this attempt, all passing** (67 API + 28 client vitest + 40 Playwright + 7 repo-root). No application source changed in this attempt (same as R05); this rerun exists purely to give an honest, verified test-summary claim, directly addressing N002-R05-04.

## Manual infrastructure verification (the substance of this attempt)

See `DEMO_EVIDENCE.md` for the full transcript: a real server VM running the actual application under a real least-privilege service identity, connected to a real least-privilege SQL login, protecting the real storage root via real NTFS ACLs; a genuinely separate cloned client VM reaching it over the LAN with non-bypassed TLS validation; that same client proven unable to reach the database port or storage filesystem directly; and that same client's own public internet blocked via a real firewall rule while its LAN access to the shell/API kept working, with the block confirmed to auto-revert cleanly.
