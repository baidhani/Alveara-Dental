# ALV-018-C01 R02 - Stage 2 regression addendum: STAGE 2 QUALIFIES

## Identity
- Story `ALV-018-C01`, attempt `R02` (an additive addendum; the R02 Stage 1 handoff, evidence, ZIP and review are unchanged). Prior attempt R01 is preserved unchanged, including its failed Stage 2 (`.alveara/reviews/ALV-018-C01/R01-stage2-evidence/`).
- Implementation commit: `94e1347e97582b3f89e8a359aaa49618328ce8b3`.
- Stage 1 review-closure commit and run commit: `426257ba2ed2353d4f18233a6c712e56948a5f9e` (`.alveara/reviews/ALV-018-C01/R02-stage1.md`, `APPROVED_PENDING_REGRESSION`).
- Run once, on a clean tree, with nothing else running: `sh .alveara/handoffs/ALV-018-C01/R02-evidence/stage2/run_stage2.sh 94e1347e97582b3f89e8a359aaa49618328ce8b3 C:/tmp/stage2-ALV-018-C01-R02` (2026-10-27, 13:04 to 14:34 -05:00). It was not rerun and no file was changed during or after it.
- Equivalence: no `src/` file differs between the implementation and the run commit (0 files); the working tree was clean before and after (0 changed paths). The run commit differs from the implementation only by the handoff and evidence files, PROGRESS.md and the review records.

## Verdict of the frozen evaluator: STAGE 2 QUALIFIES (28 of 28 criteria passed)
Full results: `R02-regression-evidence/RESULT_ALV-018-C01_R02.txt` (with `console.txt`, `compare.txt`, `runner_proof.txt`, `gates/`, `walkthroughs/`, `trx/full_serial.trx`, `backend-tests.csv`, `summary.txt`, `telemetry.jsonl`, leak counts).
- **Backend: exit 0; 5,334 passed, 0 failed, 0 skipped** (exactly the expected count) in 80.9 minutes (1 h 20 m by the runner). All 5,136 baseline ids of the ALV-017-C01 R03 run are present with the same outcome, none retired; the new ids are exactly the 198 allowlisted ones (`compare.txt`, COMPARISON PASS). This includes `TestDatabaseInUseTests.A_holder_that_never_leaves_...`, the one test that failed in R01's Stage 2, and the new `Several_holders_are_each_named_...`.
- Runner preflight passed before anything ran (`runner_proof.txt`); the working tree stayed clean.
- Template: built once, never rebuilt, 0 fallbacks, 4,023 clones, 37 real-path fixtures (at most 60).
- **Artifact counts identical before and after** (`leak_before.txt`, `leak_after.txt`: 1 database, `AlveraTest` 0, `AlveraTemplate` 0, `AlveraRestore` 0, no SQL login, no template backup; the Storage, Keys, Backup, Database and Temp entries unchanged).
- `tsc`, Vitest (1,742 in 110 files), build, lint, mocked Playwright (138 passed, 2 skipped) and repository checks (13): exit 0.
- Real-backend walkthroughs on fresh databases: export package **10**, STORY-018 portability **7**, patient merge **9**, workspace **14**, patients **7**, permission policy **7**, permission-policy administration **8**; all passed, none failed.
- **Backend elapsed** (operational evidence only, no limit applies): 80.9 minutes (R01's run: 76.1).
- The evidence folder was scanned before it was committed: no secret values; the only email is the synthetic `ann@example.test`; the two generated export packages were opened and scanned too.

## State
The story is `AWAITING_REVIEW` for the final review with this addendum, not `COMPLETE`. `ALV-018-C02` (item 52) is not started.
