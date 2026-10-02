# ALV-011-C01 R01 — Changed files

Implementation commit `7923986a34d091da0172e0c8c84efd397175083c` — 63 files. `A` = added, `M` = modified; counts are `+added −removed` lines.

## Backend product (src/Alveara.Api) (23)

- `A` `src/Alveara.Api/Architecture/Forms/CheckInReadiness.cs` +87 −0
- `M` `src/Alveara.Api/Architecture/Forms/FormEntities.cs` +6 −0
- `M` `src/Alveara.Api/Architecture/Forms/FormTemplateService.cs` +19 −2
- `M` `src/Alveara.Api/Architecture/Identity/Permission.cs` +6 −0
- `M` `src/Alveara.Api/Architecture/Identity/PermissionMatrix.cs` +10 −5
- `A` `src/Alveara.Api/Architecture/Identity/RequireAnyPermissionAttribute.cs` +34 −0
- `M` `src/Alveara.Api/Architecture/Scheduling/Appointment.cs` +11 −0
- `M` `src/Alveara.Api/Architecture/Scheduling/AppointmentFlowService.cs` +49 −26
- `M` `src/Alveara.Api/Architecture/Scheduling/AppointmentManager.cs` +3 −1
- `M` `src/Alveara.Api/Architecture/Scheduling/AppointmentViews.cs` +10 −1
- `M` `src/Alveara.Api/Architecture/Scheduling/SchedulingGuards.cs` +12 −9
- `M` `src/Alveara.Api/Architecture/Scheduling/SchedulingModels.cs` +9 −1
- `A` `src/Alveara.Api/Architecture/Scheduling/VisitAssignmentService.cs` +111 −0
- `A` `src/Alveara.Api/Architecture/Scheduling/VisitBoardService.cs` +59 −0
- `A` `src/Alveara.Api/Architecture/Scheduling/VisitOccupancy.cs` +34 −0
- `A` `src/Alveara.Api/Architecture/Scheduling/VisitStateMachine.cs` +86 −0
- `M` `src/Alveara.Api/Controllers/FormsController.cs` +14 −1
- `A` `src/Alveara.Api/Controllers/VisitsController.cs` +94 −0
- `M` `src/Alveara.Api/Data/AlveraDbContext.cs` +9 −1
- `A` `src/Alveara.Api/Migrations/20261002182306_AddVisitWorkflow.Designer.cs` +1747 −0
- `A` `src/Alveara.Api/Migrations/20261002182306_AddVisitWorkflow.cs` +121 −0
- `M` `src/Alveara.Api/Migrations/AlveraDbContextModelSnapshot.cs` +28 −1
- `M` `src/Alveara.Api/Program.cs` +3 −0

## Backend tests (src/Alveara.Api.Tests) (11)

- `A` `src/Alveara.Api.Tests/CheckInReadinessTests.cs` +261 −0
- `A` `src/Alveara.Api.Tests/SchedulingApiHarness.cs` +65 −0
- `M` `src/Alveara.Api.Tests/SchedulingTestSupport.cs` +26 −0
- `A` `src/Alveara.Api.Tests/VisitAssignmentTests.cs` +209 −0
- `A` `src/Alveara.Api.Tests/VisitBoardTests.cs` +209 −0
- `A` `src/Alveara.Api.Tests/VisitOccupancyTests.cs` +129 −0
- `A` `src/Alveara.Api.Tests/VisitPermissionTests.cs` +59 −0
- `A` `src/Alveara.Api.Tests/VisitStateMachineTests.cs` +165 −0
- `A` `src/Alveara.Api.Tests/VisitWorkflowSchemaTests.cs` +112 −0
- `A` `src/Alveara.Api.Tests/VisitWorkflowTests.cs` +218 −0
- `A` `src/Alveara.Api.Tests/VisitsApiTests.cs` +379 −0

## Frontend product (src/alveara-client/src) (16)

