# ALV-018-C01 R02 - Test results (Stage 1)

Stage 1 under the two-stage policy, repairing the one failure of the R01 Stage 2. Focused evidence only. **Full backend regression: PENDING** (frozen contract below). No full backend run was made for this attempt.

## What changed
Two test-infrastructure files (`TestDatabaseFixture.cs`, `TestDatabaseInUseTests.cs`), commit `94e1347`; one new test. No product, migration, script, package or build configuration change since the R01 implementation `1de9de5`.

## Focused backend evidence (raw logs in `artifacts/test-runs/`)
- **The two tests** (`A_holder_that_never_leaves_...` and `Several_holders_are_each_named_...`) on the first run: **2 of 2 passed** (2 m 52 s).
- **10 sequential repeats** of the same two tests, each a separate `dotnet test` on fresh fixture databases, run after the first run passed: **10 of 10 runs, 2 of 2 passed each**, 2 m 52 s each (`rep1.txt` to `rep10.txt`, `progress.txt`).
- **The affected teardown suite** (every test whose full name contains `TestDatabase`, which includes the in-use and transient-retry classes and the new test): **59 of 59 passed**, 0 failed, 0 skipped, 7 m 38 s (`suite.txt`).
- No full-suite run belongs in this Stage 1.

## Why the fix is the right one
The R01 failure was `Expected: 4, Actual: 5` on a count of holder-session lines. The test now counts numbered markers written once per failed attempt by the fixture, and checks that each reported session is named. A failed attempt that sees one, two or more sessions all satisfy it; four attempts are still required exactly, the original SQL 3702 is still the reported failure and the database still remains until cleanup.

## Evaluator and runner validation (re-run for this attempt)
`stage2/validate_evaluator.py` (output `stage2/evaluator-validation.txt`): **10 of 10** scenarios behave as required, including that a run missing or failing an id is rejected and a run with 5,334 passing ids that include exactly the 198 listed new ids is accepted. `stage2/verify_runner_outputs.py` (output `stage2/runner-proof.txt`): every walkthrough in the runner passes the output variable its spec reads.

## Client, repository and walkthrough gates
Unchanged by this attempt (no client, product or script change since R01). R01's Stage 2 passed all of them on the same product tree: tsc, Vitest 1,742 in 110 files, build, lint, mocked Playwright 138 passed and 2 skipped, repository checks 13, and the seven walkthroughs 10, 7, 9, 14, 7, 7 and 8 (`.alveara/reviews/ALV-018-C01/R01-stage2-evidence/RESULT_ALV-018-C01_R01.txt`). Those results are context, not a Stage 2 pass for R02.

## Frozen Stage 2 contract
Run once on the Stage 1 review-closure commit (equivalence rule of HANDOFF_SCHEMA section 4), clean tree, nothing else running:
`sh .alveara/handoffs/ALV-018-C01/R02-evidence/stage2/run_stage2.sh <implementation-sha> <out-folder>` (the out-folder outside the repository).
It runs the full serial backend suite with trx, tsc, full Vitest, build, lint, mocked Playwright, repository checks and the seven real-backend walkthroughs, with artifact counts before and after; `evaluate_stage2.py` decides and writes `RESULT_ALV-018-C01_R02.txt`.
Expectations: backend exactly **5,334 passed, 0 failed, 0 skipped**; the same ids and outcomes as the 5,136-test `ALV-017-C01` R03 baseline (no retired id; this includes the repaired `A_holder_that_never_leaves_...` id passing) plus **exactly the 198 ids in `stage2/expected_new_ids.txt`** (the 197 of R01 plus `Several_holders_are_each_named_...`); template used (one template, zero fallbacks, at least 3,000 clones, at most 60 real-migration fixtures); Vitest 1,742 in 110 files; mocked Playwright 138 passed and 2 skipped; repository checks 13; walkthroughs 10, 7, 9, 14, 7, 7 and 8 passed. **There is no backend time limit**; elapsed time is reported only (R01's was 76.1 minutes). The scripts differ from R01's only in the attempt name, the two counts and the expected-ids file.
