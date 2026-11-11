# ALV-N019 R04 — Pre-implementation briefing (fixture hardening)

Status: proposal for the owner's Go and the reviewer's awareness. No code has been changed. R03 evidence, packages and the PD-26 pin stay untouched.

## 1. Problem, as evidenced
Two R03 Stage 2 runs each lost one test to `Timeout expired ... obtaining a connection from the pool` at the first pooled open of a freshly restored clone, in test code AFTER `TestDatabaseFixture.InitializeAsync` returned (`SchedulingTestSupport.ArrangeAsync`, first `SaveChangesAsync`). Cause (indicated, not proven for these two events): SqlClient's pool-pruning timer shuts down the still-empty pool of the clone's connection string while a caller acquires from it. Existing protection (`RunWithTransientRetryAsync` + `IsPoolAcquisitionTimeout`) covers creation and drop only.
Second defect: when the OWNER of a fixture throws in its own `InitializeAsync` after `Fixture.InitializeAsync()` succeeded (e.g. `SafetyTestBase`), xUnit never calls `DisposeAsync`, so the clone and its two files leak. The fixture's own failed-init cleanup (`CleanUpFailedInitializationAsync`) does not apply, because the fixture had already succeeded.

## 2. Design (small, test-infrastructure only; no src/ change)
A. **Warm-up before release (reviewer option "deterministic warm-up").** At the end of `TestDatabaseFixture.InitializeAsync`, still inside the existing bounded retry, open and close one pooled connection through the same pool EF uses (`CreateContext()` then `Database.OpenConnectionAsync`). The pool then holds an idle connection (pruning removes only empty pools), and a pruning race at that open is retried by the existing narrow classifier (`IsPoolAcquisitionTimeout`, at most 3 attempts, first exception rethrown unchanged). No other exception type is retried.
B. **Cleanup on owner failure.** `SafetyTestBase.InitializeAsync` (the shared base that failed in R03) wraps its post-fixture setup: on ANY exception it calls `Fixture.DisposeAsync()` (best effort, logged) and rethrows the original. A small static helper (`TestDatabaseFixture.InitializeOwnedAsync(Func<Task>)` or equivalent) lets other owners do the same; I will list, in the Stage 1 handoff, every base class with this pattern and convert the shared ones, rather than editing 137 test files.
C. **No change** to pool settings, timeouts, SqlClient version, the Pkcs pin, product code, or the frozen Stage 2 contract mechanics (a new frozen R04 contract is generated from the R03 shape; baseline = R02/R03 ids; expected new ids = the R04 tests listed below).

## 3. Proof plan (Stage 1; all cheap, minutes not hours)
1. **Forced race, deterministic.** A test hook forces the pruning tick (the same reflection on `PruneConnectionPoolGroups` used in the earlier TESTSPEED-P2 reproduction, or an injected fault that throws the exact pool-timeout exception at the first pooled open) so no lucky timing is needed. Proves: without the warm-up the failure reproduces; with it, recovery happens.
2. **Preflight (reviewer addendum):** a script runs the forced-race check 50+ consecutive iterations (I will run 60), every one must pass; after EVERY iteration a read-only check lists `AlveraTest_*` databases and files in Test-Data\Database and requires zero. Raw per-iteration outcomes, cleanup checks and the forcing mechanism go in the R04 package.
3. **Induced failure after create:** a test fails the owner's setup after the fixture created its database; asserts the database and its two files are gone (both the fixture-internal path and the owner path of B).
4. **Narrow classification:** tests that a product exception, an arbitrary `SqlException`, an `InvalidOperationException` with a different message, and an assertion failure are NOT retried and propagate unchanged (extends `TestDatabaseTransientRetryTests`).
5. **Negative controls:** removing the warm-up, and removing the owner cleanup, each make a named test fail (then restored).
6. Existing fixture test classes (`TestDatabase*Tests`, `SafetyTestBase` consumers sampled) pass; `tsc`/client untouched, so no client gates change; PD-26 guard test and the dependency-pin check still pass.

## 4. Failure-first answers
- If the warm-up fails after 3 attempts: first exception rethrown unchanged; the fixture's existing cleanup removes the clone.
- Retry: bounded (3 attempts total, zero delay, as for creation); only the pool-acquisition timeout.
- Recovery if exhausted: the test fails loudly with cleanup done (no leak), logged by class.
- Not handled: genuine pool exhaustion that persists (same message; it fails again and the first exception is rethrown); machine-level stalls.

## 5. Risks and limits stated up front
- The warm-up reduces but cannot mathematically eliminate the race for connections opened much later in a long test (idle connections are trimmed after minutes; tests are seconds). The preflight proves recovery, not that no future timeout can occur anywhere. The only proof of the whole system is the final full Stage 2, which remains required (about 91 min) and starts only on the owner's explicit approval after Stage 1 approval.
- Files touched (estimate): `TestDatabaseFixture.cs`, `SafetyTestBase.cs`, one new test class for the preflight, extended retry tests, a preflight script under `scripts/`, the R04 handoff/evidence, a new frozen contract. Size rules: `TestDatabaseFixture.cs` is already large; the next change must split before adding (I will extract the retry helpers into their own file as part of the change).

## 6. Decisions needed
- Owner: Go to implement A and B as above and run the cheap proofs (no full run).
- Reviewer (information only): the design chooses the warm-up option and the owner-side cleanup helper; shout if you require the retry-at-first-open option instead.
