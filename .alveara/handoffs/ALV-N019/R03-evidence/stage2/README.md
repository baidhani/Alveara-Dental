# ALV-N019 R03 Stage 2: the frozen contract

Prepared in the R03 Stage 1 package, so that no file changes between the Stage 1 approval and the run (`.alveara/reviews/ALV-N019/R03-remediation-scope.md`, item 4). The contract of section 12.2 of `docs/design/ALV-N019_PRE_IMPLEMENTATION_BRIEF.md`, the same shape as the accepted R02 Stage 2, run exactly once on the approved R03 snapshot (the R03 implementation commit), with fresh results for the package graph that pins `System.Security.Cryptography.Pkcs` 9.0.21.

| Order | Step | Criterion (decided only by `evaluate_stage2.py`) |
|---|---|---|
| 1 | `dotnet test src/Alveara.Api.Tests` (the whole backend suite, serial, with a results file) | exit 0; exactly **5,697** tests, all Passed: the 5,697 ids of the R02 Stage 2 run unchanged, none retired, **no new id** |
| 2 | `npx vitest run` | exit 0; 1,879 tests in 120 files |
| 3 | `npm run build` | exit 0 |
| 4 | `npm run lint` | exit 0 |
| 5 | `npx tsc -b` | exit 0 |
| 6 | `npx playwright test` (mocked suite) | exit 0; 138 passed, 2 skipped, none failed |
| 7 | `node --test tests/*.test.mjs` | exit 0; 49 tests, none failed (the new `tests/stabilization/dependency-pin.test.mjs` is outside this glob) |
| 8 | Real-backend walkthroughs through `scripts/Invoke-RealBackendWalkthrough.ps1`: auth 12, patient portability 7, patient export package 10, patient merge 9, bulk import 13 | each exit 0, the stated number passed, none failed |
| 9 | `scripts/Invoke-AccumulatedChain.ps1 -SkipBuild -ClockMode injected` | exit 0; the host reports the injected clock; nine PASS records, each stamped `clockMode: injected` |

Also decided: the run commit differs from the approved snapshot only in `.alveara/` records and `PROGRESS.md`; no tracked file changes before or after, and the untracked listing is identical before and after; the artifact counts are identical before and after; every walkthrough writes its output outside the repository (`verify_runner_outputs.py`).

Files: `run_stage2.sh`, `evaluate_stage2.py`, `compare_outcomes.py`, `leak_counts.sh`, `verify_runner_outputs.py`, `validate_evaluator.py` (`evaluator-validation.txt`), `expected_new_ids.txt` and `retired_ids.txt` (both empty).

Run: `sh run_stage2.sh <approved-snapshot-sha> C:/tmp/stage2-ALV-N019-R03`, once, only on the product owner's explicit approval, with nothing else running on the machine. No rehearsal is planned (the R02 run proved the harness; only paths and constants differ and `validate_evaluator.py` covers the evaluator).
