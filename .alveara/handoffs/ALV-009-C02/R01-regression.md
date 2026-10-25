# ALV-009-C02 R01 - Stage 2 regression addendum: STAGE 2 QUALIFIES

## Identity
- Story `ALV-009-C02`, attempt `R01` (an additive addendum; the R01 Stage 1 handoff, evidence, ZIP and review are unchanged).
- Implementation commit: `36f817efaeced07cbcfa718c5ba3170d7b263749`.
- Stage 1 review-closure commit and run commit: `8dbaca55f9137861bc4a9e19cc662d4d0b595c07` (`.alveara/reviews/ALV-009-C02/R01-stage1.md`, `APPROVED_PENDING_REGRESSION`).
- Run once, on a clean tree, with nothing else running: `sh .alveara/handoffs/ALV-009-C02/R01-evidence/stage2/run_stage2.sh 36f817efaeced07cbcfa718c5ba3170d7b263749 C:/tmp/alv009c02/stage2` (2026-10-24, 21:37 to 22:50 -05:00). It was not rerun and no file was changed during or after it.
- Equivalence: no `src/` file differs between the implementation and the run commit (0 files); the working tree was clean before and after (0 changed paths). The run commit differs from the implementation only by the R01 handoff and evidence files, PROGRESS.md and the review closure (review artifact, `EXECUTION_STATUS.json`, `BUILD_STATE.md`).

## Verdict of the frozen evaluator: STAGE 2 QUALIFIES (26 of 26 criteria passed)
Full results: `R01-regression-evidence/RESULT_ALV-009-C02.txt` (with `compare.txt`, `gates/`, `walkthroughs/`, `trx/full_serial.trx`, `backend-tests.csv`, `summary.txt`, `telemetry.jsonl`, leak counts).
- **Backend: exit 0; 5,023 passed, 0 failed, 0 skipped** (exactly the expected count). Every one of the 4,906 ALV-009-C01 R02 Stage 2 ids is present with the same outcome and the new ids are exactly the 117 allowlisted ones (`compare.txt`: 0 missing, 0 changed, 0 not passed, "new ids: 117 (expected 117)", COMPARISON PASS).
- Template: built once, never rebuilt, 0 fallbacks, 3,810 clones (at least 3,000), 32 real-path fixtures (at most 60). Artifact counts identical before and after the run (`leak_before.txt`, `leak_after.txt`: two databases, `AlveraDev` and the one pre-existing scratch `AlveraTest_76fbf02b...` disclosed in the Stage 1 handoff; no template, restore or additional test database, SQL login or template backup left by the run).
- `tsc`, Vitest (1,647 in 103 files), build, lint, mocked Playwright (140) and repository checks (13): exit 0.
- Real-backend walkthroughs on fresh databases: external cases **9**, work queue **8**, follow-ups **8**, notifications **10**, permission policy **7**, permission-policy administration **8**; all passed, none failed.
- **Backend elapsed (operational evidence only, no limit applies): 3,830 s = 63.8 min** (ALV-009-C01 R02: 62.2 min).

## What did not change
No product, test or configuration file between the implementation and this run. Nothing is pushed.

## Disclosures carried from Stage 1
A chase job that fails all its attempts stays `Failed` (not reset automatically); the walkthrough inserts its document by SQL and makes the case overdue with a past date; the one empty scratch test database `AlveraTest_76fbf02b...` was already on the machine before the run and remains (its origin is not established; removing it is the owner's decision).
