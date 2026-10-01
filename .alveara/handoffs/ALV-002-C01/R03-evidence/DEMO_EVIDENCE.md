# ALV-002-C01 R03 — Demo Evidence

## The audit viewer, live, with real metadata

Real-backend/real-browser Playwright test 7 navigates to `/admin/audit-log` as the real admin session, asserts the `Audit log` heading, the genuinely-produced `SessionTimeoutChanged` and `LoginSucceeded` events, and — new this attempt — that the real `SessionTimeoutChanged` row visibly contains `UserAccount`. That value reaches the browser only because the real API now serializes the shared `EntityType`; before this attempt the same row rendered a dash. No permission-denied text appears for this caller. Test 8 (unchanged) still shows a Dentist-role account neither sees the "Audit Log" nav link nor can open `/admin/audit-log`.

## The request/description contract

`AuditLogPage.test.tsx` asserts the fetch URL contains `take=500` and the page text says "most recent 500". The backend window test stages 130 events and shows the default request returns 100, `take=500` returns all 130, and `take=9999` is clamped to at most 500 — the exact gap the R02 review identified.

## Full regression

- Backend: 199/199.
- Frontend: 70/70, `tsc`/build/lint clean.
- Real-backend, real-browser: 9/9 against a freshly dropped/re-migrated `AlveraE2E` database and a real `dotnet run` API built from the implementation commit. See `artifacts/playwright-real-backend/run-output.txt`.
