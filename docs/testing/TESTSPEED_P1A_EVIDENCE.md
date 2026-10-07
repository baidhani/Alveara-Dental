# Phase 1a evidence - fixture teardown optimization (TESTSPEED-P1A)

**Written for:** the reviewer agent. **Session:** `CC-20261002-f7c1`. **Status: AWAITING_REVIEW** (not complete until reviewed). Nothing has been pushed.

| Item | Value |
|---|---|
| Plan reviewed | `Alveara_TestSpeed_Phase1a_Plan_v2.zip`, SHA-256 `DE597F6DB2C61F7C52412E11AB4DD4160F7F129A5B6C37942F1809A7E172CF75`: **APPROVED FOR PHASE 1a IMPLEMENTATION** |
| Baseline commit (before) | `762ded190c0874cdd620466c9ca9e07d60a83394` (tag `test-speed-baseline-20261006`; Phase 0 evidence SHA-256 `EFC3996F5C7E3BF55528F2F04EBF543685EF5864FB3E50BC83AA2E6923E872C8`) |
| Implementation commit (after) | `1d5f92a00798263db4a0fb73221141262214ab4c` (`TESTSPEED-P1A: release the fixture database's own pooled connections before dropping it`), 3 files, 247 insertions |
| Environment | the same Windows 11 workstation (24 logical processors, about 31 GB), LocalDB `MSSQLLocalDB` (SQL Server Express 17.0.4025), .NET SDK 10.0.401; machine idle for every timed run (checked before each) |

## 1. What changed (diff audit, E6)

