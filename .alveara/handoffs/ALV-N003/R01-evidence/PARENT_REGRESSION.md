# ALV-N003 R01 — Parent Regression

**N/A — justified.** `ALV-N003` is a New Production story with no course parent (`parentCourseStory: null`); there are no parent "Done Means" to map.

Dependency regression (the stories this one builds on) was nevertheless run in full and passes with no pre-existing assertion changed:

- **`ALV-001-C01`** (auth/MFA/RBAC): the new permission was appended to the `Permission` enum and granted to Admin (which receives every permission) and OfficeManager only; `PermissionMatrixTests`, `AuthControllerPermissionMatrixTests`, `AuthControllerRbacTests` and all MFA/lockout/session tests pass. A new test asserts no other role holds it.
- **`ALV-002-C01`** (audit/concurrency/idempotency/lifecycle): `AuditService`, `ConcurrencySaveGuard`, `ConcurrencyConflictBanner` are reused unchanged; `AuditLogImmutabilityTests`, `ConcurrencyGuardTests` (`StaffProfile` rowversion), `IdempotencyGuardTests`, and the audit viewer tests pass. The new `StaffProfile` columns/indexes did not disturb `ConcurrencyGuardTests`/`TransactionRollbackTests`, which seed staff directly.
- **`ALV-N002`** (architecture): migration tests (`MigrationTests`, `MigrationUpgradeTests` — the new migration applies on top of the prior schema), `PracticeClockTests`, `MoneyTests`, topology/least-privilege/PHI-safe-logging tests pass; the practice time zone/currency are *reused* from there, not duplicated.
- **`ALV-N009`** (navigation/session UX): the new route follows the same `RequirePermission` pattern; the existing route-guard, nav-filtering and session tests pass; the real-browser limited-permission test was extended (not weakened) to cover the new nav link/route.

Full results: backend 252/252, frontend 101/101, real-browser 10/10.
