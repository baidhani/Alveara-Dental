# ALV-N021 A2-R01: changed files (git diff --name-status 9d6e533..2ef0817)

Base `9d6e533` (decision 02 stored, document only) to implementation `2ef0817`: 31 commits, 195 files changed, 73690 insertions(+), 1485 deletions(-). About 63,000 of the added lines are the six EF migration Designer snapshots (M1-M6), each a generated copy of the model.

Three commits are not A2 work and say so ("Pre-A2 leftover": `a2cc611`, `265331c`, `24417e1`); `0006779` is the mocked-Playwright configuration change the final evidence run needed. `VisitAssignmentTests.cs` was deleted in `df82656`: it tested the retired "Seen by" behaviour, replaced by `ProviderReassignmentTests`.

## Commits

| Commit | Subject |
|---|---|
| `f82c5ca` | ALV-N021 A2 step 0: T-2 explicit invariant culture at every fixed-format date/time (no process-wide culture), T-3 one practice-day boundary rule on the practice clock used by reports, calendar and board (decision 02). |
| `ce6e9da` | ALV-N021 A2 step 1: migrations M1 (visit settings, cancel category, event reason/category), M2 (provider-reassigned notification kind), M3 (append-only checkout outcomes) and the permissions CompleteOwnStartedVisit (Dentist, role-limited to Dentist/Hygienist), ReassignAppointmentProvider, CollectPayments; office manager chairside default removed (OD-07). |
| `b16a2f5` | ALV-N021 A2 step 2: checkout evaluator (read-only readiness), recorder (role rules, read-only ledger verification, corrections, retries) and checkout API; not yet wired to completion. |
| `eaaf2f5` | ALV-N021 A2 step 3a: one visit move policy for every route (booked provider for treatment, started-it rule for clinician completion), one path without shortcuts and with the two settings, check-in day rule, checkout gate on every completion route; existing visit tests brought to the approved rules. |
| `df82656` | ALV-N021 A2 step 3b: undo check-in with a reason (front office, before seating), cancel with a required category and derived short notice (report category cut), provider/room reassignment of the booking with reason, booking guards and two Urgent notifications; Seen by retired. |
| `4f73a81` | ALV-N021 A2 step 3c: blocked time over booked appointments needs confirmation and a reason and is refused over a visit under way; covered appointments are flagged (derived), check-in is refused while flagged, the front desk can keep with a reason; flaky recipient-order assertion in ProviderReassignmentTests fixed. |
| `022982c` | ALV-N021 A2 step 4a: available actions per signed-in user (GET appointments/{id}/actions) with blocked actions carrying the server's own code, next-actor text, and the same list on each board card. |
| `38e2956` | ALV-N021 A2 step 4b: the Visit board draws its buttons only from the server's per-user actions (blocked ones disabled with the server's reason), says who the visit waits for, and offers undo check-in, keep despite blocked time and booking reassignment with a reason; client-side permission rules removed. |
| `06905f1` | ALV-N021 A2 step 4c: the calendar panel draws the same per-user server actions as the Visit board (no STORY-011 shortcuts, no 'seen by'), cancel asks for a required category, short notice and history reasons are shown, flagged appointments are marked on the grid; client/server cancel category parity test. |
| `447eb3f` | ALV-N021 A2 step 4d: checkout panel on the Visit board and in the calendar drawer (server items and summary in words, outcomes recorded with an idempotency key reused on retry, ledger entry and reason where required, refusals explained); client/server checkout rule parity. |
| `6c3fcef` | ALV-N021 A2 step 4e: blocked time over booked appointments lists what it covers and needs a confirmation with a reason; over a visit under way it lists the visit and offers no way to add it. |
| `2b013b6` | ALV-N021 A2: visit-flow success measurements send a lower-case snake_case category the validator accepts (they were rejected and dropped since ALV-011-C01). |
| `22bac22` | ALV-N021 A2 step 5a: real-backend walkthroughs brought to A2 (board, flow on the injected clock; calendar and reports cancel category) and an opt-in injected clock for the walkthrough runner (owner decision); two small client explanation/label gaps fixed. |
| `fa7d849` | ALV-N021 A2 step 5b-1: test-only chain-host clock-advance route (forward only, bounded, per-run token, evidence record) for chain case 3.09, owner decision 2026-11-15; off without the token, absent from the production application. |
| `b5980d4` | ALV-N021 A2 step 5b-2: accumulated chain stages 3 (scheduling) and 4 (visit flow) for R05 cases 3.01-3.14 under the A2 rules, including the injected-clock no-show (3.09) and the two-window booking race (3.10); chain run 40/40. |
| `19b6f07` | ALV-N021 A2 step 6a: front desk takes and applies payments (CollectPayments, OD-27); every other billing write keeps ManageBilling. |
| `5c475f9` | ALV-N021 A2 step 6b: appointment history names who did each thing and every move records from and to (F-046, F-049, F-055, F-062, F-074); migration M4. |
| `b713478` | ALV-N021 A2 step 6c: Rebook and Open patient on cancelled and no-show appointments (F-045, F-051, F-073), from the server's actions list. |
| `cd56506` | ALV-N021 A2 step 6d-1a: calendar day and week fixes F-048, F-052 (shading), F-054, F-076, F-077. |
| `2dc42df` | ALV-N021 A2 step 6d-1b: labelled blocked time on the calendar (F-052) through migration M5 and a ViewSchedule read route that never returns the private reason; Open patient link contrast fix. |
| `7108d00` | ALV-N021 A2 step 6d-2: Schedule page status, links to the appointment and the patient (F-069), 'Appointment booked.' (F-070), one refusal format incl. the same patient (F-072). |
| `f67b9b1` | ALV-N021 A2 step 6d-3: Component Showcase only in development builds (F-064); start-time fields explain how to type a 24-hour time (F-068). |
| `41844aa` | ALV-N021 A2 step 6e: a booking's duration is filled in from the appointment type and stays editable (OD-21, F-044b). |
| `8969d15` | ALV-N021 A2 proof rows: check-in day rule on both DST days, Calendar/Board parity in the browser; simultaneous completion cited. |
| `7207b88` | ALV-N021 A2 OD gaps: visit-workflow settings changeable through configuration with audit (OD-03, OD-04); pre-A2 Ready-for-checkout visits gated (OD-30) and no no-show correction route (OD-20) proven. |
| `a2cc611` | Pre-A2 leftover: ALV-N020-C02 entry in the story catalog (the ledger had it since 0fa4e2c); found by the ALV-N021 A2 final evidence run. |
| `0006779` | ALV-N021 A2 final evidence run: mocked Playwright runs the Component Showcase specs on the development server (F-064); the production build is checked to have no showcase. |
| `265331c` | Pre-A2 leftover: brand inventory names useAppTheme.ts for the unchanged theme storage key (moved there by ALV-N020 21b40fc); found by the ALV-N021 A2 final evidence run. |
| `215436c` | ALV-N021 A2 migration M6: the patient-merge guard on Appointments covers CancelCategory (gap left by M1); VisitStateMachineTests updated to the A2 table (OD-01..OD-05, OD-08..OD-10). |
| `24417e1` | Pre-A2 leftover: BackupApiTests expects every managed asset class (ALV-N020-C01 added loginBackgrounds as the fifth); found by the ALV-N021 A2 final evidence run. |
| `2ef0817` | ALV-N021 A2: visit-workflow panel remounts on a new practice version instead of copying props into state (removes the one lint warning A2 added). |

