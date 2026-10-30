# Gate E - Stage 2 regression addendum: STAGE 2 QUALIFIES

An additive addendum to `.alveara/gates/E/GATE_E_REPORT.md`. The Stage 1 package, report, evidence and review are unchanged. **This is not the gate decision**: Gate E is PASS only after the final independent review of this regression evidence.

## Identity

- Gate E Stage 1 review: `.alveara/gates/E/reviews/c3d56e3-stage1.md`, `APPROVED_PENDING_REGRESSION` for package SHA-256 `A5D009579C45E806127737C8AFD72FC5AE9FE22909DB56AAAFAC2A05A9F0F727`.
- Stage 1 evidence run commit: `173051da01b1eababfb9bf10773283cfa3a6f74d`. Evidence commit: `c3d56e3b75f6eed2187787603403821e11ca05fc`. Frozen Stage 2 contract commit: `d2f9bb2f5fa3ce253a6c39df2556b029a7f8dcc7`.
- Stage 1 closure commit and run commit: `c068d37c61b4fd9841220a4664757c2009610ce6` (`Gate E: record stage 1 review`).
- Run once, on a clean tree: `sh .alveara/gates/E/stage2/run_stage2.sh 173051da01b1eababfb9bf10773283cfa3a6f74d C:/tmp/gate-e-stage2-c3d56e3` (2026-10-29, 20:59:12 to 22:41:01 -05:00, 101.8 minutes in all). It was not rerun and no repository file was changed during or after it.
- Equivalence: no `src/` file differs between `173051d` and the run commit (0 files) and **no file outside `.alveara/` and `PROGRESS.md` differs** (0 other files), both checked by the evaluator; the working tree was clean before and after (0 changed paths).
- Machine condition, disclosed: the machine held no other work of mine. My editor's helper processes (node, a web view) and the SQL Server service were running, as in the earlier Gate E runs; I did not close them. During the run I made one read-only check at about 22:29 (the clock, the tail of the summary, a directory listing and the tail of one walkthrough log, the port listener and a process count) after a monitor reached its 30-minute limit; two earlier monitors had also polled the run's summary file every 20 seconds. Nothing was started, stopped or edited.

## Verdict of the frozen evaluator: STAGE 2 QUALIFIES (all 39 criteria PASS, 0 FAIL)

Full results: `.alveara/gates/E/evidence-regression/RESULT_GATE_E.txt` (with `compare.txt`, `runner_proof.txt`, `gates/`, `summary.txt`, `telemetry.jsonl`, leak counts and `backend-tests.csv`).

- **Backend: exit 0; 5,612 passed, 0 failed, 0 skipped** (exactly the expected count). All baseline ids of the ALV-N006 Stage 2 run are present with the same outcome; no new and no retired id (`compare.txt`: `COMPARISON PASS`).
- Runner preflight passed before anything ran: all 17 walkthrough output variables match their specs.
- Template: built once, never rebuilt, 0 fallbacks, 4,269 clones, 37 real-path fixtures (at most 60).
- **Artifact counts identical before and after** (`leak_before.txt`, `leak_after.txt`: 1 database, no `AlveraTest`, `AlveraTemplate` or `AlveraRestore` database, no SQL login, no template backup).
- `tsc -b`, Vitest, build, lint, mocked Playwright and the repository checks: exit 0 (the evaluator checks Vitest 1,879 in 120 files, mocked Playwright 138 passed with 2 skipped, repository checks 13).
- Real-backend walkthroughs on fresh databases, all passed and none failed: documents **14**, documents-library **12**, follow-ups **8**, work-queue **8**, external-cases **9**, patient-merge **9**, patient-export-package **10**, patient-portability **7**, bulk-import **13**, reports **7**, notifications **10**, billing-ledger **11** (with the repair `2be1d73`), flow **10**, permission-policy **7**, permission-policy-admin **8**, workspace **14**, patients **7**.
- **Backend elapsed** (operational evidence only, no limit applies): 5,084 s = 84.7 minutes.
- The evidence folder was scanned before it was committed: no secret pattern; the only email addresses are the synthetic `ann@example.test` and a package name.

## What this adds to the Gate E record

- Every Stage 1 criterion (E1-E9) now also has a fresh full-suite result on the same product code: the 5,612-test serial regression and all 17 walkthroughs pass. The billing-ledger walkthrough passes 11 of 11 in the full contract, and the repair `2be1d73` was approved as test-only by the Stage 1 review.
- Carried unchanged into the final record, as the Stage 1 review requires: "recoverable" for notifications means durable consumer-delivery recovery and idempotent replay; no notification backup/restore drill was performed and no test restores a backup that contains notification messages.
- The other Stage 1 disclosures stand (E9 added to align the table with the plan; the E8 observations; the E6 untested list; criteria whose assertion bodies were not read line by line).

## What is not in git, and why

The raw results file `full_serial.trx` (about 9.7 MB), the walkthrough screenshots and the generated export-package ZIPs are deliberately not committed; they are in the regression package. `evidence-regression/backend-tests.csv` is derived from the trx (5,612 ids, all Passed, names unescaped) and is the next baseline. The small walkthrough evidence files (JSON, CSV, HTML) are committed.

## State

Gate E is **not PASS**: the final independent review of this regression evidence decides. Item 54 (`ALV-N007`) stays unauthorized until Gate E is PASS and durably closed. Nothing is pushed.
