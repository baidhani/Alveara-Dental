# ALV-N022 A3-R01: changed files

Base `8ea6ad5` (Addendum 02 review stored) .. implementation `63f74ff` (the sweep ran on `ecfb7f4`; `63f74ff` adds one test file and a PROGRESS entry).

```
63f74ff ALV-N022 A3: client tests for the diagnosis card's linked perio charts (Q-5aa) and the perio diagnosis picker (F-098)
ecfb7f4 ALV-N022 A3: findings remediation slice A3 (typed clearance-document and perio-diagnosis links, prescriptions tab, wording guard, odontogram absent mark and display corrections)
4135799 wip: ALV-N022 A3 slice 4 - sex display (F-067), encounter count label (F-082), lab tooth check (F-137), tooth pre-fill (Q-5ai), practice-level templates (Q-5j), CR-2 browser spec
5815928 wip: ALV-N022 A3 slice 3 - odontogram absent mark (F-091), F-093/F-094/F-095, perio labels (F-097), numbering (F-099), document and diagnosis pickers (D-1 B), Q-5aa
c745368 wip: ALV-N022 A3 slice 2 - prescriptions tab (F-106), allergy wording guard (F-105), gap grammar (F-199), Q-5aj sentence
e23509c wip: ALV-N022 A3 slice 1 - typed clearance-document and perio-diagnosis links (D-1 B), Q-5aa

 PROGRESS.md                                                                                           |    36 +
 src/Alveara.Api.Tests/ClearanceServiceTests.cs                                                        |   131 +-
 src/Alveara.Api.Tests/PerioSessionApiTests.cs                                                         |     8 +-
 src/Alveara.Api.Tests/PerioSessionServiceTests.cs                                                     |    71 +-
 src/Alveara.Api.Tests/PrescriptionWorkflowServiceTests.cs                                             |    17 +
 src/Alveara.Api.Tests/RealMigrationInventory.cs                                                       |     1 +
 src/Alveara.Api.Tests/RealMigrationInventoryTests.cs                                                  |     2 +-
 src/Alveara.Api.Tests/SafetyApiTests.cs                                                               |    27 +-
 src/Alveara.Api.Tests/SafetyContextServiceTests.cs                                                    |    10 +
 src/Alveara.Api.Tests/TypedLinkGuardMigrationTests.cs                                                 |   135 ++
 src/Alveara.Api/Architecture/Clinical/DiagnosisService.cs                                             |     9 +-
 src/Alveara.Api/Architecture/Clinical/DiagnosisViews.cs                                               |     6 +-
 src/Alveara.Api/Architecture/Periodontal/PerioReads.cs                                                |     5 +-
 src/Alveara.Api/Architecture/Periodontal/PerioSessionEntities.cs                                      |     6 +
 src/Alveara.Api/Architecture/Periodontal/PerioSessionService.Close.cs                                 |    36 +-
 src/Alveara.Api/Architecture/Periodontal/PerioViews.cs                                                |     3 +-
 src/Alveara.Api/Architecture/Prescribing/PrescriptionService.cs                                       |     8 +-
 src/Alveara.Api/Architecture/Prescribing/PrescriptionViews.cs                                         |     5 +-
 src/Alveara.Api/Architecture/Safety/ClearanceService.cs                                               |    55 +-
 src/Alveara.Api/Architecture/Safety/SafetyContextService.cs                                           |    12 +-
 src/Alveara.Api/Architecture/Safety/SafetyEntities.cs                                                 |     6 +-
 src/Alveara.Api/Architecture/Safety/SafetyRules.cs                                                    |    14 +
 src/Alveara.Api/Architecture/Safety/SafetyViews.cs                                                    |     6 +-
 src/Alveara.Api/Controllers/PerioSessionController.cs                                                 |     5 +-
 src/Alveara.Api/Controllers/SafetyController.cs                                                       |     9 +-
 src/Alveara.Api/Data/PerioSessionModel.cs                                                             |     4 +
 src/Alveara.Api/Data/SafetyModel.cs                                                                   |     5 +
 src/Alveara.Api/Migrations/20261117180509_AddTypedClearanceDocumentAndPerioDiagnosisLinks.Designer.cs | 10578 ++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
 src/Alveara.Api/Migrations/20261117180509_AddTypedClearanceDocumentAndPerioDiagnosisLinks.cs          |   146 ++
 src/Alveara.Api/Migrations/AlveraDbContextModelSnapshot.cs                                            |    36 +
 src/alveara-client/e2e/a3-display-real-backend.spec.ts                                                |   178 ++
 src/alveara-client/e2e/diagnoses-structure-real-backend.spec.ts                                       |     2 +-
 src/alveara-client/e2e/external-cases-real-backend.spec.ts                                            |     2 +-
 src/alveara-client/e2e/perio-sessions-real-backend.spec.ts                                            |    20 +-
 src/alveara-client/e2e/safety-real-backend.spec.ts                                                    |    29 +-
 src/alveara-client/playwright.a3-display.config.ts                                                    |    24 +
 src/alveara-client/playwright.config.ts                                                               |     2 +-
 src/alveara-client/src/A3Slice4.test.tsx                                                              |   116 +
 src/alveara-client/src/App.tsx                                                                        |     6 +-
 src/alveara-client/src/Clinical.test.tsx                                                              |     2 +-
 src/alveara-client/src/DiagnosisStructure.test.tsx                                                    |     2 +-
 src/alveara-client/src/OdontogramA3.test.tsx                                                          |    96 +
 src/alveara-client/src/PerioCompare.test.tsx                                                          |     4 +-
 src/alveara-client/src/PerioSessionApi.test.ts                                                        |     6 +-
 src/alveara-client/src/Safety.test.tsx                                                                |    21 +-
 src/alveara-client/src/app/PatientHeader.tsx                                                          |     3 +-
 src/alveara-client/src/components/patientFields.ts                                                    |     8 +
 src/alveara-client/src/pages/A3Display.test.tsx                                                       |    83 +
 src/alveara-client/src/pages/PatientWorkspacePage.tsx                                                 |     6 -
 src/alveara-client/src/pages/clinical/PatientClinicalPanel.tsx                                        |     5 +-
 src/alveara-client/src/pages/clinical/notes/PracticeTemplatesPage.tsx                                 |    15 +
 src/alveara-client/src/pages/clinical/notes/TemplatesPage.tsx                                         |     5 +-
 src/alveara-client/src/pages/clinical/notes/templatesPath.ts                                          |    14 +
 src/alveara-client/src/pages/diagnosis/DiagnosisHistory.tsx                                           |     6 +-
 src/alveara-client/src/pages/diagnosis/DiagnosisItem.tsx                                              |     5 +-
 src/alveara-client/src/pages/externalcases/ExternalCaseScreens.test.tsx                               |     2 +-
 src/alveara-client/src/pages/externalcases/caseFormValues.ts                                          |     3 +-
 src/alveara-client/src/pages/odontogram/Odontogram.css                                                |     4 +
 src/alveara-client/src/pages/odontogram/OdontogramPanel.tsx                                           |    22 +-
 src/alveara-client/src/pages/odontogram/ToothChart.tsx                                                |    18 +-
 src/alveara-client/src/pages/odontogram/odontogramText.ts                                             |     7 +
 src/alveara-client/src/pages/perio/Perio.css                                                          |     3 +
 src/alveara-client/src/pages/perio/PerioChartExtras.tsx                                               |    38 +-
 src/alveara-client/src/pages/perio/PerioGrid.tsx                                                      |     1 +
 src/alveara-client/src/pages/prescription/PrescriptionList.test.tsx                                   |    37 +
 src/alveara-client/src/pages/prescription/PrescriptionList.tsx                                        |    18 +-
 src/alveara-client/src/pages/safety/ClearanceCard.tsx                                                 |    17 +-
 src/alveara-client/src/pages/safety/DocumentPicker.tsx                                                |    37 +
 src/alveara-client/src/pages/safety/SafetyPanel.tsx                                                   |     2 +-
 src/alveara-client/src/pages/treatmentplan/ClinicalHistorySection.tsx                                 |     3 +-
 src/alveara-client/src/pages/treatmentplan/CompletionResultPanel.test.tsx                             |    28 +
 src/alveara-client/src/pages/treatmentplan/CompletionResultPanel.tsx                                  |     3 +-
 src/alveara-client/src/pages/treatmentplan/TreatmentPlanClinicalHistory.test.tsx                      |     2 +-
 src/alveara-client/src/pages/treatmentplan/TreatmentPlanItemFields.tsx                                |     5 +-
 src/alveara-client/src/services/diagnosisApi.ts                                                       |     2 +
 src/alveara-client/src/services/perioApi.ts                                                           |     6 +-
 src/alveara-client/src/services/prescriptionApi.ts                                                    |    10 +
 src/alveara-client/src/services/safetyApi.ts                                                          |    15 +-
 src/alveara-client/src/test/fakeClinicalServer.ts                                                     |     3 +-
 src/alveara-client/src/test/fakePerioStore.ts                                                         |    11 +-
 src/alveara-client/src/test/fakeSafetyStore.ts                                                        |    22 +-
 tests/allergy-wording-guard.test.mjs                                                                  |    45 +
 82 files changed, 12249 insertions(+), 167 deletions(-)
```

