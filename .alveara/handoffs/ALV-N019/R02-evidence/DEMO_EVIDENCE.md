# ALV-N019 R02: demo evidence

This story is infrastructure and identity work; there is no new user workflow. What can be shown:

1. **Stage-0 chain run with the chain host** (`scripts/Invoke-AccumulatedChain.ps1`): one command creates `AlveraChain_<run id>`, starts the chain host (the unchanged application with an injected clock), checks the clock mode the host reports, runs the browser steps, takes the COPY_ONLY checkpoint and verifies the E10 invariant, drops the database and deletes the run folder, then prints one line per step and exits 0. Evidence: `artifacts/chain-injected/evidence.json` (nine records, all PASS, each stamped with run id, implementation SHA, schema version and `clockMode: injected`), `clock-mode.json` (the host's own report: `ChainPracticeClock`, fixed instant `2026-01-15T15:00:00Z`), `e10-before.json` and `e10-after.json`. The same run against the production API: `artifacts/chain-real/` (`clockMode: real`).
2. **DentalKeep in a real browser** (review correction 06), captured by the chain's own steps in the run folder and copied here:
   - `artifacts/chain-injected/brand/brand-sign-in.png`: the sign-in page ("Enter your username and password to access DentalKeep.").
   - `artifacts/chain-injected/brand/brand-signed-in.png`: the signed-in page, sidebar brand "DentalKeep".
   Both were viewed. The document title is asserted in the same steps (`toHaveTitle("DentalKeep")`), not visible in a page screenshot. The sidebar mark still shows the old "AD" initials (not covered by any review correction).
3. **Refusals and cleanup** are demonstrated by tests (`chain-runner-resilience.test.mjs`), not by screenshots.
