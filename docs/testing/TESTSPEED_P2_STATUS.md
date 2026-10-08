# TESTSPEED-P2 (parallel test execution): final disposition

**Status: NOT QUALIFIED against its approved performance gate. The runner stays serial.** Decision of the independent review of the final measurement session (`.alveara/reviews/TESTSPEED-P2/FINAL_SESSION_R01.md`, package SHA-256 `B6EC0D84153DB0BD1120ABF5A267798AC26BD0A3F3DA900F6C0DCA2005306EC1`), 2026-10-07.

## What is true now
- `src/Alveara.Api.Tests/xunit.runner.json` has `parallelizeTestCollections: false` (byte-identical to the Phase 1a version). Nothing enables the four-worker configuration; nothing is pushed.
- A full backend run takes about 42 minutes serial (it took 125 minutes before Phase 1a).
- The controlled final session (S0, P1 to P3, S1) stopped without a valid closing serial run, so the declared performance rule was never evaluated. Against the valid opening serial run alone the three valid parallel runs are 1.767x (median), below the 1.8x target. That session is **not** closure evidence and is not to be reused or completed.

## What is kept (to be considered separately for integration, none of it enables parallel execution)
- Bounded retry for SQL error 1205 on the fixture drop, bounded retry of the SqlClient pool-acquisition timeout at fixture initialisation and drop, cleanup of a half-created database after a failed initialisation (`TestDatabaseFixture`).
- The `LeastPrivilegeAccessTests` login-leak correction (invalid `DROP LOGIN IF EXISTS`) and its tests.
- The auth walkthrough now removes the second isolated restore copy it creates.
- The `serial-server` collection markers and the isolation inventory (`TESTSPEED_P2_INVENTORY.md/.csv`); they are inert while the runner is serial.

## How a future parallelism initiative may start
A newly approved performance initiative may begin a new full measurement protocol on a single fixed commit. Re-enabling means restoring the configuration of `368ee8d` (`git show 368ee8d:src/Alveara.Api.Tests/xunit.runner.json`) in a reviewed commit, with the protocol and evidence described in `TESTSPEED_P2_EVIDENCE.md`, the amended measurement plan and the final session report. The earlier session must not be reused.