**Server production** (17):
- `src/Alveara.Api/Architecture/Clinical/DiagnosisService.cs`
- `src/Alveara.Api/Architecture/Clinical/DiagnosisViews.cs`
- `src/Alveara.Api/Architecture/Periodontal/PerioReads.cs`
- `src/Alveara.Api/Architecture/Periodontal/PerioSessionEntities.cs`
- `src/Alveara.Api/Architecture/Periodontal/PerioSessionService.Close.cs`
- `src/Alveara.Api/Architecture/Periodontal/PerioViews.cs`
- `src/Alveara.Api/Architecture/Prescribing/PrescriptionService.cs`
- `src/Alveara.Api/Architecture/Prescribing/PrescriptionViews.cs`
- `src/Alveara.Api/Architecture/Safety/ClearanceService.cs`
- `src/Alveara.Api/Architecture/Safety/SafetyContextService.cs`
- `src/Alveara.Api/Architecture/Safety/SafetyEntities.cs`
- `src/Alveara.Api/Architecture/Safety/SafetyRules.cs`
- `src/Alveara.Api/Architecture/Safety/SafetyViews.cs`
- `src/Alveara.Api/Controllers/PerioSessionController.cs`
- `src/Alveara.Api/Controllers/SafetyController.cs`
- `src/Alveara.Api/Data/PerioSessionModel.cs`
- `src/Alveara.Api/Data/SafetyModel.cs`

