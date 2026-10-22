# Acceptance evidence - ALV-N016 R02 (6 of 8 at Stage 1; items 7 and 8 are decided by Stage 2)

Items 1 to 6 are as in R01 (`R01-evidence/ACCEPTANCE_EVIDENCE.md`) with these R02 differences. Items 7 and 8 stay `PENDING`.

| # | Criterion | R02 evidence |
|---|---|---|
| 1 | Serial execution remains enabled; every ordinary fixture uses a distinct real SQL Server clone with equivalent migration state and settings | unchanged: `xunit.runner.json` is untouched; the clone-equivalence test of R01 still passes; the 13 rollback classes are now ordinary clone users |
| 2 | Clone isolation is directly proved | unchanged (R01) |
| 3 | The bypass inventory is complete and proves real migration, rollback and fixture-failure behaviour remain exercised | **changed, needs the reviewer's decision (`DECISION_REQUEST.md`, section 1).** The inventory names all three groups (2 migration-pipeline classes and 4 fixture classes on the real create-and-migrate path; 13 rollback classes on a clone, calling the real migrator); `RealMigrationInventoryTests` (5) proves the groups are complete and each class uses the fixture of its group; the clone-rollback proof shows the real Down and Up migrations on a clone give exactly the schema of a real create-and-migrate; the 13 classes pass on clones (250 of 250) and the full unplanned run passed all of them |
| 4 | Migration mismatch, corrupt or missing backup, injected restore failure, template-build failure and partial-clone cleanup are directly tested; fallback is observable | unchanged (R01) |
| 5 | Artifacts are measured before and after; successful-run artifacts clean up; earlier leftovers are reported only | unchanged; the A/B of the 13 classes and the unplanned full run both show identical counts before and after |
| 6 | Focused tests prove migration history, snapshot setting, file locations, isolation, fallback, cleanup and bypass behaviour without weakening existing assertions or ids | R01's tests plus the clone-rollback proof; no existing assertion was edited (the 13 classes' fixture construction lines and attributes changed; their assertions did not); 19 test ids are new against the baseline and none was renamed or removed |
| 7 | Stage 2: same ids and outcomes, zero failures and skips, zero unexpected fallback, clean gates | **PENDING.** The frozen contract is in `stage2/` |
| 8 | One formal Stage 2 backend run completes in 55 minutes or less | **PENDING.** Not claimed; see `TEST_RESULTS.md` for what the unplanned run suggests and how thin the margin is |
