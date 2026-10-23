# ALV-N018 R02 - Stage 2 regression addendum

## Identity
- Story `ALV-N018`, attempt `R02` (an additive addendum; the R02 Stage 1 handoff, evidence, ZIP and review are unchanged).
- Implementation commit: `f70e93f81a1f89ea9087c651917a2c1489d43731`.
- Stage 1 review-closure commit and run commit: `6f2e4fc974c489022eaa47af6eac059e9ade3f07` (`.alveara/reviews/ALV-N018/R02-stage1.md`, `APPROVED_PENDING_REGRESSION`).
- Run once, on a clean tree, with nothing else running: `sh .alveara/handoffs/ALV-N018/R02-evidence/stage2/run_stage2.sh f70e93f81a1f89ea9087c651917a2c1489d43731 C:/tmp/n018-stage2` (2026-10-23, 16:32 to 17:15 -05:00).
- Equivalence: no `src/` file differs between the implementation and the run commit (0 files); the working tree was clean before and after (0 changed paths). The run commit differs from the implementation only by the R02 handoff and evidence files, PROGRESS.md and the review closure (review artifact, `EXECUTION_STATUS.json`, `BUILD_STATE.md`).

## Verdict of the frozen evaluator: STAGE 2 QUALIFIES (every criterion passed)
Full results: `R02-regression-evidence/RESULT_ALV-N018.txt`.
- **Backend: exit 0; 4,628 passed, 0 failed, 0 skipped** (exactly the expected count). Same ids and outcomes as the 4,569-test ALV-N017 R02 baseline except the one declared retired id, plus exactly the 60 allowlisted new ids, no more and no fewer (`compare.txt`: COMPARISON PASS).
- Template: built once, never rebuilt, 0 fallbacks, 3,519 clones (at least 3,000), 32 real-path fixtures (at most 60). Leak counts unchanged (one database, `AlveraDev`; no `AlveraTest`, no `AlveraTemplate`, no restore database, no template backup left).
- tsc, Vitest (1,572 in 100 files), build, lint, mocked Playwright (140) and repository checks (13): exit 0.
- Real-backend walkthroughs: permission policy **7**, permission policy admin **8**, notifications **10**, documents **14**, documents library **12**, all passed, none failed.
- Performance budget (explicit run after the suite, worst of three repeats): warm path worst p95 **1.084 ms** (added over the 1.20 ms baseline: none; budget 1.0 ms), reload p95 **1.427 ms** (standard dataset; budget 25 ms) and **3.21 ms** (500 stored rows; budget 50 ms), policy write p95 **10.373 ms** (budget 100 ms).
- The restore drill (`PermissionPolicyBackupRestoreTests`, 3 tests including the one that proves API-written policy changes survive restore) ran inside the 4,628 and passed.
- **Backend elapsed (operational evidence only, no limit applies): 2,168 s = 36.1 min** (ALV-N017: 36.1 min; ALV-N016: 34.2 min; ALV-N015: 53.6 min).

## Disclosure
No leftover databases, storage folders or template backups: the before and after counts are identical and show no test database. The one value of note is that the policy write p95 (10.4 ms) is higher than ALV-N017's repair-transaction measurement (6.2 ms); the budget is 100 ms and the measured operation is the same one as before (the invalid-row repair transaction), so I attribute the difference to machine noise rather than a change; the new override write was not separately budgeted.

## Not changed by this run
No product, test or configuration file. Not pushed.
