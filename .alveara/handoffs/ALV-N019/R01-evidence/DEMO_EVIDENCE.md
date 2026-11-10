# ALV-N019 R01: demo evidence

This story is mostly infrastructure; there is no new user workflow. What can be shown:

1. **Stage-0 chain run** (`scripts/Invoke-AccumulatedChain.ps1`): one command creates `AlveraChain_<run id>`, starts the real API, runs the four S-09 checks in a real browser, takes the COPY_ONLY checkpoint, drops the database and deletes the run folder, then prints one line per step and exits 0. Output of a run: `artifacts/chain/stage0-evidence.json` (six records, all PASS, with run id, implementation SHA, schema version and clock mode on each) and the console in `artifacts/sweep/10-chain-stage0.log`.
2. **One-line refusal** of the dev start scripts: demonstrated by `tests/stabilization/dev-script-refusals.test.mjs` (stubbed environment), not by screenshots.
3. **DentalKeep brand**: the sign-in page, sidebar and page title. No screenshots are included in this attempt; the strings are asserted by tests (`brand-inventory.test.mjs`) and the client unit tests. A visual before/after screenshot set was not captured.

Screenshots: none. This is stated plainly so the reviewer does not look for them.
