# ALV-N021 A3-R01: next-story impact

- Every new client code that shows an instant must use `time/practiceTime.ts` (`showInstant`, `formatInstant`) and every "today" `sessionToday`; the repository guard refuses anything else outside its six-entry allowlist.
- Every new `datetime-local` field sends plain `...Local` text through `PracticeLocalInput` on the server.
- Test fakes of `GET /api/auth/permissions` use `signedInSession(...)` (src/test or e2e/support).
- The legacy `...Utc` input fields stay accepted alone; their retirement waits for an inventory of their callers (chain, walkthroughs, external).
- ALV-N022: Q-5q (a minimal non-clinical front-desk safety indicator) unchanged from A2.
- A future Windows/WPF client gets the Windows zone id from the server (`PracticeZoneIds.TryWindowsId`); a field for it is added when that client exists.
- Optional cleanup offered to the reviewer: move `calendarLayout.practiceNow` onto the helper module and assert the snapshot zone equals the session zone (inventory row 56).
