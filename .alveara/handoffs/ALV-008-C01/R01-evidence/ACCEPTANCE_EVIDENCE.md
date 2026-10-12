# ALV-008-C01 R01 - Acceptance evidence

Seven stop conditions (Item 34, as amended by the approved decisions). Each is mapped to named tests at the level where it can be proven. Test classes are in `src/Alveara.Api.Tests` unless stated; "walkthrough" is `src/alveara-client/e2e/completion-real-backend.spec.ts` (real Chromium, real API, real SQL Server; 11 of 11).

## 1. Original STORY-008 tests still pass
- The whole parent set passes unchanged: `TreatmentPlanCompleteAcceptedTests` (9), `TreatmentPlanCompleteAcceptedApiTests` (18), the STORY-008 frontend tests `TreatmentPlanCompleteAccepted.test.tsx` (13) and the real-browser `treatment-plans-complete-accepted-real-backend.spec.ts` (9 of 9 in the sweep). See `PARENT_REGRESSION.md` for the other parent suites (STORY-015, ALV-015-C01, STORY-007, ALV-007-C01, the odontogram and diagnosis suites).
- The legacy routes are proven unchanged at the HTTP level: `CompletionApiTests.The_legacy_completion_routes_are_unchanged_and_only_log_that_they_were_used` (state only, no record, no charge, the same responses) and the walkthrough test "LEGACY: the old button is still there and still records only a state...".

## 2. One or multiple selected planned procedures complete in one authoritative operation
- Single, subset, several: `CompletedProcedureServiceTests.A_selected_subset_is_completed_in_one_set_with_a_record_a_history_event_and_an_audit_entry_for_each_and_the_plans_version_moves`, `One_procedure_can_be_completed_alone_and_the_rest_complete_later_in_another_set`.
- Every wrong field named at once, every procedure that cannot be completed listed with its reason, nothing completed: `CompletionRulesTests` (rules, 23), `Every_procedure_that_cannot_be_completed_is_listed_with_its_reason_and_none_of_the_others_is_completed_either`, `A_procedure_of_another_plan_another_patients_encounter_and_an_unknown_or_inactive_provider_are_each_named_and_nothing_is_recorded`, `A_withdrawn_plan_and_an_unknown_plan_complete_nothing`.
- Provider, encounter (Draft or Finalized, never changed) and performed time rules, including the five-minute inclusive clock boundary and the acceptance rule's explaining message: `The_performed_time_may_be_five_minutes_ahead_of_the_practice_clock_and_not_one_millisecond_more`, `A_performed_time_before_the_plan_existed_or_before_the_procedure_was_accepted_is_refused_and_says_what_it_is_compared_with`, `Scheduling_a_procedure_does_not_move_the_time_it_must_not_be_performed_before`, `A_draft_or_a_finalized_encounter_can_be_named_and_the_encounter_itself_is_never_changed`.
- HTTP: `CompletionApiTests.A_set_is_read_for_readiness_completed_charged_replayed_listed_corrected_and_the_correction_replayed`, the role, 401 and CSRF tests.
- Browser: the walkthrough tests "REVIEW", "FAILURE PATH", "RESULT".
- Unit: `TreatmentPlanReviewComplete.test.tsx` (14).

