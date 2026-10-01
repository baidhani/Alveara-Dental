# ALV-N003 R01 — Demo Evidence

## The visible workflow, against the real API and a real browser

Playwright test 8 ("practice configuration: set up the practice end to end, see it in the scheduling preview, and find it in the audit log"), run in real Chromium against a real `Alveara.Api` process and a freshly migrated database, as the bootstrapped admin:

1. `/admin/configuration` renders the **Practice configuration** hub; the Practice tab shows **Time zone = America/Chicago** and **Currency = USD** read-only (reflected from the deployment, not editable).
2. Saves the practice name ("Practice information saved."), then adds the single location "Main Office".
3. Appointment types: adds "E2E Exam", 45 minutes → the table shows "45 min".
4. Operatories: adds "E2E Op 1". Staff: adds "Dr. E2E" (Dentist). Providers: picks "Dr. E2E" from the staff picker, specialty "General dentistry".
5. Availability: selects the provider, adds a window (Monday 09:00–17:00), saves ("Weekly availability saved.").
6. **Scheduling preview** shows "Active location: Main Office - times in America/Chicago", Dr. E2E with "Monday 09:00-17:00", the operatory, and "E2E Exam - 45 min" — read from the same read model scheduling will consume.
7. Operatories: **Inactivates** "E2E Op 1" ("Inactivated the operatory."), "Show inactive" lists it as Inactive, and the preview no longer offers it while the appointment type remains.
8. `/admin/audit-log` shows the real `ConfigurationCreated` (Operatory), `ConfigurationInactivated` (Operatory) and `ProviderAvailabilityReplaced` entries with their entity types.

Test 9 (extended) confirms a Dentist-role account never sees the **Practice Configuration** nav link and is shown permission-denied at `/admin/configuration`.

## States and protections demonstrated by component tests

- **Loading / empty / error (with Retry) / permission-denied** per panel, and a "nothing is schedulable yet" preview state — none shows invented data.
- **Inline validation** (required, length, whole-number, range, step-of-5; availability empty/inverted/overlapping windows) blocks submission without a server call.
- **Unsaved-change protection:** a "Unsaved changes" indicator; cancel, row switch and tab switch ask before discarding (declining keeps the typed text); the browser `beforeunload` prompt arms while dirty.
- **Stale edit:** a 409 concurrency conflict renders the shared `ConcurrencyConflictBanner`; "Reload current version" refetches and replaces the form with the current server values.
- **Server errors** (duplicate name, DST-gap blocked time, etc.) are shown inline with the form kept open.
- **Accessibility:** axe reports no violations on the practice tab, an open edit form, and the availability editor.

## Full regression

Backend 252/252; frontend 101/101 (`tsc`/build/lint clean); real-browser 10/10. See `artifacts/playwright-real-backend/run-output.txt`.
