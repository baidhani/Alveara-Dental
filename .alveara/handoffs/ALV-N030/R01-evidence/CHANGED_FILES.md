# ALV-N030 R01: changed files

Range: `ba82963` (origin/main, the last pushed commit) to `44545eb` (the final implementation commit), limited to `src/`, `scripts/` and `docs/testing/`. Status: A added, M modified, D deleted.

The design and control documents of the briefing rounds (R05 to R07) and this handoff folder are not listed; their commits are in `R01.md`.

## Product: API (src/Alveara.Api) (32)

| Status | File |
|---|---|
| A | `src/Alveara.Api/Architecture/Identity/AccountBootstrapEntities.cs` |
| A | `src/Alveara.Api/Architecture/Identity/DeploymentSetupService.cs` |
| A | `src/Alveara.Api/Architecture/Identity/PasswordChangeGate.cs` |
| A | `src/Alveara.Api/Architecture/Identity/PracticeOwnerService.cs` |
| A | `src/Alveara.Api/Architecture/Identity/SetupCommand.cs` |
| A | `src/Alveara.Api/Architecture/Identity/StaffMemberService.cs` |
| A | `src/Alveara.Api/Architecture/Startup/StartupMode.cs` |
| A | `src/Alveara.Api/Architecture/Startup/StartupModeMiddleware.cs` |
| A | `src/Alveara.Api/Controllers/PracticeOwnerController.cs` |
| A | `src/Alveara.Api/Controllers/SetupController.cs` |
| A | `src/Alveara.Api/Controllers/StaffMembersController.cs` |
| M | `src/Alveara.Api/Architecture/Backup/DeploymentSettings.cs` |
| M | `src/Alveara.Api/Architecture/Clinical/ClinicalNames.cs` |
| M | `src/Alveara.Api/Architecture/Configuration/StaffProviderService.cs` |
| M | `src/Alveara.Api/Architecture/FollowUps/FollowUpService.cs` |
| M | `src/Alveara.Api/Architecture/Identity/AccountService.cs` |
| M | `src/Alveara.Api/Architecture/Identity/AuditLogEntry.cs` |
| M | `src/Alveara.Api/Architecture/Identity/IdentityEntities.cs` |
| M | `src/Alveara.Api/Architecture/Identity/PermissionMatrix.cs` |
| M | `src/Alveara.Api/Architecture/Notifications/MessageService.cs` |
| M | `src/Alveara.Api/Architecture/Patients/Portability/Package/ExportDomainCatalog.cs` |
| M | `src/Alveara.Api/Architecture/Prescribing/PrescriptionPdf.cs` |
| M | `src/Alveara.Api/Architecture/Recall/UnscheduledTreatmentJobs.cs` |
| M | `src/Alveara.Api/Architecture/Safety/ClearanceService.cs` |
| M | `src/Alveara.Api/Architecture/Scheduling/BookingTreatmentLinks.cs` |
| M | `src/Alveara.Api/Architecture/Treatment/TreatmentPlanCopyPdf.cs` |
| M | `src/Alveara.Api/Architecture/Treatment/TreatmentPlanService.Links.cs` |
| M | `src/Alveara.Api/Architecture/Treatment/TreatmentPlanService.States.cs` |
| M | `src/Alveara.Api/Controllers/AuthController.cs` |
| M | `src/Alveara.Api/Controllers/ConfigurationController.cs` |
| M | `src/Alveara.Api/Data/AlveraDbContext.cs` |
| M | `src/Alveara.Api/Program.cs` |

## Product: migration (3)

| Status | File |
|---|---|
| A | `src/Alveara.Api/Migrations/20261124070258_AddAccountKindSetupTokensAndPracticeOwner.Designer.cs` |
| A | `src/Alveara.Api/Migrations/20261124070258_AddAccountKindSetupTokensAndPracticeOwner.cs` |
| M | `src/Alveara.Api/Migrations/AlveraDbContextModelSnapshot.cs` |

## Product: client (24)

