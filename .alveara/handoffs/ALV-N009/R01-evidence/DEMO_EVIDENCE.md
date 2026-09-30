# ALV-N009 R01 — Demo Evidence

Primary demo evidence is a real Chromium browser, running the actual built React application, against a real `Alveara.Api` process and a real (freshly migrated) LocalDB database — extended this attempt with two new tests covering the story's own core workflow.

## Result

```
Running 8 tests using 1 worker

  ok 1 … an unauthenticated caller cannot select a role at registration (272ms)
  ok 2 … bootstraps the first admin via the real API and signs in (788ms)
  ok 3 … enrolls a real TOTP factor and completes an MFA challenge end to end (1.5s)
  ok 4 … replaying the exact same successful MFA challenge submission is rejected, not accepted twice (70ms)
  ok 5 … security administration: list users, view detail, change role, set session timeout (402ms)
  ok 6 … permission matrix is visible to the admin and shows every role (123ms)
  ok 7 … a caller without ManageUsers/ViewPermissionMatrix never sees those nav links and is denied the pages directly, against the real API (483ms)
  ok 8 … the account menu shows the signed-in identity, and sign-out clears the session so protected routes are denied again (225ms)

  8 passed (6.1s)
```

Full raw output: `artifacts/playwright-real-backend/run-output.txt`.

## What the two new tests demonstrate, concretely

**Test 7** — via the real admin session already established in tests 1–6, creates a genuine new user through the real `/api/auth/register`, `/api/auth/{id}/role`, and `/api/auth/{id}/enabled` endpoints, assigns it the `Dentist` role (holding neither `ManageUsers` nor `ViewPermissionMatrix`), signs in as that user through the real rendered login form, and confirms:
- Neither "Security Administration" nor "Permission Matrix" appears in the rendered nav.
- A direct URL to `/admin/users` or `/admin/permissions` shows the `PermissionDenied` state, not the protected page.

**Test 8** — as the same Dentist session, confirms the sidebar shows "Dentist" (the real role from the server), clicks the real "Sign out" button, lands on the sign-in screen, then navigates directly to `/admin/users` and confirms the session-expired sign-in screen appears again (not the admin page) — proving sign-out genuinely clears protected access, not just the visible nav.

## An incidental finding this run caught

The first time this suite was run against this attempt's code, test 3 (MFA enrollment) failed: confirming MFA enrollment immediately redirected to a session-expired sign-in screen instead of showing the "MFA is now active" success message. This was a genuine backend bug (`mfa/confirm` rotating `SecurityStamp` without re-issuing the confirming caller's own cookie - see `R01.md`'s "genuine bug" section), invisible before this story added a router-level guard that actually reacts to the resulting stale session. Fixed in the same implementation commit; this run reflects the fix.

## Disposable artifacts

The `AlveraE2E` LocalDB database, its throwaway admin and Dentist accounts, and the local API/Vite dev-server processes used to generate this evidence are disposable session-local artifacts — not committed, contain no real data. The database was dropped and the API process stopped immediately after this run.
