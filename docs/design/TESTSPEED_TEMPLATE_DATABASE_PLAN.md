# Test-speed plan: a template database for the backend tests (Phase 1b, milestone M2)

**Status:** PROPOSED, for independent review. **No code has been written or changed for it.** `xunit.runner.json` stays `parallelizeTestCollections:false`; nothing here enables parallelism.
**Raised by:** the product owner, 2026-10-21, after the full backend run of `ALV-010-C01` took 1 h 34 m (2 h 41 m on an earlier run of the same suite) and about 15 stories remain.
**Written for:** the reviewer. It is the "M2" plan of `docs/MILESTONES_2026-10-07.md`.

## 1. The problem

The full serial backend suite is the slowest gate of every high-risk story, and a story that is revised pays for it twice. The two-stage review (`HANDOFF_SCHEMA.md` v3) removes some wasted runs; this plan shortens the run itself.

## 2. What was measured (read-only; nothing in the repository changed)

Source: the results file of the `ALV-010-C01` R03 Stage 2 run (`.alveara/handoffs/ALV-010-C01/R03-regression-evidence/backend-tests.csv`, commit `277ff0b`: 4,300 tests, 94 min 14 s on a quiet machine) and probes against a throw-away database (created and dropped; nothing left on disk).

| Measurement | Value |
|---|---|
| Tests; test classes | 4,300; 216 |
| Time inside the test bodies (sum of per-test durations) | **14.5 min** |
| Wall time of the run | 94.2 min |
| Time outside the test bodies (fixture setup and teardown, host start-up) | **79.7 min = 85 %** |
| Classes that create one database per test (`SafetyTestBase` and classes that build their own fixture) | 160 classes, 3,177 tests |
| Classes with one database per class | 33 classes, 213 tests |
| Tests that need no database | 910 |
| Database creations in a full run | about **3,210** |
| Outside-the-body time per database creation, if all of it were database work | at most 1.49 s |
| `CREATE DATABASE` with explicit files and read-committed snapshot on | 0.07 to 0.08 s |
| `DROP DATABASE` | 0.02 s |
| Applying the 44 migrations to an empty database | about **2.5 s** (5.1 s with `dotnet ef` start-up, minus 2.6 s for a no-op update) |
| Cloning a ready-migrated database with `BACKUP` once and `RESTORE ... WITH MOVE` | **0.14 to 0.16 s** per clone (5 clones; the snapshot setting is kept; 101 tables; 16 MB of files) |
| Cloning by copying and attaching the files | not usable: the copy is refused by file permissions |

So the dominant cost is applying every migration to every per-test database, and a restore from a template costs about a tenth of it. The in-process cost of `MigrateAsync` inside the test fixture (no `dotnet ef` start-up) was **not** measured separately and is the first step of the protocol (section 7).

## 3. Proposed design