- `M` `src/alveara-client/src/App.tsx` +9 −0
- `M` `src/alveara-client/src/app/moduleRegistry.ts` +1 −0
- `M` `src/alveara-client/src/pages/calendar/AppointmentPanel.tsx` +14 −2
- `M` `src/alveara-client/src/pages/calendar/calendarLayout.ts` +6 −1
- `A` `src/alveara-client/src/pages/flow/AssignPanel.tsx` +50 −0
- `A` `src/alveara-client/src/pages/flow/FlowBoard.css` +224 −0
- `A` `src/alveara-client/src/pages/flow/FlowBoardPage.tsx` +214 −0
- `A` `src/alveara-client/src/pages/flow/ReadinessCue.tsx` +26 −0
- `A` `src/alveara-client/src/pages/flow/VisitCardView.tsx` +96 −0
- `A` `src/alveara-client/src/pages/flow/visitLabels.ts` +61 −0
- `A` `src/alveara-client/src/pages/flow/visitTime.ts` +40 −0
- `M` `src/alveara-client/src/pages/forms/FormTemplatesPage.tsx` +1 −1
- `M` `src/alveara-client/src/pages/forms/TemplateEditor.tsx` +23 −1
- `M` `src/alveara-client/src/services/formsApi.ts` +6 −0
- `M` `src/alveara-client/src/services/schedulingApi.ts` +11 −2
- `A` `src/alveara-client/src/services/visitsApi.ts` +51 −0

## Frontend tests and test support (src/alveara-client/src) (6)

- `A` `src/alveara-client/src/FlowBoard.test.tsx` +385 −0
- `M` `src/alveara-client/src/FormTemplates.test.tsx` +23 −0
- `A` `src/alveara-client/src/pages/flow/visitHelpers.test.ts` +110 −0
- `M` `src/alveara-client/src/test/fakeCalendarServer.ts` +3 −3
- `A` `src/alveara-client/src/test/fakeFlowServer.ts` +155 −0
- `M` `src/alveara-client/src/test/fakeFormsServer.ts` +4 −2

## Browser tests and configuration (3)

- `A` `src/alveara-client/e2e/visit-board-real-backend.spec.ts` +509 −0
- `A` `src/alveara-client/playwright.board.config.ts` +24 −0
- `M` `src/alveara-client/playwright.config.ts` +1 −1

## Documentation (3)

- `M` `docs/FORMS_AND_CONSENTS.md` +1 −1
- `M` `docs/SCHEDULING.md` +77 −0
- `M` `docs/testing/REAL_BACKEND_E2E.md` +22 −0

## Progress log (1)

- `M` `PROGRESS.md` +7 −0

## Reading guide

- **STORY-011's product code extended, never forked:** `AppointmentFlowService.cs` keeps its three methods (`CheckInAsync`, `StartTreatmentAsync`, `CompleteAsync`) and now calls one `TransitionAsync` driven by `VisitStateMachine`; `PatientFlow.cs` (`PatientFlowStates`, `PatientFlowRules`) is **not in this commit** and stays as the compatibility oracle. `AppointmentManager.cs` refuses cancel/no-show/reschedule once the patient has arrived (`VisitStates.HasArrived`), resets the flow when a confirmed appointment is cancelled or no-showed.
- **STORY-011's tests: unchanged.** None of `PatientFlowRulesTests.cs`, `PatientFlowLifecycleTests.cs`, `PatientFlowApiTests.cs`, `PatientFlow.test.tsx`, `patient-flow-real-backend.spec.ts` appears in this commit (`git diff 207791d <this commit>` on them is empty). Dependency tests (`ALV-004-C01`, `STORY-004`, the ALV-N010 backend test classes) are not in this commit either. Test-support files changed additively or by widening visibility only: `SchedulingTestSupport.cs`, `fakeCalendarServer.ts` (`private` → `protected` on three helpers), `fakeFormsServer.ts`; `FormTemplates.test.tsx` gained two tests and lost none.
- **Shared lock helper:** `SchedulingGuards.AcquireLocksAsync` now calls one `AcquireLockAsync(db, resource, ct)` (same `sp_getapplock` call, same 10-second timeout, same `503 schedule_busy`), also used by `VisitOccupancy`; booking and rescheduling behave as before (their tests are unchanged and pass).
- **ALV-N010 changes are additive:** one column (`FormTemplate.RequiredAtCheckIn`, default false), one `FormTemplateService` method, two endpoints and a `RequiredAtCheckIn` field on `TemplateView`. No version, form, signed copy or trigger is touched.
- **Migration:** `AddVisitWorkflow` (+ designer) and the updated model snapshot; `Down` fails loudly if a visit is in a new state.
- **Permissions:** two permissions appended to `Permission` (existing ordinals undisturbed), the grants in `PermissionMatrix`, and `RequireAnyPermissionAttribute`.
- Generated/untracked output (`bin/`, `obj/`, `dist/`, `test-results/`, the e2e output folders) is not part of the commit.
