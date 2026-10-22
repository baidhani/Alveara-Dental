# ALV-N016 - Attempt R03 - Stage 2 regression evidence: DOES NOT QUALIFY (one client gate)

Additive regression package for attempt R03; it never replaces the Stage 1 handoff `R03.md`, its evidence or its review. The same run is the Stage 2 of `ALV-007-C01` R02 (`.alveara/handoffs/ALV-007-C01/R02-regression.md`).

## The one shared physical run

| | |
|---|---|
| Command | `sh .alveara/handoffs/ALV-N016/R03-evidence/stage2/run_stage2.sh 37d2b08420d1b24837052aafddb8994bd186d6da C:/tmp/shared_stage2` (exactly as the reviewer directed in `.alveara/reviews/ALV-N016/R03-stage1.md`), run **once**, nothing else running, template switch unset |
| Run commit (the ALV-N016 R03 Stage 1 closure commit, after the ALV-007-C01 R02 closure) | `ba51d2e321c227713001871537898a584590f084` |
| Common source commit | `37d2b08420d1b24837052aafddb8994bd186d6da` |
| Working tree | clean before (0 changed paths) and clean after |
| Files under `src/` that differ between the common source and the run commit | **0** |
| Started / ended | 2026-10-22 10:24:18 to 11:02:08 (-05:00) |
| Raw outputs | preserved unchanged in `.alveara/handoffs/ALV-N016/R03-regression-evidence/` (this folder is the single output set for both stories): `summary.txt`, `backend_test_output.txt`, `backend-tests.csv` (4,324 rows), `compare.txt`, `telemetry.jsonl`, `leak_before.txt`, `leak_after.txt`, `gates/`, and the two evaluator verdicts `RESULT_ALV-N016.txt` and `RESULT_ALV-007-C01.txt` |

## Result in one paragraph

**Everything that concerns the two stories passed; one client gate failed, in code neither story touched.** Backend: **4,324 of 4,324 passed, 0 failed, 0 skipped, in 2,055 s = 34.2 minutes** (ceiling 55 minutes); all 4,300 baseline ids and outcomes plus exactly the 24 new ids; the 5 ledger read-consistency tests and `D5_financial_history...` passed; one template build, 0 rebuilds, **0 fallbacks**, **3,289 clones**, **32 real-path fixtures**; storage, database, login, `Database`, `Temp` and backup counts identical before and after; no template artifact left; tsc, Vitest (1,544 in 97 files), build, lint and the repository checks (8) passed. **The mocked Playwright gate failed: 134 passed, 2 failed.** Both evaluators therefore print `STAGE 2 DOES NOT QUALIFY`, each with that one `FAIL` line and no other.

## The failing gate: diagnosis (not re-run; the one-run rule stands)

The two failures are one test in two projects: `architecture-map.spec.ts:145` (`[desktop]` and `[tablet]`) "the legend lists exactly the states and story types in the data": it expected a certain number of legend keys and found 7 (`toHaveCount` failed, 14 retries). Cause, from the code:

