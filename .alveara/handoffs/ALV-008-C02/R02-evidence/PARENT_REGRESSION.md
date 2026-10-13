# ALV-008-C02 R02 - Parent regression

The parent course story is `STORY-008` (its portal verification is untouched: `.colaberry/` is not modified). The full mapping of every test and walkthrough that exercised the retired code is unchanged from R01 (`docs/testing/ALV-008-C02_PARENT_REGRESSION.md`, `../R01-evidence/PARENT_REGRESSION.md`); this attempt changes none of it.

| Suite | Result |
|---|---|
| STORY-008 backend (`TreatmentPlanCompleteAccepted*`), STORY-007 / ALV-007-C01 / ALV-007-C02 billing, ALV-008-C01 completion (`CompletedProcedure*`, `CompletionApiTests`, `CompletionRulesTests`), the odontogram suites | pass in the full serial run (3324 of 3324); the completion workflow's tests pass unchanged except the two schema tests below |
| The migrations (`CompletedProcedureMigrationTests`, `CompletionAdoptionSchemaTests`, the new evidence migration test) | pass: rolling back through `AddCompletionChartVersion` and forward again works, and the earlier rollbacks still pass through it |
| Frontend (vitest, mocked Playwright, repository checks) | 1392 of 1392, 136 of 136, 8 of 8 (no frontend file changed) |
| Real-backend walkthroughs of the changed neighbours (completion, treatment-plans-complete, billing, billing-ledger, billing-statements, odontogram, odontogram-longitudinal) and the story's own adoption walkthrough | all pass (see TEST_RESULTS.md) |

## The parent tests edited in R02
`CompletedProcedureSchemaTests.A_completion_that_moved_a_chart_finding_names_that_finding_on_the_procedures_tooth_and_surface` and `...A_chart_finding_must_be_the_patients_own_on_the_procedures_tooth_and_surface` insert a completed-procedure row that names a finding directly. A completed procedure that names a finding must now also name the version that shows it Completed (trigger 51173), so each test now records its finding as Completed (version 1 is the evidence). Their assertions, rules and names are unchanged; the second is arranged so that only the belonging rule (51149) can refuse, which also removes any dependence on which of two triggers fires first.

## Parent behaviour this attempt changes on purpose
The completion workflow (`ALV-008-C01`) and adoption now record the chart version; the completion's observable results (outcome, finding, charge, history, audit) are unchanged. A completion may be refused with the new `chart_changed` (409) when an observed finding changes during recording.
