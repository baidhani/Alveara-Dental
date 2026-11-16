# ALV-N021 A3 site inventory (revision 2, the approved baseline for implementation)

Baseline commit `d20e04c` (unchanged code since; A3 not started). Every client line under `src/alveara-client/src` (tests, fakes and test servers excluded) that formats a date/time, derives a calendar day from the browser clock, takes or produces a `datetime-local` value, or turns an instant into text was read in context, including the screen-local and shared **wrappers** and their call sites. Each has one disposition. The A3 handoff must compare its changed and allowlisted lines to this table; any difference is reported.

**Dispositions**

| Code | Meaning |
|---|---|
| **MI** | migrate to the shared instant helper `formatInstant(utcIso, zone, style)` |
| **MD** | migrate to the practice-day helper `practiceToday(zone, now?)` (and date arithmetic on its result) |
| **MP** | migrate to `toPracticeLocalInput(utcIso, zone, { seconds })` (prefill of a `datetime-local` only) |
| **MS** | remove the client conversion; send the plain `...Local` text to the server (section 5 of the briefing) |
| **RS** | retain: an already-correct practice-zone helper, moved into `time/practiceTime.ts` or rebuilt on it without behaviour change |
| **RO** | retain: date-only value (never zone-converted) |
| **RL** | retain: instant logic, wire value, timer or idempotency key (not a displayed date) |
| **X** | exclude: not a date/time (number or money formatting) |

## 1. Instant display (MI unless stated)

Wrappers are migrated **at the wrapper**; their callers then need no change (listed so the reviewer can see the reach).

