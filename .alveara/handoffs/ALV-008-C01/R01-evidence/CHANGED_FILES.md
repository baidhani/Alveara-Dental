# ALV-008-C01 R01 - Changed files

Implementation commit `9bc1df833915289d30eac6c4c62d683f2fd1d405`: 52 files, 11240 insertions, 26 deletions (about 6,350 insertions are the generated migration designer and the regenerated model snapshot).

| File | + | - | What |
|---|---|---|---|
| `.alveara/story_catalog.json` | 5 | 0 | catalog entry for ALV-008-C02 (outside the story's code) |
| `PROGRESS.md` | 8 | 0 | progress entry |
| `docs/COMPLETION.md` | 68 | 0 | documentation |
| `docs/testing/REAL_BACKEND_E2E.md` | 13 | 0 | documentation |
| `src/Alveara.Api.Tests/CompletedProcedureAmendmentTests.cs` | 276 | 0 | tests |
| `src/Alveara.Api.Tests/CompletedProcedureEffectsTests.cs` | 307 | 0 | tests |
| `src/Alveara.Api.Tests/CompletedProcedureHistoryTests.cs` | 106 | 0 | tests |
| `src/Alveara.Api.Tests/CompletedProcedureMigrationTests.cs` | 68 | 0 | tests |
| `src/Alveara.Api.Tests/CompletedProcedureSchemaTests.cs` | 312 | 0 | tests |
| `src/Alveara.Api.Tests/CompletedProcedureServiceTests.cs` | 358 | 0 | tests |
| `src/Alveara.Api.Tests/CompletionApiTests.cs` | 298 | 0 | tests |
| `src/Alveara.Api.Tests/CompletionRulesTests.cs` | 178 | 0 | tests |
| `src/Alveara.Api.Tests/CompletionTestBase.cs` | 152 | 0 | tests |
| `src/Alveara.Api.Tests/SchedulingApiHarness.cs` | 5 | 0 | tests |
| `src/Alveara.Api/Architecture/Completion/CompletedProcedure.cs` | 90 | 0 | completion domain: entities, rules, views, the service and its partials |
| `src/Alveara.Api/Architecture/Completion/CompletedProcedureService.Amend.cs` | 103 | 0 | completion domain: entities, rules, views, the service and its partials |
| `src/Alveara.Api/Architecture/Completion/CompletedProcedureService.Charge.cs` | 49 | 0 | completion domain: entities, rules, views, the service and its partials |
| `src/Alveara.Api/Architecture/Completion/CompletedProcedureService.Chart.cs` | 81 | 0 | completion domain: entities, rules, views, the service and its partials |
| `src/Alveara.Api/Architecture/Completion/CompletedProcedureService.Context.cs` | 75 | 0 | completion domain: entities, rules, views, the service and its partials |
| `src/Alveara.Api/Architecture/Completion/CompletedProcedureService.History.cs` | 26 | 0 | completion domain: entities, rules, views, the service and its partials |
| `src/Alveara.Api/Architecture/Completion/CompletedProcedureService.Read.cs` | 66 | 0 | completion domain: entities, rules, views, the service and its partials |
| `src/Alveara.Api/Architecture/Completion/CompletedProcedureService.Readiness.cs` | 41 | 0 | completion domain: entities, rules, views, the service and its partials |
| `src/Alveara.Api/Architecture/Completion/CompletedProcedureService.cs` | 128 | 0 | completion domain: entities, rules, views, the service and its partials |
| `src/Alveara.Api/Architecture/Completion/CompletionRules.cs` | 132 | 0 | completion domain: entities, rules, views, the service and its partials |
| `src/Alveara.Api/Architecture/Completion/CompletionViews.cs` | 83 | 0 | completion domain: entities, rules, views, the service and its partials |
| `src/Alveara.Api/Architecture/Odontogram/OdontogramService.CompletionSupport.cs` | 40 | 0 | shared staging used by the completion set (no behaviour change for the old paths) |
| `src/Alveara.Api/Architecture/Odontogram/OdontogramService.cs` | 1 | 8 | ChangeStateAsync stages through the shared StageMoveAsync |
| `src/Alveara.Api/Architecture/Treatment/TreatmentPlanService.ClinicalHistory.cs` | 6 | 2 | optional detail fields on the history entry |
| `src/Alveara.Api/Architecture/Treatment/TreatmentPlanService.CompletionSupport.cs` | 41 | 0 | shared staging used by the completion set (no behaviour change for the old paths) |
| `src/Alveara.Api/Architecture/Treatment/TreatmentPlanService.States.cs` | 1 | 9 | CompleteAcceptedAsync stages through the shared helper |
| `src/Alveara.Api/Controllers/CompletionController.cs` | 79 | 0 | the five new routes |
| `src/Alveara.Api/Controllers/TreatmentPlansController.cs` | 18 | 4 | legacy usage log; history route calls the enriching method |
| `src/Alveara.Api/Data/AlveraDbContext.cs` | 4 | 0 | wiring: sets, model, DI registration |
| `src/Alveara.Api/Data/CompletionModel.cs` | 98 | 0 | EF model: tables, checks, indexes, trigger registrations |
| `src/Alveara.Api/Migrations/20261012020128_AddCompletedProcedures.Designer.cs` | 6015 | 0 | migration (generated designer) |
| `src/Alveara.Api/Migrations/20261012020128_AddCompletedProcedures.cs` | 339 | 0 | migration with the six triggers |
| `src/Alveara.Api/Migrations/AlveraDbContextModelSnapshot.cs` | 331 | 0 | regenerated model snapshot |
| `src/Alveara.Api/Program.cs` | 1 | 0 | wiring: sets, model, DI registration |
| `src/alveara-client/e2e/completion-real-backend.spec.ts` | 411 | 0 | real-backend walkthrough and its config |
| `src/alveara-client/playwright.completion.config.ts` | 24 | 0 | real-backend walkthrough and its config |
| `src/alveara-client/playwright.config.ts` | 1 | 1 | real-backend walkthrough and its config |
| `src/alveara-client/src/pages/treatmentplan/ClinicalHistorySection.tsx` | 7 | 0 | UI |
| `src/alveara-client/src/pages/treatmentplan/CompletionResultPanel.tsx` | 31 | 0 | UI |
| `src/alveara-client/src/pages/treatmentplan/CompletionReviewTable.tsx` | 45 | 0 | UI |
| `src/alveara-client/src/pages/treatmentplan/TreatmentPlan.css` | 6 | 0 | UI |
| `src/alveara-client/src/pages/treatmentplan/TreatmentPlanActions.tsx` | 11 | 2 | UI |
| `src/alveara-client/src/pages/treatmentplan/TreatmentPlanReviewComplete.test.tsx` | 299 | 0 | UI |
| `src/alveara-client/src/pages/treatmentplan/TreatmentPlanReviewComplete.tsx` | 142 | 0 | UI |
| `src/alveara-client/src/pages/treatmentplan/TreatmentPlanStateControls.tsx` | 1 | 0 | UI |
| `src/alveara-client/src/pages/treatmentplan/completionReviewRules.ts` | 74 | 0 | UI |
| `src/alveara-client/src/services/completionApi.ts` | 178 | 0 | UI |
| `src/alveara-client/src/services/treatmentPlansApi.ts` | 8 | 0 | UI |

The handoff commits add `.alveara/handoffs/ALV-008-C01/**`, `.alveara/BUILD_STATE.md` and `.alveara/EXECUTION_STATUS.json` only.
