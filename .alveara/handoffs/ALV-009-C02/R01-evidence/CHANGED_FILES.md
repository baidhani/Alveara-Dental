# ALV-009-C02 R01 - Changed files

Implementation range `2f98cb6` (ALV-009-C01 closure) to `36f817e` (implementation tip): 9 commits. Evidence and status files under `.alveara/` are added by the two commits after it.

## Backend (src/Alveara.Api)
- A `src/Alveara.Api/Architecture/ExternalCases/ExternalCaseEntities.cs`
- A `src/Alveara.Api/Architecture/ExternalCases/ExternalCaseRules.cs`
- A `src/Alveara.Api/Architecture/ExternalCases/ExternalCaseService.Documents.cs`
- A `src/Alveara.Api/Architecture/ExternalCases/ExternalCaseService.Reads.cs`
- A `src/Alveara.Api/Architecture/ExternalCases/ExternalCaseService.Status.cs`
- A `src/Alveara.Api/Architecture/ExternalCases/ExternalCaseService.cs`
- A `src/Alveara.Api/Architecture/ExternalCases/ExternalCaseTaskJobs.cs`
- A `src/Alveara.Api/Architecture/ExternalCases/ExternalCaseValidation.cs`
- A `src/Alveara.Api/Architecture/ExternalCases/ExternalCaseViews.cs`
- A `src/Alveara.Api/Architecture/ExternalCases/ExternalPartyService.cs`
- M `src/Alveara.Api/Architecture/FollowUps/FollowUpEntities.cs`
- M `src/Alveara.Api/Architecture/FollowUps/FollowUpService.cs`
- M `src/Alveara.Api/Architecture/FollowUps/FollowUpViews.cs`
- M `src/Alveara.Api/Architecture/FollowUps/SystemTaskWriter.cs`
- M `src/Alveara.Api/Architecture/Identity/AuditLogEntry.cs`
- M `src/Alveara.Api/Architecture/Identity/Permission.cs`
- M `src/Alveara.Api/Architecture/Identity/PermissionMatrix.cs`
- M `src/Alveara.Api/Architecture/Identity/PermissionRegistry.json`
- M `src/Alveara.Api/Architecture/Recall/FollowUpWorkHostedService.cs`
- A `src/Alveara.Api/Controllers/ExternalCasesController.cs`
- M `src/Alveara.Api/Data/AlveraDbContext.cs`
- A `src/Alveara.Api/Data/ExternalCaseModel.cs`
- M `src/Alveara.Api/Data/FollowUpModel.cs`
- A `src/Alveara.Api/Migrations/20261024223402_AddExternalCases.Designer.cs`
- A `src/Alveara.Api/Migrations/20261024223402_AddExternalCases.cs`
- M `src/Alveara.Api/Migrations/AlveraDbContextModelSnapshot.cs`
- M `src/Alveara.Api/Program.cs`

## Backend tests (src/Alveara.Api.Tests)
- A `src/Alveara.Api.Tests/ExternalCaseApiTests.cs`
- A `src/Alveara.Api.Tests/ExternalCaseBackupRestoreTests.cs`
- A `src/Alveara.Api.Tests/ExternalCaseControllerPermissionTests.cs`
- A `src/Alveara.Api.Tests/ExternalCaseRulesTests.cs`
- A `src/Alveara.Api.Tests/ExternalCaseSchemaTests.cs`
- A `src/Alveara.Api.Tests/ExternalCaseServiceTests.cs`
- A `src/Alveara.Api.Tests/ExternalCaseTaskJobTests.cs`
- M `src/Alveara.Api.Tests/RealMigrationInventory.cs`
- M `src/Alveara.Api.Tests/RealMigrationInventoryTests.cs`

## Client (src/alveara-client)
- A `src/alveara-client/e2e/external-cases-real-backend.spec.ts`
- M `src/alveara-client/playwright.config.ts`
- A `src/alveara-client/playwright.external-cases.config.ts`
- M `src/alveara-client/src/App.patientWorkspaceRoutes.test.tsx`
- M `src/alveara-client/src/App.tsx`
- M `src/alveara-client/src/app/moduleRegistry.ts`
- M `src/alveara-client/src/app/patientWorkspaceTabs.ts`
- M `src/alveara-client/src/pages/PatientWorkspacePage.tsx`
- A `src/alveara-client/src/pages/externalcases/CaseDocumentsSection.tsx`
- A `src/alveara-client/src/pages/externalcases/CaseEditForm.tsx`
- A `src/alveara-client/src/pages/externalcases/CaseFields.tsx`
- A `src/alveara-client/src/pages/externalcases/CaseStatusForm.tsx`
- A `src/alveara-client/src/pages/externalcases/ExternalCaseDetailPage.tsx`
- A `src/alveara-client/src/pages/externalcases/ExternalCaseScreens.test.tsx`
- A `src/alveara-client/src/pages/externalcases/ExternalCasesPage.tsx`
- A `src/alveara-client/src/pages/externalcases/ExternalPartiesPage.tsx`
- A `src/alveara-client/src/pages/externalcases/NewCaseForm.tsx`
- A `src/alveara-client/src/pages/externalcases/PatientExternalCasesPanel.tsx`
- A `src/alveara-client/src/pages/externalcases/caseFormValues.ts`
- A `src/alveara-client/src/pages/externalcases/externalCaseFormat.ts`
- M `src/alveara-client/src/pages/followups/WorkQueuePage.tsx`
- M `src/alveara-client/src/pages/followups/followups.css`
- A `src/alveara-client/src/services/externalCasesApi.ts`
- M `src/alveara-client/src/services/followUpsApi.ts`
- M `src/alveara-client/src/services/workQueueApi.ts`

## Docs, scripts and progress
- M `PROGRESS.md`
- M `docs/RECALL_AND_WORK_QUEUE.md`
- A `docs/REFERRALS_AND_LAB_CASES.md`

## Summary
```
64 files changed, 15557 insertions(+), 25 deletions(-)
```

## Commits
```
36f817e ALV-009-C02: the restore proof and the referral and lab behaviour doc
7ef0ea4 ALV-009-C02: a test for the second permission of the case document actions
f3c4112 ALV-009-C02: the un-mocked referral and lab walkthrough, and a dark-theme link contrast fix
1c2e539 ALV-009-C02: the referral and lab case screens
1f568b6 ALV-009-C02: the API for parties, cases, lists, timelines and linked documents
ff3d5ed ALV-009-C02: referral and lab chase tasks as durable background jobs
98d7864 ALV-009-C02: the party and case services
88ff2d9 ALV-009-C02: the referral and lab schema
3a1bb2c ALV-009-C02: the referral and lab permissions and the pure case rules
```