## 3. Completed procedures remain linked to the exact source plan/version
- `CompletedProcedureServiceTests` (the set row and each record carry the plan, the plan revision, the plan item, the catalog version and the encounter, provider and time) and `CompletedProcedureSchemaTests.The_database_refuses_a_completed_procedure_that_does_not_belong_with_its_set_plan_item_encounter_or_time` (database triggers 51147-51150, 51153, 51154: wrong plan, revision, plan item, catalog version, tooth, surface, other patient's encounter, a procedure that is not completed, a time before the plan or the acceptance).
- Preserved after revision: `Completed_care_stays_in_the_history_and_the_earlier_revision_is_kept_when_the_plan_is_revised_or_withdrawn` (STORY-008) and the history tests (`CompletedProcedureHistoryTests`: the entry shows the revision it was completed under).

## 4. The odontogram updates when applicable
- `CompletedProcedureEffectsTests`: a linked Planned or Diagnosed finding moves to Completed with its history version and audit entry; an already completed finding is reported and left alone; five not-applicable cases (no link, an Existing finding, another tooth, another surface, whole-mouth work) leave the chart alone; two matching findings refuse the whole set with the procedure named and how many matched; two procedures sharing a finding move it once; the chart's own extraction rule still applies; a failed chart write undoes the whole set.
- `CompletedProcedureSchemaTests.A_chart_finding_must_be_the_patients_own_on_the_procedures_tooth_and_surface` (database trigger 51149).
- Readiness uses the same resolution and a test proves the completion then does exactly what readiness said (`Readiness_says_what_a_completion_will_do_and_the_completion_then_does_exactly_that`).
- Walkthrough: the chart finding on tooth 16 is Planned before and Completed after; the ambiguous procedure on tooth 17 cannot be ticked, the rest completes and both findings stay Planned; a forced API call is refused with the matches listed.

## 5. Exactly one linked charge per billable completed procedure
- `CompletedProcedureEffectsTests.A_set_of_billable_and_no_charge_procedures_charges_the_billable_ones_once_and_the_no_charge_one_not_at_all` (three procedures: fee 0, 100 and 60 give charges numbered 1 and 2 only, each naming its procedure and the set's correlation id), `A_set_of_only_no_charge_procedures_opens_no_account_and_a_replay_reports_the_same_charges`, `A_replay_reports_the_original_charges_as_created_and_makes_no_second_one`.
- The amount is the plan's stored fee and the request cannot carry one: `CompletionApiTests` (an `amount` in the body is ignored) and the walkthrough test "the charge is always the plan's stored fee".
- Gate D2 view: the walkthrough's ledger holds exactly the charges of the completed billable procedures and none for the no-fee one (140 and 70, later 40 and 50, and 55).

## 6. Duplicate/retry does not double-complete or double-charge
- Replay of the same key and request, even from a stale screen, with the same procedures listed in another order: `The_same_key_and_request_replays_the_saved_result_with_no_writes_even_from_an_out_of_date_screen`.
- The same key with a different request: `The_same_key_with_a_different_request_is_a_conflict_and_records_nothing`. The same procedures under a new key: `The_same_procedures_under_a_new_key_after_success_are_refused_as_already_completed_and_never_charged_or_recorded_twice`. Duplicate item ids, an empty list and the shape rules: `CompletionRulesTests`.
- The canonical hash: `CompletionRulesTests.The_same_request_hashes_the_same_whatever_order_the_procedures_were_picked_in`, `A_time_written_another_way_or_with_extra_precision_hashes_the_same`, `Any_change_to_the_request_changes_the_hash`.
- Stale and racing: `A_completion_advances_the_plans_version_so_a_second_screen_holding_the_old_one_is_refused_until_it_reloads`, `A_plan_changed_since_it_was_read_is_a_conflict_and_nothing_is_recorded`, `A_plan_changed_after_it_was_checked_but_before_the_set_is_saved_is_still_a_conflict_and_records_nothing` (a deterministic gap test added after a negative control survived), `Two_overlapping_sets_submitted_at_once_leave_exactly_one_winner_and_each_procedure_completed_once`.
- Database: `CompletedProcedureSchemaTests.A_plan_procedure_is_completed_once_and_a_key_is_used_once_per_plan`.
- Amendments: `CompletedProcedureAmendmentTests` (replay, key conflict, a stale amendment, two racing for one number leave one winner, a no-op refused).
- Browser: the walkthrough tests "a retry or a second submission completes nothing twice" and "STALE SECOND SCREEN"; unit: the retry reuses its save key and a changed request gets a new one.

## 7. A failed submitted completion set leaves no mixed authoritative clinical/financial state
- Each failure path of the prompt, each asserting that the plan version, states, history, audit, sets, records, ledger entries, accounts and chart history are exactly as they were, and that the same submission works once the cause is removed:
  - one charge fails in a multi-procedure completion: `A_failed_charge_for_the_second_of_three_procedures_undoes_the_first_charge_the_account_and_every_completion`
  - the odontogram update fails: `A_failed_chart_write_undoes_the_whole_set_including_the_completion_states_already_saved`, and a refused chart rule `The_charts_own_rules_still_apply_completing_an_extraction_is_refused_while_other_findings_stand_on_the_tooth`
  - audit or finalization persistence fails, in either save of the transaction: `A_failed_audit_write_in_either_save_of_the_transaction_undoes_every_record_and_says_so` (two cases; the second proves the charges roll back too)
  - an ambiguous chart match: `More_than_one_linked_finding_that_could_be_the_procedures_refuses_the_whole_set_and_names_the_procedure`
  - a stale plan version and a duplicate submission (item 6) and a procedure already completed, withdrawn or not accepted (item 2).
- Amendment failure: `A_failed_audit_write_records_no_amendment_and_says_so_and_the_same_correction_works_once_the_log_is_back`.
- Browser (real SQL Server, a trigger made the audit write fail): the walkthrough test "FAILURE PATH" shows "Nothing was completed: The completion could not be saved...", no audit or SQL words, the entered provider kept, and, read back through the API, no completed procedure, no ledger entry, the chart finding still Planned and every procedure NotStarted; after the trigger was dropped the same Confirm click completed all three exactly once.
- Negative controls (`artifacts/test-runs/negative-controls-c008.txt`): removing the failed-charge rule, the chart move, the ambiguity rule, the tooth match, the acceptance rule, the withdrawn check, the key conflict, the stale-amendment check, the history enrichment, the legacy log and two database triggers is each caught; removing the atomic plan-version check was caught only after a test was added for it (disclosed in `HANDOFF.md`).

## Also covered (approved decisions and constraints)
- Amendments (D3): `CompletedProcedureAmendmentTests` (13) and `CompletedProcedureSchemaTests` (52): append-only (51144-51146), numbering without gaps (51152), the patient's own encounter (51151), the time rules (51155), a reason and at least one correction.
- Readiness from the server (R3): `Readiness_names_the_procedures_that_cannot_be_completed_and_an_ambiguous_chart_match_with_how_many_matched`; the browser test shows the table from it; the unit tests show every row from the server's data.
- Internal legacy usage log only (R1a): the API test asserts the event, its fields and that nothing identifying is in it, and that no header or response changed.
- Permissions and scope (D8): the API role tests (write, readiness and read theories), CSRF, and one plan per set.
- Migration reversibility: `CompletedProcedureMigrationTests`.
