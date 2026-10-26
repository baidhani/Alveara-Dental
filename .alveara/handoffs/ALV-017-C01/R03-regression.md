# ALV-017-C01 R03 - Stage 2 regression addendum: STAGE 2 QUALIFIES

## Identity
- Story `ALV-017-C01`, attempt `R03` (an additive addendum; the R03 Stage 1 handoff, evidence, ZIP and review are unchanged). Prior attempts R01 and R02 are preserved unchanged, including R01's two Stage 2 runs (`R01-regression-evidence/`).
- Implementation commit: `f8d40e945f8aab438c80f72128ee309c05b0a8f5`.
- Stage 1 review-closure commit and run commit: `8119ef64037a604213ede9fff7be81ee609eb139` (`.alveara/reviews/ALV-017-C01/R03-stage1.md`, `APPROVED_PENDING_REGRESSION`).
- Run once, on a clean tree, with nothing else running: `sh .alveara/handoffs/ALV-017-C01/R03-evidence/stage2/run_stage2.sh f8d40e945f8aab438c80f72128ee309c05b0a8f5 C:/tmp/story017/c01-r03-stage2` (2026-10-26, 07:03 to 08:02 -05:00). It was not rerun and no file was changed during or after it.
- Equivalence: no `src/` file differs between the implementation and the run commit (0 files); the working tree was clean before and after (0 changed paths). The run commit differs from the implementation only by the handoff and evidence files, PROGRESS.md and the review closures.

## Verdict of the frozen evaluator: STAGE 2 QUALIFIES (26 of 26 criteria passed)
Full results: `R03-regression-evidence/RESULT_ALV-017-C01_R03.txt` (with `console.txt`, `compare.txt`, `runner_proof.txt`, `gates/`, `walkthroughs/`, `trx/full_serial.trx`, `backend-tests.csv`, `summary.txt`, `telemetry.jsonl`, leak counts).
- **Backend: exit 0; 5,136 passed, 0 failed, 0 skipped** (exactly the expected count) in 51 m 52 s. Every one of the 5,080 STORY-017 baseline ids is present with the same outcome except the one declared retired; the new ids are exactly the 57 allowlisted ones (`compare.txt`, COMPARISON PASS). This includes the permission-policy classes that failed in R01 run 2.
- **Runner preflight** passed before anything ran (`runner_proof.txt`); the working tree stayed clean (the R01 run 1 failure does not recur).
- Template: built once, never rebuilt, 0 fallbacks, 3,912 clones, 36 real-path fixtures (at most 60).
- **Artifact counts identical before and after** (`leak_before.txt`, `leak_after.txt`: 4 databases, `AlveraTest` 3 both times; no template, restore, SQL login or template backup left). The three `AlveraTest_` databases present throughout are leftovers of failed runs earlier in this story, not created by this run: `AlveraTest_14dc6e15...` (an interrupted run during R01), `AlveraTest_738058d1...` (R01 Stage 2 run 2) and `AlveraTest_000c71c0...` (the R02 reproduction run before the fix). They were not dropped without the owner's say-so.
- `tsc`, Vitest (1,676 in 105 files), build, lint (oxlint), mocked Playwright (140) and repository checks (13): exit 0.
- Real-backend walkthroughs on fresh databases: patient-merge **9**, workspace **14**, patients **7**, permission-policy **7**, permission-policy-administration **8**; all passed, none failed.
- **Backend elapsed** (operational evidence only, no limit applies): 52.2 minutes.

## State
The story is `AWAITING_REVIEW` for the final review with this addendum. STORY-018 is not started.