| Status | File |
|---|---|
| A | `src/alveara-client/src/app/StartupGate.tsx` |
| A | `src/alveara-client/src/pages/AccountBootstrap.test.tsx` |
| A | `src/alveara-client/src/pages/ChangePasswordPage.tsx` |
| A | `src/alveara-client/src/pages/SetupPage.tsx` |
| A | `src/alveara-client/src/pages/accountLabels.ts` |
| A | `src/alveara-client/src/pages/configuration/StaffMemberDialogs.tsx` |
| A | `src/alveara-client/src/pages/dashboard/PracticeOwnerNotice.css` |
| A | `src/alveara-client/src/pages/dashboard/PracticeOwnerNotice.tsx` |
| A | `src/alveara-client/src/services/accountsApi.ts` |
| M | `src/alveara-client/.gitignore` |
| M | `src/alveara-client/src/App.tsx` |
| M | `src/alveara-client/src/app/RouteGuards.tsx` |
| M | `src/alveara-client/src/components/ConfigEntityPanel.tsx` |
| M | `src/alveara-client/src/components/validateFields.ts` |
| M | `src/alveara-client/src/contexts/AuthContext.tsx` |
| M | `src/alveara-client/src/contexts/authContextObject.ts` |
| M | `src/alveara-client/src/pages/AdminUsersPage.css` |
| M | `src/alveara-client/src/pages/AdminUsersPage.tsx` |
| M | `src/alveara-client/src/pages/DashboardPage.tsx` |
| M | `src/alveara-client/src/pages/LoginPage.tsx` |
| M | `src/alveara-client/src/pages/configuration/PeopleTabs.tsx` |
| M | `src/alveara-client/src/services/authApi.ts` |
| M | `src/alveara-client/src/services/configApi.ts` |
| M | `src/alveara-client/vite.config.ts` |

## Test-only: Alveara.Api.TestSupport (5)

| Status | File |
|---|---|
| A | `src/Alveara.Api.TestSupport/Alveara.Api.TestSupport.csproj` |
| A | `src/Alveara.Api.TestSupport/TestAccountService.cs` |
| A | `src/Alveara.Api.TestSupport/TestAccountsController.cs` |
| A | `src/Alveara.Api.TestSupport/TestStaffProfilesController.cs` |
| A | `src/Alveara.Api.TestSupport/TestSupportRegistration.cs` |

## Test host: Alveara.Api.ChainHost (3)

| Status | File |
|---|---|
| M | `src/Alveara.Api.ChainHost/Alveara.Api.ChainHost.csproj` |
| M | `src/Alveara.Api.ChainHost/ChainHostFactory.cs` |
| M | `src/Alveara.Api.ChainHost/ChainHostMain.cs` |

## Scripts (5)

| Status | File |
|---|---|
| A | `scripts/RestartLoop.ps1` |
| M | `scripts/Invoke-AccumulatedChain.ps1` |
| M | `scripts/Invoke-RealBackendWalkthrough.ps1` |
| M | `scripts/New-DevAdmin.ps1` |
| M | `scripts/Start-DevApi.ps1` |

## Backend tests (60)

