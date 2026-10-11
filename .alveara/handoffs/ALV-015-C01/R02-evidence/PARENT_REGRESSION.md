# ALV-015-C01 R02 - Parent regression

Parent course story `STORY-015` (portal-verified; contract immutable) and dependencies `ALV-N005` (the catalog and its usage guard, which this attempt extends) and `ALV-013-C01`.

| What | Result |
|---|---|
| STORY-015 backend classes inside the full run | all pass (`TreatmentPlanServiceTests`, `TreatmentPlanLifecycleTests`, `TreatmentPlanSchemaTests`, `TreatmentPlanApiTests`); whole run **2649 of 2649** |
| The one STORY-015 test changed | `A_retry_still_returns_the_plan_after_the_procedure_has_been_inactivated` now passes `acknowledgeUsage: true`: since the plan uses the procedure, inactivation requires acknowledgement. What the test exists for (a retried creation still returns the plan after the procedure is inactivated) is unchanged and passes |
| STORY-015 walkthrough | **11 of 11** |
| ALV-N005 catalog | all catalog backend tests pass inside the full run (the odontogram source still counts as before); `ProcedureCatalogPage.test.tsx` 22 of 22; walkthrough `procedures` **7 of 7** |
| Diagnoses and ALV-013-C01 | pass in the full run; walkthroughs `diagnoses` 12 of 12, `diagnoses_structure` 12 of 12 |
| The complete-planning walkthrough | **11 of 11** (R01: 10; +1) |
| Other existing walkthroughs | auth 12, patients 7, workspace 14, forms 10, schedule 10, calendar 12, flow 10, board 15 (after one disclosed first-attempt timing failure and two clean reruns), clinical 9, companion 9, safety 11, odontogram 11, odontogram longitudinal 11, perio 10, perio sessions 12 |
| Mocked browser suite, repository checks, vitest | 136 of 136, 8 of 8, 1309 of 1309 |

All 20 runs: **216 of 216**, each on its own fresh database (`artifacts/regression/summary.txt`; per-walkthrough outputs beside it; the summary shows the board's first-attempt failure, and the two reruns are `board-rerun1-run-output.txt` and `board-rerun2-run-output.txt`).

What changed for the parent, deliberately: only the behaviour the finding asks for. A procedure used by plans now needs an acknowledged inactivation. No contract, schema or UI of STORY-015 changed.
