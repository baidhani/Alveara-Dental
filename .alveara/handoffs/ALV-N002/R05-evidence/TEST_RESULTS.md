# ALV-N002 R05 — Test Results

## API (`src/Alveara.Api.Tests`) — `dotnet test Alveara.slnx`

```
Passed!  - Failed: 0, Passed: 67, Skipped: 0, Total: 67, Duration: 31 s
```

Unchanged from R04 — this attempt made no application source changes (the only repository change is a `.gitignore` entry for the runtime storage root). Re-run purely as a sanity check that the repository is still in a fully green state before closing this attempt.

## Client — unchanged from R04

28 vitest + 40 Playwright, not re-run this attempt since no client source changed. Last confirmed green in R04's `TEST_RESULTS.md`.

## Repo-root (`tests/`) — unchanged from R04

7 `node --test`, not re-run this attempt for the same reason.

## Manual infrastructure verification (the substance of this attempt)

See `DEMO_EVIDENCE.md` for the full transcript: bridged VM networking, real LAN-hostname certificate trust (validated with no bypass, and proven genuinely scoped by the host's own failed validation attempt), a real firewall-level public-internet cutoff with simultaneous LAN-success confirmation, and real OS-level least-privilege account + ACL enforcement with a genuine negative-access denial.

## Overall

**142 automated tests, unchanged from R04 (67 API + 28 client vitest + 40 Playwright + 7 repo-root), all passing.** This attempt's evidence is the infrastructure demonstration in `DEMO_EVIDENCE.md`, not new automated tests — the acceptance items it closes (real LAN client, real cert trust, disabled internet, real service-account ACLs) are properties of a deployed environment, not something an xUnit/vitest/Playwright suite running on the developer's own machine can assert.
