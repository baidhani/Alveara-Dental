# ALV-017-C01 R02 - Test results (Stage 1)

Focused evidence only. **Full backend regression: PENDING** (frozen contract in `stage2/`, expected 5,136 passed). Baseline: the STORY-017 final run, 5,080 passed (`stage2/baseline-backend-tests.csv`).

- **Final-tree focused run** (`artifacts/test-runs/r02_focused_f8d40e9.trx`): every `*ApiTests` class, `PermissionPolicy*`, `TestDatabase*` and `RealMigrationInventory*`: **1,082 passed, 0 failed, 0 skipped**, 16 m 28 s, on commit `f8d40e9`.
- **Race hunt:** the `PermissionPolicy` classes (the class that failed in R01) passed 136 of 136, five passes in a row, on the corrected code.
- **How the failure mode was found:** a broad run on a tree that already stopped its hosts failed once (`PermissionPolicyPipelineTests.A_row_whose_role_cannot_be_attributed...`), and the new diagnostic named the holder (session 53, `EFCore/10.0.12`, the test host, sleeping). That run is kept as `artifacts/test-runs/r02_apitests_before_drain.trx` (969 tests, 968 passed, 1 failed).
  - The next broad run (1,077) failed once on the new test's own assertion: it checked that the report had started before the background task had begun. A bounded wait fixed it.
  - The next (1,082) failed once on `RealMigrationInventoryTests` because the new test class was not listed in the inventory. It is listed in `f8d40e9`.
  - Neither of those two is a product failure. The final-tree run above is clean.
- **57 new test ids** against the baseline (`stage2/expected_new_ids.txt`): the 52 of R01 plus the 5 of `TestDatabaseInUseTests`. One baseline id is retired (declared in `stage2/retired_ids.txt`, unchanged). Identity derivation (`artifacts/identity-derivation.txt`): 57 listed = 57 ran, none unlisted, 0 baseline ids missing, 0 outcomes changed, 0 not passed; expected total 5,136.
- **Negative controls** (`artifacts/r02-negative-controls.txt`): 5 mutations of the fixture and harness (3702 not transient; holders not looked up; program name not logged; hosts not stopped; one drop attempt only) all killed by `TestDatabaseInUseTests`. A sixth, the drain not registered, was killed by the report test and run separately (the file was restored byte-identical). The runner proof reports a PROBLEM against the R01 defect.
- **Runner proof:** `artifacts/runner-proof/`.
- **Evaluator validation** (`artifacts/evaluator-validation.txt`): 10 of 10 scenarios.
- **Real-backend walkthroughs re-run** (the production host gained a hosted service): patients 7 and permission-policy 7 passed (`artifacts/walkthroughs/`), and the worktree was clean afterwards.