| # | Site | Expression | Disposition | Callers / notes |
|---|---|---|---|---|
| 1 | `pages/AdminUserDetailPage.tsx:177` | `createdAtUtc` toLocaleString | MI | |
| 2 | `pages/AuditLogPage.tsx:77` | `timestampUtc` | MI | |
| 3 | `pages/LoginPage.tsx:86` | `lockedUntilUtc` toLocaleTimeString | **MI-pre-session** | shown **before sign-in**, when no session zone exists: replaced by a duration ("Try again in N minutes", computed from the instant and the browser clock difference only), so no zone is needed; no browser-zone time is shown |
| 4 | `pages/PatientHistoryPanel.tsx:61, 71` | `changedAtUtc`, `createdAtUtc` | MI | |
| 5 | `pages/SystemStatusPage.tsx:82, 127` | `lastPollUtc`, `lastConfirmedAtUtc` | MI | |
| 6 | `pages/backup/ArchivePanel.tsx:37` | `modifiedUtc` | MI | |
| 7 | `pages/backup/BackupLists.tsx:10` | wrapper `when` | MI | local callers in the file |
| 8 | `pages/backup/BackupStatusPanel.tsx:4` | wrapper `when` ("never" when null) | MI | |
| 9 | `pages/backup/RecoveryKeyPanel.tsx:86` | `recoveryKeyConfiguredAtUtc` toLocaleDateString | MI (date style) | |
| 10 | `pages/backup/RestoreWizard.tsx:132` | `startedAtUtc` | MI | |
| 11 | `pages/billing/BillingChargeSource.tsx:6` | wrapper `when` | MI | |
| 12 | `pages/billing/BillingDocumentView.tsx:3, 21` | wrapper `day` (ledger `recordedAtUtc`, charge `serviceDateUtc`, receipt `receivedAtUtc`); `asOfUtc` "Up to" | MI (date / date-time) | T-4; issued canonical content unchanged |
| 13 | `pages/billing/BillingDocuments.tsx:127, 153` | `issuedAtUtc` | MI | |
| 14 | `pages/billing/BillingLedger.tsx:55` | `recordedAtUtc` toLocaleDateString | MI (date style) | |
| 15 | `pages/billing/BillingTimelineAging.tsx:54` | `atUtc` | MI | |
| 16 | `pages/calendar/AppointmentHistory.tsx:31` | `occurredAtUtc` | MI | |
| 17 | `pages/calendar/AppointmentPanel.tsx:187, 194` | `flowChangedAtUtc`, `statusChangedAtUtc` | MI | |
| 18 | `pages/clinical/AddendumPanel.tsx:11` | wrapper `when` | MI | |
| 19 | `pages/clinical/EncounterView.tsx:27` | wrapper `when` | MI | |
| 20 | `pages/clinical/PatientClinicalPanel.tsx:14` | wrapper `when` | MI | |
| 21 | `pages/clinical/notes/VitalsPanel.tsx:14` | wrapper `when` | MI | |
| 22 | `pages/clinical/record/ItemHistory.tsx:6` | wrapper `when` | MI | |
| 23 | `pages/clinical/record/RecordItemRow.tsx:12` | wrapper `when` (date) | MI (date style) | |
| 24 | `pages/clinical/record/RecordSectionCard.tsx:11` | wrapper `when` (date) | MI (date style) | |
| 25 | `pages/diagnosis/DiagnosisChips.tsx:6` | wrapper `when` | MI | |
| 26 | `pages/diagnosis/DiagnosisForm.tsx:140, 144` | `encounterAtUtc` | MI | |
| 27 | `pages/diagnosis/DiagnosisHistory.tsx:7` | wrapper `when` | MI | |
| 28 | `pages/diagnosis/DiagnosisItem.tsx:14, 30` | wrapper `when` and its use | MI | |
| 29 | `pages/diagnosis/DiagnosisLinkForm.tsx:18` | wrapper `when` (date) | MI (date style) | |
| 30 | `pages/documents/documentText.ts:14` | exported `formatDateTime` | MI | 7 calls: DocumentDetailsPanel 1, DocumentEntries 3, DocumentImportForm 1, DocumentStoragePage 2 |
| 31 | `pages/documents/documentText.ts:31` | exported `encounterLabel` (date of an encounter instant) | MI (date style) | 3 calls: DocumentContextFields, DocumentEntries, DocumentFilters |
| 32 | `pages/externalcases/ExternalCaseDetailPage.tsx:88` | `occurredAtUtc` | MI | |
| 33 | `pages/externalcases/PatientExternalCasesPanel.tsx:64` | event `occurredAtUtc` | MI | |
| 34 | `pages/flow/FlowBoardPage.tsx:27` | `clock(ms)`: the board's "now" (server clock + elapsed) as a time | MI (time style, from the instant) | the elapsed-time logic itself is RL (section 5) |
| 35 | `pages/followups/followUpFormat.ts:21` | exported `formatWhen` | MI | 9 calls: FollowUpHistory 1, FollowUpItem 2, MyFollowUpsPage 1, PatientFollowUpTimeline 1, PatientRecallCard 2, WorkQueuePage 2 |
| 36 | `pages/forms/FormTemplatesPage.tsx:104` | `createdAtUtc` | MI | |
| 37 | `pages/forms/PatientFormView.tsx:156, 162` | `startedAtUtc`, `voidedAtUtc` | MI | |
| 38 | `pages/forms/PatientFormsPanel.tsx:14` | wrapper `when` | MI | |
| 39 | `pages/forms/SignedFormView.tsx:8` | wrapper `when` | MI | |
| 40 | `pages/notifications/AdminNotificationsPage.tsx:13` | wrapper `when` | MI | |
| 41 | `pages/notifications/MessagesPage.tsx:13` | wrapper `when` | MI | |
| 42 | `pages/notifications/NotificationsPage.tsx:16` | wrapper `when` | MI | |
| 43 | `pages/odontogram/odontogramText.ts:5` | exported `when` | MI | 7 calls: ConditionTypesPanel 2, FindingHistory 1, FindingRow 3, ToothTimeline 1 |
| 44 | `pages/patientmerge/MergeHistoryBanner.tsx:7` | wrapper `when` (date) | MI (date style) | |
| 45 | `pages/patientmerge/PatientMergePage.tsx:133, 149, 156` | `dismissedAtUtc`, `mergedAtUtc`, `undoneAtUtc` | MI | |
| 46 | `pages/perio/PerioChartExtras.tsx:38` | wrapper `when` | MI | |
| 47 | `pages/perio/PerioHistory.tsx:25` | wrapper `when` | MI | |
| 48 | `pages/perio/PerioSessionPanel.tsx:253` | `recordedAtUtc` label | MI | |
| 49 | `pages/permissions/PolicyHistoryPanel.tsx:30` | `changedAtUtc` | MI | |
| 50 | `pages/prescription/workflow/PrintPreview.tsx:9` | wrapper `day` (prescribed instant) | MI (date style) | DOB on the same line stays `formatDateOnly` (RO) |
| 51 | `pages/procedures/procedureText.ts:15` | exported `when` | MI | 2 calls: ProcedureRow 2 |
| 52 | `pages/safety/safetyText.ts:4` | exported `when` | MI | 8 calls: ClearanceCard 3, SafetyEntryRow 3, SafetyHistory 1, SafetyPanel 1 |
| 53 | `pages/safety/safetyText.ts:5` | exported `day` | MI (date style) | **0 callers**: removed instead of migrated |
| 54 | `pages/treatmentplan/treatmentPlanRules.ts:13` | exported `when` | MI | 17 calls: PrescriptionList 1, PrescriptionRecordList 2, PrescriptionWorkflowPanel 1, ClinicalHistorySection 2, CompletionResultPanel 1, TreatmentPlanAdoptCompletion 1, TreatmentPlanCard 3, TreatmentPlanItemRow 1, TreatmentPlanItemStateLine 2, TreatmentPlanReviewComplete 1, TreatmentPlanRevisions 1, TreatmentPlanRevisionView 1 |
| 55 | `pages/treatmentplan/TreatmentPlanPatientCopy.tsx:41` | `new Date().toLocaleDateString()` (printed "today") | **MD** + `formatDateOnly` | the printed date is the practice day |

