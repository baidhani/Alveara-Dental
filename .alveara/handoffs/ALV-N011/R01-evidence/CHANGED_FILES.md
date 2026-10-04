# Changed files — ALV-N011 R01

Implementation commit `9a3ac31b5675c273e3a44d5815c012cc82e861e5` (55 files). `A` = added, `M` = modified.

## Backend (API) (14)
- `M` `src/Alveara.Api/Architecture/Identity/Permission.cs`
- `M` `src/Alveara.Api/Architecture/Identity/PermissionMatrix.cs`
- `A` `src/Alveara.Api/Architecture/Safety/ClearanceService.cs`
- `A` `src/Alveara.Api/Architecture/Safety/SafetyAlertService.cs`
- `A` `src/Alveara.Api/Architecture/Safety/SafetyContextService.cs`
- `A` `src/Alveara.Api/Architecture/Safety/SafetyEntities.cs`
- `A` `src/Alveara.Api/Architecture/Safety/SafetyRules.cs`
- `A` `src/Alveara.Api/Architecture/Safety/SafetyViews.cs`
- `M` `src/Alveara.Api/Architecture/Scheduling/VisitBoardService.cs`
- `A` `src/Alveara.Api/Controllers/SafetyController.cs`
- `M` `src/Alveara.Api/Controllers/VisitsController.cs`
- `M` `src/Alveara.Api/Data/AlveraDbContext.cs`
- `A` `src/Alveara.Api/Data/SafetyModel.cs`
- `M` `src/Alveara.Api/Program.cs`

## Migration (3)
- `A` `src/Alveara.Api/Migrations/20261004030506_AddPatientSafety.Designer.cs`
- `A` `src/Alveara.Api/Migrations/20261004030506_AddPatientSafety.cs`
- `M` `src/Alveara.Api/Migrations/AlveraDbContextModelSnapshot.cs`

## Backend tests (7)
- `A` `src/Alveara.Api.Tests/ClearanceServiceTests.cs`
- `M` `src/Alveara.Api.Tests/ClinicalPermissionTests.cs`
- `A` `src/Alveara.Api.Tests/SafetyAlertServiceTests.cs`
- `A` `src/Alveara.Api.Tests/SafetyApiTests.cs`
- `A` `src/Alveara.Api.Tests/SafetyContextServiceTests.cs`
- `A` `src/Alveara.Api.Tests/SafetySchemaTests.cs`
- `A` `src/Alveara.Api.Tests/SafetyTestBase.cs`

## Frontend (17)
- `M` `src/alveara-client/src/App.tsx`
- `M` `src/alveara-client/src/app/PatientHeader.tsx`
- `M` `src/alveara-client/src/app/patientWorkspaceTabs.ts`
- `M` `src/alveara-client/src/pages/PatientWorkspacePage.tsx`
- `M` `src/alveara-client/src/pages/clinical/EncounterView.tsx`
- `M` `src/alveara-client/src/pages/flow/FlowBoard.css`
- `M` `src/alveara-client/src/pages/flow/VisitCardView.tsx`
- `A` `src/alveara-client/src/pages/safety/ClearanceCard.tsx`
- `A` `src/alveara-client/src/pages/safety/Safety.css`
- `A` `src/alveara-client/src/pages/safety/SafetyEntryRow.tsx`
- `A` `src/alveara-client/src/pages/safety/SafetyForms.tsx`
- `A` `src/alveara-client/src/pages/safety/SafetyHistory.tsx`
- `A` `src/alveara-client/src/pages/safety/SafetyPanel.tsx`
- `A` `src/alveara-client/src/pages/safety/SafetyStrip.tsx`
- `A` `src/alveara-client/src/pages/safety/safetyText.ts`
- `A` `src/alveara-client/src/services/safetyApi.ts`
- `M` `src/alveara-client/src/services/visitsApi.ts`

## Frontend tests and fakes (7)
- `M` `src/alveara-client/src/App.patientWorkspaceRoutes.test.tsx`
- `A` `src/alveara-client/src/FlowBoardSafety.test.tsx`
- `A` `src/alveara-client/src/Safety.test.tsx`
- `A` `src/alveara-client/src/styles/safetyContrast.test.ts`
- `M` `src/alveara-client/src/test/fakeClinicalServer.ts`
- `M` `src/alveara-client/src/test/fakeFlowServer.ts`
- `A` `src/alveara-client/src/test/fakeSafetyStore.ts`

## Real-browser walkthrough and configs (3)
- `A` `src/alveara-client/e2e/safety-real-backend.spec.ts`
- `M` `src/alveara-client/playwright.config.ts`
- `A` `src/alveara-client/playwright.safety.config.ts`

## Docs and progress (4)
- `M` `PROGRESS.md`
- `M` `docs/CLINICAL_DOCUMENTATION.md`
- `A` `docs/PATIENT_SAFETY.md`
- `M` `docs/testing/REAL_BACKEND_E2E.md`

## Not touched
- `.colaberry/` (portal-owned) and every dependency's own test files except `src/alveara-client/src/App.patientWorkspaceRoutes.test.tsx` (its pinned tab list gained `safety`).
