# ALV-008-C02: parent-regression mapping

ALV-008-C02 deleted the STORY-008 state-only completion code (`TreatmentPlanService.CompleteItemAsync` and `CompleteAcceptedAsync`, the "Complete accepted procedures" group, the per-procedure "Mark X completed" form, and
the two client calls) and made the two routes answer 410. Every test and walkthrough that exercised that code was found by building and running the suites after the deletion (the compiler and the failures named them),
then given exactly one of these dispositions. Nothing was deleted to make a suite pass: a deleted test is listed here with the test or walkthrough that now proves the same intent, or with the reason there is nothing
left to prove.

**Dispositions.** *Arrangement*: the test only needed "a procedure that is completed" as a starting point; it now makes one with the test-only `LegacyCompletionFixture` (which calls the same shared staging helper the old
code called, so the rows are identical by construction) and its assertions are unchanged. *Mapped*: the test asserted behaviour of the removed method; the behaviour is now authoritative-workflow behaviour and the named
test proves it. *Replaced*: the test asserted that the old routes still worked; the named test asserts they are retired. *Moved*: the test was about something the change did not touch but lived in a file that was removed.

## Backend (`src/Alveara.Api.Tests`)

### Arrangement only (assertions unchanged)

`BillingApiTests`, `BillingChargeSourceApiTests`, `BillingDocumentServiceTests`, `BillingLedgerApiTests`, `BillingLedgerSchemaTests`, `BillingLedgerServiceTests`, `BillingProcedureChargeContractTests`,
`BillingRefundConcurrencyTests`, `BillingRefundSchemaTests`, `BillingRefundServiceTests`, `BillingStatementsApiTests`, `BillingTestBase`, `CompletedProcedureEffectsTests`, `CompletedProcedureHistoryTests`,
`CompletedProcedureServiceTests`, `CompletionTestBase`, `TreatmentPlanProcedureUsageApiTests`, `TreatmentPlanTestBase`. In each, the call that completed a procedure the old way now goes through
`LegacyCompletionFixture` (via `LegacyComplete`/`LegacyCompleteAccepted` helpers in the two test bases). Parent intent preserved: billing a completed procedure by hand (ALV-007-C01/C02), refusing a procedure that is
already completed, and amending, reading and charging a completion all still start from the same row shapes the old code produced.

### Changed or removed tests

