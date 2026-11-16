# ALV-N021 A3-R01: changed files (git diff --name-status 3845e31..b8756f1)

Base `3845e31` (A3 briefing approval recorded) to implementation `b8756f1`: 7 commits, 126 files changed, 1696 insertions(+), 236 deletions(-).

## Commits

| Commit | Subject |
|---|---|
| `3fc39b0` | ALV-N021 A3 slice 1: canonical IANA practice zone (Windows id converted, none refused at start-up), server-owned Windows mapping, practiceTimeZoneId in the session response. |
| `073a419` | ALV-N021 A3 slice 2: one exact practice-local input parser; performedAtLocal, dueAtLocal, measuredAtLocal on the five routes; legacy UTC alone kept, both refused; local field named on later errors. |
| `65f8654` | ALV-N021 A3 slice 3: client practice-time helpers (no browser fallback), the practice zone in the session state with an observed-change flag, one session builder for every fake. |
| `4aa9a41` | ALV-N021 A3 slice 4: every screen renders instants in the session's practice zone (Time unavailable without one, no browser fallback); shell banner and zone-change notice; lockout shown as a duration. |
| `0316621` | ALV-N021 A3 slice 5: practice today everywhere; the four forms send plain practice-local text (dueAtLocal, performedAtLocal, measuredAtLocal); no browser conversion of typed times. |
| `3e19831` | ALV-N021 A3 slice 6: repository guard against browser-zone instant formatting and browser-local today outside the shared helper (per-entry allowlist, stale entries fail). |
| `b8756f1` | ALV-N021 A3 slice 7a: real-browser proof from America/Chicago and Asia/Tokyo browsers against one Chicago practice (display, typed time, today). |

## Files

