# Test results - ALV-N016 R01 (Stage 1)

**Cadence classification: HIGH RISK** (test infrastructure shared by every database test; the story prompt requires two-stage review). **This is a Stage 1 package: the formal Stage 2 backend run is `PENDING` by design and has not been run.** No new pre-change full suite was run. The baseline is the `ALV-010-C01` R03 Stage 2 run: 4,300 tests, 94 m 14 s (`.alveara/handoffs/ALV-010-C01/R03-regression-evidence/`).

Real SQL Server (`.\SQLEXPRESS`, data under `C:\.alvera-data\Test-Data`). Raw outputs are under `artifacts/`.

## Fresh runs on this work

| What | Commit | Result |
|---|---|---|
| New tests: `TestDatabaseTemplateTests` (13) and `RealMigrationInventoryTests` (5) | `3ae51e2` | **18 of 18 passed** (1 m 39 s). Written test-first: the 11 first template tests were red against a stub (NotImplemented) before the implementation |
| Negative controls: 13 deliberate breakages of the template and the inventory | `3ae51e2` and the commits before it | **13 of 13 caught** (`artifacts/negative-controls/`) |
| Focused regression: the 19 inventory classes, the 14 ordinary class-fixture classes (backup, restore, logging, transaction, storage isolation), the template, inventory and cleanup tests (37 classes) | `3ae51e23adbd7769a0bafb71fd16731b1eee3466` | **444 of 444 passed**, 0 failed, 0 skipped (22 m 29 s); the run's own template: 1 build, **0 fallbacks**, 0 rebuilds, 24 clones, 281 real-migration fixtures; working tree clean before and after |
| In-process timing, the same 273 ordinary tests (9 classes, `artifacts/ab-timing/`) with the template **on** and **off** | `7c6b40f` | on: **3 m 23 s**, 273 of 273 passed, 273 clones, 0 fallbacks; off (the real create-and-migrate path for every test): **12 m 43 s**, 273 of 273 passed. **3.8 times faster** on this sample; per test 2.79 s against 0.74 s |

The timing run was made on `7c6b40f`; the two commits after it add a clone-equivalence test, a partial-clone-removal check and a `role` field on the telemetry (no behaviour change in what a fixture does). The 444-test focused regression was re-run on the final commit.

## What the measurements say (and do not say)

- In process the template build took 8,586 ms of which applying the 44 migrations took 5,307 ms (once per run), and a restore averaged 318 ms.
- The backup is 6.7 MB; the probe before the change measured 0.14 to 0.16 s per restore and about 2.5 s to apply the migrations outside the test process (`artifacts/probe/`). The in-process figures are slower under test load; the A/B result above is the authoritative one.
- **No full-suite figure is claimed.** The ceiling in acceptance item 8 (55 minutes) is judged only by the Stage 2 run.

## Leak counts of the focused run (`artifacts/focused-run/leak_*.txt`)

Storage entries 3920, user databases 5, `AlveraTest_*` databases 4, SQL logins 0, `Test-Data\Database` entries 8, `Test-Data\Temp` entries 18: **identical before and after**. No `AlveraTemplate_*` database or backup remained.

## Telemetry of the focused run

The run's own template (`role: run`): one `template_built` (44 migrations, 8537 ms) and one `template_summary` (builds 1, rebuilds 0, clones 24, fallbacks 0). The `role: test` events come from the template tests, which inject failures on purpose (4 injected fallbacks); the `role` field exists so the Stage 2 evaluator counts only the run's own.

## Frozen Stage 2 contract

To be run **once** by `stage2/run_stage2.sh <implementation-sha> <out-folder>` on the closure-only code-equivalent tree, after `APPROVED_PENDING_REGRESSION`, with nothing else running on the machine and a clean working tree. The three files in `stage2/` are part of this frozen contract (`run_stage2.sh`, `evaluate_stage2.py`, `compare_outcomes.py`, plus `leak_counts.sh`); the evaluator reads no threshold from anywhere else. It qualifies the run only when every line below passes:

1. The run commit is the Stage 1 closure commit; the tree is clean before and after; no `src/` file differs from the implementation `3ae51e2`; `ALVERA_TEST_TEMPLATE` is not set.
2. `cd src\Alveara.Api.Tests; dotnet test` (adding only `--logger "trx;LogFileName=full_serial.trx" --results-directory <out>/trx`) exits 0 with **4,318 tests discovered and passed (the 4,300 of the baseline plus the 18 new), 0 failed, 0 skipped**, and the elapsed time is **55 minutes (3,300 s) or less**.
3. `compare_outcomes.py`: every one of the 4,300 baseline test ids is present with outcome Passed (so the same ids and outcomes), no id is missing, none is not Passed, and exactly 18 ids are new.
4. From the run's own template telemetry (`role: run`): exactly one `template_built`, 0 rebuilds, **0 `template_restore_fallback`**, no `template_build_failed`, and at least 2,500 clones (about 2,850 are expected), so the template was really used.
5. `Test-Data\Storage`, `Keys`, `Backup`, `Database` and `Temp` entry counts, user databases, `AlveraTest_*`/`AlveraTemplate_*`/`AlveraRestore_*` databases, SQL logins and template backups in `Temp` are unchanged before and after, and no `AlveraTemplate_*` database or backup remains.
6. `npx tsc --noEmit` exit 0; `npx vitest run` 1,544 passed in 97 files; `npm run build` and `npm run lint` exit 0; `npx playwright test` 136 passed; `node --test tests/*.test.mjs` 8 passed.
7. The evaluator writes `RESULT.txt`; **any failed line, including the time ceiling, means the story is `CHANGES_REQUIRED` and a new Stage 1 attempt begins** (acceptance item 8 and the story prompt).

## Disclosures

- Three older local commits (`20d4926`, `4ed2d14`, `be29f3b`, from `ALV-010-C01`) carry a `Co-Authored-By` trailer against the owner's standing instruction; not rewritten. None of this story's commits has one.
- The in-process A/B was run on `7c6b40f`, before the last two commits (see above). The focused regression was re-run on the final commit.
- `ALVERA_TEST_TEMPLATE=off` is a measurement switch that sends every fixture down the real path; it must be unset in Stage 2 and the evaluator checks it.
- A killed test host (not a normal exit) can leave `AlveraTemplate_*.bak` in `Test-Data\Temp`; the next run reports it and never deletes it.
- Leftover test databases and folders from this story's own red-phase and mutation runs (4 databases, 17 scratch folders) were deleted at the owner's request before the leak counts above were taken.
- No `.trx` of the focused run is packaged (kept at `C:\tmp\n016\focused\trx`); `focused-tests.csv` lists every test with its outcome and duration.