| Status | File |
|---|---|
| A | `src/Alveara.Api.Tests/AccountBootstrapMigrationTests.cs` |
| A | `src/Alveara.Api.Tests/DeploymentSetupTests.cs` |
| A | `src/Alveara.Api.Tests/PasswordChangeGateTests.cs` |
| A | `src/Alveara.Api.Tests/RestartLoopScriptTests.cs` |
| A | `src/Alveara.Api.Tests/RoleSubsetInvariantTests.cs` |
| A | `src/Alveara.Api.Tests/StaffMemberAndOwnerTests.cs` |
| A | `src/Alveara.Api.Tests/StartupModeTests.cs` |
| A | `src/Alveara.Api.Tests/TestAccounts.cs` |
| A | `src/Alveara.Api.Tests/TestStaff.cs` |
| A | `src/Alveara.Api.Tests/TestSupportGuardTests.cs` |
| D | `src/Alveara.Api.Tests/AccountServiceBootstrapTests.cs` |
| D | `src/Alveara.Api.Tests/AccountServiceRegistrationTests.cs` |
| M | `src/Alveara.Api.Tests/AccountSafetyTests.cs` |
| M | `src/Alveara.Api.Tests/AccountServiceLoginTests.cs` |
| M | `src/Alveara.Api.Tests/Alveara.Api.Tests.csproj` |
| M | `src/Alveara.Api.Tests/AppointmentLifecycleApiTests.cs` |
| M | `src/Alveara.Api.Tests/AppointmentsApiTests.cs` |
| M | `src/Alveara.Api.Tests/AuditSessionLifecycleTests.cs` |
| M | `src/Alveara.Api.Tests/AuthControllerMfaConfirmSessionTests.cs` |
| M | `src/Alveara.Api.Tests/AuthControllerPermissionMatrixTests.cs` |
| M | `src/Alveara.Api.Tests/AuthControllerRbacTests.cs` |
| M | `src/Alveara.Api.Tests/AuthControllerTests.cs` |
| M | `src/Alveara.Api.Tests/AuthCookieAttributesTests.cs` |
| M | `src/Alveara.Api.Tests/AuthRateLimitTests.cs` |
| M | `src/Alveara.Api.Tests/AvailabilityConcurrencyTests.cs` |
| M | `src/Alveara.Api.Tests/BackupApiTests.cs` |
| M | `src/Alveara.Api.Tests/BackupRecoveryHardeningTests.cs` |
| M | `src/Alveara.Api.Tests/BackupRestoreTests.cs` |
| M | `src/Alveara.Api.Tests/BackupTestSupport.cs` |
| M | `src/Alveara.Api.Tests/CalendarBlockedTimeApiTests.cs` |
| M | `src/Alveara.Api.Tests/ChainClockAdvanceTests.cs` |
| M | `src/Alveara.Api.Tests/ChainClockSeamTests.cs` |
| M | `src/Alveara.Api.Tests/CheckInDstDayRuleTests.cs` |
| M | `src/Alveara.Api.Tests/ClinicalRecordServiceTests.cs` |
| M | `src/Alveara.Api.Tests/CompletionTestBase.cs` |
| M | `src/Alveara.Api.Tests/ConfigurationApiTests.cs` |
| M | `src/Alveara.Api.Tests/DocumentBackupRestoreTests.cs` |
| M | `src/Alveara.Api.Tests/EncounterNotesServiceTests.cs` |
| M | `src/Alveara.Api.Tests/FormsApiTests.cs` |
| M | `src/Alveara.Api.Tests/IdentityTestHelpers.cs` |
| M | `src/Alveara.Api.Tests/LoginPresentationApiTests.cs` |
| M | `src/Alveara.Api.Tests/PatientFlowApiTests.cs` |
| M | `src/Alveara.Api.Tests/PatientIdentityApiTests.cs` |
| M | `src/Alveara.Api.Tests/PatientRequirementsTests.cs` |
| M | `src/Alveara.Api.Tests/PatientsApiTests.cs` |
| M | `src/Alveara.Api.Tests/PermissionPolicyTwoHostTests.cs` |
| M | `src/Alveara.Api.Tests/PracticeTimeZoneSessionTests.cs` |
| M | `src/Alveara.Api.Tests/PrescriptionWorkflowApiTests.cs` |
| M | `src/Alveara.Api.Tests/ProviderReassignmentTests.cs` |
| M | `src/Alveara.Api.Tests/RealMigrationInventory.cs` |
| M | `src/Alveara.Api.Tests/RealMigrationInventoryTests.cs` |
| M | `src/Alveara.Api.Tests/SafetyTestBase.cs` |
| M | `src/Alveara.Api.Tests/SchedulingApiHarness.cs` |
| M | `src/Alveara.Api.Tests/SchedulingConfigurationTests.cs` |
| M | `src/Alveara.Api.Tests/SchedulingTestSupport.cs` |
| M | `src/Alveara.Api.Tests/StaffInactivationAlertTests.cs` |
| M | `src/Alveara.Api.Tests/StaffInactivationServiceTests.cs` |
| M | `src/Alveara.Api.Tests/StaffProviderServiceTests.cs` |
| M | `src/Alveara.Api.Tests/TestDatabaseFixture.cs` |
| M | `src/Alveara.Api.Tests/VisitWorkflowPermissionTests.cs` |

## Browser specs, chain stages and configs (56)