## Files

| Status | Path |
|---|---|
| M | `.alveara/story_catalog.json` |
| M | `PROGRESS.md` |
| M | `docs/stabilization/BRAND_INVENTORY.json` |
| M | `docs/testing/REAL_BACKEND_E2E.md` |
| M | `scripts/Invoke-AccumulatedChain.ps1` |
| M | `scripts/Invoke-RealBackendWalkthrough.ps1` |
| M | `src/Alveara.Api.ChainHost/ChainHostFactory.cs` |
| M | `src/Alveara.Api.ChainHost/ChainHostMain.cs` |
| A | `src/Alveara.Api.ChainHost/ClockAdvanceEndpoint.cs` |
| M | `src/Alveara.Api.Tests/AppointmentLifecycleApiTests.cs` |
| M | `src/Alveara.Api.Tests/AppointmentLifecycleTests.cs` |
| A | `src/Alveara.Api.Tests/AppointmentMergeScopeMigrationTests.cs` |
| A | `src/Alveara.Api.Tests/AppointmentMoveDestinationMigrationTests.cs` |
| A | `src/Alveara.Api.Tests/AppointmentMoveHistoryTests.cs` |
| M | `src/Alveara.Api.Tests/BackupApiTests.cs` |
| M | `src/Alveara.Api.Tests/BillingApiTests.cs` |
| M | `src/Alveara.Api.Tests/BillingLedgerApiTests.cs` |
| A | `src/Alveara.Api.Tests/BlockedTimeCalendarLabelMigrationTests.cs` |
| A | `src/Alveara.Api.Tests/BlockedTimeOverlapTests.cs` |
| A | `src/Alveara.Api.Tests/CalendarBlockedTimeApiTests.cs` |
| A | `src/Alveara.Api.Tests/CancelCategoryClientParityTests.cs` |
| A | `src/Alveara.Api.Tests/CancellationCategoryTests.cs` |
| A | `src/Alveara.Api.Tests/ChainClockAdvanceTests.cs` |
| A | `src/Alveara.Api.Tests/CheckInDstDayRuleTests.cs` |
| A | `src/Alveara.Api.Tests/CheckoutTests.cs` |
| A | `src/Alveara.Api.Tests/CollectPaymentsApiTests.cs` |
| M | `src/Alveara.Api.Tests/ConfigurationApiTests.cs` |
| M | `src/Alveara.Api.Tests/PatientFlowApiTests.cs` |
| M | `src/Alveara.Api.Tests/PatientFlowLifecycleTests.cs` |
| M | `src/Alveara.Api.Tests/PermissionPolicyHealthTests.cs` |
| M | `src/Alveara.Api.Tests/PermissionPolicyWriteTests.cs` |
| A | `src/Alveara.Api.Tests/PracticeDayBoundaryTests.cs` |
| A | `src/Alveara.Api.Tests/ProviderReassignmentTests.cs` |
| M | `src/Alveara.Api.Tests/RealMigrationInventory.cs` |
| M | `src/Alveara.Api.Tests/RealMigrationInventoryTests.cs` |
| M | `src/Alveara.Api.Tests/SchedulingTestSupport.cs` |
| M | `src/Alveara.Api.Tests/StaffInactivationNotificationSchemaTests.cs` |
| A | `src/Alveara.Api.Tests/TimeCultureGuardTests.cs` |
| A | `src/Alveara.Api.Tests/UndoCheckInTests.cs` |
| A | `src/Alveara.Api.Tests/VisitActionsTests.cs` |
| D | `src/Alveara.Api.Tests/VisitAssignmentTests.cs` |
| M | `src/Alveara.Api.Tests/VisitBoardTests.cs` |
| A | `src/Alveara.Api.Tests/VisitCheckoutMigrationTests.cs` |
| A | `src/Alveara.Api.Tests/VisitFlowMeasurementTests.cs` |
| A | `src/Alveara.Api.Tests/VisitMovePolicyTests.cs` |
| M | `src/Alveara.Api.Tests/VisitOccupancyTests.cs` |
| A | `src/Alveara.Api.Tests/VisitPathTableTests.cs` |
| M | `src/Alveara.Api.Tests/VisitPermissionTests.cs` |
| M | `src/Alveara.Api.Tests/VisitStateMachineTests.cs` |
| A | `src/Alveara.Api.Tests/VisitWorkflowPermissionTests.cs` |
| M | `src/Alveara.Api.Tests/VisitWorkflowSchemaTests.cs` |
| A | `src/Alveara.Api.Tests/VisitWorkflowSettingsTests.cs` |
| M | `src/Alveara.Api.Tests/VisitWorkflowTests.cs` |
| M | `src/Alveara.Api.Tests/VisitsApiTests.cs` |
| M | `src/Alveara.Api/Architecture/Backup/BackupService.cs` |
| M | `src/Alveara.Api/Architecture/Backup/BackupSupport.cs` |
| M | `src/Alveara.Api/Architecture/Billing/BillingService.Responsibility.cs` |
| M | `src/Alveara.Api/Architecture/Billing/BillingService.Timeline.cs` |
| M | `src/Alveara.Api/Architecture/Clinical/DiagnosisService.Structure.cs` |
| M | `src/Alveara.Api/Architecture/Configuration/ConfigurationEntities.cs` |
| M | `src/Alveara.Api/Architecture/Configuration/PracticeConfigurationService.cs` |
| M | `src/Alveara.Api/Architecture/Configuration/SchedulingConfiguration.cs` |
| M | `src/Alveara.Api/Architecture/Configuration/StaffProviderService.cs` |
| M | `src/Alveara.Api/Architecture/Documents/DocumentImportService.cs` |
| M | `src/Alveara.Api/Architecture/ExternalCases/ExternalCaseTaskJobs.cs` |
| M | `src/Alveara.Api/Architecture/FollowUps/FollowUpEntities.cs` |
| M | `src/Alveara.Api/Architecture/Identity/Permission.cs` |
| M | `src/Alveara.Api/Architecture/Identity/PermissionMatrix.cs` |
| M | `src/Alveara.Api/Architecture/Identity/PermissionProtection.cs` |
| M | `src/Alveara.Api/Architecture/Identity/PermissionRegistry.json` |
| M | `src/Alveara.Api/Architecture/Notifications/NotificationEntities.cs` |
| M | `src/Alveara.Api/Architecture/Patients/Merge/PatientDuplicateScanner.cs` |
| M | `src/Alveara.Api/Architecture/Patients/Merge/PatientMergeService.cs` |
| M | `src/Alveara.Api/Architecture/Patients/Patient.cs` |
| M | `src/Alveara.Api/Architecture/Patients/PatientDirectory.cs` |
| M | `src/Alveara.Api/Architecture/Patients/PatientDuplicateDetector.cs` |
| M | `src/Alveara.Api/Architecture/Patients/PatientEditService.cs` |
| M | `src/Alveara.Api/Architecture/Patients/Portability/PatientExportService.cs` |
| M | `src/Alveara.Api/Architecture/Permissions/PolicyAdminService.cs` |
| M | `src/Alveara.Api/Architecture/Permissions/PolicySnapshot.cs` |
| M | `src/Alveara.Api/Architecture/Permissions/PolicySnapshotBuilder.cs` |
| M | `src/Alveara.Api/Architecture/Permissions/PolicyWriteService.cs` |
| M | `src/Alveara.Api/Architecture/Procedures/ProcedureCatalogService.cs` |
| M | `src/Alveara.Api/Architecture/Procedures/ProcedureRules.cs` |
| M | `src/Alveara.Api/Architecture/Recall/RecallJobs.cs` |
| M | `src/Alveara.Api/Architecture/Reporting/CancellationDrillService.cs` |
| M | `src/Alveara.Api/Architecture/Reporting/CancellationReportService.cs` |
| M | `src/Alveara.Api/Architecture/Reporting/CompletedProceduresReportService.cs` |
| M | `src/Alveara.Api/Architecture/Reporting/MeasurementBaselineService.cs` |
| M | `src/Alveara.Api/Architecture/Reporting/ReportFilter.cs` |
| M | `src/Alveara.Api/Architecture/Reporting/ScheduleReportService.cs` |
| M | `src/Alveara.Api/Architecture/Scheduling/Appointment.cs` |
| M | `src/Alveara.Api/Architecture/Scheduling/AppointmentFlowService.cs` |
| M | `src/Alveara.Api/Architecture/Scheduling/AppointmentManager.cs` |
| M | `src/Alveara.Api/Architecture/Scheduling/AppointmentViews.cs` |
| A | `src/Alveara.Api/Architecture/Scheduling/BlockFlags.cs` |
| A | `src/Alveara.Api/Architecture/Scheduling/BlockedTimeGuard.cs` |
| A | `src/Alveara.Api/Architecture/Scheduling/CalendarBlockedTime.cs` |
| A | `src/Alveara.Api/Architecture/Scheduling/Checkout/CheckoutEvaluator.cs` |
| A | `src/Alveara.Api/Architecture/Scheduling/Checkout/CheckoutRecorder.cs` |
| A | `src/Alveara.Api/Architecture/Scheduling/Checkout/CheckoutViews.cs` |
| M | `src/Alveara.Api/Architecture/Scheduling/SchedulingGuards.cs` |
| M | `src/Alveara.Api/Architecture/Scheduling/SchedulingModels.cs` |
| A | `src/Alveara.Api/Architecture/Scheduling/VisitActions.cs` |
| M | `src/Alveara.Api/Architecture/Scheduling/VisitAssignmentService.cs` |
| M | `src/Alveara.Api/Architecture/Scheduling/VisitBoardService.cs` |
| A | `src/Alveara.Api/Architecture/Scheduling/VisitCheckout.cs` |
| A | `src/Alveara.Api/Architecture/Scheduling/VisitMovePolicy.cs` |
| M | `src/Alveara.Api/Architecture/Scheduling/VisitStateMachine.cs` |
| M | `src/Alveara.Api/Architecture/Time/PracticeClock.cs` |
| A | `src/Alveara.Api/Controllers/AppointmentCheckoutController.cs` |
| M | `src/Alveara.Api/Controllers/AppointmentsController.cs` |
| M | `src/Alveara.Api/Controllers/BillingController.cs` |
| A | `src/Alveara.Api/Controllers/CalendarController.cs` |
| M | `src/Alveara.Api/Controllers/ConfigurationController.cs` |
| M | `src/Alveara.Api/Controllers/PatientsController.cs` |
| M | `src/Alveara.Api/Controllers/ReportsController.cs` |
| M | `src/Alveara.Api/Controllers/VisitsController.cs` |
| M | `src/Alveara.Api/Data/AlveraDbContext.cs` |
| M | `src/Alveara.Api/Data/NotificationModel.cs` |
| A | `src/Alveara.Api/Data/VisitCheckoutModel.cs` |
| A | `src/Alveara.Api/Migrations/20261114162213_AddVisitWorkflowSettingsAndCancelCategory.Designer.cs` |
| A | `src/Alveara.Api/Migrations/20261114162213_AddVisitWorkflowSettingsAndCancelCategory.cs` |
| A | `src/Alveara.Api/Migrations/20261114162254_AddProviderReassignedNotification.Designer.cs` |
| A | `src/Alveara.Api/Migrations/20261114162254_AddProviderReassignedNotification.cs` |
| A | `src/Alveara.Api/Migrations/20261114170534_AddVisitCheckoutOutcomes.Designer.cs` |
| A | `src/Alveara.Api/Migrations/20261114170534_AddVisitCheckoutOutcomes.cs` |
| A | `src/Alveara.Api/Migrations/20261116005444_AddAppointmentMoveDestination.Designer.cs` |
| A | `src/Alveara.Api/Migrations/20261116005444_AddAppointmentMoveDestination.cs` |
| A | `src/Alveara.Api/Migrations/20261116040919_AddBlockedTimeCalendarLabel.Designer.cs` |
| A | `src/Alveara.Api/Migrations/20261116040919_AddBlockedTimeCalendarLabel.cs` |
| A | `src/Alveara.Api/Migrations/20261116145816_RefreshAppointmentMergeScope.Designer.cs` |
| A | `src/Alveara.Api/Migrations/20261116145816_RefreshAppointmentMergeScope.cs` |
| M | `src/Alveara.Api/Migrations/AlveraDbContextModelSnapshot.cs` |
| M | `src/Alveara.Api/Program.cs` |
| A | `src/alveara-client/chain/stage-3.spec.ts` |
| A | `src/alveara-client/chain/stage-4.spec.ts` |
| A | `src/alveara-client/chain/stage3-support.ts` |
| M | `src/alveara-client/e2e/calendar-real-backend.spec.ts` |
| M | `src/alveara-client/e2e/patient-flow-real-backend.spec.ts` |
| A | `src/alveara-client/e2e/production-build.spec.ts` |
| M | `src/alveara-client/e2e/reports-real-backend.spec.ts` |
| M | `src/alveara-client/e2e/schedule-real-backend.spec.ts` |
| M | `src/alveara-client/e2e/shell.spec.ts` |
| M | `src/alveara-client/e2e/visit-board-real-backend.spec.ts` |
| M | `src/alveara-client/playwright.config.ts` |
| M | `src/alveara-client/src/App.tsx` |
| M | `src/alveara-client/src/Calendar.test.tsx` |
| A | `src/alveara-client/src/Checkout.test.tsx` |
| M | `src/alveara-client/src/FlowBoard.test.tsx` |
| M | `src/alveara-client/src/PatientFlow.test.tsx` |
| M | `src/alveara-client/src/Schedule.test.tsx` |
| A | `src/alveara-client/src/app/moduleRegistry.test.ts` |
| M | `src/alveara-client/src/app/moduleRegistry.ts` |
| M | `src/alveara-client/src/components/ConfigEntityPanel.css` |
| M | `src/alveara-client/src/pages/ConfigurationHubPage.test.tsx` |
| M | `src/alveara-client/src/pages/PatientWorkspacePage.tsx` |
| M | `src/alveara-client/src/pages/billing/BillingLedgerPanel.test.tsx` |
| M | `src/alveara-client/src/pages/billing/BillingPanel.tsx` |
| A | `src/alveara-client/src/pages/calendar/AppointmentHistory.test.tsx` |
| A | `src/alveara-client/src/pages/calendar/AppointmentHistory.tsx` |
| M | `src/alveara-client/src/pages/calendar/AppointmentPanel.tsx` |
| M | `src/alveara-client/src/pages/calendar/Calendar.css` |
| M | `src/alveara-client/src/pages/calendar/CalendarGrid.tsx` |
| M | `src/alveara-client/src/pages/calendar/CalendarPage.tsx` |
| A | `src/alveara-client/src/pages/calendar/CancelForm.tsx` |
| M | `src/alveara-client/src/pages/calendar/NewAppointmentPanel.tsx` |
| M | `src/alveara-client/src/pages/calendar/PlacementFields.tsx` |
| A | `src/alveara-client/src/pages/calendar/bookingDuration.test.ts` |
| A | `src/alveara-client/src/pages/calendar/bookingDuration.ts` |
| M | `src/alveara-client/src/pages/calendar/calendarLayout.test.ts` |
| M | `src/alveara-client/src/pages/calendar/calendarLayout.ts` |
| M | `src/alveara-client/src/pages/calendar/explainRefusal.ts` |
| M | `src/alveara-client/src/pages/configuration/AvailabilityTab.tsx` |
| A | `src/alveara-client/src/pages/configuration/BlockOverlapNotice.tsx` |
| M | `src/alveara-client/src/pages/configuration/PracticeTab.tsx` |
| A | `src/alveara-client/src/pages/configuration/VisitWorkflowPanel.tsx` |
| M | `src/alveara-client/src/pages/flow/AssignPanel.tsx` |
| A | `src/alveara-client/src/pages/flow/CheckoutPanel.tsx` |
| M | `src/alveara-client/src/pages/flow/FlowBoard.css` |
| M | `src/alveara-client/src/pages/flow/FlowBoardPage.tsx` |
| A | `src/alveara-client/src/pages/flow/ReasonForm.tsx` |
| M | `src/alveara-client/src/pages/flow/VisitCardView.tsx` |
| M | `src/alveara-client/src/pages/flow/visitHelpers.test.ts` |
| M | `src/alveara-client/src/pages/flow/visitLabels.ts` |
| M | `src/alveara-client/src/pages/scheduling/SchedulePage.css` |
| M | `src/alveara-client/src/pages/scheduling/SchedulePage.tsx` |
| A | `src/alveara-client/src/services/checkoutApi.ts` |
| M | `src/alveara-client/src/services/configApi.ts` |
| M | `src/alveara-client/src/services/permissionPolicyApi.ts` |
| M | `src/alveara-client/src/services/schedulingApi.ts` |
| M | `src/alveara-client/src/services/visitsApi.ts` |
| M | `src/alveara-client/src/test/fakeCalendarServer.ts` |
| M | `src/alveara-client/src/test/fakeFlowServer.ts` |
| M | `src/alveara-client/src/test/fakeScheduleServer.ts` |
