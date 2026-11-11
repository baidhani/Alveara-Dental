# ALV-N019 R03 — Stage 2 rerun result (the sole permitted unchanged rerun)

**Result: STAGE 2 DOES NOT QUALIFY.** 21 of 25 criteria passed; the four that failed all come from ONE backend test that failed in its setup with a connection-pool timeout. Nothing was patched during or after the run. The decision is the reviewer's.

## What was done (as authorized in `.alveara/reviews/ALV-N019/R03-stage2-inconclusive.md`)
1. Initial R03 evidence, ZIP and the review were left unchanged.
2. Cleanup, on the product owner's explicit approval: only `AlveraTest_72e0ccf142994bd19eafa80c0a7c81a8` and its two verified files were removed (`R03-rerun-evidence/cleanup-before.txt`, `cleanup-after.txt`).
3. Equivalence: implementation `56b05c0`; run commit `faa651b`; 0 files differ from `56b05c0` outside `.alveara/` and `PROGRESS.md`; 0 tracked changes before and after; untracked listing identical.
4. The exact frozen R03 Stage 2 contract was run once more (2026-11-10 20:15:28 to 21:54:05, backend 5,360 s), output in `C:/tmp/stage2-ALV-N019-R03-rerun`, evidence in `R03-rerun-evidence/stage2-rerun/`.

## Result
- Backend: 5,696 passed, 1 failed of 5,697. The failed test: `PrescriptionWorkflowSchemaTests.The_database_refuses_an_incorrect_detail_by_itself(which: "instructionsBlank", ...)` — `Timeout expired. The timeout period elapsed prior to obtaining a connection from the pool` in `SafetyTestBase.InitializeAsync` → `SchedulingTestSupport.ArrangeAsync` (line 37/43), 21:39:20. It is a DIFFERENT test from the initial run's failure; the initial run's failed test passed in this rerun.
- Its fixture left `AlveraTest_a606d345a5f246ea8ceba3cf37fd2932` and its two files behind, which is why the artifact-count criterion fails. (Not deleted; awaiting the product owner's agreement.)
- Every other gate passed: vitest 1,879, build, lint, `tsc -b`, mocked Playwright, repository checks 49, the five real-backend walkthroughs, the stage-0 chain with the injected clock. Template telemetry: 1 template, 4,343 clones, 0 fallbacks.

## Diagnosis (read-only; no run) — `R03-rerun-evidence/diagnosis.txt`
- SQL Server log: no I/O-stall messages on the day; Windows logs: nothing at the failure times. No machine stall found.
- R02 to R03 source difference is the 2-line Pkcs pin only; SqlClient is 7.1.0 in both; the pin does not touch pooling.
- The error is the known SqlClient pool-pruning race (PROGRESS.md, TESTSPEED-P2; `TestDatabaseFixture.IsPoolAcquisitionTimeout`). The fixture retries it during database creation and drop, but not at the first pooled open that happens afterward in test code (`ArrangeAsync`, the first `SaveChangesAsync`). Both R03 failures are such opens. Strongly indicated, not proven; the pin is not excluded by a controlled experiment.
- Candidate fix (NOT applied; needs a new attempt): warm the clone's pool before the fixture returns or retry the first open, with a test that forces the pruning tick.

## Ask
Please rule: (a) accept the two R03 runs together on this diagnosis, or (b) require a new attempt (R04) with that hardening; any further full run starts only on the product owner's explicit approval.
