# ALV-N023 R01: changed files

Base `721ec8e` (briefing review R02 stored, APPROVED for implementation) .. implementation `2ade215`. The Stage 1 sweep ran on `f9c0d2a`; `2ade215` adds only `TreatmentPlanN023Proof.test.tsx`, PROGRESS.md and the handoff note. Documents-only commits (handoff note, addendum 01 and its review) are included in the range.

```
2ade215 ALV-N023: client proofs for F-102, DR-8 and D-03 (test-only)
4ec7db1 ALV-N023: handoff note - final implementation commit f9c0d2a and Stage 1 sweep results (documents only)
f9c0d2a ALV-N023: treatment-plan scheduling, follow-up, state and revisions (final implementation commit)
25be828 ALV-N023 slice 5 part 2: record negative controls (a)-(d) (documents only)
b4b62a0 wip: ALV-N023 slice 5 part 1 - treatment plan integrity browser spec; old-wording spec updated
f81d31a ALV-N023 slice 4: update 12 client tests to the approved behaviour
f497fec ALV-N023: handoff note - builder origin, long-run Go, grep scope, precedence (documents only)
9d61896 ALV-N023: handoff note - point to the saved ZIP/control tools in agent-tools (documents only)
3fb3a78 ALV-N023: handoff note - edge cases decided in advance (documents only)
f6c6b6b ALV-N023: handoff note - test counts, control (a) line, keep-current and owner-absent rules (documents only)
1cb788b ALV-N023: handoff note - old-wording specs located, lock guard for control (d), CLAUDE.md items that do not apply, PROGRESS count note (documents only)
1005d64 ALV-N023: handoff note - programme context after this story (documents only)
6c535ed ALV-N023: handoff note - current review process (focused-proof route, full regression deferred to ALV-N028, ledger, review outcomes) (documents only)
bd5bdcb ALV-N023: handoff note - 12th failing test, tsc contradiction, pattern-trap precision, fixture rule, run order, control locations, ZIP checklist (documents only)
4bf2246 ALV-N023: handoff note - commit count stated so it cannot go stale (documents only)
54b8067 ALV-N023: handoff note corrections (documents only) - stale state lines, the 11 tests by name, DR-4 refusal rewrite, silent-weakening trap, exact commands
a0c45e0 wip: ALV-N023 slice 4 (tests partly failing, see PROGRESS) - client display, appointment view unlink, practice-header and linkable-appointments reads; chat handoff document
6c1540c wip: ALV-N023 slice 3 - front desk books with accepted treatment (OQ-1): one-save links, bookable-treatment reads, booking form checklist
891ed08 wip: ALV-N023 slice 2 - derived plan state and lock rule (PD-14, CR-3, addendum 01), DR-4 bulk answers, DR-5 rename reason, PD-15 target dates, DR-7 PHI-free audit, DR-8 performed-after note
f934153 ALV-N023: store review of addendum 01 (APPROVED; implementation resumes; documents only)
707372e ALV-N023: addendum 01 - owner decisions on three state/action ambiguities found in slice 2 (documents only)
1d1e68e wip: ALV-N023 slice 1 - appointment links (DR-1, OQ-1), migration with guards 51291-51293, versioned follow-up episodes

 .alveara/reviews/ALV-N023/BRIEFING-R02-ADDENDUM-01.md                                      |    30 +
 PROGRESS.md                                                                                |    61 +
 docs/design/ALV-N023_ADDENDUM_01.md                                                        |    26 +
 docs/design/ALV-N023_CHAT_HANDOFF.md                                                       |   139 ++
 src/Alveara.Api.Tests/CompletedProcedureAdoptionHardeningTests.cs                          |     2 +-
 src/Alveara.Api.Tests/CompletedProcedureServiceTests.cs                                    |     4 +-
 src/Alveara.Api.Tests/DiagnosisReconciliationTests.cs                                      |     2 +-
 src/Alveara.Api.Tests/RealMigrationInventory.cs                                            |     1 +
 src/Alveara.Api.Tests/RealMigrationInventoryTests.cs                                       |     2 +-
 src/Alveara.Api.Tests/TreatmentPlanApiTests.cs                                             |    16 +-
 src/Alveara.Api.Tests/TreatmentPlanAppointmentLinkMigrationTests.cs                        |    89 +
 src/Alveara.Api.Tests/TreatmentPlanAppointmentLinkTests.cs                                 |   177 ++
 src/Alveara.Api.Tests/TreatmentPlanAuditTests.cs                                           |    43 +
 src/Alveara.Api.Tests/TreatmentPlanBookingApiTests.cs                                      |   163 ++
 src/Alveara.Api.Tests/TreatmentPlanCompletionApiTests.cs                                   |     7 +-
 src/Alveara.Api.Tests/TreatmentPlanLifecycleTests.cs                                       |    20 +-
 src/Alveara.Api.Tests/TreatmentPlanRevisionSchemaTests.cs                                  |     4 +
 src/Alveara.Api.Tests/TreatmentPlanRevisionServiceTests.cs                                 |    10 +-
 src/Alveara.Api.Tests/TreatmentPlanStateServiceTests.cs                                    |    37 +-
 src/Alveara.Api.Tests/TreatmentPlanStateTests.cs                                           |   186 ++
 src/Alveara.Api.Tests/TreatmentPlanTestBase.cs                                             |    35 +
 src/Alveara.Api.Tests/UnscheduledTreatmentJobTests.cs                                      |   163 +-
 src/Alveara.Api/Architecture/Completion/CompletedProcedureService.History.cs               |     2 +-
 src/Alveara.Api/Architecture/Completion/CompletedProcedureService.Read.cs                  |     3 +-
 src/Alveara.Api/Architecture/Completion/CompletedProcedureService.cs                       |     5 +-
 src/Alveara.Api/Architecture/Completion/CompletionDates.cs                                 |    18 +
 src/Alveara.Api/Architecture/Completion/CompletionViews.cs                                 |     5 +-
 src/Alveara.Api/Architecture/FollowUps/FollowUpEntities.cs                                 |     4 +
 src/Alveara.Api/Architecture/Recall/UnscheduledTreatmentJobs.cs                            |   121 +-
 src/Alveara.Api/Architecture/Scheduling/AppointmentScheduler.cs                            |     7 +-
 src/Alveara.Api/Architecture/Scheduling/BookingTreatmentLinks.cs                           |    68 +
 src/Alveara.Api/Architecture/Scheduling/SchedulingModels.cs                                |     3 +-
 src/Alveara.Api/Architecture/Treatment/TreatmentPlan.cs                                    |    12 +-
 src/Alveara.Api/Architecture/Treatment/TreatmentPlanItemAppointmentLink.cs                 |    46 +
 src/Alveara.Api/Architecture/Treatment/TreatmentPlanService.ClinicalHistory.cs             |     3 +-
 src/Alveara.Api/Architecture/Treatment/TreatmentPlanService.Items.cs                       |     4 +-
 src/Alveara.Api/Architecture/Treatment/TreatmentPlanService.Links.cs                       |   181 ++
 src/Alveara.Api/Architecture/Treatment/TreatmentPlanService.Queries.cs                     |    14 +-
 src/Alveara.Api/Architecture/Treatment/TreatmentPlanService.Reads.cs                       |    34 +
 src/Alveara.Api/Architecture/Treatment/TreatmentPlanService.ReferenceCode.cs               |     3 +-
 src/Alveara.Api/Architecture/Treatment/TreatmentPlanService.Revisions.cs                   |     5 +-
 src/Alveara.Api/Architecture/Treatment/TreatmentPlanService.States.cs                      |    47 +-
 src/Alveara.Api/Architecture/Treatment/TreatmentPlanService.cs                             |    42 +-
 src/Alveara.Api/Architecture/Treatment/TreatmentPlanStateRules.cs                          |    62 +
 src/Alveara.Api/Architecture/Treatment/TreatmentPlanViews.cs                               |     6 +-
 src/Alveara.Api/Controllers/AppointmentsController.cs                                      |     4 +-
 src/Alveara.Api/Controllers/TreatmentPlansController.cs                                    |    53 +-
 src/Alveara.Api/Data/AlveraDbContext.cs                                                    |     1 +
 src/Alveara.Api/Data/TreatmentPlanModel.cs                                                 |    20 +
 src/Alveara.Api/Migrations/20261118143203_AddTreatmentPlanItemAppointmentLinks.Designer.cs | 10660 +++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
 src/Alveara.Api/Migrations/20261118143203_AddTreatmentPlanItemAppointmentLinks.cs          |   158 ++
 src/Alveara.Api/Migrations/AlveraDbContextModelSnapshot.cs                                 |    88 +-
 src/alveara-client/e2e/treatment-plan-integrity-real-backend.spec.ts                       |   257 +++
 src/alveara-client/e2e/treatment-plans-complete-real-backend.spec.ts                       |    41 +-
 src/alveara-client/playwright.config.ts                                                    |     2 +-
 src/alveara-client/playwright.treatment-plan-integrity.config.ts                           |    24 +
 src/alveara-client/src/CalendarBookTreatment.test.tsx                                      |    90 +
 src/alveara-client/src/pages/calendar/AppointmentPanel.tsx                                 |     2 +
 src/alveara-client/src/pages/calendar/AppointmentTreatment.tsx                             |    67 +
 src/alveara-client/src/pages/calendar/BookableTreatmentField.tsx                           |    47 +
 src/alveara-client/src/pages/calendar/NewAppointmentPanel.tsx                              |     8 +-
 src/alveara-client/src/pages/calendar/bookableTreatmentText.ts                             |     8 +
 src/alveara-client/src/pages/calendar/explainRefusal.ts                                    |     2 +
 src/alveara-client/src/pages/treatmentplan/ClinicalHistorySection.tsx                      |     7 +-
 src/alveara-client/src/pages/treatmentplan/CompletionResultPanel.tsx                       |     1 +
 src/alveara-client/src/pages/treatmentplan/TreatmentPlan.css                               |    10 +
 src/alveara-client/src/pages/treatmentplan/TreatmentPlanActions.tsx                        |    46 +-
 src/alveara-client/src/pages/treatmentplan/TreatmentPlanActivity.tsx                       |    42 +
 src/alveara-client/src/pages/treatmentplan/TreatmentPlanAddItemForm.tsx                    |     8 +-
 src/alveara-client/src/pages/treatmentplan/TreatmentPlanAdoptCompletion.test.tsx           |     3 +-
 src/alveara-client/src/pages/treatmentplan/TreatmentPlanAnswers.test.tsx                   |    55 +-
 src/alveara-client/src/pages/treatmentplan/TreatmentPlanAppointmentLink.tsx                |   102 +
 src/alveara-client/src/pages/treatmentplan/TreatmentPlanCard.tsx                           |    11 +-
 src/alveara-client/src/pages/treatmentplan/TreatmentPlanClinicalHistory.test.tsx           |     2 +-
 src/alveara-client/src/pages/treatmentplan/TreatmentPlanComparison.tsx                     |     2 +-
 src/alveara-client/src/pages/treatmentplan/TreatmentPlanDecisionForm.tsx                   |     4 +-
 src/alveara-client/src/pages/treatmentplan/TreatmentPlanEstimate.tsx                       |     2 +-
 src/alveara-client/src/pages/treatmentplan/TreatmentPlanForm.tsx                           |     5 +-
 src/alveara-client/src/pages/treatmentplan/TreatmentPlanItemStateLine.tsx                  |    13 +-
 src/alveara-client/src/pages/treatmentplan/TreatmentPlanN023.test.tsx                      |   135 ++
 src/alveara-client/src/pages/treatmentplan/TreatmentPlanN023Proof.test.tsx                 |    79 +
 src/alveara-client/src/pages/treatmentplan/TreatmentPlanPatientCopy.test.tsx               |     6 +-
 src/alveara-client/src/pages/treatmentplan/TreatmentPlanPatientCopy.tsx                    |    29 +-
 src/alveara-client/src/pages/treatmentplan/TreatmentPlanReasonForm.tsx                     |     3 +-
 src/alveara-client/src/pages/treatmentplan/TreatmentPlanReviseForm.tsx                     |     5 +-
 src/alveara-client/src/pages/treatmentplan/TreatmentPlanScheduleForm.tsx                   |    29 +-
 src/alveara-client/src/pages/treatmentplan/TreatmentPlanStateControls.tsx                  |     9 +-
 src/alveara-client/src/pages/treatmentplan/TreatmentPlansPanel.test.tsx                    |    16 +-
 src/alveara-client/src/pages/treatmentplan/TreatmentPlansPanel.tsx                         |     2 +-
 src/alveara-client/src/pages/treatmentplan/treatmentPlanRules.ts                           |    38 +
 src/alveara-client/src/services/completionApi.ts                                           |     2 +
 src/alveara-client/src/services/schedulingApi.ts                                           |    11 +
 src/alveara-client/src/services/treatmentPlansApi.ts                                       |    45 +-
 src/alveara-client/src/test/fakeScheduleServer.ts                                          |    17 +
 src/alveara-client/src/test/treatmentPlanFixtures.ts                                       |     3 +
 95 files changed, 13910 insertions(+), 276 deletions(-)
```
