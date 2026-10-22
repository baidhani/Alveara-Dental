# ALV-CONTROL-C01 - Attempt R01 - Stage 2 (frontend gate) regression evidence: QUALIFIES

Additive regression package for attempt R01; it never replaces the Stage 1 handoff `R01.md`, its evidence or its review. The frozen contract is `R01-evidence/stage2/` (frontend gate only; **no backend run**, as the reviewer directed in `.alveara/reviews/ALV-CONTROL-C01/R01-stage1.md`).

## Run commit and equivalence

| | |
|---|---|
| Command | `sh .alveara/handoffs/ALV-CONTROL-C01/R01-evidence/stage2/run_frontend_gate.sh 69c6f7fe0c4155338b8d752804878d44784348a6 C:/tmp/control_gate_formal`, run **once**, nothing else running |
| Run commit (the Stage 1 closure commit) | `fcf40dd9c2d481e62b156b08ca85c1f2eb823c95` |
| Implementation commit | `69c6f7fe0c4155338b8d752804878d44784348a6` |
| Working tree | clean when the run started and clean when it ended |
| Files that differ between the reviewed evidence commit and the run commit | exactly the three the review allowed: `.alveara/BUILD_STATE.md`, `.alveara/EXECUTION_STATUS.json`, `.alveara/reviews/ALV-CONTROL-C01/R01-stage1.md` |
| Files under `src/`, `assets/`, `tests/` or `index.html` that differ between the implementation and the run commit | **0** |
| Started / ended | 2026-10-22 14:58:05 to 15:01:45 (-05:00), 3 m 40 s |
| Raw outputs | `R01-regression-evidence/`: `summary.txt`, `RESULT.txt`, `gates/` (unedited) |

## The evaluator's verdict (`R01-regression-evidence/RESULT.txt`, unedited)

```
PASS  working tree clean before and after                                                                 
PASS  no file other than the control records differs between the reviewed data commit and the run commit  []
PASS  gate tsc                                                                                            exit 0
PASS  gate vitest                                                                                         exit 0 1544 tests in 97 files
PASS  gate build                                                                                          exit 0
PASS  gate lint                                                                                           exit 0
PASS  gate playwright_mocked                                                                              exit 0 140 passed
PASS  gate repo_checks                                                                                    exit 0 13 pass, 0 fail

FRONTEND GATE QUALIFIES
```

| Required by the reviewer | Observed |
|---|---|
| all commands exit zero | tsc 0, vitest 0, build 0, lint 0, playwright 0, repository checks 0 |
| Vitest 1,544 tests in 97 files | 1,544 tests in 97 files |
| mocked Playwright 140 passed / 0 failed | 140 passed, 0 failed (16 s elapsed in the run's own timing: 23 s) |
| repository checks 13 passed / 0 failed | 13 pass, 0 fail |
| clean tree before and after | 0 changed paths before and after |

## Note on the evaluator command

The reviewer's text calls `evaluate_frontend_gate.py <output-folder>` with one argument; the frozen script (like its twin for the backend) takes `<output-folder> <implementation-sha>` and `run_frontend_gate.sh` already calls it with both, which is what produced the verdict above. Called with one argument it stops with a usage error (`ValueError: not enough values to unpack`) and decides nothing. The frozen tooling was not altered (the closure commit may not change it); called again by hand with the implementation sha it returned `FRONTEND GATE QUALIFIES` on the same output.

## Status recorded
`AWAITING_REVIEW`, `fullRegression: "PASSED"` (the frontend-only gate); not `COMPLETE`. The reviewer's final review decides completion. The separate re-qualification of ALV-N016 R03 and ALV-007-C01 R02 under a new frozen shared plan follows this item's final approval and is not part of it.
