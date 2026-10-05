# Changed files - ALV-006-C01 R01

Implementation commit `90f6ebd6cefc2bbb99149c452a9d13ae4f3cb9a0` (44 files). `A` = added, `M` = modified.

## Backend (API) (11)
- `A` `src/Alveara.Api/Architecture/Odontogram/ConditionCatalogue.cs`
- `A` `src/Alveara.Api/Architecture/Odontogram/ConditionTypeService.cs`
- `M` `src/Alveara.Api/Architecture/Odontogram/OdontogramEntities.cs`
- `M` `src/Alveara.Api/Architecture/Odontogram/OdontogramRules.cs`
- `A` `src/Alveara.Api/Architecture/Odontogram/OdontogramService.History.cs`
- `M` `src/Alveara.Api/Architecture/Odontogram/OdontogramService.cs`
- `M` `src/Alveara.Api/Architecture/Odontogram/OdontogramViews.cs`
- `M` `src/Alveara.Api/Controllers/OdontogramController.cs`
- `M` `src/Alveara.Api/Data/AlveraDbContext.cs`
- `M` `src/Alveara.Api/Data/OdontogramModel.cs`
- `M` `src/Alveara.Api/Program.cs`

## Migration (3)
- `A` `src/Alveara.Api/Migrations/20261004223713_AddConditionCatalogueAndLinks.Designer.cs`
- `A` `src/Alveara.Api/Migrations/20261004223713_AddConditionCatalogueAndLinks.cs`
- `M` `src/Alveara.Api/Migrations/AlveraDbContextModelSnapshot.cs`

## Backend tests (5)
- `A` `src/Alveara.Api.Tests/ConditionTypeServiceTests.cs`
- `A` `src/Alveara.Api.Tests/OdontogramCatalogueApiTests.cs`
- `A` `src/Alveara.Api.Tests/OdontogramCatalogueSchemaTests.cs`
- `A` `src/Alveara.Api.Tests/OdontogramHistoryAndLinksTests.cs`
- `M` `src/Alveara.Api.Tests/OdontogramSchemaTests.cs`

## Frontend (14)
- `M` `src/alveara-client/src/pages/PatientWorkspacePage.tsx`
- `A` `src/alveara-client/src/pages/odontogram/ConditionTypesPanel.tsx`
- `M` `src/alveara-client/src/pages/odontogram/FindingHistory.tsx`
- `M` `src/alveara-client/src/pages/odontogram/FindingRow.tsx`
- `M` `src/alveara-client/src/pages/odontogram/Odontogram.css`
- `M` `src/alveara-client/src/pages/odontogram/OdontogramPanel.tsx`
- `M` `src/alveara-client/src/pages/odontogram/RecordFindingForm.tsx`
- `M` `src/alveara-client/src/pages/odontogram/ToothChart.tsx`
- `M` `src/alveara-client/src/pages/odontogram/ToothDetail.tsx`
- `A` `src/alveara-client/src/pages/odontogram/ToothTimeline.tsx`
- `A` `src/alveara-client/src/pages/odontogram/dentition.ts`
- `M` `src/alveara-client/src/pages/odontogram/odontogramText.ts`
- `M` `src/alveara-client/src/pages/odontogram/toothNumbering.ts`
- `M` `src/alveara-client/src/services/odontogramApi.ts`

## Frontend tests and fakes (5)
- `M` `src/alveara-client/src/Odontogram.test.tsx`
- `A` `src/alveara-client/src/OdontogramLongitudinal.test.tsx`
- `M` `src/alveara-client/src/OdontogramWrite.test.tsx`
- `M` `src/alveara-client/src/styles/odontogramContrast.test.ts`
- `M` `src/alveara-client/src/test/fakeOdontogramStore.ts`

## Real-browser walkthrough and configs (3)
- `A` `src/alveara-client/e2e/odontogram-longitudinal-real-backend.spec.ts`
- `M` `src/alveara-client/playwright.config.ts`
- `A` `src/alveara-client/playwright.odontogram-longitudinal.config.ts`

## Docs and progress (3)
- `M` `PROGRESS.md`
- `M` `docs/ODONTOGRAM.md`
- `M` `docs/testing/REAL_BACKEND_E2E.md`

## Not touched
- `.colaberry/` (portal-owned), `assets/` and `index.html` (the Command Center), the shared conflict banner and every dependency's own test files except the STORY-006 tests listed above (their lookups were scoped; see HANDOFF).
