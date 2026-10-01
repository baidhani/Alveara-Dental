# ALV-N003 R02 — Demo Evidence

R01's end-to-end configuration demonstration (real Chromium, real API/database: practice, location, appointment type, operatory, staff, provider, weekly hours → scheduling preview → inactivation → audit viewer) is unchanged and passes against the R02 code, now exercising the schedule revision on the real API (the availability save round-trips `revision`).

## New this attempt (real browser, real dialog)

Playwright test 9 — "unsaved configuration edits survive an attempt to leave through the main navigation": on `/admin/configuration` the saved practice name is edited without saving; clicking **Dashboard** in the primary navigation raises the real browser dialog ("You have unsaved changes. Discard them?"); **dismissing** it leaves the URL on `/admin/configuration` and the typed text in place; clicking again and **accepting** navigates to the Dashboard.

## States demonstrated by component tests

- **Provider switching:** the previous provider's editor disappears immediately and "Loading schedule…" shows; a late response for a provider no longer selected changes nothing; saving always targets the provider whose rows are on screen at that provider's revision.
- **Stale schedule save:** the shared "Someone else changed this while you were editing" banner; **Reload current version** replaces the draft only if the reload succeeds; if the network fails the draft and banner stay with a retryable message, and the retry then shows the other editor's current hours.
- **Navigation:** shell link, browser back and sign-out each ask before discarding; declining keeps the page and text; a revoked session (401) redirects straight to sign-in with no prompt and no draft retained.
- **Scheduling consumer:** a slot straddling the fall-back hour is reported unavailable with the reason `crosses_dst_transition` (the reviewer's exact slot), while an ordinary slot in the same window the week before is available.

## Full regression

Backend 262/262; frontend 113/113 (`tsc`/build/lint clean); real-browser 11/11. See `artifacts/playwright-real-backend/run-output.txt`.
