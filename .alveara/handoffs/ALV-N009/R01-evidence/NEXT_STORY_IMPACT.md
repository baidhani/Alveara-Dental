# ALV-N009 R01 — Next Story Impact

## New for future stories to build on

- **`ModuleDefinition.requiredPermission` is the established contract for gating a nav entry.** Any future module registry entry that should be permission-restricted adds this one field; no module should invent its own visibility logic. `RequirePermission` (route-level) should wrap the corresponding `<Route>` element the same way, mirroring the nav's own `requiredPermission` - the two are meant to stay in sync per entry.
- **`RequireAuth`/`RequirePermission` (`app/RouteGuards.tsx`) are the established pattern for "this route needs a session" / "this route needs a specific permission."** Future routes needing authorization should be wrapped with these, not given a bespoke per-page redirect (the exact duplication this story centralized away from `MfaSettingsPage`'s prior ad hoc `Navigate` usage).
- **`AuthContext.sessionExpiringSoon` and the `setUnauthorizedHandler` reactive-401 pattern are the established session-UX primitives.** Any future authenticated flow that makes its own direct `fetch`/`authApi.ts` calls automatically benefits from reactive sign-out on a 401 without extra wiring, since the hook lives in `authApi.ts`'s shared `request()`.
- **A `SignInAsync`-rotating operation must re-issue the cookie for the caller performing it, not just the other invalidated sessions.** `ConfirmMfaEnrollmentAsync`'s fix (returning the updated account so the controller can re-sign-in) is the template: any future endpoint that rotates `SecurityStamp` as a side effect of an authenticated caller's own action must do the same, or that caller will be silently signed out by their own request.
- **`GET /api/auth/permissions`'s response shape (`username`, `role`, `permissions`, `sessionExpiresAtUtc`) is now the account-identity/session contract the client relies on.** A future change to this shape should update `authApi.ts`'s `MyPermissions` type and `AuthContext`'s consumption of it together, not just the backend.

## Prompt changes relevant to the next scheduled item

None identified that require the Execution Plan document itself to change before item 7 (`STORY-002`, a course portal story) begins.
