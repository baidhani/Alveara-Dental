# Admin-Grantable Permissions — Story Proposal for Independent Review (revision R04)

**Proposed work:** two new high-risk engineering stories on the authorization layer, placed after `ALV-N015` and before `STORY-009`, as the reviewer approved. Story identifiers and the exact Execution Plan wording remain the reviewer's.
**Type:** New production stories (no course parent). Each is **high-risk** (authentication/authorization primitive; migrations/schema) and takes the two-stage review with a full formal Stage 2.
**Document status:** REVISED FOR REVIEW (R04). **Do not implement. The Execution Plan is not amended and no story control record exists.** The proposal stops at `AWAITING_REVIEW`.
**Prepared:** 2026-10-22, session CC-20261008-k4q9. **Repository state when written:** `main` at `f67ff49` (local; `origin/main` is `4147fa6`). `ALV-N015` (item 42) is next and not started.
**Raised by:** the project owner, 2026-10-20 (`docs/DOCUMENTS.md`, "Deferred: admin-grantable permissions", open point 8).

**Revision history.** R01 (`3a9b6ce`): design direction approved with required changes (`docs/design/ADMIN_GRANTABLE_PERMISSIONS_REVIEW_RESPONSE.md`). R02 (`254b3f3`): incorporated those changes. R03 (`f67ff49`): added the owner's request to make four security permissions Admin-assignable under a security-permission-only target rule; **`CHANGES_REQUIRED`** (`docs/design/ADMIN_GRANTABLE_PERMISSIONS_R03_REVIEW_RESPONSE.md`) because that rule is unsafe. R04 (this document) incorporates that response.

**How to respond.** Section 10 lists the points still open (R1 to R7 carried from R02, R8 new). Reply APPROVE, APPROVE WITH CHANGE (say what), or REJECT (say what to do instead).

---

## 0. What changed in R04 (the reviewer's R03 response, point by point)

| Reviewer requirement | Where it is addressed |
|---|---|
| Remove the R03 target rule `S ∩ Eff(Target) ⊆ Eff(Actor)` and its security-target decision options (R8 to R12 of R03) | **Removed everywhere.** Section 3.3A now has the full-subset rule only; the old open points are gone; section 6 records why the old rule was rejected |
| Permanently Admin-only, non-grantable, non-delegable for the first release: `ManageUsers`, `ManageRoles`, `ManageMfaPolicy`, `IssuePasswordResets`, `ManagePermissionGrants` | Section 3.3 |
| `ManageAccountStatus` and `RevokeSessions` may be Admin-assignable but never delegable, only if every non-Admin action applies to an account whose **full** effective permission set is a subset of the actor's: `Eff(Target) ⊆ Eff(Actor)`; no non-Admin may act on an Admin; self-disable, self-role-change and equivalent self-escalation refused | Sections 3.3 and 3.3A |
| A future HR workflow that resets passwords for broader staff needs a separate design (target-delivery recovery, verified recovery identity, separate security review) and must not give a non-Admin a reusable token | Section 3.3B (not in this release) |
| Office manager delegation exactly as approved: only `ImportDocuments`, only for Dentist, Hygienist, Assistant, FrontDesk, Billing; never Admin, Unassigned, OfficeManager, protected permissions or delegation administration | Section 3.8 |
| `ManagePermissionGrants` makes the enum 36 permissions; the no-overrides proof is **8 × 36 = 288 cells**, preferably generated from the enum | Section 3.5 (the proof), 3.7 (dataset), Section 4 and Appendix A; the count is derived from `Enum.GetValues`, never hard-coded |
| Story 1 must not change the authorization contract of `GET /api/auth/users`; the broader user-list authorization and target-filtered listing belong to story 2; story 1 may enforce the target rules inside the existing account-administration operations and test them through controlled API/test setup | Sections 3.3A, 3.9, 4 and Appendix A |
| Update every acceptance criterion, negative control, endpoint description, invariant, UI statement, test matrix and open-decision table | Done throughout; sections 4, 10 and Appendix A in particular |
| Preserve the accepted cache, last-Admin, corrupt-row, backup/restore, p95, full Stage 2 and two-story controls | Sections 3.4 to 3.7 and 4 are unchanged in substance (counts updated) |

**What this means for the owner's request.** The owner asked that an Admin could assign `ManageRoles`, `ManageAccountStatus`, `IssuePasswordResets` and `RevokeSessions` to other roles. After the reviewer's response: **`ManageAccountStatus` and `RevokeSessions` can be assigned** (Admin only, under the full-subset rule). **`ManageRoles` and `IssuePasswordResets` stay Admin-only in the first release**, because the existing password-reset endpoint returns a reusable token to the requester, which would let a non-Admin holder take over a more privileged account, and role assignment has the same escalation shape. The path to a delegated password reset (for a future HR role) is a separate design, section 3.3B.

## 1. What the project owner asked for

