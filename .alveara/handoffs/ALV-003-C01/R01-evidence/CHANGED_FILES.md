# ALV-003-C01 R01 — Changed files

Implementation commit `fdafb00b37b717bb768265453b0309eee8ff5717` — 66 files. `A` = added, `M` = modified; counts are `+added −removed` lines.

## Backend product (src/Alveara.Api) (18)

- `M` `src/Alveara.Api/Architecture/Identity/Permission.cs` +3 −0
- `M` `src/Alveara.Api/Architecture/Identity/PermissionMatrix.cs` +3 −2
- `M` `src/Alveara.Api/Architecture/Patients/Patient.cs` +18 −0
- `A` `src/Alveara.Api/Architecture/Patients/PatientDirectory.cs` +104 −0
- `A` `src/Alveara.Api/Architecture/Patients/PatientDuplicateDetector.cs` +81 −0
- `A` `src/Alveara.Api/Architecture/Patients/PatientEditService.cs` +84 −0
- `A` `src/Alveara.Api/Architecture/Patients/PatientEntities.cs` +84 −0
- `A` `src/Alveara.Api/Architecture/Patients/PatientInput.cs` +77 −0
- `M` `src/Alveara.Api/Architecture/Patients/PatientRegistrationService.cs` +80 −71
- `A` `src/Alveara.Api/Architecture/Patients/PatientRegistrationSettingsService.cs` +80 −0
- `A` `src/Alveara.Api/Architecture/Patients/PatientRelationshipService.cs` +113 −0
- `A` `src/Alveara.Api/Architecture/Patients/PatientWrite.cs` +61 −0
- `M` `src/Alveara.Api/Controllers/PatientsController.cs` +124 −16
- `M` `src/Alveara.Api/Data/AlveraDbContext.cs` +27 −0
- `A` `src/Alveara.Api/Migrations/20261002010543_AddPatientIdentityWorkspace.Designer.cs` +1191 −0
- `A` `src/Alveara.Api/Migrations/20261002010543_AddPatientIdentityWorkspace.cs` +206 −0
- `M` `src/Alveara.Api/Migrations/AlveraDbContextModelSnapshot.cs` +145 −0
- `M` `src/Alveara.Api/Program.cs` +5 −0

## Backend tests (src/Alveara.Api.Tests) (7)

- `A` `src/Alveara.Api.Tests/PatientDirectoryTests.cs` +126 −0
- `A` `src/Alveara.Api.Tests/PatientDuplicateWarningTests.cs` +177 −0
- `A` `src/Alveara.Api.Tests/PatientEditTests.cs` +210 −0
- `A` `src/Alveara.Api.Tests/PatientIdentityApiTests.cs` +292 −0
- `A` `src/Alveara.Api.Tests/PatientRelationshipTests.cs` +263 −0
- `A` `src/Alveara.Api.Tests/PatientRequirementsTests.cs` +271 −0
- `A` `src/Alveara.Api.Tests/PatientTestSupport.cs` +41 −0

## Frontend product (src/alveara-client/src) (28)

