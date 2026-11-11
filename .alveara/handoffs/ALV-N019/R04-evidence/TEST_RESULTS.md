# ALV-N019 R04: test results (Stage 1, focused; no full run)

| # | Check | Result | Evidence |
|---|---|---|---|
| 1 | Preflight control arm (warm-up OFF, real forced pruning), 800 cycles | 154 authentic first-open pool-acquisition timeouts, 0 initialisation failures | `artifacts/preflight/control.jsonl`, `SUMMARY.txt` |
| 2 | Preflight treatment arm (warm-up ON), 60 iterations x 15 cycles | 900 of 900 first opens ok; 39 warm-up races recovered by the bounded retry; fixture databases left 0 and files left 0 after every iteration | `artifacts/preflight/treatment.jsonl` |
| 3 | Calibration runs earlier in the attempt (control 150 cycles: 30 timeouts; treatment 4 x 15: 60 ok) | consistent with 1 and 2 | `artifacts/calibration/` |
| 4 | The 18 new backend ids | 18 of 18 passed | `artifacts/new-tests.trx`, `new-test-ids.txt` |
| 5 | Negative control: warm-up removed | the treatment test FAILS | `artifacts/negative-controls/nc1-warmup-removed.txt` |
| 6 | Negative control: owner cleanup removed | 4 of 5 owner tests FAIL | `nc2-owner-cleanup-removed.txt` |
| 7 | Negative control: classifier widened | 3 classification tests FAIL | `nc3-classifier-widened.txt` |
| 8 | After restoring all three (byte for byte) | 15 of 15 passed | `restored-check.txt` |
| 9 | All `TestDatabase*` classes plus a sample of every owner family (both R03 failure classes, the multi-class owners, `LeastPrivilegeLoginCleanupTests`, the new classes) | 325 of 325 passed in 11 m 30 s; no leftover database or file afterwards | `artifacts/sample-filter.txt` |
| 10 | `node --test tests/*.test.mjs` | 49 of 49 | rerun at package time |
| 11 | Build | 0 errors; 32 warnings, the same as before the change | |
| 12 | Evaluator self-validation of the R04 Stage 2 contract; runner proof | 20 of 20 scenarios behave as stated; every walkthrough output goes to the runner folder | `stage2/evaluator-validation.txt`, `stage2/runner-proof.txt` |

Not run, by design: the full backend suite, vitest, the mocked Playwright suite, the real-backend walkthroughs and the stage-0 chain (the frozen Stage 2). No client file changed.

Disclosure: three isolated `AlveraTest_*` databases leaked by the owner-cleanup negative control (that is the defect it demonstrates) were removed after verifying their exact names and files; `AlveraDev` is intact.
