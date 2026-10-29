# ALV-N006 R01 - Stage 2 regression addendum: STAGE 2 QUALIFIES

## Identity
- Story `ALV-N006`, attempt `R01` (an additive addendum; the R01 Stage 1 handoff, evidence, ZIP and review are unchanged).
- Implementation commit: `4a7d6f48d98333152272bd470bcbb40ab9e3aa5e`.
- Stage 1 review-closure commit and run commit: `f3372c3a0a6c58ffadc55effaf767ee3443fbae3` (`.alveara/reviews/ALV-N006/R01-stage1.md`, `APPROVED_PENDING_REGRESSION`).
- Run once, on a clean tree: `sh .alveara/handoffs/ALV-N006/R01-evidence/stage2/run_stage2.sh 4a7d6f48d98333152272bd470bcbb40ab9e3aa5e C:/tmp/stage2-ALV-N006-R01` (2026-10-29, 09:40 to 11:17 -05:00). It was not rerun and no file was changed during or after it.
- Equivalence: no `src/` file differs between the implementation and the run commit (0 files, checked by the evaluator); the working tree was clean before and after (0 changed paths). The run commit differs from the implementation only by the handoff and evidence files, `PROGRESS.md` and the review records.
- Machine condition, disclosed: nothing of mine other than the run was running. During the run I made three read-only checks at about the 50 minute mark (a process listing, one count of test databases and a look at the output folder). I did not close other applications on the machine, so I cannot say what else the owner had open; the run was serial and the frozen evaluator sets no time limit, so load can only have made it slower, not fail it.

## Verdict of the frozen evaluator: STAGE 2 QUALIFIES (33 of 33 criteria passed, 0 failed)
Full results: `R01-regression-evidence/RESULT_ALV-N006_R01.txt` (with `console.txt`, `compare.txt`, `runner_proof.txt`, `gates/`, `walkthroughs/`, `backend-tests.csv`, `summary.txt`, `telemetry.jsonl`, leak counts).
- **Backend: exit 0; 5,612 passed, 0 failed, 0 skipped** (exactly the expected count) in 83.0 minutes (1 h 22 m by the test runner). All 5,481 baseline ids of the `ALV-018-C02` R01 run are present with the same outcome, none retired; the new ids are exactly the 131 allowlisted ones (`compare.txt`: retired 0, unexpected new 0, expected but missing 0, `COMPARISON PASS`).
- Runner preflight passed before anything ran (`runner_proof.txt`); the working tree stayed clean.
- Template: built once, never rebuilt, 0 fallbacks, 4,269 clones, 37 real-path fixtures (at most 60).
- **Artifact counts identical before and after** (`leak_before.txt`, `leak_after.txt`: 1 database, `AlveraTest` 0, `AlveraTemplate` 0, `AlveraRestore` 0, no SQL login, no template backup; the Storage, Keys, Backup, Database and Temp entries unchanged).
- `tsc`, Vitest (1,879 in 120 files), build, lint, mocked Playwright (138 passed, 2 skipped) and repository checks (13): exit 0.
- Real-backend walkthroughs on fresh databases, all passed and none failed: reports **7**, follow-ups **8**, patient flow **10**, notifications **10**, permission policy **7** and **8**, workspace **14**, patient export package **10**, patient portability **7**, patient merge **9**, bulk import **13**, patients **7**.
- **Backend elapsed** (operational evidence only, no limit applies): 83.0 minutes (the previous story's Stage 2: 85.3).
- The evidence folder was scanned before it was committed: no secret pattern; the only email address is the synthetic `ann@example.test`.

## Known test debt carried, not part of this verdict
`playwright.billing-ledger.config.ts` fails its test 8 on the last pushed commit `6899818` already (a selector that matches three status paragraphs). It was excluded from the frozen Stage 2 list, as the Stage 1 review requires; it was not fixed and must not be described as passing. It stays recorded as known test debt for the Gate E evidence review (`R01-evidence/artifacts/billing-ledger-pre-existing.txt`).

## What is not in git, and why
**The raw results file `full_serial.trx` (about 9.7 MB) and the two generated export-package ZIPs of the export-package walkthrough are deliberately not committed.** Nothing reads them from the repository. `backend-tests.csv` is derived from the trx (5,612 ids, all Passed, names unescaped) and is what the next story's comparison uses as its baseline. All three are in the regression ZIP, and can be added to git from there if the reviewer wants them.

## State
The story is `AWAITING_REVIEW` for the final review with this addendum, not `COMPLETE`. Gate E is not started.