- `M` `src/alveara-client/src/App.tsx` +31 −0
- `M` `src/alveara-client/src/app/AppShell.css` +47 −3
- `M` `src/alveara-client/src/app/AppShell.tsx` +35 −21
- `A` `src/alveara-client/src/app/PatientHeader.tsx` +66 −0
- `M` `src/alveara-client/src/app/moduleRegistry.ts` +4 −0
- `A` `src/alveara-client/src/app/patientWorkspaceTabs.ts` +22 −0
- `A` `src/alveara-client/src/components/DuplicateComparisonPanel.css` +56 −0
- `A` `src/alveara-client/src/components/DuplicateComparisonPanel.tsx` +117 −0
- `A` `src/alveara-client/src/components/PatientFieldsForm.css` +19 −0
- `A` `src/alveara-client/src/components/PatientFieldsForm.tsx` +46 −0
- `A` `src/alveara-client/src/components/PatientPicker.css` +51 −0
- `A` `src/alveara-client/src/components/PatientPicker.tsx` +98 −0
- `A` `src/alveara-client/src/components/SafeLink.tsx` +20 −0
- `A` `src/alveara-client/src/components/patientFields.ts` +39 −0
- `A` `src/alveara-client/src/contexts/PatientContext.tsx` +76 −0
- `A` `src/alveara-client/src/contexts/PatientRequirementsContext.tsx` +43 −0
- `A` `src/alveara-client/src/contexts/patientContextStore.ts` +28 −0
- `A` `src/alveara-client/src/contexts/patientRequirementsStore.ts` +7 −0
- `A` `src/alveara-client/src/pages/HouseholdGuarantorPanel.tsx` +196 −0
- `A` `src/alveara-client/src/pages/PatientDetailsPanel.tsx` +160 −0
- `A` `src/alveara-client/src/pages/PatientHistoryPanel.tsx` +74 −0
- `M` `src/alveara-client/src/pages/PatientRegistrationPage.css` +7 −19
- `M` `src/alveara-client/src/pages/PatientRegistrationPage.tsx` +82 −74
- `A` `src/alveara-client/src/pages/PatientRegistrationSettingsPage.tsx` +112 −0
- `A` `src/alveara-client/src/pages/PatientSearchPage.tsx` +106 −0
- `A` `src/alveara-client/src/pages/PatientWorkspace.css` +180 −0
- `A` `src/alveara-client/src/pages/PatientWorkspacePage.tsx` +96 −0
- `M` `src/alveara-client/src/services/patientsApi.ts` +117 −5

## Frontend tests (src/alveara-client/src) (8)

- `A` `src/alveara-client/src/App.patientWorkspaceRoutes.test.tsx` +83 −0
- `A` `src/alveara-client/src/PatientHousehold.test.tsx` +202 −0
- `A` `src/alveara-client/src/PatientRequirements.test.tsx` +146 −0
- `A` `src/alveara-client/src/PatientWorkspace.test.tsx` +282 −0
- `A` `src/alveara-client/src/contexts/PatientContext.test.tsx` +150 −0
- `A` `src/alveara-client/src/pages/PatientRegistrationDuplicates.test.tsx` +141 −0
- `A` `src/alveara-client/src/styles/patientWorkspaceContrast.test.ts` +131 −0
- `A` `src/alveara-client/src/test/fakePatientServer.ts` +154 −0

## Browser tests and configuration (src/alveara-client/e2e, playwright config) (3)

- `A` `src/alveara-client/e2e/patient-workspace-real-backend.spec.ts` +471 −0
- `M` `src/alveara-client/playwright.config.ts` +1 −1
- `A` `src/alveara-client/playwright.workspace.config.ts` +24 −0

## Documentation (2)

- `A` `docs/PATIENT_WORKSPACE.md` +47 −0
- `M` `docs/testing/REAL_BACKEND_E2E.md` +20 −0

## Reading guide

- **Parent (STORY-003) product files extended, tests untouched:** `Patient.cs`, `PatientRegistrationService.cs`, `PatientsController.cs`, `Permission.cs`, `PermissionMatrix.cs`, `AlveraDbContext.cs`, `Program.cs`, `patientsApi.ts`, `PatientRegistrationPage.tsx/.css`, `App.tsx`, `moduleRegistry.ts`.
- **Shell:** `AppShell.tsx/.css` replace the patient-context placeholder with `PatientHeader` and host the patient-context and requirements providers; nav entries are added in `moduleRegistry.ts` (a new optional `activeWhen`).
- **Migration:** `AddPatientIdentityWorkspace` (+ designer) and the updated model snapshot.
- **Shared components of completed stories are NOT modified** (`Notification.css`, `ConcurrencyConflictBanner.css`, `ConfigurationHubPage`, practice-configuration services): see findings F1/F2 in `R01.md`.
- Generated/untracked output (`bin/`, `obj/`, `dist/`, `test-results/`, `TestResults/`) is not part of the commit.
