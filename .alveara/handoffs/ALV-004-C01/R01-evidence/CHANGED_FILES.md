# ALV-004-C01 R01 — Changed files

Implementation commit `4179fa4aa1249a35b8f9d714216c7994292d7e39` — 36 files. `A` = added, `M` = modified; counts are `+added −removed` lines.

## Backend product (src/Alveara.Api) (13)

- `M` `src/Alveara.Api/Architecture/Scheduling/Appointment.cs` +40 −2
- `A` `src/Alveara.Api/Architecture/Scheduling/AppointmentManager.cs` +232 −0
- `M` `src/Alveara.Api/Architecture/Scheduling/AppointmentScheduler.cs` +38 −81
- `A` `src/Alveara.Api/Architecture/Scheduling/AppointmentViews.cs` +45 −0
- `A` `src/Alveara.Api/Architecture/Scheduling/SchedulingGuards.cs` +57 −0
- `M` `src/Alveara.Api/Architecture/Scheduling/SchedulingModels.cs` +12 −3
- `A` `src/Alveara.Api/Architecture/Scheduling/SchedulingRecorder.cs` +44 −0
- `M` `src/Alveara.Api/Controllers/AppointmentsController.cs` +69 −6
- `M` `src/Alveara.Api/Data/AlveraDbContext.cs` +14 −0
- `A` `src/Alveara.Api/Migrations/20261002140255_AddAppointmentLifecycle.Designer.cs` +1703 −0
- `A` `src/Alveara.Api/Migrations/20261002140255_AddAppointmentLifecycle.cs` +103 −0
- `M` `src/Alveara.Api/Migrations/AlveraDbContextModelSnapshot.cs` +65 −0
- `M` `src/Alveara.Api/Program.cs` +1 −0

## Backend tests (src/Alveara.Api.Tests) (3)

- `A` `src/Alveara.Api.Tests/AppointmentLifecycleApiTests.cs` +315 −0
- `A` `src/Alveara.Api.Tests/AppointmentLifecycleTests.cs` +445 −0
- `M` `src/Alveara.Api.Tests/SchedulingTestSupport.cs` +32 −0

## Frontend product (src/alveara-client/src) (11)

- `M` `src/alveara-client/src/App.tsx` +9 −0
- `M` `src/alveara-client/src/app/moduleRegistry.ts` +1 −0
- `A` `src/alveara-client/src/pages/calendar/AppointmentPanel.tsx` +213 −0
- `A` `src/alveara-client/src/pages/calendar/Calendar.css` +271 −0
- `A` `src/alveara-client/src/pages/calendar/CalendarGrid.tsx` +122 −0
- `A` `src/alveara-client/src/pages/calendar/CalendarPage.tsx` +209 −0
- `A` `src/alveara-client/src/pages/calendar/NewAppointmentPanel.tsx` +99 −0
- `A` `src/alveara-client/src/pages/calendar/PlacementFields.tsx` +46 −0
- `A` `src/alveara-client/src/pages/calendar/calendarLayout.ts` +136 −0
- `A` `src/alveara-client/src/pages/calendar/explainRefusal.ts` +48 −0
- `M` `src/alveara-client/src/services/schedulingApi.ts` +47 −1

## Frontend tests and test support (src/alveara-client/src) (4)

- `A` `src/alveara-client/src/Calendar.test.tsx` +493 −0
- `A` `src/alveara-client/src/pages/calendar/calendarLayout.test.ts` +117 −0
- `A` `src/alveara-client/src/test/fakeCalendarServer.ts` +190 −0
- `M` `src/alveara-client/src/test/fakeScheduleServer.ts` +11 −4

## Browser tests and configuration (3)

- `A` `src/alveara-client/e2e/calendar-real-backend.spec.ts` +437 −0
- `A` `src/alveara-client/playwright.calendar.config.ts` +24 −0
- `M` `src/alveara-client/playwright.config.ts` +1 −1

## Documentation (2)

- `M` `docs/SCHEDULING.md` +62 −0
- `M` `docs/testing/REAL_BACKEND_E2E.md` +23 −0

## Reading guide

- **STORY-004's product files extended, never replaced:** `Appointment.cs` (statuses, notes, cancel reason, `AppointmentEvent`), `AppointmentScheduler.cs` (now uses the shared conflict/lock guard so booking also refuses a patient in two places; notes; a history entry in the same save; `CalendarAsync`; its rejection/measurement helpers moved to `SchedulingRecorder`), `SchedulingModels.cs`, `AppointmentsController.cs` (new endpoints; the existing three are unchanged in contract), `AlveraDbContext.cs`, `Program.cs`, `schedulingApi.ts` (additive exports), `App.tsx`, `moduleRegistry.ts`.
- **STORY-004's tests: unchanged.** `AppointmentSchedulerTests.cs`, `AppointmentsApiTests.cs`, `Schedule.test.tsx` and `schedule-real-backend.spec.ts` are not in this commit (`git diff 99dcc4e 4179fa4` on them is empty). Two of STORY-004's *test-support* files gained additive members only: `SchedulingTestSupport.cs` (a manager factory, reload/history helpers and a settable-clock `TestClock`) and `fakeScheduleServer.ts` (one protected hook so the calendar's fake can reuse its canned-failure queue).
- **Migration:** `AddAppointmentLifecycle` (+ designer) and the updated model snapshot.
- **Shared components of completed stories are NOT modified** (`ConcurrencyConflictBanner.css` etc.): see finding F2 in `R01.md`; the calendar drawer applies the same scoped workaround ALV-003-C01 used.
- Generated/untracked output (`bin/`, `obj/`, `dist/`, `test-results/`) is not part of the commit.
