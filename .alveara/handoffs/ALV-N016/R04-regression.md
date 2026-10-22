# ALV-N016 - Attempt R04 - Stage 2 (equivalence + frontend gate) regression evidence: DOES NOT QUALIFY (one unrelated Vitest test)

Additive regression package for attempt R04; it never replaces the Stage 1 handoff `R04.md`, its evidence or its review. The same run is the Stage 2 of `ALV-007-C01` R03 (`.alveara/handoffs/ALV-007-C01/R03-regression.md`).

## The one shared Stage 2 run (as the reviewer decided: equivalence plus one fresh frontend gate; no backend run)

| | |
|---|---|
| Frontend gate | `sh .alveara/handoffs/ALV-CONTROL-C01/R01-evidence/stage2/run_frontend_gate.sh 69c6f7fe0c4155338b8d752804878d44784348a6 C:/tmp/resubmit_frontend`, run **once**, nothing else running |
| Run commit (the shared closure commit, after both Stage 1 closure-only commits `d2890b1` and `9183c18`) | `9183c18fd36d7141fea752ce8928b96f43314e78` |
| Working tree | clean when the run started and when it ended |
| Started / ended | 2026-10-22 15:41:13 to 15:44:38 (-05:00) |
| Raw outputs | `.alveara/handoffs/ALV-N016/R04-regression-evidence/` (one set, referenced by both stories): `frontend/`, `reuse_ALV-N016.txt`, `reuse_ALV-007-C01.txt`, `flake-diagnostic/` |

## Verdicts

**Frontend gate (`frontend/RESULT.txt`, unedited): DOES NOT QUALIFY, one line: Vitest.**
```
PASS  working tree clean before and after                                                                 
PASS  no file other than the control records differs between the reviewed data commit and the run commit  ['.alveara/handoffs/ALV-007-C01/R03-evidence/ACCEPTANCE_EVIDENCE.md', '.alveara/handoffs/ALV-007-C01/R03-evidence/CHANGED_FILES.md', '.alveara/handoffs/ALV-007-C01/R03-evidence/DECISION_REQUEST.md', '.alveara/handoffs/ALV-007-C01/R03-evidence/DEMO_EVIDENCE.md', '.alveara/handoffs/ALV-007-C01/R03-evidence/NEXT_STORY_IMPACT.md', '.alveara/handoffs/ALV-007-C01/R03-evidence/PARENT_REGRESSION.md', '.alveara/handoffs/ALV-007-C01/R03-evidence/TEST_RESULTS.md', '.alveara/handoffs/ALV-007-C01/R03.md', '.alveara/handoffs/ALV-N016/R04-evidence/ACCEPTANCE_EVIDENCE.md', '.alveara/handoffs/ALV-N016/R04-evidence/CHANGED_FILES.md', '.alveara/handoffs/ALV-N016/R04-evidence/DECISION_REQUEST.md', '.alveara/handoffs/ALV-N016/R04-evidence/DEMO_EVIDENCE.md', '.alveara/handoffs/ALV-N016/R04-evidence/NEXT_STORY_IMPACT.md', '.alveara/handoffs/ALV-N016/R04-evidence/PARENT_REGRESSION.md', '.alveara/handoffs/ALV-N016/R04-evidence/TEST_RESULTS.md', '.alveara/handoffs/ALV-N016/R04-evidence/stage2/compare_outcomes.py', '.alveara/handoffs/ALV-N016/R04-evidence/stage2/evaluate_reuse.py', '.alveara/handoffs/ALV-N016/R04-evidence/stage2/evaluate_stage2.py', '.alveara/handoffs/ALV-N016/R04-evidence/stage2/evaluate_stage2_alv007c01.py', '.alveara/handoffs/ALV-N016/R04-evidence/stage2/leak_counts.sh', '.alveara/handoffs/ALV-N016/R04-evidence/stage2/run_stage2.sh', '.alveara/handoffs/ALV-N016/R04.md', '.alveara/reviews/ALV-007-C01/R03-stage1.md', '.alveara/reviews/ALV-N016/R04-stage1.md']
PASS  gate tsc                                                                                            exit 0
FAIL  gate vitest                                                                                         exit 1
PASS  gate build                                                                                          exit 0
PASS  gate lint                                                                                           exit 0
PASS  gate playwright_mocked                                                                              exit 0 140 passed
PASS  gate repo_checks                                                                                    exit 0 13 pass, 0 fail

FRONTEND GATE DOES NOT QUALIFY
```
Vitest: **1 failed | 1543 passed (1,544)**, `src/pages/PatientRegistrationPage.test.tsx` > "clears a field's prompt as soon as the user starts fixing it". Everything else passed: tsc, build, lint exit 0; **mocked Playwright 140 passed / 0 failed**; **repository checks 13 passed / 0 failed**.

**Reuse evaluations on the same closure commit: both `REUSE EQUIVALENCE HOLDS`** (`reuse_ALV-N016.txt`, `reuse_ALV-007-C01.txt`): the preserved shared run had exactly one failed line, no backend or backend-test file differs, nothing outside the control records and the Command Center client differs, the tree is clean.

## The failed line: diagnosis (the gate was not rerun)
A single, unrelated client unit test failed once under the full Vitest load; it is not in a file this release (or either story) touched. A diagnostic after the fact (`flake-diagnostic/isolated-runs.txt`): the same test passes 12 of 12 times in isolation, and passed in all three earlier complete Vitest runs of this release. Its assertion checks synchronously, right after an awaited `user.type`, that a validation message has cleared, which is timing-sensitive under load. It is **not** claimed to be environment-only (the reviewer's rule for rerunning unchanged is for environment-only inconclusive runs), and the one-run rule was kept: no second gate run was made.

## What this does and does not claim
- It does **not** claim the frontend gate qualified; the frozen evaluator says it did not, on one Vitest test.
- It does not call into question the backend equivalence or the ALV-CONTROL-C01 repair (Playwright 140 and repository 13 passed).
- How to proceed is the reviewer's decision, for example: (a) treat the Vitest line as a known flaky unrelated test and allow the unchanged gate to be rerun once; or (b) require that test to be made deterministic first (a one-line change in a test of ALV-003-C01's page, then a new gate).

## Status recorded
`CHANGES_REQUIRED`, `fullRegression: "FAILED"`, blocking issue ALV-N016-R04-01 (the frozen frontend gate: one Vitest test), following the earlier precedent; the reviewer's decision is awaited.
