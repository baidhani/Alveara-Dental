# ALV-008-C01 R01 - Parent regression

The parent course story is `STORY-008` (its completion contract is immutable and must keep passing). The dependencies' contracts were run as well.

## What was run, and the result
| Suite | Result |
|---|---|
| STORY-008 backend: `TreatmentPlanCompleteAcceptedTests` (9), `TreatmentPlanCompleteAcceptedApiTests` (18) | pass, **unchanged files** |
| STORY-008 frontend: `TreatmentPlanCompleteAccepted.test.tsx` (13) | pass, unchanged file |
| STORY-008 real-browser walkthrough `treatment-plans-complete-accepted-real-backend.spec.ts` | 9 of 9, unchanged file (the legacy button keeps its exact accessible name) |
| ALV-015-C01 / STORY-015 treatment plans: every `TreatmentPlan*` backend class, the frontend `treatmentplan` folder, both treatment-plan walkthroughs | pass, unchanged files |
| STORY-007 / ALV-007-C01 billing: every `Billing*` backend class (they complete an item the old way and then charge it by hand), the Billing frontend, both billing walkthroughs | pass, unchanged files |
| Odontogram and diagnosis backend classes (the chart's state change was refactored to share its staging) | pass, unchanged files |
| Everything else | the full serial backend suite and the whole real-backend sweep (see `TEST_RESULTS.md`) |

A focused run of the parent classes during development (treatment plans, billing, odontogram, diagnoses, reconciliation, condition types plus the new classes) passed 1,131 of 1,131 before the last additions; the final full run is in `TEST_RESULTS.md`.

## How the parent contract was kept
- The two legacy routes (`POST .../items/{itemId}/complete`, `POST .../complete-accepted`) return what they always did, write only a state row, an `ItemCompleted` event and an audit entry, do **not** touch the plan's row (so its version does not move), make no completed-procedure record, chart change or charge. The shared helper they now stage through takes no part in moving the plan's version; only the new operation does.
- The clinical-history route keeps every existing field; the seven new fields were added at the end and are null for a procedure completed the old way (`CompletionApiTests.The_clinical_history_route_adds_the_detail_for_a_workflow_completion_and_leaves_it_null_for_an_old_one`, `CompletedProcedureHistoryTests`).
- The only change to the legacy routes is the approved internal `legacy_completion_used` log (R1a), proved to change no response.
- STORY-007 and ALV-007-C01 tests complete an item through the legacy route and charge it by hand; the new operation never charges an item completed that way (it refuses it as already completed), so those tests are unaffected.

## Tests edited in a parent's files
None. The shared test helper `SchedulingApiHarness.cs` gained an optional hook for capturing logs (used only by the new API test). `TreatmentPlanStateControls.tsx` gained a sentence in its confirmation form (see `HANDOFF.md`); the existing tests that read that form pass unchanged and a new test covers the sentence.