| Status | Path |
|---|---|
| M | `PROGRESS.md` |
| M | `docs/testing/REAL_BACKEND_E2E.md` |
| A | `src/Alveara.Api.Tests/PracticeLocalInputApiTests.cs` |
| A | `src/Alveara.Api.Tests/PracticeLocalInputTests.cs` |
| A | `src/Alveara.Api.Tests/PracticeTimeZoneSessionTests.cs` |
| M | `src/Alveara.Api/Architecture/FollowUps/FollowUpViews.cs` |
| M | `src/Alveara.Api/Architecture/Time/PracticeClock.cs` |
| A | `src/Alveara.Api/Architecture/Time/PracticeLocalInput.cs` |
| A | `src/Alveara.Api/Architecture/Time/PracticeZoneIds.cs` |
| M | `src/Alveara.Api/Controllers/AuthController.cs` |
| M | `src/Alveara.Api/Controllers/ClinicalNotesController.cs` |
| M | `src/Alveara.Api/Controllers/CompletionController.cs` |
| M | `src/Alveara.Api/Controllers/FollowUpsController.cs` |
| M | `src/Alveara.Api/Program.cs` |
| M | `src/alveara-client/e2e/accessibility.spec.ts` |
| M | `src/alveara-client/e2e/backup-warning-contrast.spec.ts` |
| M | `src/alveara-client/e2e/notification-contrast.spec.ts` |
| A | `src/alveara-client/e2e/practice-time-zones-real-backend.spec.ts` |
| M | `src/alveara-client/e2e/production-build.spec.ts` |
| M | `src/alveara-client/e2e/shell.spec.ts` |
| A | `src/alveara-client/e2e/support/signedInSession.ts` |
| M | `src/alveara-client/e2e/system-status-stale-contrast.spec.ts` |
| M | `src/alveara-client/e2e/system-status.spec.ts` |
| M | `src/alveara-client/playwright.config.ts` |
| A | `src/alveara-client/playwright.practice-time.config.ts` |
| M | `src/alveara-client/src/App.patientRoute.test.tsx` |
| M | `src/alveara-client/src/App.patientWorkspaceRoutes.test.tsx` |
| M | `src/alveara-client/src/App.routeGuards.test.tsx` |
| M | `src/alveara-client/src/App.unsavedNavigation.test.tsx` |
| M | `src/alveara-client/src/app/AppShell.css` |
| M | `src/alveara-client/src/app/AppShell.permissionAwareNav.test.tsx` |
| A | `src/alveara-client/src/app/AppShell.practiceZone.test.tsx` |
| M | `src/alveara-client/src/app/AppShell.tsx` |
| M | `src/alveara-client/src/components/PatientFieldsForm.tsx` |
| M | `src/alveara-client/src/components/dateOnly.test.tsx` |
| M | `src/alveara-client/src/components/dateOnly.ts` |
| A | `src/alveara-client/src/contexts/AuthContext.practiceZone.test.tsx` |
| M | `src/alveara-client/src/contexts/AuthContext.test.tsx` |
| M | `src/alveara-client/src/contexts/AuthContext.tsx` |
| M | `src/alveara-client/src/contexts/PatientContext.test.tsx` |
| M | `src/alveara-client/src/contexts/authContextObject.ts` |
| M | `src/alveara-client/src/pages/AdminUserDetailPage.test.tsx` |
| M | `src/alveara-client/src/pages/AdminUserDetailPage.tsx` |
| M | `src/alveara-client/src/pages/AdminUsersPage.test.tsx` |
| M | `src/alveara-client/src/pages/AuditLogPage.tsx` |
| M | `src/alveara-client/src/pages/BackupRecoveryPage.test.tsx` |
| M | `src/alveara-client/src/pages/LoginPage.tsx` |
| M | `src/alveara-client/src/pages/MfaSettingsPage.test.tsx` |
| M | `src/alveara-client/src/pages/PatientHistoryPanel.tsx` |
| M | `src/alveara-client/src/pages/ProcedureCatalogPage.test.tsx` |
| M | `src/alveara-client/src/pages/SystemStatusPage.tsx` |
| M | `src/alveara-client/src/pages/backup/ArchivePanel.tsx` |
| M | `src/alveara-client/src/pages/backup/BackupLists.tsx` |
| M | `src/alveara-client/src/pages/backup/BackupStatusPanel.tsx` |
| M | `src/alveara-client/src/pages/backup/RecoveryKeyPanel.tsx` |
| M | `src/alveara-client/src/pages/backup/RestoreWizard.tsx` |
| M | `src/alveara-client/src/pages/billing/BillingChargeSource.tsx` |
| M | `src/alveara-client/src/pages/billing/BillingDocumentView.tsx` |
| M | `src/alveara-client/src/pages/billing/BillingDocuments.tsx` |
| M | `src/alveara-client/src/pages/billing/BillingLedger.tsx` |
| M | `src/alveara-client/src/pages/billing/BillingTimelineAging.tsx` |
| M | `src/alveara-client/src/pages/calendar/AppointmentHistory.tsx` |
| M | `src/alveara-client/src/pages/calendar/AppointmentPanel.tsx` |
| M | `src/alveara-client/src/pages/clinical/AddendumPanel.tsx` |
| M | `src/alveara-client/src/pages/clinical/EncounterView.tsx` |
| M | `src/alveara-client/src/pages/clinical/PatientClinicalPanel.tsx` |
| M | `src/alveara-client/src/pages/clinical/notes/VitalsPanel.tsx` |
| M | `src/alveara-client/src/pages/clinical/record/ItemHistory.tsx` |
| M | `src/alveara-client/src/pages/clinical/record/RecordItemRow.tsx` |
| M | `src/alveara-client/src/pages/clinical/record/RecordSectionCard.tsx` |
| M | `src/alveara-client/src/pages/configuration/accountPermissionGating.test.tsx` |
| M | `src/alveara-client/src/pages/diagnosis/DiagnosisChips.tsx` |
| M | `src/alveara-client/src/pages/diagnosis/DiagnosisForm.tsx` |
| M | `src/alveara-client/src/pages/diagnosis/DiagnosisHistory.tsx` |
| M | `src/alveara-client/src/pages/diagnosis/DiagnosisItem.tsx` |
| M | `src/alveara-client/src/pages/diagnosis/DiagnosisLinkForm.tsx` |
| M | `src/alveara-client/src/pages/documents/documentText.ts` |
| M | `src/alveara-client/src/pages/exports/ExportCenterPage.test.tsx` |
| M | `src/alveara-client/src/pages/exports/ExportCenterRoutes.test.tsx` |
| M | `src/alveara-client/src/pages/exports/exportFormat.ts` |
| M | `src/alveara-client/src/pages/externalcases/ExternalCaseDetailPage.tsx` |
| M | `src/alveara-client/src/pages/externalcases/PatientExternalCasesPanel.tsx` |
| M | `src/alveara-client/src/pages/externalcases/externalCaseFormat.ts` |
| M | `src/alveara-client/src/pages/flow/FlowBoardPage.tsx` |
| M | `src/alveara-client/src/pages/followups/FollowUpScreens.test.tsx` |
| M | `src/alveara-client/src/pages/followups/followUpFormat.ts` |
| M | `src/alveara-client/src/pages/forms/FormTemplatesPage.tsx` |
| M | `src/alveara-client/src/pages/forms/PatientFormView.tsx` |
| M | `src/alveara-client/src/pages/forms/PatientFormsPanel.tsx` |
| M | `src/alveara-client/src/pages/forms/SignedFormView.tsx` |
| M | `src/alveara-client/src/pages/imports/importTestServer.ts` |
| M | `src/alveara-client/src/pages/loginOutcome.test.ts` |
| M | `src/alveara-client/src/pages/loginOutcome.ts` |
| M | `src/alveara-client/src/pages/notifications/AdminNotificationsPage.tsx` |
| M | `src/alveara-client/src/pages/notifications/MessagesPage.tsx` |
| M | `src/alveara-client/src/pages/notifications/NotificationsPage.tsx` |
| M | `src/alveara-client/src/pages/odontogram/odontogramText.ts` |
| M | `src/alveara-client/src/pages/patientmerge/MergeHistoryBanner.tsx` |
| M | `src/alveara-client/src/pages/patientmerge/PatientMergePage.tsx` |
| M | `src/alveara-client/src/pages/perio/PerioChartExtras.tsx` |
| M | `src/alveara-client/src/pages/perio/PerioHistory.tsx` |
| M | `src/alveara-client/src/pages/perio/PerioSessionPanel.tsx` |
| M | `src/alveara-client/src/pages/permissions/PolicyHistoryPanel.tsx` |
| M | `src/alveara-client/src/pages/prescription/workflow/PrintPreview.tsx` |
| M | `src/alveara-client/src/pages/procedures/procedureText.ts` |
| M | `src/alveara-client/src/pages/reports/reportFormat.ts` |
| M | `src/alveara-client/src/pages/reports/reportTestServer.ts` |
| M | `src/alveara-client/src/pages/safety/safetyText.ts` |
| M | `src/alveara-client/src/pages/scheduling/SchedulePage.tsx` |
| M | `src/alveara-client/src/pages/treatmentplan/TreatmentPlanAdoptCompletion.tsx` |
| M | `src/alveara-client/src/pages/treatmentplan/TreatmentPlanPatientCopy.tsx` |
| M | `src/alveara-client/src/pages/treatmentplan/TreatmentPlanReviewComplete.test.tsx` |
| M | `src/alveara-client/src/pages/treatmentplan/TreatmentPlanReviewComplete.tsx` |
| M | `src/alveara-client/src/pages/treatmentplan/completionReviewRules.ts` |
| M | `src/alveara-client/src/pages/treatmentplan/treatmentPlanRules.ts` |
| M | `src/alveara-client/src/services/authApi.ts` |
| M | `src/alveara-client/src/services/clinicalNotesApi.ts` |
| M | `src/alveara-client/src/services/completionApi.ts` |
| M | `src/alveara-client/src/services/followUpsApi.ts` |
| A | `src/alveara-client/src/services/vitalsPayload.test.ts` |
| M | `src/alveara-client/src/test/fakePatientServer.ts` |
| M | `src/alveara-client/src/test/setup.ts` |
| A | `src/alveara-client/src/test/signedInSession.ts` |
| A | `src/alveara-client/src/time/practiceTime.test.ts` |
| A | `src/alveara-client/src/time/practiceTime.ts` |
| A | `tests/practice-time-guard.test.mjs` |
