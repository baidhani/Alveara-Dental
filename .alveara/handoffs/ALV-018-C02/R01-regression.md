# ALV-018-C02 R01 - Stage 2 regression addendum: STAGE 2 QUALIFIES

## Identity
- Story `ALV-018-C02`, attempt `R01` (an additive addendum; the R01 Stage 1 handoff, evidence, ZIP and review are unchanged).
- Implementation commit: `a13d5b44b98a6c5fc72cd967e5415a4990027b73`.
- Stage 1 review-closure commit and run commit: `bf6a19e9d16ce92fea95f5f20fdf5a747ad14596` (`.alveara/reviews/ALV-018-C02/R01-stage1.md`, `APPROVED_PENDING_REGRESSION`).
- Run once, on a clean tree, with nothing else of mine running: `sh .alveara/handoffs/ALV-018-C02/R01-evidence/stage2/run_stage2.sh a13d5b44b98a6c5fc72cd967e5415a4990027b73 C:/tmp/stage2-ALV-018-C02-R01` (2026-10-28, 18:42 to 20:18 -05:00). It was not rerun and no file was changed during or after it.
- Equivalence: no `src/` file differs between the implementation and the run commit (0 files); the working tree was clean before and after (0 changed paths). The run commit differs from the implementation only by the handoff and evidence files, PROGRESS.md and the review records.
- Machine condition, disclosed: before the run I closed the extra Codex, C# language-server and Edge helper processes I could. The ChatGPT/Codex desktop app (about 15 processes, about 1.6 GB) was left running at the owner's instruction. The run was serial and the frozen evaluator has no time limit, so this does not affect any criterion.

## Verdict of the frozen evaluator: STAGE 2 QUALIFIES (29 of 29 criteria passed)
Full results: `R01-regression-evidence/RESULT_ALV-018-C02_R01.txt` (with `console.txt`, `compare.txt`, `runner_proof.txt`, `gates/`, `walkthroughs/`, `backend-tests.csv`, `summary.txt`, `telemetry.jsonl`, leak counts).
- **Backend: exit 0; 5,481 passed, 0 failed, 0 skipped** (exactly the expected count) in 85.3 minutes (1 h 24 m by the test runner). All 5,334 baseline ids of the ALV-018-C01 R02 run are present with the same outcome, none retired; the new ids are exactly the 147 allowlisted ones (`compare.txt`, COMPARISON PASS).
- Runner preflight passed before anything ran (`runner_proof.txt`); the working tree stayed clean.
- Template: built once, never rebuilt, 0 fallbacks, 4,142 clones, 37 real-path fixtures (at most 60).
- **Artifact counts identical before and after** (`leak_before.txt`, `leak_after.txt`: 1 database, `AlveraTest` 0, `AlveraTemplate` 0, `AlveraRestore` 0, no SQL login, no template backup; the Storage, Keys, Backup, Database and Temp entries unchanged).
- `tsc`, Vitest (1,790 in 114 files), build, lint, mocked Playwright (138 passed, 2 skipped) and repository checks (13): exit 0.
- Real-backend walkthroughs on fresh databases: bulk import **13**, export package **10**, STORY-018 portability **7**, patient merge **9**, workspace **14**, patients **7**, permission policy **7**, permission-policy administration **8**; all passed, none failed.
- **Backend elapsed** (operational evidence only, no limit applies): 85.3 minutes (the previous story's Stage 2: 80.9).
- The evidence folder was scanned before it was committed: no secret pattern; the only email address is the synthetic `ann@example.test`.

## What is not in git, and why
**The raw results file `full_serial.trx` (about 9 MB) and the two generated export-package ZIPs of the export-package walkthrough are deliberately not committed.** Nothing reads them from the repository. `backend-tests.csv` is derived from the trx (5,481 ids, all Passed, names unescaped) and is what the next story's Stage 2 reads as its baseline; the trx is also still in the output folder `C:/tmp/stage2-ALV-018-C02-R01/`. The regression ZIP `Alveara_Regression_ALV-018-C02_a13d5b4_R01.zip` **does contain** the trx and the two package ZIPs, so the reviewer has the exact raw files. The previous story (`ALV-018-C01` R02) committed its trx; if the reviewer wants it in git here too, it can be added back from the ZIP or the output folder without changing anything else (the evidence commit would then be a follow-up, not a rerun).

## State
The story is `AWAITING_REVIEW` for the final review with this addendum, not `COMPLETE`. Item 53 (`ALV-N006`) is not started.
