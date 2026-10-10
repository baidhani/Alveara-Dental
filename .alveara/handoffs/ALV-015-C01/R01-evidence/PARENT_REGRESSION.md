# ALV-015-C01 R01 - Parent regression

Parent course story: `STORY-015` (portal-verified 2026-10-09T01:26:06Z at `9d5d82e`, 3 of 3 criteria). Its completion contract is immutable and must keep passing. Companion dependencies `ALV-N005` and `ALV-013-C01` are regressed with it, because this attempt touches the diagnosis service and extends the plan code that reads the catalog.

| STORY-015 Done Means | Still proven by (all passing in this attempt's runs) |
|---|---|
| 1. Given a patient diagnosis, creating a treatment plan links it to proposed procedures and fee estimates | `TreatmentPlanServiceTests`, `TreatmentPlanApiTests.A_plan_links_its_diagnosis_copies_the_catalog_fee_and_every_step_is_in_its_history_with_who_and_when`, `TreatmentPlansPanel.test.tsx` (create), STORY-015 walkthrough "ACCEPTANCE 1" and "the fee is copied" |
| 2. An error in treatment plan entry, when saved, prompts for correction | `TreatmentPlanServiceTests` (every wrong field named, nothing stored), `TreatmentPlanApiTests.A_plan_with_mistakes_is_refused_with_a_message_for_every_field_to_correct_and_saves_nothing`, the panel's gap and refusal tests, STORY-015 walkthrough "ACCEPTANCE 2" |
| 3. All treatment plan entries are logged with user ID and timestamp | `TreatmentPlanLifecycleTests.Every_change_is_audited_with_the_user_and_time_and_recorded_in_the_history_without_clinical_wording`, the failed-audit tests, STORY-015 walkthrough "ACCEPTANCE 3"; the new changes follow the same rule (`TreatmentPlanStateServiceTests`, `DiagnosisReconciliationTests`) |

## Results
| What | Result |
|---|---|
| STORY-015 backend classes inside the full serial run | `TreatmentPlanServiceTests` 32, `TreatmentPlanLifecycleTests` 16, `TreatmentPlanSchemaTests` 57, `TreatmentPlanApiTests` 24: **129 of 129**; the whole run **2640 of 2640** |
| STORY-015 real-backend walkthrough (`playwright.treatment-plans.config.ts`, own fresh database in the sweep) | **11 of 11** |
| Diagnosis and ALV-013-C01 (touched by this attempt) | all existing diagnosis backend tests pass inside the full run, including the three guard tests updated in STORY-015; `Diagnoses.test.tsx`, `DiagnosisStructure.test.tsx` and the diagnosis component tests pass; walkthroughs `diagnoses` **12 of 12** and `diagnoses_structure` **12 of 12** |
| ALV-N005 procedure catalog (read by the plan code) | all catalog backend tests pass inside the full run; walkthrough `procedures` **7 of 7** |
| The other 14 existing real-backend walkthroughs | auth 12, patients 7, workspace 14, forms 10, schedule 10, calendar 12, flow 10, board 15, clinical 9, companion 9, safety 11, odontogram 11, odontogram longitudinal 11, perio 10, perio sessions 12 (the 17 existing total **187 of 187**) |
| Mocked browser suite and repository checks | **136 of 136** and **8 of 8** |

All 20 walkthrough runs: **215 of 215 passed**, each on its own fresh database (`artifacts/regression/summary.txt` and the per-walkthrough outputs).

## What changed for the parent, deliberately
Nothing in STORY-015's contract. The plan view gained fields (all additive) and the plan history gained entry types; existing tests that read plans were not changed. The only change to existing tests was in STORY-015 itself (the three guard tests), already recorded there.

## Not regressed
The periodontal grid tests' timing margin (see `TEST_RESULTS.md`) is an existing sensitivity, not a regression: those tests pass on the previous commit under the same conditions and on this tree when run alone.
