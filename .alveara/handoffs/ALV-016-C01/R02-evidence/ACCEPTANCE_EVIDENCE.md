# ALV-016-C01 R02 - Acceptance evidence

## The review finding

| Finding | What the review required | Proof |
|---|---|---|
| **ALV-016-C01-R01-01** - the required high-risk full serial backend regression did not pass; a SQL deadlock (1205) escaped the completion as an unclassified provider exception | Correct the completion race with the project's transient-failure and concurrency conventions; preserve the all-or-nothing completion-set invariant; a deadlock victim either safely retries within a bounded, idempotent design or returns the documented domain-safe conflict or save result, never an unhandled provider exception; add direct regression coverage proving the selected policy and the exactly-one-winner and no-duplicate effects; produce a fresh R02 implementation and evidence pair and ZIP with a fully passing full backend regression; do not rerun until it passes; map the repair's effect on ALV-008-C01's contract and reopen it transparently if the correction requires | **Reproduced first** on the unchanged code with a real deadlock (`artifacts/reproduction/reproduction-before-fix.txt`: `InvalidOperationException > DbUpdateException > SqlException(1205)`). **Policy:** a bounded (3 attempts), idempotent retry of a deadlock victim, then the documented `save_failed` 503 (`HANDOFF.md`). **Direct coverage:** `CompletionDeadlockTests` (10; engineered real deadlocks): `A_completion_that_is_a_deadlock_victim_is_run_again_and_succeeds_exactly_once`, `A_completion_that_is_a_deadlock_victim_on_every_attempt_ends_in_save_failed_and_records_nothing` (the plan, states, events, audit and ledger are exactly as before: all-or-nothing), `After_a_deadlock_the_retry_meets_the_winners_result_so_exactly_one_set_wins_and_nothing_is_duplicated` (exactly one winner, no procedure completed twice, one set, two procedures, two charges), `After_a_deadlock_a_double_submit_of_the_same_request_replays_the_saved_set_instead_of_recording_it_twice`, the adoption pair, `A_domain_refusal_is_not_retried_and_passes_through_unchanged`, the classifier and policy tests. **Not a rerun:** the failing test was not rerun until it passed; the cause was found, reproduced deterministically and fixed, and the full regression then passed on the changed code: **3686 of 3686** (`artifacts/test-runs/backend-full-summary.txt`). **ALV-008-C01's contract:** not reopened; mapped in `PARENT_REGRESSION.md`. |

## The six stop conditions of the prompt (unchanged by R02)
The R01 evidence (`.alveara/handoffs/ALV-016-C01/R01-evidence/ACCEPTANCE_EVIDENCE.md`) maps each of the six stop conditions to named tests and screenshots, and the review accepted it. R02 changes no prescription code, so each still holds; the same tests pass again on the R02 tree in the full regression:

| # | Stop condition | R02 result |
|---|---|---|
| 1 | Original STORY-016 tests still pass exactly | STORY-016's 185 backend tests, 31 screen tests and 10 real-browser tests pass (full backend, full vitest, the `prescriptions` walkthrough 10 of 10) |
| 2 | Structured quantity, refills, instructions, prescriber and status are stored | the schema, service and API tests pass; the `prescription-workflow` walkthrough 15 of 15 |
| 3 | Entered dose fields are structurally validated by documented local rules | rules, service, API and schema tests pass; screen tests and the walkthrough |
| 4 | Current medications and ALV-N011 safety alerts are visible during prescribing | the safety-panel component test and the walkthrough's "THE SCREEN" |
| 5 | The UI does not claim medical dosage appropriateness or interaction checking | the no-drug-knowledge-source test, the options test, the banner and print statements, the walkthrough's no-claim assertion |
| 6 | Prescription actions are audited and finalized safely | the audit, replay, concurrency, failed-log and immutability tests; the walkthrough's "TRUST" and "FAILURE PATH" |

## Why the finding did not touch the prescription story's own acceptance
The failing test belonged to ALV-008-C01 (completion of planned procedures). The prescription workflow never calls the completion service, and the prescription workflow's own concurrency (draft row versions, one finalized event, one ending event) was already proved by deterministic tests in R01. The review blocked on the mandatory full regression, not on a prescription behaviour.
