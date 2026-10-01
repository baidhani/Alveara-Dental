# ALV-N003 R02 — Parent Regression

**N/A — justified.** `ALV-N003` is a New Production story with no course parent (`parentCourseStory: null`).

Dependency regression, all passing with no pre-existing assertion weakened:

- **`ALV-001-C01` / `ALV-N009`** (auth, RBAC, session UX, navigation): this attempt replaces `<BrowserRouter>` with `createBrowserRouter`/`RouterProvider` (required for route-transition blocking). The *entire* existing route-guard, shell, nav-filtering, session-expiry, disconnected-banner, login/MFA and accessibility suites pass unchanged on the new router (101 pre-existing frontend tests all green before any new test was added), and the real-browser auth/MFA/session/sign-out tests pass. `RequireAuth`/`RequirePermission` are untouched. The navigation guard blocks only a signed-in session, so session expiry/revocation still redirects immediately and clears protected state (tested).
- **`ALV-002-C01`**: the shared audit, concurrency (`ConcurrencySaveGuard`, conflict problem shape), idempotency and lifecycle tests pass; the conflict banner is reused (with a non-submitting button fix) and the audit viewer tests pass.
- **`ALV-N002`**: migration tests including upgrade-from-prior-schema pass with the new migration; clock/time tests pass; the practice time zone is still reused, not duplicated.

Full results: backend 262/262, frontend 113/113, real-browser 11/11.
