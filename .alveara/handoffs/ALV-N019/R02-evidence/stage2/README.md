# ALV-N019 R02 Stage 2: the frozen contract

Authorized by `.alveara/reviews/ALV-N019/R02.md` (`APPROVED_PENDING_REGRESSION`): the contract of section 12.2 of `docs/design/ALV-N019_PRE_IMPLEMENTATION_BRIEF.md`, run exactly once on the approved snapshot (implementation `54806a3` plus the catalog data of `4570651`).

| Order | Step | Criterion (decided only by `evaluate_stage2.py`) |
|---|---|---|
| 1 | `dotnet test src/Alveara.Api.Tests` (the whole backend suite, serial, with a results file) | exit 0; exactly **5,697** tests, all Passed: the 5,612 ids of the ALV-N006 R01 Stage 2 baseline unchanged (no retired id) plus exactly the **85** ids in `expected_new_ids.txt` (no more, no fewer) |
| 2 | `npx vitest run` | exit 0; 1,879 tests in 120 files |
| 3 | `npm run build` | exit 0 |
| 4 | `npm run lint` | exit 0 |
| 5 | `npx tsc -b` | exit 0 |
| 6 | `npx playwright test` (mocked suite) | exit 0; 138 passed, 2 skipped, none failed |
| 7 | `node --test tests/*.test.mjs` | exit 0; 49 tests, none failed |
| 8 | Real-backend walkthroughs through `scripts/Invoke-RealBackendWalkthrough.ps1`: auth 12, patient portability 7, patient export package 10, patient merge 9, bulk import 13 | each exit 0, the stated number passed, none failed |
| 9 | `scripts/Invoke-AccumulatedChain.ps1 -SkipBuild -ClockMode injected` | exit 0; the host reports the injected clock; nine PASS records and none else, each stamped `clockMode: injected` |

Also decided: the run commit differs from the approved snapshot only in `.alveara/` records and `PROGRESS.md` (no product, test, script, configuration, solution, package or catalog file); no tracked file changes before or after, and the untracked listing is identical before and after; the artifact counts (databases, SQL logins, `Test-Data` folders) are identical before and after; every walkthrough writes its output into the out-folder outside the repository (`verify_runner_outputs.py`, run before anything starts). The template telemetry of the test fixture and the backend elapsed time are reported as information only.

Files: `run_stage2.sh` (the runner), `evaluate_stage2.py` (the verdict), `compare_outcomes.py` (test-id comparison), `leak_counts.sh`, `verify_runner_outputs.py` (`runner-proof.txt`), `validate_evaluator.py` (`evaluator-validation.txt`: 20 scenarios, each accepted or rejected as stated), `expected_new_ids.txt` (85 ids: the new and changed test classes of R01 and R02, derived from their recorded results and cross-checked against the discovered test list), `retired_ids.txt` (empty).

Run: `sh run_stage2.sh 45706516294e7f66ca25da8e343a739c39ed2e2d C:/tmp/stage2-ALV-N019-R02`, once, with nothing else running on the machine. A product or test failure is not patched during the run; it returns for a new attempt. An environmental inconclusive result may be rerun unchanged with the cause and cleanup evidence preserved.