`git show --stat 1d5f92a`: `PROGRESS.md` (an unticked entry at that commit), `src/Alveara.Api.Tests/TestDatabaseFixture.cs` (+24 lines), `src/Alveara.Api.Tests/TestDatabaseTeardownTests.cs` (new, 216 lines). The fixture diff adds one `internal` method, `ReleasePooledConnections()` (clears the pool through the connection EF builds for the fixture's database), and a guarded call at the start of `DisposeAsync`; the existing `ALTER DATABASE ... SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE` is unchanged. No timer, property or warning. No production code, runner setting, parallelism, template, cleanup behaviour or test class was touched (`git/commit_stat.txt`, `git/fixture_diff.txt`).

## 2. Result: before and after (an observed comparison, not a guaranteed improvement)

| Measure | Before (Phase 0, `762ded1`) | After (`1d5f92a`) |
|---|---:|---:|
| Full serial backend run: executed / passed / failed / skipped | 2,265 / 2,265 / 0 / 0 | **2,271 / 2,271 / 0 / 0** (the 2,265 plus the six new tests) |
| Full serial run wall-clock (first start to last end) | **124.8 min** | **43.9 min** (**64.8 % shorter**; runner: 2 h 4 m versus 43 m 54 s) |
| 374-test subset, instrumented, wall-clock | 23 min 01 s | **12 min 26 s** (about 46 % shorter) |
| Subset: database drop (fixture tear-down) | 13.04 min, **57.7 %** of fixture lifetime; per drop about 3.03 s | **0.37 min, 3.2 %**; per fixture teardown median **26 ms**, p95 **35 ms**, mean 87 ms |
| Subset: create + migrate | 4.16 min (18.4 %) | 4.40 min (37.0 %; now the largest fixture cost) |
| Measurement set (6 classes, 191 fixture lifetimes), teardown | about 3.03 s each (Phase 0 / benchmark) | median **23 ms**, p95 **47 ms** |

**How to read these numbers.** There is **one full-suite run on each side** (the Phase 0 baseline and run 1 here), plus one uninstrumented and one instrumented subset run before and one instrumented subset run after. The reviewer accepted this as an observed comparison; **no statistical stability is claimed**. The second post-change full run and the extra subset repeats listed in the plan were **skipped at the user's decision** to save about 90 minutes; the evidence does not rely on them. The subset comparison is instrumented against instrumented (the instrumented and uninstrumented Phase 0 subset runs differed by 1 percent). The full-run and subset gains differ because the subset contains classes with heavy test bodies.

## 3. Equivalence (E1 to E4)

- **Discovered lists (E2):** baseline 2,248; implementation 2,254; **added exactly the six `TestDatabaseTeardownTests` tests; none missing** (`verify/equivalence_full1.md`).
- **Outcomes (E3):** every baseline test is present with outcome Passed; 0 failed, 0 skipped; the six new tests passed (scripted TRX comparison `p1a_compare.py`).
- **Clean pinned worktree (E2):** the implementation was built and run in a detached worktree of `1d5f92a` with an empty `git status --porcelain` (`verify/impl_commit.txt`, `verify/impl_status.txt`).
- **Leak check (E4):** `AlveraTest_%` databases: **22 before and 22 after** (the same 22 leftovers from earlier runs, never touched); `db_count_full1.csv` shows 22 to 23 during the run (the live database), no growth; the LocalDB data directory holds 44 `AlveraTest_*` files (22 databases times 2 files), none new; **no scratch database remains**. The sweep's walkthroughs created 17 `alveara_p1a_*` databases as every walkthrough run does (not test-fixture databases; they follow the earlier convention and were not deleted). The leak check deleted nothing.

## 4. The scoped mechanism, measured exactly (reviewer's condition 3)

Scratch benchmark, the fixture's exact connection string, `SINGLE_USER WITH ROLLBACK IMMEDIATE` retained (`benchmarks/`):

| Variant | Drop median | p95 |
|---|---:|---:|
| A0 today (30 interleaved cycles) | 3,034 ms | 3,043 ms |
| A1 plan v1: `ClearPool` on `new SqlConnection(fixture string)` | **3,034 ms (no effect)** | 3,048 ms |
| A2 global `ClearAllPools()` | 25 ms | 98 ms |
| A3 `ClearPool` with a non-identical string | 3,034 ms | 3,041 ms |

Scoped variants, **rerun with the corrected parser** (15 interleaved cycles; the first output ended in an exception because a comma in one variant name broke the summary's own CSV parsing; the CSV was complete, but the rerun's summary is the evidence used):

| Variant | Drop median | p95 |
|---|---:|---:|
| B0 today | 3,038 ms | 3,048 ms |
| B1 `ClearPool` via an opened connection | 3,038 ms | 3,120 ms |
| **B2 `ClearPool` via the EF context's own connection (implemented)** | **22 ms** | **24 ms** |
| B3 unopened key then a 250 ms delay | 3,295 ms | 3,303 ms |
| B4 opened connection, called twice | 3,039 ms | 3,597 ms |
| B5 global `ClearAllPools()` | 27 ms | 35 ms |

With a real API host and real sign-ins (6 repetitions each): today 3,024 to 3,043 ms; scoped via the EF connection 20 to 24 ms (first run 107 ms, warm-up) with the host disposed first and 21 to 26 ms with the host alive at teardown. **The scoped mechanism, not the global call, is what is implemented, and the result was measured for that exact mechanism.**

## 5. Scoping established by tests, not by pooling theory (condition 2)

`TestDatabaseTeardownTests` (six functional tests, **no elapsed-time assertion**): T1 and T2 prove the release really closes the database's pooled sessions for a direct context and for an API host; **T3 proves another fixture's idle session and its `@@SPID` are untouched**; T4 (host still alive), T5 (held transaction) and T6 (a different pool) prove the retained `ROLLBACK IMMEDIATE` still drops the database. All six pass in the full run.

## 6. Mutation controls (the negative proof; scratch copy of the implementation; `verify/mutation/`)

| Control | Result |
|---|---|
| **M1** remove the call from `DisposeAsync` | the measurement set's teardown median is **3,037 ms** (p95 3,051 ms, max 3,480 ms) against the acceptance of 500 ms and 1.5 s: **fails** |
| **M2** replace the scoped call with `SqlConnection.ClearAllPools()` | **T3 fails** (and T6 also fails, since a global clear closes the other pool's session); 2 of 6 fail |
| **M3** plan v1's form (`ClearPool` on `new SqlConnection(ConnectionString)`) | **T1, T2 and T3 fail**; 3 of 6 fail |

## 7. Performance acceptance (the plan's decision thresholds, not permanent tests)

1. Measurement set: median 23 ms (limit 500 ms), p95 47 ms (limit 1.5 s): **met**. One of 191 lifetimes took 3,037 ms (see disclosures); it does not move the median or p95.
2. Subset: median 26 ms, p95 35 ms; **lifetimes above 1 s: 5, listed by class**: `BackupRecoveryHardeningTests`, `BackupServiceTests`, `BackupRestoreTests`, `BackupApiTests` (about 3.04 s each; see disclosures).
3. Full serial run at least 40 percent shorter than 124.8 minutes: **met (64.8 percent)**.

## 8. Complete sweep (E7), on the clean main checkout at `1d5f92a`

Frontend 1,194 of 1,194; `tsc` exit 0; production build exit 0; oxlint reports nothing mentioning diagnosis files; mocked browser 136 of 136; repository checks 8 of 8; **all 17 real-backend walkthroughs passed**: auth 12, patients 7, workspace 14, forms 10, schedule 10, calendar 12, flow 10, board 15, clinical 9, companion 9, safety 11, odontogram 11, odontogram longitudinal 11, perio 10, perio sessions 12, diagnoses 12, diagnoses structure 12 (`verify/sweep/`).

## 9. The reviewer's six implementation conditions

| # | Condition | Met |
|---|---|---|
| 1 | `ReleasePooledConnections` is `internal` | yes (`TestDatabaseFixture.cs`) |
| 2 | scoping established by the functional isolation tests | yes: T3, and mutation M2 (section 5 and 6) |
| 3 | correct the benchmark parser and rerun affected variants | yes: section 4 (rerun with a clean summary) |
| 4 | limited to the fixture, its test file and progress/evidence records; no parallelism, template, `ClearAllPools`, production code or ALV/portal item | yes: section 1 |
| 5 | preserve the full sweep; baseline and implementation SHAs, full-suite result, no databases left behind, before/after methodology and raw data | yes: sections 2, 3, 8 and the raw files |
| 6 | stop at `AWAITING_REVIEW` | yes; no Phase 1b or other speed work started |

## 10. Disclosures and limits (read before relying on the numbers)

1. **One full run each side.** The comparison is observed, not a statistically stable estimate; the second full run and the extra subset repeats were skipped by the user's decision.
2. **Some teardowns still take about 3 s.** In the subset, five lifetimes in four Backup classes (production backup code opens its own `new SqlConnection(connectionString)`, a different pool from EF's, which this change does not release; this is an **inference from the code and from the earlier measurement that a directly built connection uses a different pool, not a measured cause**). In the measurement set, one lifetime (the `Billing` row of `Only_roles_that_manage_clinical_notes_can_record_correct_and_withdraw` in `DiagnosisApiTests`) took 3,037 ms for a reason **not established**; it occurred once in 191 lifetimes. Neither was investigated further, and neither is a correctness issue.
3. **The mechanism is version-sensitive** (the key built from the same string does not work; the EF-built connection does). The reason is not established. The plan's re-validation trigger applies: re-run the measurement when the SQL driver, EF Core, .NET or LocalDB versions change.
4. **Instrumentation** (scratch worktree only, never committed) makes the shared API harness build its host eagerly and adds timing code to the fixture; the subset run with it (12 m 26 s) is therefore compared with the instrumented before-run (23 m 01 s).
5. **The first full run on the new commit is the only after-run**; the full-run figure includes any variation due to other processes on the workstation (machine CPU was about 13 percent in Phase 0 with the test and database processes under 2 percent of it).
6. **PROGRESS.md:** the entry at the implementation commit was deliberately left unticked; this evidence commit completes it with the figures above. The existing ALV-013-C01 entry in `PROGRESS.md` still reads "awaiting review" although that story has since been approved and closed; it was not changed here (out of scope).
7. **Process notes:** two driver stalls were caused by an idle MSBuild node left by our own builds and were resolved by shutting the build server down and a small watcher that stops only idle nodes (never during an active build); neither affected a timed run.
8. **Not done, by design:** parallelism, a template database, the cleanup command, deletion of any leftover database, the mutation harness and any handoff-policy change.

## 11. Files

`verify/`: commit and status records, both discovered lists, the full-run TRX and console output, `equivalence_full1.md`, `db_count_full1.csv`, instrumented subset and measurement-set raw rows with their statistics, mutation outputs, sweep outputs and the driver log. `benchmarks/`: the scratch benchmarks, their raw CSVs and outputs (including the corrected rerun) and the API-host probe. `git/`: the commit statistics and the fixture diff. `scripts/`: the comparison, statistics, driver and instrumentation scripts. Baseline TRX and Phase 0 data are in the Phase 0 evidence package (SHA-256 above).
