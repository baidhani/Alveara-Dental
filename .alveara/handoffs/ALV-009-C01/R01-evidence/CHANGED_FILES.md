# ALV-009-C01 R01 - Changed files

Implementation range `8ccf9e2` (STORY-009 completion) to `4b12cc1` (implementation tip): 12 commits. Evidence and status files under `.alveara/` are added by the two commits after it.

## Backend (src/Alveara.Api)
- M `src/Alveara.Api/Architecture/BackgroundWork/BackgroundJobRunner.cs`
- M `src/Alveara.Api/Architecture/FollowUps/FollowUpEntities.cs`
- M `src/Alveara.Api/Architecture/FollowUps/FollowUpReminderChannels.cs`
- M `src/Alveara.Api/Architecture/FollowUps/FollowUpReminderHostedService.cs`
- A `src/Alveara.Api/Architecture/FollowUps/FollowUpReminderJobHandler.cs`
- M `src/Alveara.Api/Architecture/FollowUps/FollowUpReminderService.cs`
- A `src/Alveara.Api/Architecture/FollowUps/FollowUpService.WorkQueue.cs`
- M `src/Alveara.Api/Architecture/FollowUps/FollowUpService.cs`
- M `src/Alveara.Api/Architecture/FollowUps/FollowUpViews.cs`
- A `src/Alveara.Api/Architecture/FollowUps/SystemTaskWriter.cs`
- A `src/Alveara.Api/Architecture/FollowUps/WorkQueueViews.cs`
- M `src/Alveara.Api/Architecture/Identity/AuditLogEntry.cs`
- M `src/Alveara.Api/Architecture/Identity/Permission.cs`
- M `src/Alveara.Api/Architecture/Identity/PermissionMatrix.cs`
- M `src/Alveara.Api/Architecture/Identity/PermissionRegistry.json`
- A `src/Alveara.Api/Architecture/Recall/FollowUpWorkHostedService.cs`
- A `src/Alveara.Api/Architecture/Recall/RecallAdminService.cs`
- A `src/Alveara.Api/Architecture/Recall/RecallEntities.cs`
- A `src/Alveara.Api/Architecture/Recall/RecallJobs.cs`
- A `src/Alveara.Api/Architecture/Recall/RecallRules.cs`
- A `src/Alveara.Api/Architecture/Recall/UnscheduledTreatmentJobs.cs`
- A `src/Alveara.Api/Controllers/WorkQueueController.cs`
- M `src/Alveara.Api/Data/AlveraDbContext.cs`
- M `src/Alveara.Api/Data/FollowUpModel.cs`
- A `src/Alveara.Api/Data/RecallModel.cs`
- A `src/Alveara.Api/Migrations/20261024060516_AddWorkQueue.Designer.cs`
- A `src/Alveara.Api/Migrations/20261024060516_AddWorkQueue.cs`
- M `src/Alveara.Api/Migrations/AlveraDbContextModelSnapshot.cs`
- M `src/Alveara.Api/Program.cs`

## Backend tests (src/Alveara.Api.Tests)
- A `src/Alveara.Api.Tests/FollowUpReminderJobTests.cs`
- M `src/Alveara.Api.Tests/RealMigrationInventory.cs`
- M `src/Alveara.Api.Tests/RealMigrationInventoryTests.cs`
- A `src/Alveara.Api.Tests/RecallCalculatorTests.cs`
- A `src/Alveara.Api.Tests/RecallJobTests.cs`
- M `src/Alveara.Api.Tests/SchedulingApiHarness.cs`
- A `src/Alveara.Api.Tests/UnscheduledTreatmentJobTests.cs`
- A `src/Alveara.Api.Tests/WorkQueueApiTests.cs`
- A `src/Alveara.Api.Tests/WorkQueueBackupRestoreTests.cs`
- A `src/Alveara.Api.Tests/WorkQueueSchemaTests.cs`

## Client (src/alveara-client)
- M `src/alveara-client/e2e/follow-ups-real-backend.spec.ts`
- A `src/alveara-client/e2e/work-queue-real-backend.spec.ts`
- M `src/alveara-client/playwright.config.ts`
- A `src/alveara-client/playwright.work-queue.config.ts`
- M `src/alveara-client/src/App.tsx`
- M `src/alveara-client/src/app/moduleRegistry.ts`
- M `src/alveara-client/src/pages/followups/FollowUpActionForm.tsx`
- M `src/alveara-client/src/pages/followups/FollowUpHistory.tsx`
- M `src/alveara-client/src/pages/followups/FollowUpItem.tsx`
- M `src/alveara-client/src/pages/followups/FollowUpScreens.test.tsx`
- M `src/alveara-client/src/pages/followups/FollowUpsPanel.tsx`
- A `src/alveara-client/src/pages/followups/PatientFollowUpTimeline.tsx`
- A `src/alveara-client/src/pages/followups/PatientRecallCard.tsx`
- A `src/alveara-client/src/pages/followups/QueueRowEditor.tsx`
- A `src/alveara-client/src/pages/followups/RecallSettingsPage.tsx`
- A `src/alveara-client/src/pages/followups/WorkQueuePage.tsx`
- A `src/alveara-client/src/pages/followups/WorkQueueScreens.test.tsx`
- M `src/alveara-client/src/pages/followups/followUpFormat.ts`
- M `src/alveara-client/src/pages/followups/followups.css`
- M `src/alveara-client/src/services/followUpsApi.ts`
- A `src/alveara-client/src/services/workQueueApi.ts`

## Docs, scripts and progress
- M `PROGRESS.md`
- M `docs/FOLLOW_UPS.md`
- A `docs/RECALL_AND_WORK_QUEUE.md`
- M `scripts/Invoke-RealBackendWalkthrough.ps1`

## Summary
```
 64 files changed, 14025 insertions(+), 136 deletions(-)
```
