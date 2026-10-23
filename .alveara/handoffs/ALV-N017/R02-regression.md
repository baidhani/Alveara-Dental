# ALV-N017 R02 - Stage 2 regression addendum

## Identity
- Story `ALV-N017`, attempt `R02` (an additive addendum; the R02 Stage 1 handoff, evidence, ZIP and review are unchanged).
- Implementation commit: `134fbdfc77097638c936f45edeb0c86b7c386090` (its `src/` tree is identical to R01's product commit `266d1e3`).
- Stage 1 review-closure commit and run commit: `e2a42636118908951a5aff871aef353fa4347bef` (`.alveara/reviews/ALV-N017/R02-stage1.md`, `APPROVED_PENDING_REGRESSION`).
- Run once, on a clean tree, with nothing else running: `sh .alveara/handoffs/ALV-N017/R02-evidence/stage2/run_stage2.sh 134fbdfc77097638c936f45edeb0c86b7c386090 C:/tmp/n017/stage2/out` (2026-10-23, 11:16 to 11:58 -05:00).
- Equivalence: no `src/` file differs between the implementation and the run commit (0 files); the working tree was clean before and after (0 changed paths). The run commit differs from the implementation only by the R02 handoff and evidence files, PROGRESS.md and the review closure (review artifact, `EXECUTION_STATUS.json`, `BUILD_STATE.md`).

## Verdict of the frozen evaluator: STAGE 2 QUALIFIES (every criterion passed)
Full results: `R02-regression-evidence/RESULT_ALV-N017.txt`.
- **Backend: exit 0; 4,569 passed, 0 failed, 0 skipped** (exactly the expected count). Same ids and outcomes as the 4,410-test ALV-N015 R02 baseline, plus exactly the 159 allowlisted new ids, no more and no fewer (`compare.txt`: COMPARISON PASS).
- Template: built once, never rebuilt, 0 fallbacks, 3,460 clones (at least 3,000), 32 real-path fixtures (at most 60). Leak counts unchanged, no template database or backup left.
- tsc, Vitest (1,553 in 98 files), build, lint, mocked Playwright (140) and repository checks (13): exit 0.
- Real-backend walkthroughs: permission policy **7**, notifications **10**, documents **14**, documents library **12**, all passed, none failed.
- Performance budget (explicit run after the suite, worst of three repeats): warm path worst p95 **1.082 ms** (added over the 1.20 ms baseline: none; budget 1.0 ms), reload p95 **1.389 ms** (standard dataset; budget 25 ms) and **5.29 ms** (500 stored rows; budget 50 ms), policy write p95 **6.174 ms** (budget 100 ms).
- The restore drill (`PermissionPolicyBackupRestoreTests`) ran inside the 4,569 and passed.
- **Backend elapsed (operational evidence only, no limit applies): 2,168 s = 36.1 min** (ALV-N016: 34.2 min; ALV-N015: 53.6 min).

## Disclosure
The leak counts show one `AlveraTest_...` database present both before and after the run (`databases_total` 2, unchanged). It was created at 10:58 that morning, before this Stage 2 started at 11:16, during my own evidence runs (the exact test run was not established), so it is not a product of this Stage 2; the criterion is that counts are unchanged, which holds. It has not been deleted; I will remove it only if the owner agrees.

## Not changed by this run
No product, test or configuration file. Not pushed.
