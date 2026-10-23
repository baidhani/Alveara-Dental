# ALV-N015 R02 - Stage 2 regression addendum

## Identity
- Story `ALV-N015`, attempt `R02` (an additive addendum; the R02 Stage 1 handoff, evidence, ZIP and review are unchanged).
- Implementation commit: `71bf8499737135df5b2079c4ce744a56ac65d991`.
- Stage 1 review-closure commit and run commit: `d3a0d307e2e0a2bc360b31b233fb861c215bc74b` (`.alveara/reviews/ALV-N015/R02-stage1.md`, `APPROVED_PENDING_REGRESSION`).
- Run once, on a clean tree, with nothing else running: `sh .alveara/handoffs/ALV-N015/R02-evidence/stage2/run_stage2.sh 71bf8499737135df5b2079c4ce744a56ac65d991 C:/tmp/n015/stage2/out` (2026-10-23, 07:09 to 08:09 -05:00).
- Equivalence: no `src/` file differs between the implementation and the run commit (0 files); the working tree was clean before and after (0 changed paths). The run commit differs from the implementation only by the R02 handoff and evidence files, PROGRESS.md and the review closure (review artifact, `EXECUTION_STATUS.json`, `BUILD_STATE.md`).
- To keep the tree clean, an uncommitted line-ending-only change to `docs/design/ADMIN_GRANTABLE_PERMISSIONS_PROPOSAL.md` was stashed for the run and restored afterwards (the stash held no content difference).

## Verdict of the frozen evaluator: STAGE 2 DOES NOT QUALIFY (2 of 24 criteria fail)
Full results: `R02-regression-evidence/RESULT_ALV-N015.txt`. **22 criteria pass**, including:
- Backend: exit 0; **4,410 tests passed, 0 failed, 0 skipped**; elapsed 3,218 s = **53.6 min** (ceiling 55 min).
- Same existing test ids and outcomes as the 4,324-test ALV-N016 R03 baseline: **0 missing, 0 outcome changes, 0 not passed** (`compare.txt`).
- Template: built once, never rebuilt, 0 fallbacks, 3,369 clones (at least 3,000), 32 real-path fixtures (at most 60).
- Leak counts unchanged; no template database or backup left.
- tsc, Vitest, build, lint, mocked Playwright and repository checks: exit 0 (the evaluator's Vitest 1,553 in 98 files and Playwright 140 and repository-check 13 expectations hold).
- Real-backend walkthroughs: notifications 10, documents 14, documents-library 12, all passed, none failed.

## The two failing criteria (both are count expectations I froze)
1. "discovered and passed are exactly the expected count": expected **4,393**, observed **4,410** (all Passed).
2. "same ids ... plus exactly the 69 new ids": observed **86** new ids (0 missing, 0 changed, 0 not passed); the check fails only because 86 is not 69.

**Cause (my error, not a product failure):** the expected counts came from `dotnet test --list-tests`, which renders parameterized rows differently from the results file and under-counts. The 86 new ids are: `NotificationSchemaTests` 56, `NotificationApiTests` 24, `NotificationPermissionTests` 6. The 17 beyond my 69 are parameterized rows (permission and role data rows) of the new tests. Each new id passed. The Stage 1 package already disclosed that the list and results files differ for parameterized rows.

## Disposition requested
Under HANDOFF_SCHEMA section 4 a failed frozen check is a regression failure, so `fullRegression` is recorded as `FAILED`. I am not rerunning, not editing the frozen contract and not changing the evaluator. I request that the reviewer rule whether the two count criteria are a contract miscount (every test passed, nothing missing or changed, 86 new ids all passing) so that the run qualifies, or whether a corrected Stage 2 contract must be run. If a rerun is ordered, the 53.6 minute duration (earlier ALV-N016 run: 34 minutes) leaves only 1.4 minutes under the ceiling and should be considered; I have not investigated the difference (the machine was otherwise idle for this run).

## Not changed by this run
No product, test, build or configuration file. Not pushed.
