# ALV-009-C01 R01 - Stage 2 regression addendum: STAGE 2 DOES NOT QUALIFY

## Identity
- Story `ALV-009-C01`, attempt `R01` (an additive addendum; the R01 Stage 1 handoff, evidence, ZIP and review are unchanged).
- Implementation commit: `4b12cc19bf8de4bf97a9c5e0aab3aee95218b8ae`.
- Stage 1 review-closure commit and run commit: `169bce5720d1d7fd1a3f70f4eb2eea81cf84ae4d` (`.alveara/reviews/ALV-009-C01/R01-stage1.md`, `APPROVED_PENDING_REGRESSION`).
- Run once, on a clean tree, with nothing else running: `sh .alveara/handoffs/ALV-009-C01/R01-evidence/stage2/run_stage2.sh 4b12cc19bf8de4bf97a9c5e0aab3aee95218b8ae C:/tmp/alv009c01/stage2` (2026-10-24, 11:48 to 12:35 -05:00).
- Equivalence: no `src/` file differs between the implementation and the run commit (0 files); the working tree was clean before and after (0 changed paths). Not rerun.

## Verdict of the frozen evaluator: 22 criteria passed, 3 failed
Full results: `R01-regression-evidence/RESULT_ALV-009-C01.txt` (and `compare.txt`, `gates/`, `walkthroughs/`, `trx/full_serial.trx`, `backend-tests.csv`).

### What passed
- **Backend exit 0; 4,906 passed, 0 failed, 0 skipped** in 2,410 s = 40.2 min (operational evidence only). Every one of the **4,734 STORY-009 final-run ids is present with the same outcome**: 0 missing, 0 changed, 0 not passed (`compare.txt`).
- Template: built once, never rebuilt, 0 fallbacks, 3,742 clones, 32 real-path fixtures. Leak counts identical before and after (one database, `AlveraDev`; no test, restore or template database or backup left).
- `tsc`, build, lint, mocked Playwright (140) and repository checks (13): exit 0.
- Real-backend walkthroughs on fresh databases: work queue **8**, follow-ups **8**, notifications **10**, permission policy **7**, permission-policy administration **8**; all passed, none failed.

### What failed, and why
1. **Count: 4,906 passed against an expected 4,896; and "exactly the 162 listed new ids" (172 found).** Cause: an error in the Stage 1 evidence, not a product defect. The allowlist (`stage2/expected_new_ids.txt`) was derived from the Stage 1 focused and `*ApiTests` runs, whose filter never matched the class `RecallPermissionTests` (committed in `2d7c812`, the first slice). The class holds **10 new ids, all of which passed in this run**: `Changing_one_patients_recall_interval_needs_only_the_follow_up_permission_the_clinical_roles_already_hold`, `The_recall_settings_permission_is_not_protected_so_it_can_be_granted_revoked_and_delegated`, and `Only_the_administrator_and_the_office_manager_manage_the_recall_settings_by_default` for the roles Admin, Assistant, Billing, Dentist, FrontDesk, Hygienist, OfficeManager and Unassigned. The 162 listed ids are all present and passed (0 allowlisted ids missing). The correct expectation was therefore **172 new ids, 4,906 total**. The Stage 1 package also did not report these 10 tests as having run: its claim "none missing" was true for the classes it ran, but the class list was incomplete.
2. **Vitest: 1 failed of 1,617 (101 of 102 files passed).** `src/PatientWorkspace.test.tsx` > "editing a patient > saves only on request, sends the version it read, and shows the saved values" (ALV-003-C01, last changed in `fdafb00`; this story did not touch the file or the screen it tests) received `city: "AustinDallas"` where it expected `"Dallas"`: the text typed was appended to the old value, so the preceding `userEvent.clear` had not taken effect when `type` ran. The same file passed in every earlier run of this story (full Vitest 1,617 of 1,617 on the Stage 1 tree and in the sweep) and **passed 6 of 6 when run alone afterwards** (19 of 19 each time, `R01-regression-evidence/patient-workspace-isolation.txt`). The most likely cause is a timing race in that test under the load of the full suite (the full backend run had just finished and the machine was warm), but that is a diagnosis from the failure shape and the isolation runs, not something proven. It is an existing test, not a regression caused by ALV-009-C01 code, yet it is a flaky test in the suite and the frozen contract treats any Vitest failure as a failure.
3. The failures in (1) and (2) are why `gate vitest` and the two backend-count criteria are FAIL in the evaluator output; no other criterion failed.

## What I did and did not do
- I did not rerun Stage 2, edit any source or test file, or change the frozen contract after seeing the result. The working tree is unchanged since the run; this addendum only adds evidence files, a status record and a PROGRESS entry.
- Under `.alveara/HANDOFF_SCHEMA.md` section 4.6 a product or test regression failure is `CHANGES_REQUIRED` with `fullRegression: "FAILED"` and a new attempt; an environment-only inconclusive result may rerun the unchanged contract. Neither item here is environmental (one is an error in the evidence, one is a flaky test), so the status record is set to `AWAITING_REVIEW` with `fullRegression: "FAILED"` and **the decision is left to the reviewer**.

## Proposed R02 (for the reviewer's approval or correction)
1. Fix the allowlist: add the 10 `RecallPermissionTests` ids (172 new ids, 4,906 backend tests expected) and re-derive the list from a run whose filter covers every class added by the story (`git diff --name-only` of `src/Alveara.Api.Tests` against the baseline commit decides the filter, not memory).
2. Make `PatientWorkspace.test.tsx` deterministic for the edit case (wait for the cleared value before typing, or set the value in one step) and show it stable under repeated and loaded runs; no change to product code.
3. Rerun the Stage 1 focused evidence for the changed test files, build a new frozen Stage 2 contract with the corrected counts, and run it once after review.
4. Alternative the reviewer may prefer: accept this run as proof that all 4,734 baseline ids still pass and rule on the allowlist error and the flaky test without a new attempt. I do not recommend skipping a clean frozen run.

## Disclosure
No product defect was found by Stage 2. Both failures are in my own evidence and in an unrelated existing test, and both are stated above rather than worked around. Not pushed.