1. **An Admin can configure any rule for any permission for any role.** *Deliberate security exception:* five security permissions are never configurable and never grantable (section 3.3), and two more (`ManageAccountStatus`, `RevokeSessions`) can be assigned by an Admin only, never delegated, and are usable by a non-Admin only under the full-subset target rule (3.3A). An Admin keeps broad authority over every other permission.
2. **The Office manager can, by default, grant only the right to add (import) documents** (`ImportDocuments`) to other roles, and nothing else.
3. The Admin can assign account-status and session-revocation rights to other roles (for example the Office manager, or an HR manager if that role is added later). Role assignment and password reset are not assignable in the first release (section 0, 3.3B).
4. Everything else about who may do what stays as it is today until an Admin changes it.

Not asked for, and not proposed: per-user permissions, new roles, time-limited grants, approval workflows.

## 2. Verified current state (what exists today)

Checked in the code on 2026-10-22.

- **Permissions are fixed in one table in code.** `PermissionMatrix` maps each of the **8** roles (`Dentist`, `Hygienist`, `Assistant`, `FrontDesk`, `Billing`, `OfficeManager`, `Admin`, `Unassigned`) to a set of the **35** permissions in the `Permission` enum (36 after `ManagePermissionGrants` is added). `Admin` is defined as every permission; `Unassigned` holds none.
- **One seam.** Every check goes through `PermissionMatrix.RoleHas`: **274** `[RequirePermission(...)]` uses, **1** `[RequireAnyPermission(...)]` use, and **3** controllers that call it directly (`ClinicalNotesController`, `FormsController`, `VisitsController`). `AuthController` reads `PermissionsFor` for `GET /api/auth/permissions` and `GET /api/auth/permission-matrix`. Nothing else reads the static table.
- **Permissions are not in the session cookie.** The cookie carries the role; the matrix is consulted on every request.
- **A per-request lookup already exists for revocation.** `OnValidatePrincipal` in `Program.cs` compares the cookie's `security_stamp` with the account's current value on every request; changing a role or disabling an account bumps the stamp and ends that person's sessions.
- **The client already refreshes permissions** every 30 seconds from `GET /api/auth/permissions` (`ALV-N009` R02).
- **The matrix screen is read-only**, gated by `ViewPermissionMatrix` (Admin and Office manager).
- **The paths that can demote or disable an account are exactly two:** `AccountService.ChangeRoleAsync` and `AccountService.SetAccountEnabledAsync` (called by `AuthController`). Registration creates `Unassigned`, disabled accounts; bootstrap creates the first Admin. **There is no account deletion** today. **There is no protection against demoting or disabling the last enabled Admin.**
- **A password reset hands a reusable token to the requester.** `IssuePasswordReset` returns the token "exactly once, to the admin who requested it"; `reset-password/complete` then accepts a new password for that account. This is why `IssuePasswordResets` is not assignable in this release. A step-up reauthentication mechanism exists in `AccountService` (audit events `SensitiveActionStepUpFailed`, `MfaStepUpFailed`); I did not check which endpoints require it.
- **The account-administration endpoints and their permissions:** `GET /api/auth/users` (`ManageUsers`: username, role, disabled, MFA, session timeout, created date); change role (`ManageRoles`); enable/disable and session timeout (`ManageAccountStatus`); reset-password (`IssuePasswordResets`); revoke-sessions (`RevokeSessions`).
- **Bulk operations bypass the application guards.** `AlveraDbContext` notes that its `SaveChanges` guard does not cover `ExecuteUpdate`/`ExecuteDelete` or raw SQL, so an invariant that must hold absolutely needs a database-level backstop (section 3.4).
- **Audit exists and is protected** (`AuditService.Record`, named event types, read-only through the application).
- **Tests that pin today's behaviour:** `PermissionMatrixTests` and `AuthControllerPermissionMatrixTests`.

## 3. Proposed design

### 3.1 Principle: code keeps the defaults, the database holds the changes
The `PermissionMatrix` table in code stays as the **default**. Stored rows are **changes to the default**: a *grant* or a *revoke*. The **effective** permissions of a role, `Eff(role)`, are `defaults + grants − revokes`, computed by one service. With no stored rows the result is exactly today's matrix. A new permission added in code arrives with its default and needs no data change.

### 3.2 Data
Role and permission are stored as stable **names** (never ordinals), validated in code against a committed **permission registry** (3.6).
- `RolePermissionOverride` — `Role`, `Permission`, `Effect` (`Grant`/`Revoke`, with a `CHECK` constraint), `Reason`, `ChangedBy`, `ChangedAtUtc`, `RowVersion`; unique on (`Role`, `Permission`). A row exists only while it differs from the default.
- `RolePermissionHistory` — immutable (database triggers, as the ledger tables) record of every change: role, permission, old and new effective value, reason, who, when, whether delegated.
- `PermissionDelegation` — (`DelegateRole`, `Permission`, `TargetRole`). **Default (in code):** (`OfficeManager`, `ImportDocuments`, each of Dentist, Hygienist, Assistant, FrontDesk, Billing). Only an Admin can change delegation rows, and only to permissions that are delegable (3.3).
- `PermissionPolicyState` — one row holding a `Version` that is incremented in the same transaction as any policy change.