1. **One template per test run.** On first use in the test process, a static, lock-protected initializer creates `AlveraTemplate_<guid>` with the same explicit files and `READ_COMMITTED_SNAPSHOT ON` as today, applies all migrations once with the real `MigrateAsync`, takes one `BACKUP` to `Test-Data\\Temp`, and drops the template database. The backup file is removed when the run ends.
2. **Each fixture clones.** `TestDatabaseFixture.InitializeAsync` replaces "create + migrate" with `RESTORE DATABASE <name> FROM DISK = <backup> WITH MOVE ..., REPLACE`, with its files in `Test-Data\\Database` as now. Everything else (naming, the pool release, the drop, the bounded retries, the failed-initialisation cleanup, the hooks the fixture's own tests use) is unchanged.
3. **Stale-template protection.** The template records the list of migration ids it was built from. Before every clone the fixture compares that list with `db.Database.GetMigrations()` of the code under test; a difference rebuilds the template (it can only differ if the process loaded different code, which a per-run template makes impossible, but the check is cheap and is the mutation control for the design). The backup is never reused across runs.
4. **Tests that must exercise real migrations keep doing so.** A test that calls `MigrateAsync` itself (the roll-back-and-forward schema tests) or tests the fixture's own initialisation failures (`TestDatabaseInitFailureCleanupTests`) uses a constructor option that bypasses the template and runs the real path. An inventory of such tests is part of the Stage 1 package.
5. **Fallback and failure.** If a restore fails, the fixture logs the safe classification, removes the half-created database and falls back to the existing create-and-migrate path for that fixture (bounded as today); if the template cannot be built, the run fails loudly with the original error. A failed run never leaves the template or the backup behind except through a killed process, whose leftovers are named `AlveraTemplate_*` and are reported, not deleted.
6. **Out of scope:** parallel execution (milestone M4, separate and later), shared databases per class, SQL settings tuning, anything in production code, and any change to what a test asserts.

## 4. Projection (to be measured, not promised)

About 3,210 database creations. If applying migrations costs about 1.2 s in process and a restore about 0.15 s, the saving is about 1.0 s each, about **55 minutes of the 94**, leaving a run of roughly **35 to 45 minutes**. The range is wide because the in-process migration cost and the host start-up share of the remaining overhead are not yet separated. The earlier milestone note projected 12 to 14 minutes off the 2,405-test suite; that came from a smaller, unmeasured model and is superseded by these numbers.

## 5. Mutation and equivalence controls (all in the Stage 1 package)

| Control | Expected |
|---|---|
| A migration added after the template was built | the fixture detects the difference and rebuilds |
| A corrupt or missing backup file | the fixture falls back to create-and-migrate and the test passes |
| A restore failure injected for one fixture | fallback, logged, no leaked database |
| A template build failure | the run fails with the original error, nothing left behind |
| A test that writes data | the next clone is unaffected (isolation) |
| The same suite before and after | the same 4,300 test ids with the same outcomes; no test changed or skipped |
| Leak counts (`Test-Data\\Storage`, test databases, SQL logins, `Test-Data\\Database` and `Temp` entries) | unchanged before and after |

## 6. Risks

- A migration that seeds data is in the template and so in every clone, as it is today after `MigrateAsync`; a test that depends on a database with no migrations applied would break, and is found by the inventory and by running the whole suite.
- `RESTORE` needs the SQL Server service account to write to `Test-Data\\Database` and read the backup in `Test-Data\\Temp`; both worked in the probe on this machine. A different machine configuration is not tested.
- The backup is a shared file read by every clone; the suite is serial, so there is no read contention today. A later parallel plan must re-check this.

## 7. Measurement protocol

1. **Step 0, before any change:** measure in process the time of `MigrateAsync` and of a restore inside the existing fixture on a quiet machine, so the projection can be corrected before work starts.
2. **Baseline:** the `ALV-010-C01` R03 Stage 2 run on `277ff0b` (4,300 tests, 94 m 14 s) is the recorded baseline; because the same suite once took 161 min on this machine, the protocol reruns the baseline on the exact starting commit of the change if the machine's state differs from that run.
3. **After:** two full serial runs on the changed commit, one after the other, on a quiet machine, to show the run-to-run variance; each with leak counts before and after.
4. **Evidence:** exact commits, commands, elapsed times, counts, results files, the per-test outcome comparison (same ids and outcomes), and the mutation-control results.
5. **Two-stage review:** test infrastructure is high risk under the cadence. Stage 1 carries the design, the inventory, the focused tests and the controls; Stage 2 is the frozen full serial run on the closure commit.

## 8. Decisions requested from the reviewer

1. Is the template-by-restore design acceptable, with a per-run template and no reuse across runs?
2. Is the inventory-and-opt-out rule for tests that need real migrations acceptable?
3. Is the measurement protocol (step 0, a baseline on the exact starting commit, two after-runs) enough, and is a result of "about half" the right bar for acceptance, or should the bar be set before work starts?
4. Should this be one story-sized change with its own handoff (recommended), or be folded into the next story's work?

## 9. Not proposed here, kept for later

Parallel lanes on isolated databases (the closed phase 2 reached 1.77 to 1.9 times against a 1.8 times gate; the backup and restore classes were most of the serial tail), SQL settings for test databases (small initial files, delayed durability), and a shared database for classes that only read after setup. Each needs its own measurement; none is part of this plan.
