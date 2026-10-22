# ALV-N016 - Attempt R01 - Stage 2 regression evidence: DOES NOT QUALIFY

The frozen Stage 2 contract of `R01-evidence/TEST_RESULTS.md`, run **once** on the Stage 1 closure commit, with `stage2/run_stage2.sh`, as the reviewer's Stage 1 decision (`.alveara/reviews/ALV-N016/R01-stage1.md`) directed. **Every criterion passed except one: the backend run took 3,466 s (57.8 min) against the ceiling of 3,300 s (55 min), 166 s (5.0 %) over.** Under acceptance item 8 and the reviewer's decision that is `CHANGES_REQUIRED`, even though correctness passed.

## Run commit and equivalence

| | |
|---|---|
| Run commit (the Stage 1 closure commit) | `e89721cd8f31a1089646d4f5997a41835f9670f8` |
| Implementation commit | `3ae51e23adbd7769a0bafb71fd16731b1eee3466` |
| Reviewed status commit | `cfadd60b464e1c6796a756150b3ea6526a14dea0` |
| Working tree | clean when the run started and clean when it ended |
| Files that differ between the reviewed status commit and the run commit | exactly the three the review allowed: `.alveara/BUILD_STATE.md`, `.alveara/EXECUTION_STATUS.json`, `.alveara/reviews/ALV-N016/R01-stage1.md` |
| Files under `src/` that differ between the implementation and the run commit | **0** |
| Template mode switch | not set |

## The evaluator's verdict (`R01-regression-evidence/RESULT.txt`, unedited)

| | Criterion and result |
|---|---|
| PASS | run commit is the Stage 1 closure commit - e89721cd8f31a1089646d4f5997a41835f9670f8 |
| PASS | working tree clean before and after - |
| PASS | no src/ file differs between the implementation and the run commit - 0 files |
| PASS | the template mode switch is not set - |
| PASS | backend run exit code 0 - |
| FAIL | backend elapsed is 55 minutes or less - 3466s = 57.8 min (ceiling 55 min) |
| PASS | discovered and passed are exactly the expected count, none failed or skipped - {'Passed': 4318} (expected Passed 4318) |
| PASS | same existing test ids and outcomes as the ALV-010-C01 baseline; exactly the 18 new ids - COMPARISON PASS |
| PASS | the run built exactly one template and it was not rebuilt - built 1 |
| PASS | zero fallback to create-and-migrate by the run's own template - 0 fallback events |
| PASS | no template build failure - |
| PASS | the template was really used (at least 2500 clones) - 3034 clones; real-migration fixtures 281 |
| PASS | Storage, Temp, Database, Backup and Keys entries, databases, SQL logins and template backups unchanged - before {'databases_total': 1, 'AlveraTest': 0, 'AlveraTemplate': 0, 'AlveraRestore': 0, 'sql_logins': 0, 'folder_Storage': 3920, 'folder_Keys': 1, 'folder_Backup': 1, 'folder_Database': 0, 'folder_Temp': 1, 'template_backups_in_Temp': 0} after {'databases_total': 1, 'AlveraTest': 0, 'AlveraTemplate': 0, 'AlveraRestore': 0, 'sql_logins': 0, 'folder_Storage': 3920, 'folder_Keys': 1, 'folder_Backup': 1, 'folder_Database': 0, 'folder_Temp': 1, 'template_backups_in_Temp': 0} |
| PASS | no AlveraTemplate database or backup left - |
| PASS | gate tsc - exit 0 |
| PASS | gate vitest - exit 0 |
| PASS | gate build - exit 0 |
| PASS | gate lint - exit 0 |
| PASS | gate playwright_mocked - exit 0 |
| PASS | gate repo_checks - exit 0 |

**STAGE 2 DOES NOT QUALIFY** (one failed criterion: the time ceiling).

Started 2026-10-21T21:20:29-05:00, ended 2026-10-21T22:21:52-05:00. Backend: 4,318 discovered, 4,318 passed, 0 failed, 0 skipped; all 4,300 baseline test ids present with outcome Passed; exactly the 18 new ids; `Duration: 57 m 22 s`.

## Where the 57.8 minutes go (read from this run's own results and telemetry)

| Part | Minutes | Source |
|---|---|---|
| Inside the test bodies (sum of per-test durations) | 22.1 | `backend-tests.csv` |
| Restoring 3,034 clones (269 ms each) | 13.6 | `telemetry.jsonl`, `template_summary` of the run's own template |
| The 281 fixtures on the real create-and-migrate path (the inventory) at about 2.5 s each | about 11.7 | an estimate: the migration cost measured in the probe and in the A/B, not measured per fixture here |
| Everything else (API host start-up, drops, pool release, one 6.2 s template build) | about 10.3 | the remainder |
| **Total** | **57.8** | `summary.txt` |

The template removed most of the per-database cost (the baseline run was 94.2 minutes). What remains above the ceiling is in three places a next attempt could address within the story's boundary: the cost of each restore (269 ms in process against 150 ms in the probe; the template's files are 16 MB although the backup is 6.7 MB), the 281 real-path fixtures, and host start-up. None of this is claimed to be sufficient; it is the measured shape of the remainder.

## Retained raw results

`R01-regression-evidence/`: `summary.txt` (the run's own record), `RESULT.txt`, `compare.txt`, `backend_test_output.txt`, `backend-tests.csv` (every test with its outcome and duration), `telemetry.jsonl`, `leak_before.txt`, `leak_after.txt`, `gates/*.txt`. The raw `.trx` (7 MB, `C:\tmp\n016\stage2_run\trx\full_serial.trx`) is included in the regression ZIP and is not committed.

## Disclosures

- This is the only Stage 2 run of attempt R01. It was started once, was not interrupted, and was not repeated.
- Three older local commits (`20d4926`, `4ed2d14`, `be29f3b`, from `ALV-010-C01`) carry a `Co-Authored-By` trailer against the owner's standing instruction; not rewritten. None of this story's commits has one.
- The 55-minute ceiling is a threshold fixed by the story's prompt before the run; it is not changed here.
- The previous baseline run (94 m 14 s) and the earlier unmeasured projection of 35 to 45 minutes were both on this machine; this run's 57.8 minutes is the measured result.
