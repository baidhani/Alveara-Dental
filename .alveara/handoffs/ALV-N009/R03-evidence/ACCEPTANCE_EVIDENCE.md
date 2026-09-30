# ALV-N009 R03 — Acceptance Evidence

| Acceptance criterion | Evidence |
|---|---|
| Navigation is driven by real authorization data/capabilities rather than fake users or hard-coded display assumptions | Unchanged, untouched this attempt — `AppShell.permissionAwareNav.test.tsx`; real-backend Playwright test 7. |
| Unauthorized direct routes/actions are denied server-side | Unchanged — `App.routeGuards.test.tsx`; real-backend Playwright test 7; server-side `[RequirePermission]`. |
| Session expiry/sign-out removes protected application state | **Corrected this attempt (ALV-N009-R02-01, -02).** `AuthControllerPermissionMatrixTests.cs`'s new sliding-expiration test proves an idle session's deadline can no longer be silently extended. `AuthContext.test.tsx`'s two new ordering tests prove a stale, superseded response can no longer resurrect a cleared session. |
| `ALV-N001` shell/layout regression tests remain green | Unchanged — all pass. |
| `STORY-001`/`ALV-001-C01` authentication/RBAC behaviors remain green | Unchanged — full backend suite 179/179 passes; the one touched backend file (`Program.cs`) is purely additive cookie-auth configuration. |

## R02 findings closed this attempt

| Review finding | Correction | Evidence |
|---|---|---|
| ALV-N009-R02-01 (P1): background poll renews the sliding-expiration cookie, defeating idle timeout | `Program.cs`: `options.SlidingExpiration = false` | `AuthControllerPermissionMatrixTests.GET_permissions_does_not_extend_the_session_expiry_on_repeated_calls_SlidingExpiration_is_off` |
| ALV-N009-R02-02 (P1): an older in-flight refresh can restore state after a newer 401/logout | `AuthContext.tsx`: `requestSeqRef` monotonic ordering guard, bumped synchronously by logout() and the reactive-401 handler | `AuthContext.test.tsx`'s two new `R03:` tests (stale-refresh-vs-401, stale-refresh-vs-logout) |
| ALV-N009-R02-03 (P2): packaged status snapshot didn't match the manifest's declared evidence SHA, due to an unnecessary supplemental commit | This attempt uses the documented two-commit sequence exactly (implementation, then evidence) with no supplemental commit | `R03-evidence/GIT_STATE.md` |
