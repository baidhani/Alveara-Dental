# ALV-015-C01 R01 - Changed files

Implementation commit `3ec1215044916ed728ac643f98c876a3466fb3d3`: 73 files, 11,732 insertions, 134 deletions (about 4,700 of the insertions are the generated migration designer). Paths are under `src/` unless noted.

## New (backend)
- `Alveara.Api/Architecture/Treatment/`: `TreatmentPlanRevision.cs` (entities and states), `TreatmentPlanRevisionRules.cs` (revise validation, layout), `TreatmentPlanRevisionViews.cs`, `TreatmentPlanService.Revisions.cs`, `.States.cs`, `.Compare.cs`, `.ReferenceCode.cs`, `DiagnosisReconciliationService.cs`
- `Alveara.Api/Data/TreatmentPlanRevisionModel.cs`
- `Alveara.Api/Migrations/20261009050158_AddTreatmentPlanCompletion.cs` and `.Designer.cs`

## Changed (backend)
- `Treatment/TreatmentPlan.cs`, `TreatmentPlanRules.cs`, `TreatmentPlanService.cs`, `.Items.cs`, `.Queries.cs`, `TreatmentPlanViews.cs`: revision numbers, the new change types and inputs; create, add, withdraw and rename now save a revision; views gain fields
- `Controllers/TreatmentPlansController.cs`: 11 endpoints
- `Data/TreatmentPlanModel.cs`, `Data/AlveraDbContext.cs`, `Migrations/AlveraDbContextModelSnapshot.cs`, `Program.cs`: the new model, sets, snapshot and service registration
- **Shared (STORY-013 / ALV-013-C01):** `Clinical/DiagnosisService.cs`, `Clinical/DiagnosisViews.cs`, `Data/DiagnosisModel.cs`: the Resolved state and fields, the reference lock, the trigger declaration

## New (tests, backend)
`Alveara.Api.Tests/`: `TreatmentPlanRevisionSchemaTests.cs`, `TreatmentPlanRevisionMigrationTests.cs`, `TreatmentPlanRevisionServiceTests.cs`, `TreatmentPlanStateServiceTests.cs`, `DiagnosisReconciliationTests.cs`, `TreatmentPlanCompletionApiTests.cs`

## New (frontend)
`alveara-client/src/pages/treatmentplan/`: `TreatmentPlanReviseForm.tsx`, `TreatmentPlanPhaseEditor.tsx`, `TreatmentPlanPhaseGroups.tsx`, `TreatmentPlanItemRow.tsx`, `TreatmentPlanItemStateLine.tsx`, `TreatmentPlanEstimate.tsx`, `TreatmentPlanDecisionForm.tsx`, `TreatmentPlanScheduleForm.tsx`, `TreatmentPlanStateControls.tsx`, `TreatmentPlanRevisions.tsx`, `TreatmentPlanRevisionView.tsx`, `TreatmentPlanComparison.tsx`, `TreatmentPlanPatientCopy.tsx`, `TreatmentPlanReferenceCodeForm.tsx`, `TreatmentPlanReferences.tsx`, `treatmentPlanLayout.ts`

## Changed (frontend)
`services/treatmentPlansApi.ts` (types and calls), `services/diagnosisApi.ts` and `pages/diagnosis/DiagnosisItem.tsx` (a tied reference), `pages/treatmentplan/TreatmentPlanCard.tsx`, `TreatmentPlanActions.tsx`, `TreatmentPlansPanel.tsx`, `treatmentPlanRules.ts`, `TreatmentPlan.css`

## New (frontend tests and browser)
`treatmentPlanLayout.test.ts`, `TreatmentPlanRevise.test.tsx`, `TreatmentPlanAnswers.test.tsx`, `TreatmentPlanRevisions.test.tsx`, `TreatmentPlanPatientCopy.test.tsx`, `TreatmentPlanReferences.test.tsx`, `pages/diagnosis/DiagnosisPlanReference.test.tsx`, `test/treatmentPlanFixtures.ts` (shared fixtures, moved out of the STORY-015 panel test); `e2e/treatment-plans-complete-real-backend.spec.ts`, `playwright.treatment-plans-complete.config.ts`; changed: `treatmentPlanRules.test.ts`, `TreatmentPlansPanel.test.tsx` (fixtures), `playwright.config.ts` (the new spec excluded from the mocked run)

## Documentation
New `docs/TREATMENT_PLAN_COMPLETION.md`; changed `docs/TREATMENT_PLANS.md`, `docs/DIAGNOSES.md`, `docs/testing/REAL_BACKEND_E2E.md`; `PROGRESS.md` (the implementation entry)

## Evidence commit (separate)
`.alveara/handoffs/ALV-015-C01/R01.md`, `R01-evidence/`, `.alveara/BUILD_STATE.md`, `.alveara/EXECUTION_STATUS.json`

## Not changed
`HANDOFF_SCHEMA.md`, `QUALITY_GATES.md`, `xunit.runner.json` (the runner stays serial), every file of ALV-N005, and every STORY-015 test except the shared fixtures and wiring noted above.
