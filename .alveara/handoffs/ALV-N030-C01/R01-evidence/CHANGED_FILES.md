# ALV-N030-C01 R01: changed files

Diff `ecf6406..1d952d9` (slices 1 to 6), code and tests only. The handoff, brief and control files are listed in the evidence commit.

| Status | File |
|---|---|
| added | `scripts/New-DevRecoveryToken.ps1` |
| modified | `src/Alveara.Api.Tests/CheckoutTests.cs` |
| added | `src/Alveara.Api.Tests/DeploymentRecoveryTests.cs` |
| added | `src/Alveara.Api.Tests/OwnerInvariantAuditTests.cs` |
| added | `src/Alveara.Api.Tests/PracticeOwnerCreationTests.cs` |
| added | `src/Alveara.Api.Tests/PracticeOwnerInvariantTests.cs` |
| added | `src/Alveara.Api.Tests/PracticeOwnerTransferTests.cs` |
| modified | `src/Alveara.Api.Tests/StaffMemberAndOwnerTests.cs` |
| modified | `src/Alveara.Api/Architecture/Auditing/AuditLogReader.cs` |
| modified | `src/Alveara.Api/Architecture/Backup/DeploymentSettings.cs` |
| modified | `src/Alveara.Api/Architecture/Configuration/StaffInactivationService.cs` |
| modified | `src/Alveara.Api/Architecture/Configuration/StaffProviderService.cs` |
| modified | `src/Alveara.Api/Architecture/Identity/AccountBootstrapEntities.cs` |
| modified | `src/Alveara.Api/Architecture/Identity/AccountService.cs` |
| modified | `src/Alveara.Api/Architecture/Identity/AuditLogEntry.cs` |
| added | `src/Alveara.Api/Architecture/Identity/DeploymentRecoveryService.cs` |
| modified | `src/Alveara.Api/Architecture/Identity/DeploymentSetupService.cs` |
| added | `src/Alveara.Api/Architecture/Identity/OwnerInvariantAudit.cs` |
| added | `src/Alveara.Api/Architecture/Identity/PracticeOwnerGuard.cs` |
| modified | `src/Alveara.Api/Architecture/Identity/PracticeOwnerService.cs` |
| added | `src/Alveara.Api/Architecture/Identity/PracticeOwnerTransferService.cs` |
| added | `src/Alveara.Api/Architecture/Identity/RecoveryCommand.cs` |
| modified | `src/Alveara.Api/Architecture/Identity/StaffMemberService.cs` |
| modified | `src/Alveara.Api/Architecture/Scheduling/Checkout/CheckoutRecorder.cs` |
| modified | `src/Alveara.Api/Controllers/AuthController.cs` |
| modified | `src/Alveara.Api/Controllers/PracticeOwnerController.cs` |
| added | `src/Alveara.Api/Controllers/RecoveryController.cs` |
| modified | `src/Alveara.Api/Controllers/StaffMembersController.cs` |
| added | `src/Alveara.Api/Migrations/20261125183354_AddSetupTokenPurpose.Designer.cs` |
| added | `src/Alveara.Api/Migrations/20261125183354_AddSetupTokenPurpose.cs` |
| added | `src/Alveara.Api/Migrations/20261125184749_RecordPracticeOwnerInvariantViolations.Designer.cs` |
| added | `src/Alveara.Api/Migrations/20261125184749_RecordPracticeOwnerInvariantViolations.cs` |
| modified | `src/Alveara.Api/Migrations/AlveraDbContextModelSnapshot.cs` |
| modified | `src/Alveara.Api/Program.cs` |
| modified | `src/alveara-client/e2e/account-bootstrap-real-backend.spec.ts` |
| added | `src/alveara-client/e2e/owner-transfer-recovery-real-backend.spec.ts` |
| modified | `src/alveara-client/playwright.config.ts` |
| added | `src/alveara-client/playwright.owner-transfer.config.ts` |
| modified | `src/alveara-client/src/App.tsx` |
| modified | `src/alveara-client/src/components/ConfigEntityPanel.tsx` |
| modified | `src/alveara-client/src/components/validateFields.ts` |
| modified | `src/alveara-client/src/contexts/AuthContext.tsx` |
| modified | `src/alveara-client/src/contexts/authContextObject.ts` |
| modified | `src/alveara-client/src/pages/AccountBootstrap.test.tsx` |
| modified | `src/alveara-client/src/pages/AdminUserDetailPage.tsx` |
| modified | `src/alveara-client/src/pages/AdminUsersPage.tsx` |
| added | `src/alveara-client/src/pages/OwnerAdministration.test.tsx` |
| added | `src/alveara-client/src/pages/RecoveryPage.tsx` |
| added | `src/alveara-client/src/pages/TransferPracticeOwnerDialog.tsx` |
| modified | `src/alveara-client/src/pages/configuration/PeopleTabs.tsx` |
| modified | `src/alveara-client/src/pages/dashboard/PracticeOwnerNotice.tsx` |
| modified | `src/alveara-client/src/services/accountsApi.ts` |
| modified | `src/alveara-client/src/services/authApi.ts` |

Totals: 53 files changed, 25594 insertions(+), 95 deletions(-)

The large insertion count is mostly the two generated migration `.Designer.cs` snapshots. The hand-written change is the services, controllers, tests and client files above.
