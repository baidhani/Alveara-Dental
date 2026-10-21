# ALV-010-C01 - Attempt R03 - Stage 2 regression evidence

The frozen Stage 2 contract of `R03-evidence/TEST_RESULTS.md`, run once on the Stage 1 closure commit, as the reviewer's Stage 1 decision (`.alveara/reviews/ALV-010-C01/R03-stage1.md`) directed.

## Run commit and equivalence check

| | |
|---|---|
| Run commit (the Stage 1 closure commit) | `277ff0b9c0fb4d4ccd61d82c768a4242e08a2ec4` |
| Implementation commit | `a3b4bc92db59043a31ba089cc31dbe2fd4ce25ac` (`src/` identical to `f3eb57b`, the tree the R01 Stage 1 review approved) |
| Reviewed status commit | `d6579ba871394a8ce62c88a3d0de721e812d7351` |
| Working tree when the run started | clean (0 changed paths) |
| Working tree when the run ended | clean (0 changed paths) |
| Files that differ between the reviewed status commit and the run commit | exactly the three the review allowed: `.alveara/BUILD_STATE.md`, `.alveara/EXECUTION_STATUS.json`, `.alveara/reviews/ALV-010-C01/R03-stage1.md` |
| Files under `src/` that differ between the implementation commit and the run commit | **0** (`git diff --name-only a3b4bc9 277ff0b -- src` is empty) |

Between the implementation commit and the closure commit lie only the R03 evidence and status commits (`131e8e1`, `d6579ba`: `.alveara/` and `PROGRESS.md`), both of which the reviewer reviewed. No source, test, migration, script, package or build configuration changed.

## Results

| Step | Command | Expected | Result |
|---|---|---|---|
| Before counts | `Test-Data\Storage` entries, user databases, SQL logins | recorded | storage **3920**, databases **3** (`AlveraTest_*` 2, `AlveraRestore_*` 0, `AlveraWalk_*` 0, `alveara_*` 0), SQL logins **0** |
| Full serial backend | `cd src\Alveara.Api.Tests; dotnet test --nologo --logger "trx;LogFileName=full_serial.trx" --results-directory C:\tmp\c010c01\stage2_r03\trx` | 4,300 discovered, 0 failed, 0 skipped | **4,300 discovered, 4,300 passed, 0 failed, 0 skipped**; elapsed 5654s (1 h 34 m); `Duration: 1 h 33 m` |
| Types | `npx tsc --noEmit` | exit 0 | **exit 0** |
| Full Vitest | `npx vitest run` | 1,544 passed in 97 files | **1,544 passed in 97 files** |
| Production build | `npm run build` | exit 0 | **exit 0** |
| Lint | `npm run lint` | exit 0 | **exit 0** (existing warnings only) |
| Mocked Playwright | `npx playwright test` | 136 passed | **136 passed** |
| Repository checks | `node --test tests/*.test.mjs` | 8 passed | **8 passed, 0 failed** |
| After counts | the same as before | unchanged | storage **3920**, databases **3**, SQL logins **0**: **unchanged** |

Started 2026-10-21T14:39:00-05:00, ended 2026-10-21T16:16:48-05:00. Every frozen expectation is met.

## Retained raw results

`R03-regression-evidence/`: `summary.txt` (the run's own record), `backend_test_output.txt`, `backend-tests.csv` (every test with its outcome and duration), `leak_before.txt`, `leak_after.txt`, `gates/*.txt`. The raw `.trx` (7 MB, `C:\tmp\c010c01\stage2_r03\trx\full_serial.trx`) is included in the regression ZIP and is not committed to the repository.

## Disclosures

- Three local commits (`20d4926`, `4ed2d14`, `be29f3b`) carry a `Co-Authored-By` trailer against the owner's standing instruction; they are not rewritten. Unpushed.
- Two `AlveraTest_*` databases left by two earlier interrupted runs exist before and after this run (the database count is 3 in both); they are not touched and not deleted.
- The run took 1 h 34 m, against 2 h 41 m for the historical run on `be29f3b`. The cause has not been measured here; the storage cleanup is one candidate and is not claimed.
- This is the only full regression run on this tree. It was started once and was not interrupted.
