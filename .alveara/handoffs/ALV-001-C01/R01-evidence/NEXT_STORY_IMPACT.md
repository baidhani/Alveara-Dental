# ALV-001-C01 R01 — Next Story Impact

**Next authorized item:** item 6 — `ALV-N009` (Authorization-aware navigation/session UX), which depends on `STORY-001`, `ALV-001-C01`, `ALV-N001`, `ALV-N002` — all now satisfied pending this attempt's review approval.

## What `ALV-N009` can now rely on

- **Real session identity in the client:** `contexts/AuthContext.tsx` already re-derives `{role, permissions[]}` from `GET /api/auth/permissions` on every mount and exposes `hasPermission(permission: string)`. `ALV-N009`'s "authorization-aware navigation" work can consume this context directly rather than building its own session-state plumbing.
- **The permission matrix as the authorization vocabulary:** `moduleRegistry.ts`'s own comment already documents that nav-level permission filtering is `ALV-N009`'s job, consuming the same registry rather than replacing it. `ALV-N009` should extend `ModuleDefinition` with a `requiredPermission?: Permission` field (or equivalent) and filter `AppShell`'s rendered nav links via `useAuth().hasPermission(...)`, matching the same enforcement pattern `AdminUsersPage` already demonstrates for its own actions.
- **`RequirePermissionAttribute` as the server-side enforcement primitive:** any new admin/authorization-aware route should use `[Authorize] [RequirePermission(Permission.X)]`, not a fresh `[Authorize(Roles=...)]` string, to stay consistent with the matrix as the single source of truth.
- **Session-expiry UX pattern established:** `LoginPage`'s `?reason=expired` banner is the pattern to reuse if `ALV-N009` adds a route guard that redirects an unauthenticated/expired session back to `/login`.

## Assumptions `ALV-N009` should not disturb without re-verifying

- `AuthContext`'s "fail closed to signed-out on any error" behavior (network failure, not just a 401, is treated as signed-out) is deliberate — see the comment in `AuthContext.tsx`. If `ALV-N009` adds retry/offline-tolerant session handling, it should preserve this fail-closed default rather than assume signed-in on ambiguous failure.
- The nav registry is currently unconditional (Security Administration included) by explicit, documented pre-`ALV-N009` convention. `ALV-N009` is expected to change this; that is not a regression to flag, it is the point of that story.

## Interfaces/migrations this story adds that other stories will build on

- **`PermissionMatrix`/`Permission` enum** already names clinical (`ViewPatientRecords`, `ManageClinicalNotes`, `ManageTreatmentPlans`), scheduling (`ManageAppointments`, `ViewSchedule`), and billing (`ViewBilling`, `ManageBilling`) permissions with a first-pass per-role assignment, even though no module enforces them yet. Future stories that build those modules (`ALV-N003`/`STORY-003` for patient/scheduling, `ALV-007-C01`/`STORY-007` for billing, etc.) should authorize against these same permissions rather than inventing their own role-check logic — and should treat the current per-role assignment as a first-pass proposal that may need adjustment once real usage patterns are known, not a fixed contract.
- **`UserAccount.SecurityStamp`** is now a first-class revocation mechanism any future story that changes account security state should participate in (bump it whenever a change should invalidate existing sessions), following the existing `BumpSecurityStamp` helper convention in `AccountService.cs`.
- **The `AuthAttempts` rate-limiter policy** (`Program.cs`) is currently applied only to the specific auth-attempt endpoints this story owns. A future story adding another unauthenticated, abuse-prone endpoint should consider reusing the same named policy rather than defining a new one.

## Prompt changes relevant to the next scheduled item

None identified that require the Execution Plan document itself to change before `ALV-N009` begins.
