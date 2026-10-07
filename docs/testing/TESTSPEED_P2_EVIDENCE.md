# TESTSPEED-P2 Gate 2 corrected evidence (v2): AWAITING_REVIEW

Replaces `superseded/EVIDENCE_SUMMARY_v1_SUPERSEDED.md` (kept for the record) after the review `CHANGES_REQUIRED` of package SHA-256 `3AE3992E…9EA1`.
Tested commit: **`fe28a80`** (local, **not pushed**). Chain on `1b92fcf`: `368ee8d` parallel configuration, `919f2dc` 1205 drop retry plus failed-init cleanup, `261b5a6` pool-acquisition retry, `31b501b`, `0c83d9c`, `02da4ea`, `fe28a80` the login-leak correction (test code only). Later commits only change PROGRESS.md and `docs/testing`. **No production code changed.**

## 0. What changed since the first evidence set
Only `LeastPrivilegeAccessTests` and a new serial test class `LeastPrivilegeLoginCleanupTests` (5 tests). `loginfix/changes_since_tested_261b5a6.txt` and `git diff 261b5a6 fe28a80 --stat -- src/Alveara.Api src/Alveara.Api.Tests/TestDatabaseFixture.cs xunit.runner.json ParallelismCollections.cs AccountServiceLoginTests.cs RealProcessDatabaseOutageTests.cs` (empty) show that the fixture, the runner configuration, the collection definition, production code and the two real-marker classes are **byte-identical** to the first tested commit `261b5a6`, so the P3, hammer, retry-mutation and real-marker evidence of that set still applies and is retained (folders `p3/`, `hammer_final/`, `mutation/`, `realmarker/`).

## 1. Correction of my earlier diagnosis (important)
The v1 summary said the login leak came from the test's own pooled session keeping the login "logged in". **That was a guess and was wrong as the primary cause.** Running the new cleanup showed `Microsoft.Data.SqlClient.SqlException: Incorrect syntax near the keyword 'IF'`: the old cleanup used `DROP LOGIN IF EXISTS`, which is **not valid T-SQL** (only `DROP USER` has `IF EXISTS`). The whole batch was rejected every single time, and the empty `catch` swallowed it, so no login was ever dropped. The pooled-session handling in the new cleanup is still correct defence (proved by tests that hold sessions open) but was not the cause.

## 2. The correction (`LeastPrivilegeAccessTests.DisposeAsync`)
Clears the restricted login's pools (the EF-built and the directly built connection use different pools), drops the user and the login with a valid guarded statement (`IF EXISTS (...) DROP LOGIN`), retries **only** SQL 15434 ("still logged in") at most 5 attempts, throws every other failure and verifies the exact login is absent from `sys.server_principals`. Tests in `LeastPrivilegeLoginCleanupTests` (serial collection, inventory updated; all use the exact login name): login gone with pooled sessions of both kinds left behind and zero waits (first drop succeeds); a drop that cannot succeed is thrown (15434) after exactly 4 waits and then succeeds once the held session is released; a session closing during the wait is retried once; a different SQL error (8134) is thrown at once with no retry; a cleanup that succeeds without removing the login is reported.
Mutation controls on the final code (`loginfix/`, `gate2_final_runs/mutation_L7_final.txt`): **all 7 killed**: invalid `DROP LOGIN IF EXISTS` back (6 tests fail), no up-front pool release (3), no retry (2), cap raised to six (1), every SQL error retried (1), swallow again (3), absence check removed (1). Two earlier runs were less clean and are reported: on the first version of the tests, removing the up-front release and removing the absence check **survived**; the tests were strengthened (assert that no wait is needed; add the "removes nothing" test) and the mutants rerun; a redundant pool release inside the retry (which a mutant showed to be unobservable) was removed from the code. One mutant of mine (an uncapped catch) hung instead of failing; it was stopped and replaced by a capped one.

