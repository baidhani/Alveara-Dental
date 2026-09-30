# ALV-N009 R01 — Changed Files

`git diff --stat` for implementation commit `1127f01bf36fcc4e60cd83627d51d5878d854314`: 18 files changed, 887 insertions(+), 47 deletions(-).

## Backend — production code

| File | Change |
|---|---|
| `Architecture/Identity/AccountService.cs` | `ConfirmMfaEnrollmentAsync` now returns the updated `UserAccount` instead of `void`, so the controller can re-issue the caller's own cookie after confirmation |
| `Controllers/AuthController.cs` | `GET /api/auth/permissions` also returns `username` and `sessionExpiresAtUtc`; `mfa/confirm` re-signs-in with the fresh account state after confirming (fixes the session-continuity bug) |

## Backend — tests

| File | Change |
|---|---|
| `AuthControllerMfaConfirmSessionTests.cs` | New: 2 real-HTTP tests proving the confirming caller's own session survives both first-enrollment and replacement-factor MFA confirmation |
| `AuthControllerPermissionMatrixTests.cs` | 1 new test: `GET /api/auth/permissions` returns a non-empty `username` and a future `sessionExpiresAtUtc` |

## Frontend — production code

| File | Change |
|---|---|
| `app/RouteGuards.tsx` | New: `RequireAuth`, `RequirePermission` |
| `app/moduleRegistry.ts` | `ModuleDefinition.requiredPermission` (optional); set on the two admin entries |
| `app/AppShell.tsx` | Nav filtered by `hasPermission`; account identity/sign-out block; session-expiring-soon notice; `DisconnectedBanner` removed (moved to `App.tsx`) |
| `app/AppShell.css` | Styles for the new account/sign-out/session-warning elements |
| `App.tsx` | `RequireAuth`/`RequirePermission` wired into the route tree; global `DisconnectedBanner` added above all routes |
| `contexts/AuthContext.tsx` | Defensive `hasPermission`; `sessionExpiringSoon`; reactive 401 handling via `setUnauthorizedHandler`; `username`/`sessionExpiresAtUtc` in signed-in state |
| `services/authApi.ts` | `MyPermissions` extended; `setUnauthorizedHandler` export |
| `pages/LoginPage.tsx` | Returns the caller to the `from` location captured by `RequireAuth` after a successful sign-in |

## Frontend — tests

| File | Change |
|---|---|
| `app/AppShell.permissionAwareNav.test.tsx` | New: nav filtering, identity display, sign-out |
| `App.routeGuards.test.tsx` | New: signed-out denial, permission-denied rendering, permitted access, post-login return-to-intended-page |
| `contexts/AuthContext.test.tsx` | New: defensive `hasPermission`, session-expiry-soon timing, reactive 401, logout clearing |
| `App.disconnected.test.tsx` | Updated for the relocated connectivity banner (tolerant of the login page's own session-expired alert appearing alongside it under the same root-cause outage) |
| `app/AppShell.emptyRegistry.test.tsx` | Updated to wrap `AuthProvider` (new dependency) |

## Frontend — real-backend/real-browser

| File | Change |
|---|---|
| `e2e/auth-real-backend.spec.ts` | 2 new tests: permission-aware nav/direct-URL denial for a limited-permission role; account-menu identity + sign-out flow, against the real API |
