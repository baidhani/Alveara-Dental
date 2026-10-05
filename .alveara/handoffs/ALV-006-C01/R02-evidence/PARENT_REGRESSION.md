# Parent regression - ALV-006-C01 R02

**Parent course story: `STORY-006`** (portal-verified 2026-10-04T22:14:59Z at `b8eef58`, 3 of 3 criteria). Its completion contract is immutable. This attempt changed the odontogram's refusal rules for a tooth that is, or becomes, missing; the parent's Done Means were run **again after that change**.

| Parent Done Means | Proof on the R02 tree |
|---|---|
| Given a tooth is selected, when a condition is recorded, then it is saved with the correct lifecycle state | `OdontogramServiceTests.A_recorded_condition_is_saved_with_exactly_the_state_chosen_and_who_recorded_it` (all four states) and the schema state checks, in the dedicated odontogram run (367 of 367) and the full run; `ToothPresenceApiTests.The_way_out_and_the_replacement_both_work_over_http_and_the_parents_ordinary_workflow_is_untouched` (record in a chosen state, plan, complete over HTTP); frontend `OdontogramWrite.test.tsx`; the **unchanged original real-backend walkthrough** step `ACCEPTANCE 1`. |
| Given a completed treatment, when recorded, then the odontogram updates | `OdontogramServiceTests.Completing_a_planned_treatment_updates_what_the_chart_shows` and `Work_recorded_as_completed_directly_shows_on_the_chart`; the HTTP workflow test above; the original walkthrough step `ACCEPTANCE 2`. |
| Trust: all odontogram updates are logged with user and timestamp | `OdontogramServiceTests.Every_update_is_logged_with_the_user_and_a_time_and_the_log_never_names_the_tooth_or_condition` and `The_change_its_history_and_its_log_entry_are_saved_together_or_not_at_all`; the original walkthrough step `TRUST`. R02's refusals change nothing and write neither a history version nor an audit entry (asserted in `Planned_missing_then_caries_then_missing_completed_cannot_persist_the_invalid_state_and_nothing_changes`). |

## What R02 changed that a parent test could see
- The parent's own tests record `Missing` only on teeth with no other finding, or in `Planned`/`Diagnosed` states, so the new refusal does not touch them; none was edited in R02. A patient who records `Missing` as Existing or Completed over an existing caries now gets the stable refusal (a behaviour the parent never specified).
- Parent test code touched in R02: none (R01's scoping of three frontend lookups and the schema helper's stored scope are carried forward and documented in the R01 package).

## Dependencies
`ALV-005-C01` and `ALV-N011` and the shared patient workspace: the safety strip stays in the patient header; the workspace, clinical, clinical companion, safety, board, flow, forms, patients, calendar, schedule and auth walkthroughs passed on the R02 tree (see `TEST_RESULTS.md`).
