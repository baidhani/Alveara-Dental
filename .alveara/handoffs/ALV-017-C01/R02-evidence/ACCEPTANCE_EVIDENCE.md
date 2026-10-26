# ALV-017-C01 R02 - Acceptance evidence

The six acceptance conditions of the story are unchanged and R01's evidence stands (`../R01-evidence/ACCEPTANCE_EVIDENCE.md`, 6 of 6): R02 changed no merge behaviour. R02 answers the two review findings (see `../R02.md`):

1. **ALV-017-C01-R01-01 (runner variable and output folder):** `artifacts/runner-proof/runner_proof.txt` (all five walkthroughs OK, out-folder outside the repository), `control-r01-defect.txt` (the R01 defect is a PROBLEM), `control-out-folder-inside-repo.txt`; evaluator validation scenario J; `artifacts/walkthroughs/` (clean worktree after patients 7 and permission-policy 7).
2. **ALV-017-C01-R01-02 (teardown):** `TestDatabaseInUseTests` (5 tests) and the diagnostics; the root-cause chain in `../R02.md`; the reproduction `artifacts/test-runs/r02_apitests_before_drain.trx`; `artifacts/r02-negative-controls.txt`; the final-tree run `artifacts/test-runs/r02_focused_f8d40e9.trx`; the repeated `PermissionPolicy` runs (5 x 136).
