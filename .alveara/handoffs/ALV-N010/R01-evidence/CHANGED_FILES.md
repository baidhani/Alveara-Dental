# ALV-N010 R01 — Changed files

Implementation commit `a4c6c4ff5dc98404ecec851cad28206dce9adaf0` — 49 files. `A` = added, `M` = modified; counts are `+added −removed` lines.

## Backend product (src/Alveara.Api) (15)

- `A` `src/Alveara.Api/Architecture/Forms/FormDefinition.cs` +148 −0
- `A` `src/Alveara.Api/Architecture/Forms/FormEntities.cs` +182 −0
- `A` `src/Alveara.Api/Architecture/Forms/FormTemplateService.cs` +141 −0
- `A` `src/Alveara.Api/Architecture/Forms/FormWrite.cs` +37 −0
- `A` `src/Alveara.Api/Architecture/Forms/PatientFormReader.cs` +65 −0
- `A` `src/Alveara.Api/Architecture/Forms/PatientFormService.cs` +258 −0
- `A` `src/Alveara.Api/Architecture/Forms/PatientFormViews.cs` +33 −0
- `M` `src/Alveara.Api/Architecture/Identity/Permission.cs` +6 −0
- `M` `src/Alveara.Api/Architecture/Identity/PermissionMatrix.cs` +10 −6
- `A` `src/Alveara.Api/Controllers/FormsController.cs` +151 −0
- `M` `src/Alveara.Api/Data/AlveraDbContext.cs` +67 −0
- `A` `src/Alveara.Api/Migrations/20261002031128_AddVersionedForms.Designer.cs` +1543 −0
- `A` `src/Alveara.Api/Migrations/20261002031128_AddVersionedForms.cs` +250 −0
- `M` `src/Alveara.Api/Migrations/AlveraDbContextModelSnapshot.cs` +352 −0
- `M` `src/Alveara.Api/Program.cs` +3 −0

## Backend tests (src/Alveara.Api.Tests) (6)

- `A` `src/Alveara.Api.Tests/FormLifecycleTests.cs` +295 −0
- `A` `src/Alveara.Api.Tests/FormSigningTests.cs` +377 −0
- `A` `src/Alveara.Api.Tests/FormTemplateVersionTests.cs` +271 −0
- `A` `src/Alveara.Api.Tests/FormTestSupport.cs` +97 −0
- `A` `src/Alveara.Api.Tests/FormsApiTests.cs` +329 −0
- `A` `src/Alveara.Api.Tests/SignedFormSnapshotTests.cs` +160 −0

## Frontend product (src/alveara-client/src) (16)

- `M` `src/alveara-client/src/App.tsx` +12 −1
- `M` `src/alveara-client/src/app/moduleRegistry.ts` +1 −0
- `M` `src/alveara-client/src/app/patientWorkspaceTabs.ts` +2 −0
- `A` `src/alveara-client/src/components/FormFieldInputs.css` +52 −0
- `A` `src/alveara-client/src/components/FormFieldInputs.tsx` +88 −0
- `M` `src/alveara-client/src/pages/PatientWorkspacePage.tsx` +13 −0
- `A` `src/alveara-client/src/pages/forms/FormSignReview.tsx` +132 −0
- `A` `src/alveara-client/src/pages/forms/FormStatusBadge.tsx` +10 −0
- `A` `src/alveara-client/src/pages/forms/FormTemplatesPage.tsx` +119 −0
- `A` `src/alveara-client/src/pages/forms/FormVoidPanel.tsx` +59 −0
- `A` `src/alveara-client/src/pages/forms/Forms.css` +159 −0
- `A` `src/alveara-client/src/pages/forms/PatientFormView.tsx` +229 −0
- `A` `src/alveara-client/src/pages/forms/PatientFormsPanel.tsx` +122 −0
- `A` `src/alveara-client/src/pages/forms/SignedFormView.tsx` +66 −0
- `A` `src/alveara-client/src/pages/forms/TemplateEditor.tsx` +172 −0
- `A` `src/alveara-client/src/services/formsApi.ts` +186 −0

## Frontend tests and test support (src/alveara-client/src) (6)

- `M` `src/alveara-client/src/App.patientWorkspaceRoutes.test.tsx` +3 −1
- `A` `src/alveara-client/src/FormTemplates.test.tsx` +161 −0
- `A` `src/alveara-client/src/PatientForms.test.tsx` +389 −0
- `A` `src/alveara-client/src/styles/formsContrast.test.ts` +100 −0
- `A` `src/alveara-client/src/test/fakeFormsServer.ts` +221 −0
- `M` `src/alveara-client/src/test/fakePatientServer.ts` +7 −0

## Browser tests and configuration (3)

- `A` `src/alveara-client/e2e/forms-real-backend.spec.ts` +364 −0
- `M` `src/alveara-client/playwright.config.ts` +1 −1
- `A` `src/alveara-client/playwright.forms.config.ts` +24 −0

## Documentation (3)

- `A` `docs/FORMS_AND_CONSENTS.md` +72 −0
- `M` `docs/PATIENT_WORKSPACE.md` +4 −0
- `M` `docs/testing/REAL_BACKEND_E2E.md` +20 −0

## Reading guide

- **Completed-story files touched, additively only:** `Permission.cs` / `PermissionMatrix.cs` (four permissions appended, grants added), `AlveraDbContext.cs` (new sets, mappings and the immutability guard added beside the existing audit guard), `Program.cs` (service registration), `App.tsx` / `moduleRegistry.ts` / `patientWorkspaceTabs.ts` / `PatientWorkspacePage.tsx` (new routes, nav entry, tab and tab components), `playwright.config.ts` (one `testIgnore` entry), `AlveraDbContextModelSnapshot.cs` (generated).
- **Tests of completed stories:** the only edit is `App.patientWorkspaceRoutes.test.tsx` (ALV-003-C01's own test, which pinned the tab list to exactly three; it now expects the Forms tab, gated by `ViewSignedForms`) and `test/fakePatientServer.ts` (one protected hook so a subclass can serve the forms API; behaviour unchanged). No STORY-003 test file changed.
- **Migration:** `AddVersionedForms` (+ designer) with two `INSTEAD OF UPDATE, DELETE` triggers added by hand.
- Generated/untracked output (`bin/`, `obj/`, `dist/`, `test-results/`) is not part of the commit.
