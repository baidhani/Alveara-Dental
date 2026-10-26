# ALV-017-C01 R01 - Stage 2 regression addendum: STAGE 2 DID NOT QUALIFY (two runs)

## Identity
- Story `ALV-017-C01`, attempt `R01` (an additive addendum; the R01 Stage 1 handoff, evidence, ZIP and review are unchanged).
- Implementation commit: `5f0efb5a443a30760a267844270c1b5e60c87aa5`.
- Stage 1 review-closure commit: `d7f38923b350aae26bd39675e5562386991cc603` (`.alveara/reviews/ALV-017-C01/R01-stage1.md`, `APPROVED_PENDING_REGRESSION`).
- No `src/` file differs between the implementation and either run commit (0 files in both evaluator checks).

The frozen Stage 2 was run **twice**. Neither run qualifies under the frozen evaluator. Both are reported as they came out; no evaluator criterion, result file or log was edited.

## Run 1 - run commit `d7f3892` (the closure commit) - 24 of 25 criteria passed
Output: `R01-regression-evidence/run1/` (`RESULT_ALV-017-C01.txt`, `console.txt`, `summary.txt`, `compare.txt`, `gates/`, `walkthroughs/`, `trx/full_serial.trx`, `telemetry.jsonl`, leak counts, `stray-folder-listing.txt`).
- **Backend: exit 0; 5,131 passed, 0 failed, 0 skipped** in 48 m 41 s. Every one of the 5,080 baseline ids present with the same outcome except the one declared retired; the new ids exactly the 52 allowlisted ones (`COMPARISON PASS`).
- Template: built once, 0 fallbacks, 3,912 clones, 32 real-path fixtures. Artifact counts identical before and after.
- `tsc`, Vitest (1,676 in 105 files), build, lint, mocked Playwright (140) and repository checks (13): exit 0. Walkthroughs: patient-merge 9, workspace 14, patients 7, permission-policy 7, permission-policy-admin 8, all passed.
- **The one failed criterion: "working tree clean before and after".** The patients walkthrough wrote its screenshots into `src/alveara-client/patient-e2e-out/` (untracked, 1 changed path). Cause: my adaptation of `run_stage2.sh` passed `PATIENTS_E2E_OUT`; that spec reads `PATIENT_E2E_OUT`. Product and test code were not involved.

## Run 2 - run commit `4916013` (the closure commit plus the one-line fix of that variable in `run_stage2.sh`; nothing else) - 21 of 25 criteria passed
Output: `R01-regression-evidence/run2/`.
- Working tree clean before and after (the fix worked); `src/` identical to the implementation; template 1 built, 0 fallbacks; all gates exit 0; all five walkthroughs passed with the same counts.
- **Backend: 5,130 passed, 1 failed, 0 skipped** in 45 m 23 s. The failed test is the existing `PermissionPolicyPipelineTests.A_stored_row_can_never_grant_a_protected_permission_and_its_role_is_denied_until_repaired(protectedName: "ManageUsers")`. Its failure message is from the test fixture's cleanup, not from an assertion: `SqlException : Cannot drop database "AlveraTest_738058d1..." because it is currently in use`, after the fixture's own transient retries (`TestDatabaseFixture.DisposeAsync`).
- Four criteria failed as consequences of that one event: backend exit code, the expected count (5,130 + 1 failed), the id comparison (`OUTCOME CHANGED: 1`, `NOT PASSED: 1`), and the artifact-count check (databases 2 to 3, `AlveraTest` 1 to 2: the database that could not be dropped was left behind).
- The same test passed in run 1 on the same code, and in the Stage 1 focused run (3,076 of 3,076). The failure is a database-cleanup race in the test infrastructure; the cause has not been established and it was not tied to this story's changes.

## Disclosures
- Two scratch test databases are on the machine and were not dropped without the owner's say-so: `AlveraTest_14dc6e152f1548529b2138ccb83bbfe4` (left before Stage 2, disclosed in the Stage 1 handoff) and `AlveraTest_738058d19ecb49449ac200f413c05b35` (left by run 2).
- Between them, the two runs cover every criterion; no single run passed all 25. Whether this satisfies the "one clean Stage 2" requirement, or a third run is needed, is for the reviewer to decide.
- Time spent: run 1 about 55 minutes, run 2 about 53 minutes.
