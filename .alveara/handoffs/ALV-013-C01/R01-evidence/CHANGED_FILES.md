# Changed files - ALV-013-C01 R01

Implementation commit `b94eb699193b7e25a0bb236684eeb0a2c5ebda10` (38 files). `A` = added, `M` = modified.

## Backend (API) (8)
- `M` `src/Alveara.Api/Architecture/Clinical/Diagnosis.cs`
- `M` `src/Alveara.Api/Architecture/Clinical/DiagnosisRules.cs`
- `A` `src/Alveara.Api/Architecture/Clinical/DiagnosisService.Structure.cs`
- `M` `src/Alveara.Api/Architecture/Clinical/DiagnosisService.cs`
- `M` `src/Alveara.Api/Architecture/Clinical/DiagnosisViews.cs`
- `M` `src/Alveara.Api/Controllers/DiagnosesController.cs`
- `M` `src/Alveara.Api/Data/AlveraDbContext.cs`
- `M` `src/Alveara.Api/Data/DiagnosisModel.cs`

## Migration (3)
- `A` `src/Alveara.Api/Migrations/20261006122255_AddDiagnosisStructure.Designer.cs`
- `A` `src/Alveara.Api/Migrations/20261006122255_AddDiagnosisStructure.cs`
- `M` `src/Alveara.Api/Migrations/AlveraDbContextModelSnapshot.cs`

## Backend tests (4)
- `A` `src/Alveara.Api.Tests/DiagnosisStructureApiTests.cs`
- `A` `src/Alveara.Api.Tests/DiagnosisStructureRulesTests.cs`
- `A` `src/Alveara.Api.Tests/DiagnosisStructureSchemaTests.cs`
- `A` `src/Alveara.Api.Tests/DiagnosisStructureServiceTests.cs`

## Frontend (UI) (11)
- `M` `src/alveara-client/src/pages/diagnosis/Diagnosis.css`
- `A` `src/alveara-client/src/pages/diagnosis/DiagnosisAmendForm.tsx`
- `A` `src/alveara-client/src/pages/diagnosis/DiagnosisChips.tsx`
- `M` `src/alveara-client/src/pages/diagnosis/DiagnosisForm.tsx`
- `A` `src/alveara-client/src/pages/diagnosis/DiagnosisHistory.tsx`
- `M` `src/alveara-client/src/pages/diagnosis/DiagnosisItem.tsx`
- `A` `src/alveara-client/src/pages/diagnosis/DiagnosisLinkForm.tsx`
- `A` `src/alveara-client/src/pages/diagnosis/DiagnosisReasonForm.tsx`
- `A` `src/alveara-client/src/pages/diagnosis/DiagnosisStructureFields.tsx`
- `A` `src/alveara-client/src/pages/diagnosis/diagnosisStructureRules.ts`
- `M` `src/alveara-client/src/services/diagnosisApi.ts`

## Frontend tests and test fakes (6)
- `A` `src/alveara-client/src/DiagnosisStructure.test.tsx`
- `A` `src/alveara-client/src/DiagnosisStructureApi.test.ts`
- `A` `src/alveara-client/src/pages/diagnosis/diagnosisStructureRules.test.ts`
- `M` `src/alveara-client/src/test/fakeClinicalServer.ts`
- `M` `src/alveara-client/src/test/fakeDiagnosisStore.ts`
- `M` `src/alveara-client/src/test/fakeOdontogramStore.ts`

## Real-browser walkthrough and test configuration (3)
- `A` `src/alveara-client/e2e/diagnoses-structure-real-backend.spec.ts`
- `M` `src/alveara-client/playwright.config.ts`
- `A` `src/alveara-client/playwright.diagnoses-structure.config.ts`

## Docs and records (3)
- `M` `PROGRESS.md`
- `M` `docs/DIAGNOSES.md`
- `M` `docs/testing/REAL_BACKEND_E2E.md`

## Parent files touched and how
- `Diagnosis.cs`, `DiagnosisRules.cs`, `DiagnosisService.cs`, `DiagnosisViews.cs`, `DiagnosesController.cs`, `DiagnosisModel.cs`, `diagnosisApi.ts`, `DiagnosisForm.tsx`, `DiagnosisItem.tsx` (STORY-013 files) were **extended compatibly**: new optional members and parameters with defaults, new properties appended to responses, the status and change-type lists widened, the default list now shows Active and Resolved. STORY-013's own tests pass unchanged.
- `fakeDiagnosisStore.ts`, `fakeClinicalServer.ts` and `fakeOdontogramStore.ts` (shared test fakes) were extended; `fakeOdontogramStore.label` became public so the fake server can describe a linked finding. No test file of STORY-013 was edited.
- `playwright.config.ts` only gained the new walkthrough in its `testIgnore` list (like every real-backend spec).
