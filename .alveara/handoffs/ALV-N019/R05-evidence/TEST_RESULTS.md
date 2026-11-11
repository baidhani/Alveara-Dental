# ALV-N019 R05: test results (Stage 1, focused; no full run)

| # | Check | Result | Evidence |
|---|---|---|---|
| 1 | R04 Stage 2 (context): backend 5,715 expected | 5,712 passed, 3 failed (2 my R04 defects, 1 pre-existing flake); no pool timeout; 4,354 clones, 0 fallbacks | `r04-stage2-failure/` |
| 2 | R04 Stage 2 (context): mocked Playwright | 137 passed, 1 failed (pre-existing flake), 2 skipped | `r04-stage2-failure/playwright_mocked.txt` |
| 3 | R04 Stage 2 (context): every other criterion | vitest, build, lint, tsc, repo checks, five walkthroughs, injected chain all passed; leak counts identical | `r04-stage2-failure/RESULT_ALV-N019_R04.txt` |
| 4 | `RealMigrationInventoryTests` after the fix | 5 of 5 | |
| 5 | Login-cleanup flake, R03 implementation `56b05c0` | 1 failure in 8 runs (proves it pre-dates R04) | |
| 6 | Login-cleanup test after the fix | 12 of 12 | |
| 7 | Architecture-map spec before / after | 1 failure in 3 / 8 of 8 | |
| 8 | `npx tsc -b`, `npm run lint` | ok (existing lint warnings only) | |
| 9 | Sweep: 26 source-scanning classes + `TestDatabase*` + 3 new fixture classes | 375 of 375, 15 m 35 s, no leftover database or file | `artifacts/sweep-375-of-375.txt` |
| 10 | Evaluator self-validation of the R05 contract; runner proof | every scenario behaves as stated; every walkthrough output goes to the runner folder | `stage2/evaluator-validation.txt`, `stage2/runner-proof.txt` |

Not run, by design: the full backend suite and the other frozen Stage 2 steps.
