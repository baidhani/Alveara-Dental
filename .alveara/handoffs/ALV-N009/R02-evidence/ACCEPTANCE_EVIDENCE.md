# ALV-N009 R02 — Acceptance Evidence

| Acceptance criterion | Evidence |
|---|---|
| Navigation is driven by real authorization data/capabilities rather than fake users or hard-coded display assumptions | Unchanged from R01, untouched this attempt — `AppShell.permissionAwareNav.test.tsx`; real-backend Playwright test 7. |
| Unauthorized direct routes/actions are denied server-side | Unchanged from R01 — `App.routeGuards.test.tsx`; real-backend Playwright test 7; server-side `[RequirePermission]`. |
| Session expiry/sign-out removes protected application state | **Corrected this attempt (ALV-N009-R01-01).** `AuthContext.test.tsx`'s new `R02: crosses the authoritative session expiry and unmounts protected state` test proves the transition the R01 review's fake-timer probe found missing. Reactive-401 and logout tests from R01 continue to pass. Real-backend Playwright test 8 continues to pass. |
| `ALV-N001` shell/layout regression tests remain green | Unchanged — `App.accessibility.test.tsx`, `App.keyboard.test.tsx`, `App.responsive.test.tsx`, `App.disconnected.test.tsx`, `AppShell.emptyRegistry.test.tsx` all pass. |
| `STORY-001`/`ALV-001-C01` authentication/RBAC behaviors remain green | Unchanged — full backend suite 178/178 passes unmodified (no backend file touched this attempt). |

## Additional failure paths corrected (review findings beyond the acceptance table)

| Review finding | Correction | Evidence |
|---|---|---|
| ALV-N009-R01-02 (P1): permission/role changes not revalidated during a signed-in visit | `AuthContext.tsx` now revalidates on a 30s bounded poll plus window focus/visibilitychange while signed in | `AuthContext.test.tsx`'s new `R02: revalidates a signed-in session on a bounded poll...` test |
| ALV-N009-R01-03 (P2): deep-link destination lost when reauthentication requires MFA | `LoginPage.tsx` passes `redirectTo` through `/mfa-challenge` state; `MfaChallengePage.tsx` navigates there after success | `App.routeGuards.test.tsx`'s new `R02: returns a caller to the page they were denied even when reauthentication requires MFA` test |