### 3.3 Protected permissions and other invariants
**Protected (permanently Admin-only, non-grantable and non-delegable for the first release):** `ManageUsers`, `ManageRoles`, `ManageMfaPolicy`, `IssuePasswordResets` and the new `ManagePermissionGrants`. They:
- are held by Admin and **can never be granted to any other role**, by anyone, including an Admin;
- **can never be delegated**;
- cannot be the subject of an override row or a delegation row (such a row is *invalid persisted data*, 3.6);
- `ManagePermissionGrants` is new, code-defined and held by Admin by default; it is the only permission that authorizes editing grants.

**Admin-assignable, never delegable:** `ManageAccountStatus` and `RevokeSessions`. An Admin may grant either to, or revoke it from, any role except `Admin` and `Unassigned`, through the ordinary override mechanism (with a typed confirmation, R4). No delegation row may name them. A non-Admin holder exercises them **only under the target rules of 3.3A**, enforced inside the account-administration operations themselves, not just by the attribute.

**Delegable:** every other permission, only through an Admin-defined delegation row; by default only `ImportDocuments` by the Office manager (3.8).

**Other invariants (enforced in the service, checked by tests, and where possible by the database):**
1. Admin always holds every permission; Admin has no editable rows.
2. `Unassigned` never holds a permission.
3. Nobody can grant or revoke a permission they do not themselves hold; a delegate cannot change the grants of their own role.
4. **At least one enabled Admin always exists** (3.4).

### 3.3A Target rules for the two Admin-assignable permissions
Applied inside `SetAccountEnabledAsync`, the session-timeout setter (part of account status) and `RevokeAllSessionsAsync` whenever the actor is **not Admin**. Let `A` be the actor's role, `T` the target account's role, and `Eff(R)` the **full** effective permission set of role `R` (3.1):
1. **A non-Admin may never act on an Admin account.**
2. **The action is allowed only if `Eff(T) ⊆ Eff(A)`**: the actor's role must hold every permission the target's role holds. The rule reads the effective permissions, so a later change to a role's grants changes what its holders may do on the next request.
3. **Self actions:** a holder cannot disable their own account. No non-Admin can change a role at all (`ManageRoles` is Admin-only), and enabling an account whose role holds more than the actor's is blocked by rule 2, so the equivalent self-escalation paths are closed. Revoking one's own sessions is allowed (`Eff(A) ⊆ Eff(A)`).
4. Refusals return a stable error (`account_target_not_allowed`, 403), change nothing, and are audited with actor, target and the rule that failed. Every allowed action is audited with the same fields plus the actor's role.
5. Admin actors are not restricted by rules 1 to 4 (the last-Admin guard of 3.4 still applies to them).

