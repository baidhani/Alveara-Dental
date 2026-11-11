# ALV-N019 R03 - Stage 2 regression addendum: STAGE 2 DOES NOT QUALIFY (one environmental test failure)

## Identity
- Story `ALV-N019`, attempt `R03` (an additive addendum; the R03 Stage 1 handoff, evidence, ZIP and review, and all R01 and R02 records, are unchanged).
- R03 implementation commit (the approved snapshot): `56b05c02d5e447a8286e1dcd2f3750560f34f3cb`. Stage 1 review: `.alveara/reviews/ALV-N019/R03-stage1.md` (`APPROVED_PENDING_REGRESSION`); closure and run commit: `54d889e` (control files only).
- Run exactly once, with the product owner's explicit approval: `sh .alveara/handoffs/ALV-N019/R03-evidence/stage2/run_stage2.sh 56b05c02d5e447a8286e1dcd2f3750560f34f3cb C:/tmp/stage2-ALV-N019-R03` (2026-11-10, 18:19:05 to 19:59:47 -06:00). It was **not** rerun. After it ended I made one read-only diagnosis (below); no file under the run commit was changed.
- Equivalence: no tracked file changed before or after the run; nothing outside `.alveara/` and `PROGRESS.md` differs between the approved snapshot and the run commit (the evaluator's criterion passed).
- Machine condition: nothing of mine other than the run was running, apart from my progress timers (read-only checks of `summary.txt` every 9 minutes or less). No rehearsal was made for R03.

## Verdict of the frozen evaluator: STAGE 2 DOES NOT QUALIFY (21 of 25 criteria passed, 4 failed)
Full results: `R03-regression-evidence/RESULT_ALV-N019_R03.txt` (with `console.txt`, `compare.txt`, `summary.txt`, `gates/`, `walkthroughs/`, `chain_evidence/`, `backend-tests.csv`, `leak_before.txt`, `leak_after.txt`, `focused-diagnosis.txt`).

**The four failed criteria all come from one cause: a single backend test.**
1. `backend run exit code 0`: the backend run exited 1.
2. `discovered and passed are exactly the expected count`: 5,696 Passed and 1 Failed (expected 5,697 Passed).
3. the id comparison against the R02 baseline: the same one test is Failed (5,696 ids unchanged, none missing, no new id).
4. `artifact counts unchanged`: the failed test's fixture left one database behind (see below).

**The failed test:** `Alveara.Api.Tests.ImportIntakeServiceTests.A_malformed_file_is_refused_with_its_problems_logged_by_codes_only_and_stored_nowhere`, at 18:35:00 (about 16 minutes into the backend run). The error is in the test's **setup** (`InitializeAsync`, first `SaveChangesAsync`): `System.InvalidOperationException: Timeout expired. The timeout period elapsed prior to obtaining a connection from the pool. This may have occurred because all pooled connections were in use and max pool size was reached.` It is a connection-pool timeout, not an assertion and not product code.

**Everything else passed in the same run:** vitest 1,879 in 120 files, build, lint, `tsc -b`, the mocked Playwright suite (138 passed, 2 skipped), repository checks (49), the five real-backend walkthroughs (auth 12, patient portability 7, patient export package 10, patient merge 9, bulk import 13), and the stage-0 chain with the injected-clock host (nine PASS, `clockMode: injected`). 5,696 of the 5,697 backend ids Passed.

## Diagnosis (`R03-regression-evidence/focused-diagnosis.txt`)
- **The leftover:** the fixture created its database at 18:35:00.610 (`AlveraTest_72e0ccf142994bd19eafa80c0a7c81a8`) and failed before its cleanup ran, so the database and its 2 files stayed (`databases_total` 1 to 2, `AlveraTest` 0 to 1, `folder_Database` 0 to 2). It is **still present and not deleted**; I will delete only that database and its two files, and only with the product owner's agreement.
- **Focused reruns of the failed test's whole class** (`ImportIntakeServiceTests`, same binaries, same machine, after the run): **3 of 3 passed** (10 tests each, 17 to 19 seconds).
- The same test passed in the accepted R02 Stage 2 run and in every Stage 1 sweep, with the same `Microsoft.Data.SqlClient` 7.1.0 and EF Core versions; only the `System.Security.Cryptography.Pkcs` pin differs.
- **Assessment (a belief, not a proof):** a transient connection-pool exhaustion in one test setup under load, environmental, and unrelated to the Pkcs pin (the package is not part of connection pooling). A repeat of the full run is the only thing that could prove the run clean.

## What is being asked of the reviewer
Under the execution-control policy a failed test makes the run "does not qualify", and an environmental inconclusive result may be rerun unchanged. The product owner's priority is to avoid another 91-minute run. The reviewer is asked to **rule on this evidence**: either (a) accept the run as environmental-inconclusive for this one test on the strength of the diagnosis and the three focused reruns, so that F-001 may be recorded as fixed without a rerun, or (b) require the unchanged full rerun, which will only start on the product owner's explicit approval.

## Not in git, and why
The raw results file `full_serial.trx` (about 9.5 MB) and the two generated export-package ZIPs are deliberately not committed; the trx is in the regression ZIP. `backend-tests.csv` is derived from it (5,697 ids; 5,696 Passed, 1 Failed).

## State
The story is `AWAITING_REVIEW` with `fullRegression: FAILED` and the decision is the reviewer's. It is not `COMPLETE`; `ALV-N020` is not started. Nothing is pushed.
