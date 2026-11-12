# ALV-N020 R02: parent and dependency regression

- **Parent course story:** none (`parentRegression: "n/a"`).
- **Dependency ALV-N019:** the chain runner it delivered was changed in R02 (Playwright as a child process, the owned-API control channel). Its stage-0 steps all passed again in the R02 chain re-run (S-09a-d, C-01, B-01, B-02, E10, CLEAN), and its runner self-test `tests/stabilization/chain-runner-resilience.test.mjs` passed 8 of 8 (refusals, commit stamp, API killed at start-up still cleans up).
- **Unchanged from R01:** two ALV-N009 route-guard tests deliberately expect the dashboard after a first-visit deep link.
- **Broad regression:** `DEFERRED_TO_ALV-N028`; ledger updated.