## 2. Already practice-zone (RS)

| # | Site | Disposition | Notes |
|---|---|---|---|
| 56 | `pages/calendar/calendarLayout.ts:150-156` `practiceNow` | RS: moved to `time/practiceTime.ts`; Calendar and Flow Board callers (`CalendarPage.tsx:57, 113, 223`, `FlowBoardPage.tsx:64, 125`) import it from there | they keep their scheduling-snapshot zone, which must equal the session zone (asserted in a test) |
| 57 | `pages/exports/exportFormat.ts:4-10` `formatMoment` | RS rebuilt on `formatInstant`: **its browser-zone fallback for a null/unknown zone is removed** (returns "Time unavailable") | callers: reports `FollowUpsView.tsx:47`, `MetricsSection.tsx:47`, `MoreRecords.tsx:16, 67`, `RecordsPanel.tsx:83, 99, 103`, `ExportHistoryTable.tsx:24`, `BaselineSection.tsx:69` and `ReportFrame.tsx` (1) |

## 3. Browser "today" (MD)

| # | Site | Disposition | Callers / notes |
|---|---|---|---|
| 58 | `components/dateOnly.ts:19-21` `todayDateOnly` | MD: removed; callers use `practiceToday` | `PatientFieldsForm.tsx:9` (DOB upper limit) |
| 59 | `pages/documents/documentText.ts:17-19` `todayValue` | MD | `DocumentImportForm.tsx:24` |
| 60 | `pages/externalcases/externalCaseFormat.ts:20-23` `todayInput` | MD | `CaseStatusForm.tsx:16, 32, 52` (default and max) |
| 61 | `pages/reports/reportFormat.ts:5-6, 16, 35-41` `today`, presets, last month | MD (`isoDate` of a browser `Date` removed; presets computed from `practiceToday` by date-only arithmetic) | `defaultQuery`, the four presets |
| 62 | `pages/scheduling/SchedulePage.tsx:34-38` `tomorrow` | MD (+1 day by date-only arithmetic) | Schedule page default date |