**Migration** (3):
- `src/Alveara.Api/Migrations/20261117180509_AddTypedClearanceDocumentAndPerioDiagnosisLinks.Designer.cs`
- `src/Alveara.Api/Migrations/20261117180509_AddTypedClearanceDocumentAndPerioDiagnosisLinks.cs`
- `src/Alveara.Api/Migrations/AlveraDbContextModelSnapshot.cs`

**Server tests** (9):
- `src/Alveara.Api.Tests/ClearanceServiceTests.cs`
- `src/Alveara.Api.Tests/PerioSessionApiTests.cs`
- `src/Alveara.Api.Tests/PerioSessionServiceTests.cs`
- `src/Alveara.Api.Tests/PrescriptionWorkflowServiceTests.cs`
- `src/Alveara.Api.Tests/RealMigrationInventory.cs`
- `src/Alveara.Api.Tests/RealMigrationInventoryTests.cs`
- `src/Alveara.Api.Tests/SafetyApiTests.cs`
- `src/Alveara.Api.Tests/SafetyContextServiceTests.cs`
- `src/Alveara.Api.Tests/TypedLinkGuardMigrationTests.cs`

**Client production** (29):
- `src/alveara-client/src/App.tsx`
- `src/alveara-client/src/app/PatientHeader.tsx`
- `src/alveara-client/src/components/patientFields.ts`
- `src/alveara-client/src/pages/PatientWorkspacePage.tsx`
- `src/alveara-client/src/pages/clinical/PatientClinicalPanel.tsx`
- `src/alveara-client/src/pages/clinical/notes/PracticeTemplatesPage.tsx`
- `src/alveara-client/src/pages/clinical/notes/TemplatesPage.tsx`
- `src/alveara-client/src/pages/clinical/notes/templatesPath.ts`
- `src/alveara-client/src/pages/diagnosis/DiagnosisHistory.tsx`
- `src/alveara-client/src/pages/diagnosis/DiagnosisItem.tsx`
- `src/alveara-client/src/pages/externalcases/caseFormValues.ts`
- `src/alveara-client/src/pages/odontogram/Odontogram.css`
- `src/alveara-client/src/pages/odontogram/OdontogramPanel.tsx`
- `src/alveara-client/src/pages/odontogram/ToothChart.tsx`
- `src/alveara-client/src/pages/odontogram/odontogramText.ts`
- `src/alveara-client/src/pages/perio/Perio.css`
- `src/alveara-client/src/pages/perio/PerioChartExtras.tsx`
- `src/alveara-client/src/pages/perio/PerioGrid.tsx`
- `src/alveara-client/src/pages/prescription/PrescriptionList.tsx`
- `src/alveara-client/src/pages/safety/ClearanceCard.tsx`
- `src/alveara-client/src/pages/safety/DocumentPicker.tsx`
- `src/alveara-client/src/pages/safety/SafetyPanel.tsx`
- `src/alveara-client/src/pages/treatmentplan/ClinicalHistorySection.tsx`
- `src/alveara-client/src/pages/treatmentplan/CompletionResultPanel.tsx`
- `src/alveara-client/src/pages/treatmentplan/TreatmentPlanItemFields.tsx`
- `src/alveara-client/src/services/diagnosisApi.ts`
- `src/alveara-client/src/services/perioApi.ts`
- `src/alveara-client/src/services/prescriptionApi.ts`
- `src/alveara-client/src/services/safetyApi.ts`

