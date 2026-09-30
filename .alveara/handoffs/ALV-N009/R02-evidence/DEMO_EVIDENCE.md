# ALV-N009 R02 — Demo Evidence

## Corrected behaviors, demonstrated

### 1. Passing the session expiry now ends the signed-in state (ALV-N009-R01-01)

Reproduced the R01 reviewer's own fake-timer probe as a permanent regression test (`AuthContext.test.tsx`, `R02: crosses the authoritative session expiry and unmounts protected state`): a session with a 30-second expiry, advanced 45 seconds with no other API call in flight. Before this attempt: state stayed `signed-in`. After: `refresh()` fires at the boundary, the mocked server reports the cookie as genuinely expired, and state transitions to `signed-out` — which `RequireAuth` turns into an immediate redirect, unmounting any protected page that was on screen.

### 2. Signed-in sessions revalidate without waiting for an unrelated 401 (ALV-N009-R01-02)

New test (`AuthContext.test.tsx`, `R02: revalidates a signed-in session on a bounded poll...`): a caller starts signed in with `ManageUsers`; the mocked server is then reprogrammed (simulating a role change by another admin) to 401 every subsequent call. With no navigation, no focus event, and no unrelated protected call — just 30 seconds of elapsed time — the 30-second bounded poll fires `refresh()` on its own, the 401 is caught, and state clears to `signed-out`.

### 3. MFA deep-link redirect survives the extra hop (ALV-N009-R01-03)

New test (`App.routeGuards.test.tsx`, `R02: returns a caller to the page they were denied even when reauthentication requires MFA`): a direct visit to `/admin/permissions` while signed out redirects to `/login`; signing in returns `mfaRequired`; the challenge screen now carries the captured destination through its own `location.state`; completing the challenge lands the caller on `/admin/permissions` (asserted via the `Permission matrix` heading), not the dashboard.

## Real-backend, real-browser regression

8/8 passing, unchanged in assertion from R01, rerun against a freshly dropped/re-migrated `AlveraE2E` LocalDB database and a real `dotnet run` API process. See `artifacts/playwright-real-backend/run-output.txt`. No incidental findings this run — the corrections were entirely client-side and none of the eight flows exercise the 30-second poll or expiry boundary within their own runtime (each completes in well under 2 seconds), so this run is a straightforward confirm-no-regression pass, not a source of new coverage for the three corrections themselves (those are covered deterministically via Vitest fake timers instead, see `TEST_RESULTS.md`).