## 4. `datetime-local` input

| # | Site | Disposition | Notes |
|---|---|---|---|
| 63 | `pages/followups/followUpFormat.ts:8-11` (instant to input) | MP | edit form prefill |
| 64 | `pages/followups/followUpFormat.ts:14-18` (input to UTC instant) | MS | create/update send `dueAtLocal` |
| 65 | `pages/followups/FollowUpForm.tsx:76` (control) | retain control; value is plain local text | |
| 66 | `pages/treatmentplan/completionReviewRules.ts:12-14` (now to input, seconds) | MP with `{ seconds: true }` | default "now" of the performed time |
| 67 | `pages/treatmentplan/completionReviewRules.ts:21` (input to UTC instant) | MS | send `performedAtLocal` |
| 68 | `pages/treatmentplan/TreatmentPlanAdoptCompletion.tsx:42, 143`, `TreatmentPlanReviewComplete.tsx:37, 132` | default via MP; control retained (plain text) | |
| 69 | `services/clinicalNotesApi.ts:58` (`new Date(local).toISOString()`) | MS | send `measuredAtLocal` |
| 70 | `pages/clinical/notes/VitalsPanel.tsx:162` (control) | retain control (plain text); its error key moves from `measuredAtUtc` to `measuredAtLocal` | |
| 71 | `pages/configuration/AvailabilityTab.tsx:404-405` (blocked time) | retain: already plain local text converted by the server | |

## 5. Date-only, logic, wire, keys and non-dates (retained or excluded)

| # | Site | Disposition | Reason |
|---|---|---|---|
| 72 | `components/dateOnly.ts:6-12` `formatDateOnly` (incl. the parts check on line 11) | RO | date-only display built from parts (A1) |
| 73 | `pages/externalcases/externalCaseFormat.ts:13-16` `formatDay` | RO | practice-local date string; 6 calls (CaseDocumentsSection 1, ExternalCaseDetailPage 3, ExternalCasesPage 1, PatientExternalCasesPanel 1) |
| 74 | `pages/procedures/procedureText.ts:17` `dateText` | RO | date-only (`T00:00:00` local parse) |
| 75 | `pages/treatmentplan/treatmentPlanRules.ts:16` `dayOf` | RO | date-only |
| 76 | `pages/treatmentplan/treatmentPlanLayout.ts:101` target date | RO | date-only |
| 77 | `pages/calendar/calendarLayout.ts:15` `fromDayNumber` | RO | date-only day arithmetic in UTC by design (no zone meaning) |
| 78 | `contexts/AuthContext.tsx:165-167` session expiry | RL | instant arithmetic |
| 79 | `pages/flow/FlowBoardPage.tsx:50, 90, 111` `clientNow`, `fetchedAt`, tick | RL | elapsed-time logic from the server clock |
| 80 | `pages/imports/useImportBatch.ts:38, 40` | RL | polling timer |
| 81 | `hooks/useSystemStatus.ts:68` | RL | a UTC instant recorded for the stale check |
| 82 | `services/clinicalApi.ts:201`, `externalCasesApi.ts:48`, `followUpsApi.ts:53`, `patientMergesApi.ts:63`, `permissionPolicyApi.ts:54`, `pages/notifications/ComposeMessage.tsx:9` | RL | idempotency keys (`Date.now()` only as entropy) |
| 83 | report counts: `BalancesView.tsx` (6), `CancellationsView.tsx` (3), `CompletedProceduresView.tsx`, `LedgerProof.tsx` (3), `MetricsSection.tsx` (2), `NumberButton.tsx`, `PatientFlowView.tsx` (5 incl. line 31), `ProductionView.tsx` (2), `ScheduleView.tsx` (5), `TreatmentPlansView.tsx` (2), `CollectionsView.tsx`, `ComparisonSection.tsx`, `RecordsPanel.tsx` (count) | X | number formatting |
| 84 | `pages/configuration/SignInScreenTab.tsx:165` | X | number (character limit) |
| 85 | `pages/imports/importFormat.ts:14`, `pages/treatmentplan/treatmentPlanRules.ts:65` | X | money |