**What this means with today's defaults.** If an Admin gave the Office manager `ManageAccountStatus` and `RevokeSessions`, the Office manager could enable, disable and sign out accounts whose role is FrontDesk, Billing, OfficeManager or Unassigned (their permission sets are subsets of the Office manager's), and could not touch a Dentist, Hygienist, Assistant or Admin (each holds permissions the Office manager lacks).
**The user list.** `GET /api/auth/users` stays behind `ManageUsers` in story 1, exactly as today, so story 1 changes no authorization contract (the reviewer's requirement). In story 2, the user list is made available to a holder of `ManageAccountStatus` or `RevokeSessions` and returns a non-Admin caller only the accounts that caller may act on under the rules above; this is the part of the change that lets a non-Admin holder actually use the screens.

### 3.3B Not in this release: delegated password reset and role assignment
`IssuePasswordResets` and `ManageRoles` stay Admin-only. A future HR workflow that can reset passwords for broader staff categories requires a **separate design** that must not give a non-Admin a reusable token capable of taking over a more privileged account. The reviewer's stated starting requirements are: a **target-delivery recovery design** (the reset secret is delivered to the person whose account it is, not to the requester), a **verified recovery identity**, and a **separate security review**. Nothing in this proposal prepares or implies that workflow beyond keeping the two permissions protected.

### 3.4 The last-enabled-Admin invariant
**Definition.** An Admin is *available* when `Role = Admin` and the account is not disabled. (Temporary sign-in lockout after failed attempts is time-limited and is **not** counted as removal; it is listed so the choice is explicit.) The invariant is: after any committed change, at least one Admin is available.

**Unreachable by non-Admin holders.** Target rule 1 means a non-Admin holder of `ManageAccountStatus` can never disable an Admin, so the guard below protects against Admin-on-Admin races only; it is still required.

**One rule over every path.** A single component, `AdminAvailabilityGuard`, is called inside the transaction of every operation that can reduce the set of available Admins. Today those are `ChangeRoleAsync` and `SetAccountEnabledAsync`; the rule also covers any future delete or similar path, because an architecture guard test fails if any code other than the guarded service writes `UserAccount.Role` or `IsDisabled` or deletes a `UserAccount`.

**Concurrency-safe protocol.** Inside the operation's transaction: (1) take a transaction-scoped application lock on the resource `alveara.admin-availability` in exclusive mode (`sp_getapplock`, bounded wait of 5 s); (2) count available Admins **excluding the account the operation would remove**, reading the latest committed state (`READCOMMITTEDLOCK`, so row-versioned snapshot reads cannot hide a concurrent commit); (3) if the count would be zero, refuse with a stable error (`last_admin`, 409) and change nothing; (4) otherwise apply the change and commit, which releases the lock. A wait that times out returns a retry-safe error (`admin_guard_busy`) and changes nothing.
**Database backstop.** A trigger on `UserAccounts` takes the same lock and rejects any `UPDATE`/`DELETE` statement that would leave zero available Admins, so raw SQL and bulk operations (which bypass the application guards) cannot break the invariant.

**Direct concurrency tests (mandatory):** two Admins A and B concurrently demote each other (exactly one succeeds); concurrently disable each other; one demotes while the other disables; twenty concurrent mixed attempts across three Admins always leave at least one; self-demotion of the last Admin is refused; a raw-SQL update leaving none is rejected by the trigger; an applock timeout changes nothing and is retry-safe; the bootstrap path is unaffected.

### 3.5 Policy resolution: the exact protocol
**Objects.** `PolicySnapshot` is immutable: `Version`, the effective permission set per role, the delegation set, and a per-role health flag (3.6). `PolicyStore` is a process-wide singleton holding `Current` (one reference, replaced atomically) and `VerifiedAt` (monotonic time of the last successful version read).

**Writer (one transaction).** `UPDATE PermissionPolicyState SET Version = Version + 1 OUTPUT inserted.Version` (this takes the row lock and serializes writers); validate the caller's row version and every invariant against the current rows; write the override or delegation rows, the history row and the audit entry; commit. Rows and version therefore change together or not at all; a reader can only observe committed pairs.

**Reader (every authorization evaluation).**
1. Read the version `V` (one scalar read).
2. If `V` equals `Current.Version`, evaluate on `Current`. The reference is captured once at the start of the evaluation and the snapshot is immutable, so no evaluation can observe a partly updated policy.
3. If `V` differs, the evaluation **waits for a single-flight reload** (one reloader at a time; others wait for its result, bounded wait of 2 s) and then evaluates on the **new** snapshot, never the old one. The reload reads the version `v1`, then all rows, then the version `v2`; if `v1 != v2` the read raced a writer and is repeated (at most 3 attempts) — the same version-bracket technique used to fix the ledger read race (`37d2b08`). The built snapshot is published with a single atomic reference swap.
4. **If the store cannot be read:** the last snapshot may serve for at most **60 seconds counted from `VerifiedAt`** (the last time it was confirmed current, not when it was loaded). After that the result is deny (HTTP 503, stable code `permission_policy_unavailable`). At start-up with no snapshot the result is deny. **The code defaults are never used as a fallback while stored rows may exist**; they are used only after a successful read that found no rows.

**The one case where a revocation is not immediate:** an instance whose store reads are failing keeps using its snapshot for up to 60 s. This is stated as the accepted exposure.

**No-overrides equivalence proof (mandatory).** With no rows, the service must return exactly the code defaults for **every (role, permission) pair**. The test enumerates the cells from the enums, so its size is `Enum.GetValues<Role>().Length × Enum.GetValues<Permission>().Length` (today 8 × 35 = 280; **8 × 36 = 288 once `ManagePermissionGrants` exists**) and grows automatically if a permission is added; no number is written into the test.

**Two-instance proof (mandatory):** two application hosts on one database; a revocation (and a grant, and a revert) made through host A applies to the very next authorization on host B with no waiting; repeated 200 times with interleaved requests; a fault-injected store failure on B is served from the snapshot for no more than 60 s and then denies; a writer racing a reloader never yields a request that sees a half-applied change (checked with a command interceptor and a barrier).

### 3.6 Invalid persisted policy data
**The committed permission registry.** A file lists every permission name ever shipped with its status (`Active` or `Retired`). A test compares it with `Enum.GetNames<Permission>()` and fails if a permission disappears from code without being marked `Retired` in the same change; retiring a permission ships with a data migration that deletes its rows. A rename is a retirement plus a new permission.

**Row classes.**
- *Valid.*
- *Retired permission* (named in the registry as Retired): ignored, reported at information level, removable. It is not invalid.
- *Invalid:* a role that is not a known role; a permission name absent from the registry; an effect other than Grant/Revoke; an override for Admin or Unassigned; an override naming a protected permission (`ManageUsers`, `ManageRoles`, `ManageMfaPolicy`, `IssuePasswordResets`, `ManagePermissionGrants`); a delegation naming a protected permission or `ManageAccountStatus` or `RevokeSessions`; a delegation to Admin, Unassigned or OfficeManager.

**Behaviour (conservative, explicit).** An invalid row **never restores access**, because the row might have been a revoke whose permission name was corrupted, and the system cannot know which permission to withhold:
1. Invalid override with a **known, non-Admin role** → that role is in *repair required*: **every permission check for that role is denied** (403, stable code `permission_policy_repair_required`). Other roles are unaffected.
2. Invalid row whose **role cannot be attributed** → the policy store is *degraded*: all non-Admin roles are denied (503, `permission_policy_degraded`). Admin keeps access by invariant, so an Admin can repair.
3. Invalid **delegation** row → that delegation is disabled and delegated grants through it are refused; ordinary checks are unaffected.

**Visibility, logging, audit, measurement.** The matrix screen shows an Admin-visible red status ("Permission policy needs repair: N invalid rows") and `GET /api/permissions/health` (Admin, `ManagePermissionGrants`) lists each invalid row's id, raw values and the effect. Logs carry the error class, row id and counts only (no patient or user data; role and permission names are not PHI but no free text is logged). One audit entry `PermissionPolicyInvalidRowDetected` is written per (row id, row version) so repeats do not flood the log. A privacy-safe counter records the number of invalid rows. **Repair workflow:** an Admin removes a bad row (`POST /api/permissions/invalid-rows/{id}/remove`, reason required, audited, with a history row); the cell then returns to its default. Rows are never auto-corrected.
**Tests:** each row class written by raw SQL; the exact deny behaviour and codes; Admin still works; removal restores the default; audit written once; log content contains no secrets; the registry test fails on a silent permission removal.

### 3.7 Performance budget (predeclared)
**Baseline, measured 2026-10-22** on the development machine, in-process test server against SQL Server Express, through the real pipeline (cookie, security-stamp lookup, `RequirePermission`), n = 2000 requests after 300 warm-up:
| Request | p50 | p95 | p99 |
|---|---|---|---|
| `GET /api/auth/permission-matrix` (permission-gated) | 0.62 ms | 1.20 ms | 1.77 ms |
| `GET /api/auth/permissions` (authenticated, no permission attribute) | 0.57 ms | 0.80 ms | 1.21 ms |

**Acceptance thresholds, frozen before implementation** (same harness and machine, n = 2000 after 300 warm-up, quiet machine, three repeats, the worst of the three is judged):
- **Warm path** (snapshot current): the added cost of authorization is **at most 1.0 ms at p95**, with **at most one additional database round trip** (the scalar version read; zero if it is folded into the existing security-stamp query).
- **Cache miss / reload** (the request that finds a changed version): **p95 ≤ 25 ms** with the standard dataset (all 8 roles × 36 permissions, 60 overrides, 5 delegations) and **≤ 50 ms** with a stress dataset of 500 override rows.
- **Policy write transaction:** p95 ≤ 100 ms.
- **Database-read behaviour** is verified with a command interceptor: a warm evaluation issues exactly the version read (and nothing else); a reload issues the bracketed reads (version, rows, version) and nothing else; a concurrent burst of 50 requests on a changed version issues one reload, not 50.
A breach is `CHANGES_REQUIRED` unless the reviewer re-approves a different budget with the measured numbers.

### 3.8 Delegation policy (approved, unchanged)
By default `OfficeManager` may grant or revoke **only `ImportDocuments`**, **only for** `Dentist`, `Hygienist`, `Assistant`, `FrontDesk` and `Billing`. It may **not** target `Admin`, `Unassigned` or `OfficeManager`; it may not edit delegation rules; it may not alter any protected permission, `ManageAccountStatus` or `RevokeSessions`, or delegation administration. The Office manager card and related matrix information are visible only to the existing `ViewPermissionMatrix` holders (Admin and Office manager).

### 3.9 API, screens, audit, sessions
**Story 1 (engine; no ordinary access change):** `GET /api/permissions/effective` (`ViewPermissionMatrix`: effective yes/no, source default/granted/revoked, who/when/why, delegable flag, health), `GET /api/permissions/history` (`ViewAuditLog`), `GET /api/permissions/health` and the invalid-row removal above (`ManagePermissionGrants`); `AuthController`'s two permission endpoints read the service. **`GET /api/auth/users` keeps its current authorization and response** (it stays behind `ManageUsers`). The target rules of 3.3A are enforced inside the existing account-administration operations and tested through controlled API/test setup (for example an override row inserted by the test), since no write API for overrides exists yet.
**Story 2 (administration):** `PUT /api/permissions/overrides` (`ManagePermissionGrants`), `POST /api/permissions/delegated-grants` (a delegate), `GET/PUT /api/permissions/delegations` (Admin); and the **broader user-list authorization with target-filtered listing** (3.3A). All writes: CSRF, row versions or idempotency keys, stable error codes, closed sets of role and permission names, bounded reasons; malformed input is 400.
**Screens (story 2):** the matrix gains an Admin edit mode (each cell shows its source; a change asks for a reason, shows how many enabled users hold the role, asks for typed confirmation for the high-impact list (R4), which includes `ManageAccountStatus` and `RevokeSessions`, and offers "revert to default"; the five protected permissions are shown locked and not grantable); the Office manager's "Who may import documents" card; the invalid-row repair panel. WCAG 2.1 AA, light and dark, keyboard operable.
**Audit:** new event types `PermissionGranted`, `PermissionRevoked`, `PermissionReverted`, `DelegationChanged`, `PermissionPolicyInvalidRowDetected`, `InvalidPolicyRowRemoved` (and the target-rule audit entries of 3.3A), each with role, permission, old → new, reason and actor, written in the same transaction as the change; **if the audit write fails the whole change is rolled back**.
**Sessions.** Ordinary grant, revoke and revert changes apply on the **next server authorization check** and need **no** session-stamp bump; an open browser catches up through the existing 30 s refresh. Protected permissions cannot leave Admin, so there is no protected revocation to handle. User-role and account-status changes keep their existing stamp and session invalidation.

### 3.10 Failure-first answers
| Question | Answer |
|---|---|
| Store unreadable? | last snapshot up to 60 s from `VerifiedAt`, then 503 deny; never defaults while rows may exist |
| Writer racing a reader? | rows and version change in one transaction; reader brackets the read with the version and retries; snapshots are immutable and swapped atomically |
| Two admins edit the same cell? | row version; the loser gets 409 and nothing is stored |
| Same request submitted twice? | idempotency key; the second is a quiet replay |
| Audit write fails? | the whole change is rolled back |
| Invalid or corrupt row? | section 3.6: the affected role is denied, or the store degraded; Admin can repair |
| Last Admin removed concurrently? | application lock plus database trigger; exactly one succeeds |
| A non-Admin holder acts on an Admin or on a more privileged account? | refused by the target rules (`account_target_not_allowed`), audited, nothing changes |
| A permission removed from code? | the registry test forces a Retired entry and a data migration |
| Handled / not handled | handled: concurrency, duplicates, store outage, invalid data, escalation attempts, lock-out. Not handled: a compromised Admin account (the account-security layer's job, unchanged); delegated password reset and role assignment (deliberately out of this release, 3.3B) |

## 4. Mandatory evidence and test plan
Both stories are high-risk and use the two-stage process; each requires a **full formal Stage 2 after its Stage 1 design/code review is approved**. Mandatory types per feature: happy path, failure path, boundary, idempotency.

**Story 1 (policy engine) requires at minimum:**
1. The **no-overrides equivalence proof over every (role, permission) cell, generated from the enums** (8 × 36 = 288 once `ManagePermissionGrants` exists, 280 before), plus the existing backend and client suites passing unchanged.
2. An **architecture inventory guard** proving all permission resolution uses the effective-permissions seam (and that `UserAccount.Role`/`IsDisabled` are written only by the guarded service).
3. **Real API-pipeline grant, revoke and revert checks** covering every module sample, **preserving the existing 403 contracts** (body and code), and a test proving **`GET /api/auth/users` returns the same status and body as today for every role**.
4. **Invariant** tests (3.3), **last-Admin concurrency** tests (3.4), **idempotency**, **audit-rollback**, **immutable-history** (direct update/delete rejected), **outage** (within and past 60 s), **corrupt-row** (3.6) and **two-instance** (3.5) tests.
5. **Target rule tests (3.3A)**, for `SetAccountEnabledAsync`, the session-timeout setter and `RevokeAllSessionsAsync`, through controlled setup (an override row inserted by the test): a non-Admin holder acting on an Admin (refused); on a role holding a permission the actor lacks (refused); on a role whose permission set is a subset of the actor's (allowed); on a peer with the same role (allowed); on an `Unassigned` account (allowed); disabling their own account (refused); revoking their own sessions (allowed); the same actions as Admin (allowed); refusals and successes audited with the failing rule; a change in a role's grants changes the outcome on the next request; a concurrent or replayed request cannot bypass a rule (barrier test). Direct tests that **no non-Admin can reach `ChangeRoleAsync`, `IssuePasswordResetTokenAsync`, the override write path or the policy health/removal endpoints** by any combination of overrides, because their permissions are protected and an override row naming them is invalid data.
6. A **backup/restore drill** proving every new policy, history, delegation and state table survives.
7. **p95 performance evidence** against section 3.7 (baseline, dataset, warm, reload, database-read behaviour).
8. A **real-backend walkthrough** and the **full Stage 2**.
9. **Negative controls** for the core mutations: defaults-only, ignore revokes, skip the escalation check, fail open on outage, skip the audit write, skip the Admin guard, ignore an invalid row, **skip target rule 1, skip target rule 2 (the subset check), allow an override for a protected permission**.

**Story 2 (administration)** may begin only after story 1 is `COMPLETE`, and requires: Admin editing with reason and confirmation (typed confirmation for the high-impact list); Office manager limits (only `ImportDocuments`, only the five target roles, no escalation, no self-edit); the broader user-list authorization with **target-filtered listing** (a holder sees only the accounts they may act on; Admin sees all; everyone else is refused as today); stale-write handling (409); accessibility in light and dark; read-only behaviour for unauthorized users; audit history; the 30 s refresh picking up a change; the real-backend walkthrough; and the full Stage 2.

## 5. What does not change
Roles stay a closed enum of job titles; a user still has exactly one role; the cookie still carries only the role; the 274 + 1 + 3 enforcement sites keep their attributes (their implementation changes); no endpoint's required permission changes in story 1, including `GET /api/auth/users`; story 1 changes no ordinary access (the only behaviour changes are the new last-Admin protection, the target rules that apply only when a non-Admin holds `ManageAccountStatus` or `RevokeSessions` (which no role does by default), and the new read-only endpoints).

## 6. Alternatives considered and rejected
1. **Make the database the whole matrix.** Rejected: new permissions would need data migrations and the reviewed default would lose its single answer.
2. **Put permissions in the cookie.** Rejected: a revoke would not apply until sign-in.
3. **Per-user grants.** Not requested; larger audit surface.
4. **A general policy engine (attribute- or relationship-based).** Over-built for eight job-title roles.
5. **Leave permissions fixed.** Status quo.
6. **Fail open to the code defaults when the store is unreadable or a row is invalid.** Rejected: it silently restores revoked access.
7. **Ignore invalid rows.** Rejected: a corrupted revoke would silently restore access.
8. **Degrade the whole system for any invalid row.** Rejected as unnecessarily broad: an attributable row takes only its role offline.
9. **Let an Admin grant protected permissions after a confirmation (R01).** Withdrawn at the reviewer's direction.
10. **A security-permission-only target rule `S ∩ Eff(Target) ⊆ Eff(Actor)` (R03) so that `ManageRoles` and `IssuePasswordResets` could also be assigned.** Rejected by the reviewer and withdrawn: a holder of `IssuePasswordResets` could reset a Dentist, receive the reusable token, take over the account and gain clinical permissions the holder lacked; `ManageRoles` restricted only by security permissions could alter roles carrying clinical, patient or billing powers beyond the actor's authority.
11. **Assign all four permissions with no target rules.** Rejected: account takeover and Admin creation.
12. **A delegated password-reset design inside this release.** Deferred to a separate design with target-delivery recovery and its own security review (3.3B).

## 7. Delivery as two stories, risks and open questions
**Story 1 — policy engine:** tables and triggers, the effective-permissions service with the section 3.5 protocol, the seam replacement at all sites, the architecture guard, the last-Admin guard (service plus trigger), the permission registry, invalid-data behaviour and repair, immutable history and audit, the read-only endpoints, the target rules of 3.3A inside the existing account-administration operations; no ordinary access change on deployment and no change to `GET /api/auth/users`.
**Story 2 — policy administration:** override and delegation write APIs, the editable Admin matrix, the Office manager card, confirmation flows, stale-write handling, the broader user-list authorization with target-filtered listing, accessibility and error states.
**Risks.** Blast radius (every endpoint is authorized through the seam) — mitigated by one seam, the generated equivalence proof, the guard, and a first story that changes no ordinary access. Lock-out — the Admin invariant plus the last-Admin protection (a behaviour change to account administration with its own tests and a release note). Escalation — five protected permissions, the full-subset target rule, the no-escalation and no-self-edit rules, the closed delegation table. A corrupt row takes a role offline by design (3.6): the cost of failing closed, visible and repairable. Multi-instance deployment — covered by the version protocol and the two-instance test. Backup coverage — proved by the drill. **The owner's wish for delegated role and password administration is not met in this release** (3.3B): a practical gap, accepted as the price of not creating an account-takeover path. The software makes access changes auditable; the practice's access policy itself is a business decision.

## 8. Decisions already settled by the reviewer
Two stories after `ALV-N015` and before `STORY-009` (identifiers reviewer-owned); code defaults plus stored role overrides; per role, not per user; the five protected permissions (`ManageUsers`, `ManageRoles`, `ManageMfaPolicy`, `IssuePasswordResets`, `ManagePermissionGrants`) Admin-only, non-grantable and non-delegable in the first release; `ManageAccountStatus` and `RevokeSessions` Admin-assignable, never delegable, under `Eff(Target) ⊆ Eff(Actor)`; no non-Admin acts on an Admin; no self-escalation; delegated password reset needs a separate design; the delegation policy and its visibility; the 60 s limit and no default fallback; session behaviour; generated permission counts (36 permissions, 288 cells); `GET /api/auth/users` unchanged in story 1; the mandatory evidence; story 2 only after story 1 is `COMPLETE`.

## 9. Work plan and packaging
Small, reversible steps per story, each a focused Stage 1 package with a frozen Stage 2 (full serial backend, client and repository gates, a real-backend walkthrough, a restore drill including the new tables) per `HANDOFF_SCHEMA.md` v3. Story 1 is medium (mostly mechanical, heavy on proof); story 2 is medium-large (UI-heavy). Nothing starts before the reviewer approves this revision, amends the plan and assigns identifiers.

## 10. Open points for the reviewer (response template)
R1 to R7 were put in R02 and the reviewer's responses accepted that part of the design "directionally"; they are listed again so each gets an explicit answer. R8 is new.

| Point | Proposal | Reply (APPROVE / APPROVE WITH CHANGE / REJECT) | Notes |
|---|---|---|---|
| **R1** Invalid persisted data (3.6) | known non-Admin role: that role denied (`permission_policy_repair_required`); unattributable role: store degraded, all non-Admin denied (503); invalid delegation: that delegation disabled; Admin-only repair by removing the row | | |
| **R2** Numeric p95 budget (3.7) | warm added p95 ≤ 1.0 ms with at most one extra round trip; reload p95 ≤ 25 ms (standard) and ≤ 50 ms (500-row stress); write p95 ≤ 100 ms; against the recorded baseline (p95 1.20 ms) | | |
| **R3** Last-Admin protocol (3.4) | transaction-scoped application lock plus latest-committed count in the service, and a database trigger as a backstop for raw SQL | | |
| **R4** Confirmation list in story 2 (non-protected, high-impact) | typed confirmation for `ManageAccountStatus`, `RevokeSessions`, `ViewPatientRecords`, `ViewAuditLog`, `ManageBackups`, `ManageBilling`, `ManagePrescriptions`, `ManagePracticeConfiguration`, `ManageDocumentSettings`, `ManageDocumentStorage`; ordinary confirmation (with the affected-user count) for the rest | | |
| **R5** Permission registry (3.6) | a committed registry of permission names with Active/Retired status, a test against the enum, and a migration requirement for retirement | | |
| **R6** Repair endpoint in story 1 | the health and invalid-row-removal APIs ship in story 1 (the screen panel in story 2) | | |
| **R7** Version read folded into the stamp query | allowed as an optimization if measured to meet 3.7, otherwise a separate scalar read | | |
| **R8** Story 2 user-list authorization | `GET /api/auth/users` accepts a holder of `ManageUsers`, `ManageAccountStatus` or `RevokeSessions`, and returns a non-Admin caller only the accounts the caller may act on under 3.3A; unchanged in story 1 | | |

## Appendix A — Draft acceptance criteria (for the reviewer to reshape)

**Story 1 (engine):** (1) With no stored rows the effective permissions equal today's matrix in every (role, permission) cell, the cells generated from the enums (8 × 36 = 288 after `ManagePermissionGrants`). (2) No code resolves permissions except the effective-permissions service, and `UserAccount.Role`/`IsDisabled` are written only by the guarded account service (architecture guards). (3) The 274 + 1 + 3 sites and the two `AuthController` permission endpoints read the service; existing 403 bodies and codes are unchanged; **`GET /api/auth/users` is unchanged for every role**. (4) A stored grant, revoke or revert changes the result on the next authorization, on one instance and on a second instance. (5) The five protected permissions (`ManageUsers`, `ManageRoles`, `ManageMfaPolicy`, `IssuePasswordResets`, `ManagePermissionGrants`) can never be granted, delegated or overridden, and such rows are invalid data; `ManageAccountStatus` and `RevokeSessions` can never be delegated. (6) The target rules of 3.3A hold for every non-Admin use of `ManageAccountStatus` and `RevokeSessions`: never an Admin; always `Eff(Target) ⊆ Eff(Actor)`; no self-disable; audited; with tests and negative controls. (7) At least one available Admin always exists under concurrency (service lock and database trigger). (8) The reader and writer protocol of 3.5 holds (no partly updated policy; 60 s limit; deny afterwards; never defaults while rows may exist). (9) Invalid rows behave as 3.6 and are repairable by an Admin with an audit trail and no PHI in logs. (10) Every change is audited in the same transaction (rollback on audit failure) and recorded in an immutable history. (11) The p95 budget of 3.7 is met and evidenced. (12) A restore drill carries every new table. (13) No ordinary access changes on deployment. (14) Full Stage 2.

**Story 2 (administration):** (1) An Admin can set, revert and annotate any non-protected cell from the matrix screen with a reason and the required confirmation (typed for the high-impact list). (2) An Office manager can grant and revoke only `ImportDocuments`, only for Dentist, Hygienist, Assistant, FrontDesk and Billing, cannot target Admin, Unassigned or OfficeManager, cannot edit delegation rules and cannot escalate. (3) Delegation rules are editable only by an Admin, and never name a protected permission, `ManageAccountStatus` or `RevokeSessions`. (4) The five protected permissions are shown locked and cannot be changed; `ManageAccountStatus` and `RevokeSessions` can be assigned only by an Admin, with typed confirmation, and appear as non-delegable. (5) The user list is available to a holder of `ManageAccountStatus` or `RevokeSessions` and returns only the accounts that holder may act on; Admin sees all; everyone else is refused as today. (6) Stale writes return 409 and store nothing. (7) History is visible to `ViewAuditLog` holders; the matrix, the card and the health status are visible only to Admin and Office manager. (8) The UI reflects a change within about 30 seconds. (9) Accessibility, keyboard and theme checks pass. (10) The real-backend walkthrough proves a revoke blocks the next request and a revert restores it. (11) Full Stage 2.
