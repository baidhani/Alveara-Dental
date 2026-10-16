# ALV-016-C01 R01 - Changed files

Implementation commit `f09bb80f8698a94e5b344bb3cda4259aad361509` on top of `4ac24fb`: 41 files (plus the walkthrough screenshots), all additions except the small edits marked **edit**. Raw: `git diff --numstat 4ac24fb f09bb80`.

## Backend: rules, entities, services and controller (`src/Alveara.Api/Architecture/Prescribing`, `Controllers`)
- `src/Alveara.Api/Architecture/Prescribing/PrescribingSafetyReader.cs` (+42 -0)
- `src/Alveara.Api/Architecture/Prescribing/PrescriptionDraftService.cs` (+292 -0)
- `src/Alveara.Api/Architecture/Prescribing/PrescriptionFinalizeService.cs` (+99 -0)
- `src/Alveara.Api/Architecture/Prescribing/PrescriptionLifecycleService.cs` (+102 -0)
- `src/Alveara.Api/Architecture/Prescribing/PrescriptionRecordReader.cs` (+44 -0)
- `src/Alveara.Api/Architecture/Prescribing/PrescriptionViews.cs` (+3 -1) **edit**
- `src/Alveara.Api/Architecture/Prescribing/PrescriptionWorkflow.cs` (+77 -0)
- `src/Alveara.Api/Architecture/Prescribing/PrescriptionWorkflowRules.cs` (+216 -0)
- `src/Alveara.Api/Architecture/Prescribing/PrescriptionWorkflowViews.cs` (+65 -0)
- `src/Alveara.Api/Controllers/PrescriptionWorkflowController.cs` (+136 -0)

## Backend: model, migration and wiring (`Data`, `Migrations`, `Program.cs`)
- `src/Alveara.Api/Data/AlveraDbContext.cs` (+4 -0) **edit**
- `src/Alveara.Api/Data/PrescriptionModel.cs` (+3 -3) **edit**
- `src/Alveara.Api/Data/PrescriptionWorkflowModel.cs` (+125 -0)
- `src/Alveara.Api/Migrations/20261014024801_AddPrescriptionWorkflow.Designer.cs` (+6776 -0)
- `src/Alveara.Api/Migrations/20261014024801_AddPrescriptionWorkflow.cs` (+316 -0)
- `src/Alveara.Api/Migrations/AlveraDbContextModelSnapshot.cs` (+377 -0) **edit**
- `src/Alveara.Api/Program.cs` (+5 -0) **edit**

## Backend tests (`src/Alveara.Api.Tests`)
- `src/Alveara.Api.Tests/PrescriptionWorkflowApiTests.cs` (+303 -0)
- `src/Alveara.Api.Tests/PrescriptionWorkflowRulesTests.cs` (+288 -0)
- `src/Alveara.Api.Tests/PrescriptionWorkflowSchemaTests.cs` (+491 -0)
- `src/Alveara.Api.Tests/PrescriptionWorkflowServiceTests.cs` (+583 -0)

## Frontend: API client, screens and registry (`src/alveara-client/src`)
- `src/alveara-client/src/App.patientWorkspaceRoutes.test.tsx` (+2 -1) **edit**
- `src/alveara-client/src/App.tsx` (+2 -1) **edit**
- `src/alveara-client/src/app/patientWorkspaceTabs.ts` (+2 -0) **edit**
- `src/alveara-client/src/pages/PatientWorkspacePage.tsx` (+7 -0) **edit**
- `src/alveara-client/src/pages/prescription/workflow/DraftComposer.tsx` (+341 -0)
- `src/alveara-client/src/pages/prescription/workflow/FinalizeConfirmation.tsx` (+49 -0)
- `src/alveara-client/src/pages/prescription/workflow/PrescriptionRecordList.tsx` (+88 -0)
- `src/alveara-client/src/pages/prescription/workflow/PrescriptionWorkflow.css` (+20 -0)
- `src/alveara-client/src/pages/prescription/workflow/PrescriptionWorkflowPanel.test.tsx` (+544 -0)
- `src/alveara-client/src/pages/prescription/workflow/PrescriptionWorkflowPanel.tsx` (+124 -0)
- `src/alveara-client/src/pages/prescription/workflow/PrintPreview.tsx` (+69 -0)
- `src/alveara-client/src/pages/prescription/workflow/workflowRules.ts` (+60 -0)
- `src/alveara-client/src/services/prescriptionWorkflowApi.ts` (+171 -0)

## Real-browser walkthrough and configs (`src/alveara-client/e2e`, `playwright.*`)
- `src/alveara-client/e2e/prescription-workflow-real-backend.spec.ts` (+492 -0)
- `src/alveara-client/playwright.config.ts` (+1 -1) **edit**
- `src/alveara-client/playwright.prescription-workflow.config.ts` (+24 -0)

## Docs and progress
- `PROGRESS.md` (+7 -0) **edit**
- `docs/PRESCRIPTION_WORKFLOW.md` (+68 -0)
- `docs/testing/PRESCRIPTION_WORKFLOW_NEGATIVE_CONTROLS.md` (+45 -0)
- `docs/testing/REAL_BACKEND_E2E.md` (+13 -0) **edit**

## Not in the implementation commit (this handoff)
- `.alveara/handoffs/ALV-016-C01/` (this attempt's handoff, evidence and artifacts), `.alveara/BUILD_STATE.md`, `.alveara/EXECUTION_STATUS.json`, `PROGRESS.md`: the evidence/status commit.

## Files deliberately not changed
- STORY-016's `PrescriptionService.cs`, `PrescriptionRules.cs`, `AllergyCheck.cs`, `Prescription.cs`, `PrescriptionsController.cs`, its migration `AddPrescriptions` and all of its tests and screens (`PrescriptionsPanel.tsx`, `PrescriptionForm.tsx`, `PrescriptionList.tsx`, `PrescriptionSafetyPanel.tsx`, `prescriptionApi.ts`); the safety, clinical-record and billing code.