## 6. Session contract (BR-5)

| Where | Change |
|---|---|
| `src/Alveara.Api/Controllers/AuthController.cs` `MyPermissions` | add `practiceTimeZoneId` (canonical IANA) to every successful authenticated response; no permission check beyond `[Authorize]` |
| `src/Alveara.Api` start-up | canonical IANA id derived once from `PracticeTimeZone` (Windows id converted; no IANA form = start-up failure) and the server-owned `PracticeZoneIds` mapping |
| `src/alveara-client/src/services/authApi.ts:35` session type | `practiceTimeZoneId: string` (required) |
| `src/alveara-client/src/contexts/AuthContext.tsx` | stores it from every `permissions` response (sign-in, reload, renewal, tab visible again) and exposes it to the helpers |
| client fakes and tests answering `GET /api/auth/permissions` (34 files) | all switched to one shared test builder `signedInSession(...)` that includes `practiceTimeZoneId` (default `America/Chicago`), so a missing field is a deliberate test case, not an accident: `src/app/AppShell.permissionAwareNav.test.tsx`, `src/App.patientRoute.test.tsx`, `src/App.patientWorkspaceRoutes.test.tsx`, `src/App.routeGuards.test.tsx`, `src/App.unsavedNavigation.test.tsx`, `src/contexts/AuthContext.test.tsx`, `src/contexts/PatientContext.test.tsx`, `src/pages/AdminUserDetailPage.test.tsx`, `src/pages/AdminUsersPage.test.tsx`, `src/pages/BackupRecoveryPage.test.tsx`, `src/pages/configuration/accountPermissionGating.test.tsx`, `src/pages/exports/ExportCenterPage.test.tsx`, `src/pages/exports/ExportCenterRoutes.test.tsx`, `src/pages/imports/importTestServer.ts`, `src/pages/MfaSettingsPage.test.tsx`, `src/pages/ProcedureCatalogPage.test.tsx`, `src/pages/reports/reportTestServer.ts`, `src/test/fakePatientServer.ts`; mocked specs `e2e/accessibility.spec.ts`, `backup-warning-contrast.spec.ts`, `login-banner-contrast.spec.ts`, `login-layout-polish.spec.ts`, `login-presentation-access.spec.ts`, `login-presentation.spec.ts`, `notification-contrast.spec.ts`, `production-build.spec.ts`, `shell.spec.ts`, `system-status-stale-contrast.spec.ts`, `system-status.spec.ts`; real-backend `e2e/permission-policy-real-backend.spec.ts`, `permission-policy-admin-real-backend.spec.ts`, `chain/stage-1.spec.ts`, `gate-a/gate-a-browser.spec.ts` (these read the real server and need no fake change) |
| server tests reading the response (7 files) | one new assertion test per role (section 9 of the briefing); the others unchanged |

## 7. Server request fields (briefing section 5)

| Route | Field today | A3 field |
|---|---|---|
| `POST api/treatment-plans/{planId}/complete-procedures` | `performedAtUtc` | `performedAtLocal` (seconds allowed) |
| `POST api/treatment-plans/{planId}/items/{itemId}/adopt-completion` | `performedAtUtc` | `performedAtLocal` (seconds allowed) |
| `POST api/patients/{patientId}/follow-ups` | `dueAtUtc` | `dueAtLocal` |
| `PUT api/follow-ups/{id}` | `dueAtUtc` | `dueAtLocal` |
| `POST api/encounters/{encounterId}/vitals` | `measuredAtUtc` | `measuredAtLocal` |
| `GET api/auth/permissions` | - | adds `practiceTimeZoneId` |
