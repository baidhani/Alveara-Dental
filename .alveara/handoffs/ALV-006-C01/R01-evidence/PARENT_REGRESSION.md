# Parent regression - ALV-006-C01 R01

**Parent course story: `STORY-006`** (portal-verified 2026-10-04T22:14:59Z at `b8eef58`, 3 of 3 criteria). Its completion contract is immutable and was run **before** (the original walkthrough and unit tests had passed at the end of `STORY-006`) and **after** this attempt.

| Parent Done Means | Proof on the final code |
|---|---|
| Given a tooth is selected, when a condition is recorded, then it is saved with the correct lifecycle state | Backend `OdontogramServiceTests.A_recorded_condition_is_saved_with_exactly_the_state_chosen_and_who_recorded_it` (all four states) and the `OdontogramSchemaTests` state checks; frontend `OdontogramWrite.test.tsx` ("records a condition in the state the clinician chose ... and the chart shows it", "each of the four states is saved exactly as chosen"); the **unchanged original real-backend walkthrough** step `ACCEPTANCE 1` (saved states `Diagnosed`, `Existing`, `Planned`, `Completed`, stored as FDI keys). |
| Given a completed treatment, when recorded, then the odontogram updates | `OdontogramServiceTests.Completing_a_planned_treatment_updates_what_the_chart_shows` and `Work_recorded_as_completed_directly_shows_on_the_chart`; frontend ("a completed treatment is recorded straight from diagnosed, and the odontogram updates"); original walkthrough step `ACCEPTANCE 2`. |
| Trust: all odontogram updates are logged with user and timestamp | `OdontogramServiceTests.Every_update_is_logged_with_the_user_and_a_time_and_the_log_never_names_the_tooth_or_condition` and `The_change_its_history_and_its_log_entry_are_saved_together_or_not_at_all` (a refused audit write stores nothing); original walkthrough step `TRUST` (7 recorded, 5 state changes, 1 withdrawal in the audit log with user and time and no clinical content). This attempt's new changes (catalogue, links) are audited the same way (new walkthrough step `TRUST AND NUMBERING`). |

## What this attempt changed that a parent test could see
- `FindingView` gained three fields (additive); the six conditions are now catalogue rows seeded identically; the chart opens as Mixed when a patient has a primary-tooth finding. The parent's contract above is unchanged.
- Parent test code touched: `OdontogramSchemaTests` (the helper supplies the stored scope), and in the frontend three lookups were scoped to the findings list, one test now selects the Permanent view before checking the "not drawn" listing, and the contrast test gained the new controls. No parent assertion was weakened or removed.

## Dependencies
`ALV-005-C01` (clinical record), `ALV-N011` (safety strip) and the shared patient workspace: the safety strip stays in the patient header on the odontogram; workspace 14, clinical 9, clinical companion 9 and safety 11 real-backend walkthroughs and the board, flow, forms, patients, calendar, schedule and auth walkthroughs passed on the new build (see `TEST_RESULTS.md`).
