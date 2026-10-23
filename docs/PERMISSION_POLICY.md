# Effective permission policy and authorization safety (ALV-N017)

Design authority: `docs/design/ADMIN_GRANTABLE_PERMISSIONS_PROPOSAL.md` revision R04 (approved). This story builds the engine and the safety controls only. It adds **no policy-write API**, **no editable matrix** and **no delegation screen**, and it changes **no ordinary access** on deployment (ALV-N018 adds administration).

## What changed

* **One seam.** Every permission decision goes through `IEffectivePermissions` (`Architecture/Permissions/EffectivePermissions.cs`): the 278 `[RequirePermission]` uses and the one `[RequireAnyPermission]` through the two attributes, the direct controller checks (`ClinicalNotesController`, `FormsController`, `VisitsController`, `NotificationsController`), the notification consumer, and the two permission endpoints of `AuthController`. The static `PermissionMatrix` is now only the **code default**; an architecture test (`AuthorizationArchitectureTests`) fails if any file other than the matrix and `PolicySnapshotBuilder` reads it.
* **Code defaults plus stored changes.** `Eff(role) = defaults + valid grants - valid revokes`. With no stored rows every (role, permission) cell equals the default (the proof is generated from the enums, no count is written in the test). The administrator always holds every permission and an unassigned account none, whatever a row says (neither needs the store).
* **New permission** `ManagePermissionGrants` (administrator only). The **protected** permissions `ManageUsers`, `ManageRoles`, `ManageMfaPolicy`, `IssuePasswordResets` and `ManagePermissionGrants` can never be granted to another role or delegated; `ManageAccountStatus` and `RevokeSessions` may be granted by an administrator but never delegated (`PermissionProtection`).
* **Permission registry.** `PermissionRegistry.json` (embedded) lists every permission name ever shipped as Active or Retired; `PermissionRegistryTests` fails if the enum and the registry disagree, so a permission cannot be removed or renamed silently.

## The tables (migration `AddPermissionPolicy`, error codes 51204 to 51206)

`RolePermissionOverrides` (role and permission as **names**, effect Grant or Revoke, reason, who, when, row version; unique per role and permission), `RolePermissionHistories` (immutable: trigger 51204), `PermissionDelegations` (default: the office manager may grant or revoke `ImportDocuments` for the five other working roles), `PermissionPolicyState` (one row; its version moves forward by exactly one: trigger 51205). Role and permission values are deliberately not constrained by the database so that wrong data is representable and the engine can be proven to refuse it. The migration rolls back only while no history row exists (51206).

## How a decision is made (`PermissionPolicyStore`)

1. Read the policy version (one scalar read; once per request, however many checks the request makes).
2. Equal to the current snapshot's version: decide on that immutable snapshot.
3. Different: wait for **one** shared reload (single flight, at most 2 s), then decide on the **new** snapshot, never the old one. The reload reads the version, the rows, the version again and repeats (at most 3 attempts, with a short pause) if a writer got in between, then publishes the snapshot with one atomic swap.
4. Store unreadable: the last snapshot serves for at most **60 seconds counted from the last confirmation that it was current**; after that, or with no snapshot, the answer is **503 `permission_policy_unavailable`**. The defaults are never used while stored rows may exist.

Refusals: `permission_denied` (403, the body callers already know), `permission_policy_repair_required` (403), `permission_policy_degraded` (503), `permission_policy_unavailable` (503). A reload that races more than three consecutive writers is treated like an unreadable store (fail closed).

## Invalid stored data never restores access

An override naming a known non-administrator role but an unknown permission, an unknown effect or a protected permission puts **that role in repair**: every check for it is denied. A row whose role cannot be attributed **degrades the store**: every non-administrator check is denied while the administrator keeps working. A row for the administrator or an unassigned account changes nothing and is reported. A row naming a retired permission is ignored and removable. An invalid delegation is disabled. Each invalid row is reported **once per row version** (audit `PermissionPolicyInvalidRowDetected`, ids and a reason code only) and a privacy-safe log line carries counts only.

## Endpoints (`PermissionsController`)

`GET /api/permissions/effective` (`ViewPermissionMatrix`: every cell with source default, granted or revoked, who, when, why, whether it can be granted or delegated, and the health), `GET /api/permissions/history` (`ViewAuditLog`), `GET /api/permissions/health` and `POST /api/permissions/invalid-rows/{id}/remove` (`ManagePermissionGrants`, CSRF, a reason of up to 500 characters and the row version you were shown). Removing an invalid (or retired) row bumps the version, deletes the row, writes its history row and its audit entry in **one transaction**; a failure of any part changes nothing (`policy_write_failed`, 503). A valid row cannot be removed here (`row_not_removable`, 409); a changed row is `row_changed` (409); an unknown row is `row_not_found` (404).

`GET /api/auth/users` keeps its current authorization and response for every role.

## Account safety (`AccountService`, `AccountSafety.cs`)

* **At least one enabled administrator always remains.** Inside the operation's transaction: a transaction-scoped application lock `alveara.admin-availability` (bounded wait 5 s), a count of the available administrators excluding the one being removed (`READCOMMITTEDLOCK`), and a refusal (`last_admin`, 409) when none would remain; a busy lock is `admin_guard_busy` (503) and changes nothing. The database backstop is the trigger `TR_UserAccounts_LastAdmin` (51207, busy 51208), which takes the same lock, so raw SQL and bulk updates cannot break the rule. Role and enabled state are written only by `AccountService` (architecture test).
* **Target rules** for a non-administrator who holds `ManageAccountStatus` or `RevokeSessions` (nobody does by default): never an administrator account (`target_is_admin`); only when `Eff(target role)` is a subset of `Eff(actor role)` (`target_exceeds_actor`); no self-disable (`self_disable`). Refusals are 403 `account_target_not_allowed` with the rule, change nothing and are audited (`AccountActionRefused`, ids and the rule only); allowed actions by a non-administrator carry the actor's role in the audit entry.

## Failure and recovery

Store outage, corrupt rows, a writer racing a reader, two hosts and an audit failure during the repair are covered by tests (see Verification). Not handled: a compromised administrator account (the account-security layer's job); delegated password reset and role assignment (deliberately out of this release).

## Verification

`PermissionRegistryTests`, `PermissionPolicySchemaTests`, `PolicySnapshotBuilderTests`, `PermissionPolicyStoreTests`, `EffectivePermissionServiceTests`, `PermissionPolicyPipelineTests`, `PermissionPolicyHealthTests`, `PermissionPolicyTwoHostTests`, `PermissionPolicyPerformanceTests` (read counts on every run; timings with `ALVERA_PERF_EVIDENCE=<folder>`), `PermissionPolicyBackupRestoreTests`, `AccountSafetyTests`, `AuthorizationArchitectureTests`; walkthrough `e2e/permission-policy-real-backend.spec.ts` (`playwright.permission-policy.config.ts`).