## 3. Three consecutive complete parallel suites on `fe28a80` (4 workers, new start point)
| Run | Result | Wall | vs serial 43.9 min | Parallel phase | Serial tail |
|---|---|---|---|---|---|
| 1 | 2,297 of 2,297 passed, 0 failed, 0 skipped | 23 m 32 s | 1.87x | 16.6 min | 6.9 min |
| 2 | 2,297 of 2,297 | 24 m 30 s | 1.79x | 17.1 min | 7.4 min |
| 3 | 2,297 of 2,297 | 26 m 56 s | **1.63x** | 18.3 min | 8.6 min |
No retried runs; failures zero; `gate2_final_runs/equivalence.txt`: all 2,271 Phase 1a baseline ids present with unchanged outcomes in every run, 26 new ids (the remediation and login-cleanup tests) all passed, the three runs identical in ids and outcomes. Discovery 2,280. Smoke canary (4 workers, 286 tests) passed in 5 m 20 s. Runner configuration unchanged (`effective_runner_config.json`: 4 workers, conservative).
**Speed, stated plainly:** this set is slower than the first set (1.93x, 1.88x, 1.90x on `261b5a6`). Across all six runs the speedup is 1.63x to 1.93x. The plan's 1.8x improvement threshold is met by 1 of 3 runs here (run 2 is marginally below, run 3 clearly below). The parallel phase is steady (16.4 to 18.3 min over both sets); the slow runs are slow in the serial tail and in the summed test-body time (78.7 min in run 3 against about 71 in the others), which points at the serial classes running slower on the machine in that run; the cause was not investigated or established. I did not rerun to obtain a better number. The reviewer should decide whether the 1.8x criterion applies to each run or to the representative result.

## 4. Leaks, processes, ports from the new start point (`gate2_final_runs/snapshots.txt`)
| | Start point | After canary | After runs 1, 2, 3 | After walkthroughs |
|---|---|---|---|---|
| test logins | 273 | 273 | 273, 273, 273 | 273 |
| `AlveraTest_` databases | 50 | 50 | 50, 50, 50 | 50 |
| `AlveraRestore_` databases | 57 | 57 | 57, 57, 57 | **58** |
| `AlveraMigrationFailureTest_` | 1 | 1 | 1, 1, 1 | 1 |
| orphan processes / port 5193 | 0 / free | | 0 / free every time | 0 / free |
**The login leak is gone**: flat at 273 for the canary and all three full runs (it was +2 per run before). **Open observation:** `AlveraRestore_` rose by one during the walkthrough step, to `AlveraRestore_845e56f3...` created at 14:21:08, half a minute after the first walkthrough's real API started; the first-set sweep did the same (`...4f9c5b68...` at 11:53:18). That database is created by the real API process the walkthroughs start (its own scheduled restore drill), not by the backend test project, which kept the count flat in all four test runs; I have not investigated it further. The 17 `alveara_p3_*` walkthrough databases (all databases +18 in total) are the walkthroughs' own and are not deleted (standing rule), as are the 273 old logins and the other leftovers.

## 5. Complete fresh walkthrough sweep (new database names `alveara_p3_*`)
**17 of 17 specs passed first time, 187 tests, 0 failed, 0 not run**, including `perio_sessions` 12 of 12 (`gate2_final_runs/sweep/walkthrough_summary.txt`). Frontend and repository: `tsc` exit 0, `npm run build` exit 0, vitest 75 files and 1,194 of 1,194, mocked browser suite 136 of 136, repository checks 8 of 8.
**`perio_sessions` failure of the first set:** the cause is **not established**. I could not reproduce it: 10 further isolated runs on fresh databases passed 12 of 12 each (`perio_investigation/`), two earlier reruns also passed, and it passed first time in this sweep, so the evidence is 12 isolated passes plus one full-sweep pass against a single earlier failure (a 30 s timeout after `page.reload()` waiting for the "Step by step (keyboard)" button; the API log of that run only shows request cancellations). Per the review, the complete fresh sweep above is the acceptance evidence; the isolated reruns are supplementary.

## 6. Retained evidence from the first set (code and configuration unchanged)
Probe P3: 11 of 11 passed, 40 of 40 fresh-key-ring trials cross-readable (`p3/`). Controlled SqlClient-prune hammer on the final fixture: 800 cycles, 16,000 results passed, 23 retries all recovered, no growth (`hammer_final/`). 18 of 18 retry/cleanup mutants killed (`mutation/mutation_results_v2.txt`). Real-marker controls: protected 13 of 13 pass, mutant fails exactly the two real protected tests (`realmarker/`). Thread sweep of the first set (4 workers kept; 8 was 9% faster on the canary): in `superseded/gate2_runs_earlier_set_261b5a6/`.

## 7. What is not claimed
- Speed: see section 3; the 1.8x threshold is not met by every run of this set.
- The hammer is an accelerator; three runs cover about 4,000 fixtures against a natural failure rate of about 1 in 4,000 to 5,000 before the fix.
- The `perio_sessions` single failure and the `AlveraRestore_` database created by the real API are unexplained observations, listed for the reviewer's decision.
- Nothing is pushed. If the work is not approved, the P2 runner configuration is reverted and the suite stays serial.
