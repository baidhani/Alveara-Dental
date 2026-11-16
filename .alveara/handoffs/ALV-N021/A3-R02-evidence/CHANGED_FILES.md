# ALV-N021 A3-R02: changed files (git diff a7bdacc..6f0f128)

R01 implementation `b8756f1`; then documentation commits (R01 package `95f97a8`, `083cad5`; review record `a7bdacc`). R02 implementation `6f0f128` is one commit containing only CR-1 and CR-2 and their tests.

```
 PROGRESS.md                                        |  6 ++
 src/alveara-client/src/Calendar.test.tsx           | 26 ++++++++
 src/alveara-client/src/FlowBoard.test.tsx          | 25 ++++++-
 .../src/pages/billing/BillingDocumentView.test.tsx | 77 ++++++++++++++++++++++
 .../src/pages/calendar/CalendarPage.tsx            | 12 ++--
 .../src/pages/calendar/calendarLayout.test.ts      | 11 ++--
 .../src/pages/calendar/calendarLayout.ts           | 10 +--
 .../src/pages/flow/FlowBoardPage.tsx               | 11 ++--
 src/alveara-client/src/test/fakeCalendarServer.ts  |  4 +-
 src/alveara-client/src/test/fakeFlowServer.ts      |  4 +-
 src/alveara-client/src/test/fakePatientServer.ts   |  4 +-
 src/alveara-client/src/time/practiceTime.ts        |  3 +
 tests/practice-time-guard.test.mjs                 | 10 +--
 13 files changed, 171 insertions(+), 32 deletions(-)
```

| Status | Path |
|---|---|
| M | `PROGRESS.md` |
| M | `src/alveara-client/src/Calendar.test.tsx` |
| M | `src/alveara-client/src/FlowBoard.test.tsx` |
| A | `src/alveara-client/src/pages/billing/BillingDocumentView.test.tsx` |
| M | `src/alveara-client/src/pages/calendar/CalendarPage.tsx` |
| M | `src/alveara-client/src/pages/calendar/calendarLayout.test.ts` |
| M | `src/alveara-client/src/pages/calendar/calendarLayout.ts` |
| M | `src/alveara-client/src/pages/flow/FlowBoardPage.tsx` |
| M | `src/alveara-client/src/test/fakeCalendarServer.ts` |
| M | `src/alveara-client/src/test/fakeFlowServer.ts` |
| M | `src/alveara-client/src/test/fakePatientServer.ts` |
| M | `src/alveara-client/src/time/practiceTime.ts` |
| M | `tests/practice-time-guard.test.mjs` |

Source changes since the R01 implementation (`git diff --stat b8756f1..6f0f128 -- src tests`):

```
 src/alveara-client/src/Calendar.test.tsx           | 26 ++++++++
 src/alveara-client/src/FlowBoard.test.tsx          | 25 ++++++-
 .../src/pages/billing/BillingDocumentView.test.tsx | 77 ++++++++++++++++++++++
 .../src/pages/calendar/CalendarPage.tsx            | 12 ++--
 .../src/pages/calendar/calendarLayout.test.ts      | 11 ++--
 .../src/pages/calendar/calendarLayout.ts           | 10 +--
 .../src/pages/flow/FlowBoardPage.tsx               | 11 ++--
 src/alveara-client/src/test/fakeCalendarServer.ts  |  4 +-
 src/alveara-client/src/test/fakeFlowServer.ts      |  4 +-
 src/alveara-client/src/test/fakePatientServer.ts   |  4 +-
 src/alveara-client/src/time/practiceTime.ts        |  3 +
 tests/practice-time-guard.test.mjs                 | 10 +--
 12 files changed, 165 insertions(+), 32 deletions(-)
```
