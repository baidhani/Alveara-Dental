# ALV-N019 R04: acceptance evidence

R04 does not change the five item-56 acceptance statements (counted 5 of 5 in R02 and R03; `R02-evidence/ACCEPTANCE_EVIDENCE.md`). It answers the R03 rerun ruling (`.alveara/reviews/ALV-N019/R03-stage2-rerun.md`), the preflight addendum and the briefing approval.

| Requirement | Status | Evidence |
|---|---|---|
| Warm the real clone pool before release using only the existing bounded pool-timeout classification | **MET** | `TestDatabaseFixture.cs` (warm-up inside `RunWithTransientRetryAsync` with `IsPoolAcquisitionTimeout`, 3 attempts); `The_warm_up_retry_*` tests |
| Owner-side initialisation cleanup: preserve the original exception; remove only the fixture-owned clone and files; cleanup failure observable without hiding the original | **MET** | `FixtureOwnerCleanupTests` (5 tests, both observed owner paths) |
| Extract retry helpers as needed | **MET** | `TestDatabaseFixture.Retry.cs`; behaviour unchanged |
| Convert and account for the inventoried owner families incl. `ImportIntakeServiceTests` and `SafetyTestBase` / `PrescriptionWorkflowSchemaTests` | **MET** | `R04.md` conversion report; 99 direct owners + `SafetyTestBase` (32 derived); the guard scan |
| Repository guard, narrow-classification tests, real forced-pruning proof, failure-cleanup tests, negative controls, 60-iteration preflight | **MET** | `FixtureOwnerGuardTests` (10), `FixtureForcedPruningTests` (3), `FixtureOwnerCleanupTests` (5); `artifacts/preflight/`; `artifacts/negative-controls/` |
| Stage 1 evidence 1: control arm shows at least one authentic first-open timeout in at least 800 forced cycles | **MET** | 800 cycles, 154 authentic timeouts (`preflight/control.jsonl`) |
| Stage 1 evidence 2: treatment 60 iterations x 15 cycles, every first open succeeds, zero databases/files after every iteration | **MET** | 900 of 900 ok; 0 and 0 after each of 60 iterations (`preflight/treatment.jsonl`, `SUMMARY.txt`) |
| Stage 1 evidence 3: both failure paths leave no clone/files; cleanup failure separately observable | **MET** | `FixtureOwnerCleanupTests` |
| Stage 1 evidence 4: classifier rejects non-pool product, SQL, invalid-operation and assertion exceptions | **MET** | `FixtureOwnerGuardTests` (theory of 6 plus a real SqlException) |
| Stage 1 evidence 5: inventory/conversion report, per-iteration raw data, negative controls, exact changed-file list | **MET** | `R04.md`, `artifacts/inventory/`, `artifacts/preflight/`, `artifacts/negative-controls/`, `artifacts/changed-files-name-status.txt` |
| Stage 1 evidence 6: the 9.0.21 Pkcs pin and scanner-clean proof intact; no unrelated package upgrade | **MET** | no project or package file in the diff; the R03 advisory evidence is unchanged |
| One new frozen Stage 2 only after Stage 1 approval | **PREPARED, NOT RUN** | `stage2/` (expected 5,715 tests = 5,697 + 18 listed ids) |