- The Command Center's Architecture Map (`assets/map.js`, `MAP_STATUS_LABELS`) knows only six statuses (Planned, Awaiting review, Changes required, Blocked, Reopened, Complete) and, for any other status, shows the **raw status text** (`MAP_STATUS_LABELS[r.status] || r.status`, line 57). The spec has the same closed table (`STATE_LABELS`, line 28) and computes its expected legend from it.
- `HANDOFF_SCHEMA.md` defines `AWAITING_REGRESSION` and `REVALIDATION_REQUIRED` as valid statuses, and this release's control records now legitimately hold both at once (`AWAITING_REGRESSION` on two stories after the two Stage 1 closures; `REVALIDATION_REQUIRED` on the eight descendants of ALV-007-C01, which the reviewer directed). With two unlabelled statuses the spec's expected set collapses them into one `undefined` entry (6 keys) while the app shows two raw labels (7 keys). In the earlier R01 Stage 2 run only one unlabelled status existed (`AWAITING_REGRESSION`), so the same test passed.
- So the defect is a **latent gap in the Command Center map and its spec (no label for two valid schema states)**, exposed by correct control-state data. No file of either story is involved (no `assets/`, client or `.alveara` data file was changed by the stories' source; the statuses are the reviewer-directed control state).

## What this does and does not claim

- It does **not** claim Stage 2 qualified for either story. The frozen contract lists the Playwright gate and the evaluators say it failed.
- It does **not** claim a product defect in the billing or the template work: every backend, template and billing criterion passed with margin (34.2 min against 55; the earlier unplanned run took 53 m 09 s).
- Per the reviewer's instruction, no standalone or second full run was made and the failing gate was not re-run or worked around.
- A fix would be small and local (give the two statuses labels in `assets/map.js`, and in the spec's `STATE_LABELS`/`command-center.spec.ts` tables, with a test that every status in `HANDOFF_SCHEMA.md` has a label), but it is **outside both stories' scope** and would change the source the run certified, so it needs the reviewer's decision on how to proceed (for example a small separately reviewed Command Center correction followed by a new shared Stage 2 on the corrected tree, or a stated rule that this gate may be judged separately).

## ALV-N016 evaluator verdict (`R03-regression-evidence/RESULT_ALV-N016.txt`, unedited)

```
PASS  run commit is the Stage 1 closure commit                                                                                     ba51d2e321c227713001871537898a584590f084
PASS  working tree clean before and after                                                                                          
PASS  no src/ file differs between the implementation and the run commit                                                           0 files
PASS  the common source differs from the ALV-N016 R02 implementation only by the approved ALV-007-C01 ledger correction (2 files)  ['src/Alveara.Api.Tests/BillingLedgerReadConsistencyTests.cs', 'src/Alveara.Api/Architecture/Billing/BillingService.Ledger.cs']
PASS  the template mode switch is not set                                                                                          
PASS  backend run exit code 0                                                                                                      
PASS  backend elapsed is 55 minutes or less                                                                                        2055s = 34.2 min (ceiling 55 min)
PASS  discovered and passed are exactly the expected count, none failed or skipped                                                 {'Passed': 4324} (expected Passed 4324)
PASS  the 5 ledger read-consistency tests ran and passed                                                                           5 found, outcomes ['Passed']
PASS  the D5 financial-history concurrency test passed                                                                             
PASS  same existing test ids and outcomes as the ALV-010-C01 baseline; exactly the 24 new ids                                      COMPARISON PASS
PASS  the run built exactly one template and it was not rebuilt                                                                    built 1
PASS  zero fallback to create-and-migrate by the run's own template                                                                0 fallback events
PASS  no template build failure                                                                                                    
PASS  the template was really used (at least 3000 clones)                                                                          3289 clones
PASS  only the named real-path classes bypass the template (at most 60 fixtures)                                                   32 real-migration fixtures
PASS  Storage, Temp, Database, Backup and Keys entries, databases, SQL logins and template backups unchanged                       before {'databases_total': 1, 'AlveraTest': 0, 'AlveraTemplate': 0, 'AlveraRestore': 0, 'sql_logins': 0, 'folder_Storage': 3920, 'folder_Keys': 1, 'folder_Backup': 1, 'folder_Database': 0, 'folder_Temp': 1, 'template_backups_in_Temp': 0} after {'databases_total': 1, 'AlveraTest': 0, 'AlveraTemplate': 0, 'AlveraRestore': 0, 'sql_logins': 0, 'folder_Storage': 3920, 'folder_Keys': 1, 'folder_Backup': 1, 'folder_Database': 0, 'folder_Temp': 1, 'template_backups_in_Temp': 0}
PASS  no AlveraTemplate database or backup left                                                                                    
PASS  gate tsc                                                                                                                     exit 0
PASS  gate vitest                                                                                                                  exit 0
PASS  gate build                                                                                                                   exit 0
PASS  gate lint                                                                                                                    exit 0
FAIL  gate playwright_mocked                                                                                                       exit 1
PASS  gate repo_checks                                                                                                             exit 0

STAGE 2 DOES NOT QUALIFY
```

## Acceptance items decided by this run
- **Item 7** (same ids and outcomes, zero failures and skips, zero unexpected fallback, clean gates): the backend, ids, outcomes, fallback and cleanup parts **pass**; the clean-gates part **fails** on the mocked Playwright gate described above.
- **Item 8** (one formal Stage 2 backend run in 55 minutes or less): **met: 34.2 minutes** (2,055 s).

## Status recorded
`CHANGES_REQUIRED`, `fullRegression: "FAILED"`, blocking issue ALV-N016-R03-01 (the Playwright gate), following the precedent of R01; the reviewer's final review decides what the failure means for each attempt.