| Parent test (file) | Disposition | Where the intent is proven now |
|---|---|---|
| `TreatmentPlanCompleteAcceptedTests.Completing_again_changes_nothing_and_a_plan_with_nothing_accepted_is_refused` | Mapped (the "again" half kept as `Completing_again_changes_nothing`, now via the fixture; the "nothing accepted" half moved) | `CompletedProcedureServiceTests.Every_procedure_that_cannot_be_completed_is_listed_with_its_reason_and_none_of_the_others_is_completed_either`, `...The_same_key_and_request_replays_the_saved_result_with_no_writes_even_from_an_out_of_date_screen` |
| `TreatmentPlanCompleteAcceptedTests.A_stale_screen_a_missing_version_and_a_withdrawn_plan_complete_nothing` | Mapped | `CompletedProcedureServiceTests.A_plan_changed_since_it_was_read_is_a_conflict_and_nothing_is_recorded`, `...A_plan_changed_after_it_was_checked_but_before_the_set_is_saved_is_still_a_conflict_and_records_nothing`, `...A_withdrawn_plan_and_an_unknown_plan_complete_nothing`, `CompletionApiTests` (row version required, 400) |
| `TreatmentPlanCompleteAcceptedTests.A_failed_audit_write_completes_none_of_the_procedures_and_says_so` | Mapped | `CompletedProcedureServiceTests.A_failed_audit_write_in_either_save_of_the_transaction_undoes_every_record_and_says_so` (both saves), and for adoption `CompletedProcedureAdoptionHardeningTests.A_failure_writing_the_chart_the_ledger_or_the_log_undoes_...` |
| `TreatmentPlanCompleteAcceptedTests.Two_simultaneous_completions_leave_each_procedure_completed_exactly_once` | Mapped | `CompletedProcedureServiceTests.Two_overlapping_sets_submitted_at_once_leave_exactly_one_winner_and_each_procedure_completed_once`; for adoption `CompletedProcedureAdoptionHardeningTests.Two_adoptions_from_the_same_plan_version_...` and `...The_same_adoption_submitted_twice_at_once_...` |
| `TreatmentPlanCompleteAcceptedApiTests.Only_dentists_and_administrators_can_complete_the_accepted_procedures` | Mapped + Replaced | `CompletionApiTests.Only_dentists_and_administrators_can_read_the_readiness_complete_and_correct` (the product route's role matrix); `CompletionApiTests.The_sign_in_role_and_csrf_checks_still_come_before_the_410` (the retired route keeps the matrix) |
| `TreatmentPlanCompleteAcceptedApiTests.The_contract_a_csrf_token_a_current_version_and_an_existing_patient_and_plan_are_required` | Mapped + Replaced | `CompletionApiTests.A_write_without_a_csrf_token_is_refused_and_records_nothing`, `...Anonymous_callers_get_401_from_every_new_route`, the validation and stale-version tests in `CompletedProcedureServiceTests`; `...A_retired_completion_route_answers_410_...` (an unknown plan or malformed body still answers 410 and looks nothing up) |
| `TreatmentPlanCompleteAcceptedApiTests.A_plan_with_nothing_accepted_is_a_409_with_a_stable_code` | Mapped | `CompletedProcedureServiceTests.Every_procedure_that_cannot_be_completed_is_listed_...` (`items_not_completable` names `not_accepted` per procedure) |
| `TreatmentPlanCompleteAcceptedApiTests.Anonymous_callers_get_401_from_the_new_endpoints` | Renamed `...from_the_clinical_history`; the `complete-accepted` anonymous check moved | `CompletionApiTests.The_sign_in_role_and_csrf_checks_still_come_before_the_410` (anonymous 401 on both retired routes) |
| (new, kept from the same file's contract test) | Moved | `TreatmentPlanCompleteAcceptedApiTests.An_unknown_patients_clinical_history_is_not_found_and_a_write_is_not_part_of_it` |
| `TreatmentPlanCompleteAcceptedApiTests.The_three_acceptance_criteria_hold_end_to_end` | Arrangement (completes with the fixture, then asserts the same history, revision and log) | itself, unchanged assertions |
| `TreatmentPlanStateServiceTests`: assertion that completing a declined procedure is refused `item_not_accepted` | Mapped (the method asserted on no longer exists; the rest of the test, `Declining_a_scheduled_procedure_unschedules_it_and_completing_is_final`, is kept) | `CompletedProcedureServiceTests.Every_procedure_that_cannot_be_completed_is_listed_...` (`not_accepted`) and the fixture throwing `InvalidOperationException` for a non-accepted procedure |
| `TreatmentPlanCompletionApiTests`: unknown-item completion expected `item_not_found` | Replaced (expectation changed; the retired route looks nothing up) | `CompletionApiTests.A_retired_completion_route_answers_410_with_the_replacement_and_changes_nothing_whatever_the_request_carries` |
| `CompletionApiTests.The_legacy_completion_routes_are_unchanged_and_only_log_that_they_were_used` | Replaced | `CompletionApiTests.A_retired_completion_route_answers_410_...`, `...The_sign_in_role_and_csrf_checks_still_come_before_the_410`, `...Each_attempt_on_a_retired_route_is_logged_as_a_phi_free_event_and_the_old_usage_event_is_gone`, `...A_procedure_completed_the_old_way_is_still_refused_by_the_workflow_as_already_completed_...` |
| `CompletionApiTests` clinical-history test that completed with the old route | Arrangement | itself |

## Frontend unit tests (`src/alveara-client/src`)

| Parent test | Disposition | Where the intent is proven now |
|---|---|---|
| `TreatmentPlanCompleteAccepted.test.tsx`: the 8 "complete-accepted button" tests (offered only when something accepted is open; not drawn for a reader; lists exactly what will be completed and sends nothing until confirmed; cancelling sends nothing; refusal; stale copy; dropped connection; accessibility with the confirmation open) | Replaced (the control is removed) | `TreatmentPlanReviewComplete.test.tsx` "makes Review and complete the only way to complete: the legacy completion controls are gone"; the review screen's own tests for confirm, refusal, stale and dropped connection; `TreatmentPlanAdoptCompletion.test.tsx` for the adopt form's equivalents (offered only when a completed procedure exists and never to a reader, nothing sent until valid, refusal, stale, dropped connection with the same key, accessibility) |
| `TreatmentPlanCompleteAccepted.test.tsx`: the 5 "clinical history" tests | Moved (unchanged except the refresh test) | `TreatmentPlanClinicalHistory.test.tsx`; its refresh test now refreshes after an adoption instead of after the removed button |
| `TreatmentPlanAnswers.test.tsx`: "completes an accepted procedure only after confirming that it is final" and "shows the server's refusal and keeps the form open" | Replaced | the same file: "offers no way to mark a procedure completed..."; the refusal and confirmation behaviour belongs to the review screen's tests |
| `TreatmentPlanReviewComplete.test.tsx`: "labels the old button as legacy completion" and "the other legacy control" | Replaced | same file: "makes Review and complete the only way to complete..." (group, button and per-procedure form all absent) |

## Real-backend walkthroughs (`src/alveara-client/e2e`)

| Parent walkthrough | Disposition | Result of the run on a fresh database |
|---|---|---|
| `treatment-plans-complete-accepted-real-backend.spec.ts` (STORY-008, 9 tests) | Replaced by `completion-adoption-real-backend.spec.ts`: history/replay/failure/stale/read-only/criteria 2 and 3 are all re-proven (failure path with the log made to fail, out-of-date second screen, revise-after-completion keeps the original revision and the completed care, the plan's log lists each change once with who and when, hygienist and front desk refused), now against adoption; the "completes every accepted procedure in one click" step has no successor because the click is gone | 8 passed |
| `completion-real-backend.spec.ts` (ALV-008-C01) | Edited: the legacy button assertions became absence assertions, and the LEGACY step now checks both routes answer 410 and that a completion made the old way (seeded) shows with no detail and is reported adoptable | 11 passed |
| `treatment-plans-complete-real-backend.spec.ts` (ALV-015-C01) | Edited: the per-procedure "Mark completed" click was replaced by an absence assertion and a seeded legacy completion; everything after it (completed is final, revision history, comparison, audit events including `TreatmentPlanItemCompleted`) is unchanged | 11 passed |
| `billing-real-backend.spec.ts`, `billing-ledger-real-backend.spec.ts`, `billing-statements-real-backend.spec.ts` (ALV-007) | Arrangement: the completion is seeded with `e2e/support/legacyCompletion.ts` instead of the retired route | 10, 11 and 9 passed |

## What remains true of the parents

- **STORY-008** (completing the accepted procedures, the clinical history): completion is only through the authoritative workflow; the clinical history is unchanged and still shows a legacy completion as "Completed by" until it is adopted. Acceptance criteria 1 to 3 are re-proven by `TreatmentPlanCompleteAcceptedApiTests.The_three_acceptance_criteria_hold_end_to_end` and the adoption walkthrough's parent step.
- **ALV-007-C02** (statements and receipts) has one deliberate behaviour change, found by this story's verification: `BillingService.LoadSnapshotAsync` now ignores completion records finalized after a document's instant, as it already ignored later amendments, because adoption is the first operation that creates a record after the charge it bills and would otherwise make an earlier statement unreproducible (`BillingTimelineAgingTests.A_statement_issued_before_a_legacy_completion_was_adopted_still_reprints_exactly_as_issued_...`, killed by negative control A24). Every existing document, timeline and aging test passes unchanged.
- **STORY-007 / ALV-007-C01 / ALV-007-C02** (billing): billing a procedure completed the old way by hand is unchanged and adoption reuses that charge (`CompletedProcedureAdoptionServiceTests.A_charge_that_billing_already_made_by_hand_is_reused_never_duplicated`, `CompletionReconciliationTests.A_hand_billed_legacy_completion_is_not_an_orphan_and_adopting_it_reuses_the_charge`).
- **ALV-008-C01** (the authoritative workflow): the completion workflow's code was not edited, except that its read side now also reports `legacyCompleted`, `adopted` and `adoptionReason` (additive fields with defaults).
- **ALV-015-C01** (plan revisions and answers): the plan service lost two methods and gained none; its answers, scheduling and revision tests ran green after the arrangement changes above.
