# Changed files — ALV-005-C01 R01

Implementation commit `cd56749b5b48f86225995197969d9ee93171055a` (62 files). `A` = added, `M` = modified.

## Backend (API) (24)
- `A` `src/Alveara.Api/Architecture/Clinical/ClinicalNames.cs`
- `A` `src/Alveara.Api/Architecture/Clinical/ClinicalRecord.cs`
- `A` `src/Alveara.Api/Architecture/Clinical/ClinicalRecordReader.cs`
- `A` `src/Alveara.Api/Architecture/Clinical/ClinicalRecordService.cs`
- `A` `src/Alveara.Api/Architecture/Clinical/ClinicalRecordViews.cs`
- `A` `src/Alveara.Api/Architecture/Clinical/ClinicalWrite.cs`
- `M` `src/Alveara.Api/Architecture/Clinical/Encounter.cs`
- `A` `src/Alveara.Api/Architecture/Clinical/EncounterNoteRules.cs`
- `A` `src/Alveara.Api/Architecture/Clinical/EncounterNoteService.cs`
- `A` `src/Alveara.Api/Architecture/Clinical/EncounterNotes.cs`
- `M` `src/Alveara.Api/Architecture/Clinical/EncounterReader.cs`
- `M` `src/Alveara.Api/Architecture/Clinical/EncounterService.cs`
- `A` `src/Alveara.Api/Architecture/Clinical/EncounterSigningService.cs`
- `M` `src/Alveara.Api/Architecture/Clinical/EncounterViews.cs`
- `A` `src/Alveara.Api/Architecture/Clinical/EncounterVitalsService.cs`
- `A` `src/Alveara.Api/Architecture/Clinical/NoteTemplateService.cs`
- `M` `src/Alveara.Api/Architecture/Identity/Permission.cs`
- `M` `src/Alveara.Api/Architecture/Identity/PermissionMatrix.cs`
- `M` `src/Alveara.Api/Controllers/ClinicalController.cs`
- `A` `src/Alveara.Api/Controllers/ClinicalNotesController.cs`
- `A` `src/Alveara.Api/Controllers/ClinicalRecordController.cs`
- `M` `src/Alveara.Api/Data/AlveraDbContext.cs`
- `A` `src/Alveara.Api/Data/ClinicalRecordModel.cs`
- `M` `src/Alveara.Api/Program.cs`

## Migration (3)
- `A` `src/Alveara.Api/Migrations/20261003235041_AddClinicalRecordAndNotes.Designer.cs`
- `A` `src/Alveara.Api/Migrations/20261003235041_AddClinicalRecordAndNotes.cs`
- `M` `src/Alveara.Api/Migrations/AlveraDbContextModelSnapshot.cs`

## Backend tests (5)
- `M` `src/Alveara.Api.Tests/ClinicalPermissionTests.cs`
- `A` `src/Alveara.Api.Tests/ClinicalRecordApiTests.cs`
- `A` `src/Alveara.Api.Tests/ClinicalRecordSchemaTests.cs`
- `A` `src/Alveara.Api.Tests/ClinicalRecordServiceTests.cs`
- `A` `src/Alveara.Api.Tests/EncounterNotesServiceTests.cs`

## Frontend (19)
- `M` `src/alveara-client/src/App.tsx`
- `M` `src/alveara-client/src/pages/PatientWorkspacePage.tsx`
- `M` `src/alveara-client/src/pages/clinical/AddendumPanel.tsx`
- `M` `src/alveara-client/src/pages/clinical/Clinical.css`
- `M` `src/alveara-client/src/pages/clinical/EncounterView.tsx`
- `M` `src/alveara-client/src/pages/clinical/FinalizeReview.tsx`
- `M` `src/alveara-client/src/pages/clinical/PatientClinicalPanel.tsx`
- `A` `src/alveara-client/src/pages/clinical/notes/NoteEditor.tsx`
- `A` `src/alveara-client/src/pages/clinical/notes/NotesPanel.tsx`
- `A` `src/alveara-client/src/pages/clinical/notes/TemplateForm.tsx`
- `A` `src/alveara-client/src/pages/clinical/notes/TemplatesPage.tsx`
- `A` `src/alveara-client/src/pages/clinical/notes/VitalsPanel.tsx`
- `A` `src/alveara-client/src/pages/clinical/record/ClinicalRecordPanel.tsx`
- `A` `src/alveara-client/src/pages/clinical/record/ItemHistory.tsx`
- `A` `src/alveara-client/src/pages/clinical/record/RecordItemRow.tsx`
- `A` `src/alveara-client/src/pages/clinical/record/RecordSectionCard.tsx`
- `M` `src/alveara-client/src/services/clinicalApi.ts`
- `A` `src/alveara-client/src/services/clinicalNotesApi.ts`
- `A` `src/alveara-client/src/services/clinicalRecordApi.ts`

## Frontend tests and fakes (5)
- `A` `src/alveara-client/src/ClinicalNotes.test.tsx`
- `A` `src/alveara-client/src/ClinicalRecord.test.tsx`
- `A` `src/alveara-client/src/styles/clinicalNotesContrast.test.ts`
- `A` `src/alveara-client/src/test/fakeClinicalRecordStore.ts`
- `M` `src/alveara-client/src/test/fakeClinicalServer.ts`

## Real-browser walkthrough and configs (3)
- `A` `src/alveara-client/e2e/clinical-companion-real-backend.spec.ts`
- `A` `src/alveara-client/playwright.clinical-companion.config.ts`
- `M` `src/alveara-client/playwright.config.ts`

## Docs and progress (3)
- `M` `PROGRESS.md`
- `M` `docs/CLINICAL_DOCUMENTATION.md`
- `M` `docs/testing/REAL_BACKEND_E2E.md`

## Not touched
- `.colaberry/` (portal-owned) and `docs/stories/STORY-005.md` (the parent contract).
- STORY-005's test files (`ClinicalEncounterSchemaTests`, `EncounterServiceTests`, `EncountersApiTests`, `Clinical.test.tsx`, `clinicalContrast.test.ts`, `clinical-real-backend.spec.ts`).