| Status | File |
|---|---|
| A | `src/alveara-client/e2e/account-bootstrap-real-backend.spec.ts` |
| A | `src/alveara-client/e2e/server-states-real-backend.spec.ts` |
| A | `src/alveara-client/e2e/support/testAccounts.ts` |
| A | `src/alveara-client/playwright.account-bootstrap.config.ts` |
| A | `src/alveara-client/playwright.server-states.config.ts` |
| M | `src/alveara-client/chain/stage-0.spec.ts` |
| M | `src/alveara-client/chain/stage-1.spec.ts` |
| M | `src/alveara-client/chain/stage-3.spec.ts` |
| M | `src/alveara-client/e2e/a3-display-real-backend.spec.ts` |
| M | `src/alveara-client/e2e/auth-real-backend.spec.ts` |
| M | `src/alveara-client/e2e/billing-integrity-real-backend.spec.ts` |
| M | `src/alveara-client/e2e/billing-ledger-real-backend.spec.ts` |
| M | `src/alveara-client/e2e/billing-real-backend.spec.ts` |
| M | `src/alveara-client/e2e/billing-statements-real-backend.spec.ts` |
| M | `src/alveara-client/e2e/bulk-import-real-backend.spec.ts` |
| M | `src/alveara-client/e2e/calendar-real-backend.spec.ts` |
| M | `src/alveara-client/e2e/clinical-companion-real-backend.spec.ts` |
| M | `src/alveara-client/e2e/clinical-real-backend.spec.ts` |
| M | `src/alveara-client/e2e/completion-adoption-real-backend.spec.ts` |
| M | `src/alveara-client/e2e/completion-real-backend.spec.ts` |
| M | `src/alveara-client/e2e/diagnoses-real-backend.spec.ts` |
| M | `src/alveara-client/e2e/diagnoses-structure-real-backend.spec.ts` |
| M | `src/alveara-client/e2e/documents-library-real-backend.spec.ts` |
| M | `src/alveara-client/e2e/documents-real-backend.spec.ts` |
| M | `src/alveara-client/e2e/encounter-chart-real-backend.spec.ts` |
| M | `src/alveara-client/e2e/external-cases-real-backend.spec.ts` |
| M | `src/alveara-client/e2e/finalized-encounter-real-backend.spec.ts` |
| M | `src/alveara-client/e2e/follow-ups-real-backend.spec.ts` |
| M | `src/alveara-client/e2e/forms-real-backend.spec.ts` |
| M | `src/alveara-client/e2e/identity-audit-integrity-real-backend.spec.ts` |
| M | `src/alveara-client/e2e/notifications-real-backend.spec.ts` |
| M | `src/alveara-client/e2e/odontogram-longitudinal-real-backend.spec.ts` |
| M | `src/alveara-client/e2e/odontogram-real-backend.spec.ts` |
| M | `src/alveara-client/e2e/patient-export-package-real-backend.spec.ts` |
| M | `src/alveara-client/e2e/patient-flow-real-backend.spec.ts` |
| M | `src/alveara-client/e2e/patient-merge-real-backend.spec.ts` |
| M | `src/alveara-client/e2e/patient-portability-real-backend.spec.ts` |
| M | `src/alveara-client/e2e/patient-registration-real-backend.spec.ts` |
| M | `src/alveara-client/e2e/patient-workspace-real-backend.spec.ts` |
| M | `src/alveara-client/e2e/perio-real-backend.spec.ts` |
| M | `src/alveara-client/e2e/perio-sessions-real-backend.spec.ts` |
| M | `src/alveara-client/e2e/permission-policy-admin-real-backend.spec.ts` |
| M | `src/alveara-client/e2e/permission-policy-real-backend.spec.ts` |
| M | `src/alveara-client/e2e/practice-time-zones-real-backend.spec.ts` |
| M | `src/alveara-client/e2e/prescription-workflow-real-backend.spec.ts` |
| M | `src/alveara-client/e2e/prescriptions-real-backend.spec.ts` |
| M | `src/alveara-client/e2e/procedures-real-backend.spec.ts` |
| M | `src/alveara-client/e2e/reports-real-backend.spec.ts` |
| M | `src/alveara-client/e2e/safety-real-backend.spec.ts` |
| M | `src/alveara-client/e2e/schedule-real-backend.spec.ts` |
| M | `src/alveara-client/e2e/treatment-plan-integrity-real-backend.spec.ts` |
| M | `src/alveara-client/e2e/treatment-plans-complete-real-backend.spec.ts` |
| M | `src/alveara-client/e2e/treatment-plans-real-backend.spec.ts` |
| M | `src/alveara-client/e2e/visit-board-real-backend.spec.ts` |
| M | `src/alveara-client/e2e/work-queue-real-backend.spec.ts` |
| M | `src/alveara-client/playwright.auth.config.ts` |

## Other (2)

| Status | File |
|---|---|
| M | `docs/testing/REAL_BACKEND_E2E.md` |
| M | `src/Alveara.slnx` |
