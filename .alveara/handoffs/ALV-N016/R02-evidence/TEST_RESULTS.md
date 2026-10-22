# Test results - ALV-N016 R02 (Stage 1)

**Cadence classification: HIGH RISK** (test infrastructure shared by every database test). **A Stage 1 package: the formal Stage 2 backend run is `PENDING` and has not been run.** The one full run that did happen was unplanned and is not Stage 2 evidence (see `DECISION_REQUEST.md`, section 3). Baseline: the ALV-010-C01 R03 Stage 2 run (4,300 tests, 94 m 14 s); the R01 formal Stage 2 result is in `R01-regression.md` (4,318 of 4,318 passed in 3,466 s, which missed the 3,300 s ceiling by 166 s).

## What R02 changes

Implementation `0da6f36e6321b46a26d5c63c13fcd2d1c9908aaf`: the 13 rollback classes start from a clone instead of the real create-and-migrate path (6 classes keep it), the inventory names all three groups, and a new test proves a clone rolled back and forward through the real migrator has exactly the schema of a real create-and-migrate. Nothing else in `src/` differs from the R01 implementation `3ae51e2` (the prefetch pool that was built and reverted nets to nothing).

## Fresh results on this tree

| What | Result |
|---|---|
| The inventory guard (`RealMigrationInventoryTests`, 5) and the template tests (`TestDatabaseTemplateTests`, 14, including the clone-rollback proof) | **19 of 19 passed** |
| Negative controls of the changed inventory and guard (`artifacts/negative-controls/negative-controls-r02.txt`; the 13 of R01 are kept as `negative-controls-r01.txt`) | **5 of 5 breakages caught**: a rollback class marked real again; a class in the wrong group; the migration-pipeline class without its real fixture; a class dropped from the inventory; the base class ignoring the attribute |
| The 13 moved classes, 250 tests, on the real path (R01 tree) against on clones (R02), run back to back | **666 s against 184 s**, 250 of 250 passed both ways; 0 fallbacks; counts of storage, databases, logins, `Database` and `Temp` unchanged (`artifacts/rollback-on-clones-ab/`) |
| Prefetch experiment (reverted), 273 ordinary tests, five runs | see the table below; no gain beyond noise (`DECISION_REQUEST.md`) |

### Prefetch experiment runs (artifacts/prefetch-experiment/)

| Depth | Elapsed | Passed |
|---|---|---|
| depth 0 | 3 m 00 s (180 s) | 273 of 273 |
| depth 2 | 3 m 05 s (185 s) | 273 of 273 |
| depth 4 | 2 m 46 s (166 s) | 273 of 273 |
| depth 0 | 3 m 05 s (185 s) | 273 of 273 |
| depth 2 | 2 m 39 s (159 s) | 273 of 273 |


## The unplanned full run (NOT Stage 2 evidence)


## What the unplanned full run measured (not Stage 2 evidence)

Commit `0da6f36e6321b46a26d5c63c13fcd2d1c9908aaf`, started 2026-10-21 23:25, ended 2026-10-22 00:19: the whole backend suite, **4,319 tests: 4,318 passed, 1 failed, 0 skipped, 3189 s (53 m 09 s)**; the run's own template: 1 build, 0 rebuilds, **0 fallbacks**, 3,284 clones, **32 real-migration fixtures** (it was 281 in R01), restore time 16.2 min in total; storage, database, login, `Database` and `Temp` counts identical before and after. The one failure is described in `DECISION_REQUEST.md` (a latent race in production billing code), not in the infrastructure.

Per-test results are in `artifacts/unplanned-full-run/tests.csv`.

## What this package does not claim

- No formal Stage 2 result. The unplanned run suggests about 53.1 minutes for the whole suite on this code, **111 s under the 3,300 s ceiling, a thin margin (about 3.4 %)** against run-to-run noise of about 7 % observed on this machine; it also had one failure from a production race that is not an infrastructure problem.
- The R01 projection of about 49.7 minutes (R01's 3,466 s less the measured 482 s) was not borne out by the unplanned run (53.1 min); the restore total was larger in that run (16.2 min against 13.6 min), i.e. the machine was slower.

## Frozen Stage 2 contract (to be run once after `APPROVED_PENDING_REGRESSION`)

`stage2/run_stage2.sh <implementation-sha> <out-folder>` on the closure-only code-equivalent tree, with nothing else running and a clean tree. `stage2/evaluate_stage2.py` decides every line:

1. The run commit is the Stage 1 closure commit; the tree is clean before and after; no `src/` file differs from the implementation `0da6f36`; `ALVERA_TEST_TEMPLATE` is not set.
2. `cd src\Alveara.Api.Tests; dotnet test` (adding only `--logger "trx;LogFileName=full_serial.trx" --results-directory <out>/trx`): exit 0, **4,319 passed, 0 failed, 0 skipped**, elapsed **3,300 s (55 minutes) or less**.
3. `compare_outcomes.py`: all 4,300 baseline ids present with outcome Passed, none missing or not Passed, exactly 19 new ids.
4. The run's own template (`role: run`): exactly one build, 0 rebuilds, 0 `template_restore_fallback`, no build failure, at least 3,000 clones, and **at most 60 fixtures on the real create-and-migrate path** (about 32 expected).
5. Storage, `Keys`, `Backup`, `Database` and `Temp` entry counts, user databases, `AlveraTest_*`, `AlveraTemplate_*` and `AlveraRestore_*` databases, SQL logins and template backups unchanged before and after; no template artifact left.
6. `npx tsc --noEmit`, `npx vitest run` (1,544 passed in 97 files), `npm run build`, `npm run lint`, `npx playwright test` (136 passed) and `node --test tests/*.test.mjs` (8 passed).
7. Any failed line, including the time ceiling, means `CHANGES_REQUIRED` and a new Stage 1 attempt.

## Disclosures

- The unplanned full run (above and `DECISION_REQUEST.md`, section 3).
- A latent production race in billing (`DECISION_REQUEST.md`, section 2), found by that run; not fixed here.
- Three older local commits (`20d4926`, `4ed2d14`, `be29f3b`, from `ALV-010-C01`) carry a `Co-Authored-By` trailer against the owner's standing instruction; not rewritten. None of this story's commits has one.
- The prefetch pool commit and its revert are in the history; the net effect on `src/` is nothing.
- The leftovers of the prefetch experiment (4 `AlveraPool_*` databases and one stray template backup, left because a test host exited before its cleanup ran) were deleted by me; they were my own runs'.
- `ALVERA_TEST_TEMPLATE=off` remains a measurement switch the evaluator requires unset.
- No `.trx` is packaged for the focused or unplanned runs except in the ZIP for the unplanned full run (kept at `C:\tmp\n016\focused_r02\trx`).