**Client tests and fakes** (15):
- `src/alveara-client/src/A3Slice4.test.tsx`
- `src/alveara-client/src/Clinical.test.tsx`
- `src/alveara-client/src/DiagnosisStructure.test.tsx`
- `src/alveara-client/src/OdontogramA3.test.tsx`
- `src/alveara-client/src/PerioCompare.test.tsx`
- `src/alveara-client/src/PerioSessionApi.test.ts`
- `src/alveara-client/src/Safety.test.tsx`
- `src/alveara-client/src/pages/A3Display.test.tsx`
- `src/alveara-client/src/pages/externalcases/ExternalCaseScreens.test.tsx`
- `src/alveara-client/src/pages/prescription/PrescriptionList.test.tsx`
- `src/alveara-client/src/pages/treatmentplan/CompletionResultPanel.test.tsx`
- `src/alveara-client/src/pages/treatmentplan/TreatmentPlanClinicalHistory.test.tsx`
- `src/alveara-client/src/test/fakeClinicalServer.ts`
- `src/alveara-client/src/test/fakePerioStore.ts`
- `src/alveara-client/src/test/fakeSafetyStore.ts`

**Browser specs and configs** (7):
- `src/alveara-client/e2e/a3-display-real-backend.spec.ts`
- `src/alveara-client/e2e/diagnoses-structure-real-backend.spec.ts`
- `src/alveara-client/e2e/external-cases-real-backend.spec.ts`
- `src/alveara-client/e2e/perio-sessions-real-backend.spec.ts`
- `src/alveara-client/e2e/safety-real-backend.spec.ts`
- `src/alveara-client/playwright.a3-display.config.ts`
- `src/alveara-client/playwright.config.ts`

**Repo checks** (1):
- `tests/allergy-wording-guard.test.mjs`

**Records** (1):
- `PROGRESS.md`

The migration Designer file is generated by EF (about 10,600 of the added lines). One migration: three nullable columns, two foreign keys, one CHECK, two triggers; Down refused while typed ids exist. Rollback-class inventory 29 -> 30.
