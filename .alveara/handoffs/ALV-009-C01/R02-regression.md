# ALV-009-C01 R02 - Stage 2 regression addendum: STAGE 2 QUALIFIES

## Identity
- Story `ALV-009-C01`, attempt `R02` (an additive addendum; the R02 Stage 1 handoff, evidence, ZIP and review are unchanged; the failed R01 regression `R01-regression.md` and its ZIP remain immutable).
- Implementation commit: `340624b0fb9c54b32c82782e7c6b2e90bd651b74`.
- Stage 1 review-closure commit and run commit: `80775d7f2e9f9d00d364450d0f6ca35abb37779c` (`.alveara/reviews/ALV-009-C01/R02-stage1.md`, `APPROVED_PENDING_REGRESSION`).
- Run once, on a clean tree, with nothing else running: `sh .alveara/handoffs/ALV-009-C01/R02-evidence/stage2/run_stage2.sh 340624b0fb9c54b32c82782e7c6b2e90bd651b74 C:/tmp/alv009c01/stage2-r02` (2026-10-24, 14:20 to 15:29 -05:00). It was not rerun and no file was changed during or after it.
- Equivalence: no `src/` file differs between the implementation and the run commit (0 files); the working tree was clean before and after (0 changed paths). The run commit differs from the implementation only by the R02 handoff and evidence files, PROGRESS.md and the review closure (review artifact, `EXECUTION_STATUS.json`, `BUILD_STATE.md`).

## Verdict of the frozen evaluator: STAGE 2 QUALIFIES (25 of 25 criteria passed)
Full results: `R02-regression-evidence/RESULT_ALV-009-C01.txt` (with `compare.txt`, `gates/`, `walkthroughs/`, `trx/full_serial.trx`, `backend-tests.csv`, `summary.txt`, `telemetry.jsonl`, leak counts).
- **Backend: exit 0; 4,906 passed, 0 failed, 0 skipped** (exactly the corrected expected count). Every one of the 4,734 STORY-009 final-run ids is present with the same outcome and the new ids are exactly the 172 allowlisted ones (`compare.txt`: 0 missing, 0 changed, 0 not passed, "new ids: 172 (expected 172)", COMPARISON PASS). The ten `RecallPermissionTests` ids R01 omitted are in the allowlist and passed.
- Template: built once, never rebuilt, 0 fallbacks, 3,742 clones (at least 3,000), 32 real-path fixtures (at most 60). Leak counts identical before and after (one database, `AlveraDev`; no test, restore or template database, SQL login or template backup left).
- `tsc`, **Vitest (1,617 in 102 files, including `PatientWorkspace.test.tsx`, which failed once in the R01 run)**, build, lint, mocked Playwright (140) and repository checks (13): exit 0.
- Real-backend walkthroughs on fresh databases: work queue **8**, follow-ups **8**, notifications **10**, permission policy **7**, permission-policy administration **8**; all passed, none failed.
- **Backend elapsed (operational evidence only, no limit applies): 3,731 s = 62.2 min**, against 2,410 s = 40.2 min for the R01 run on the same tests. I did not find the cause; the machine ran nothing else that I started, and the test host was consuming CPU throughout (sampled at 60 minutes), so it was slow rather than stuck. Disclosed here because the difference is large; it does not affect any criterion.

## What did not change
No product, test or configuration file between the implementation and this run. Nothing is pushed.

## Disclosures carried from R01 and R02
Recall tasks are not closed when the patient books; failed recall/treatment jobs are not auto-reset; the browser demo seeds the old visit in SQL and omits accepted-but-unscheduled treatment; restart safety is proven by runner-recovery tests rather than by killing the process (see `R01.md` and `R02.md`).
