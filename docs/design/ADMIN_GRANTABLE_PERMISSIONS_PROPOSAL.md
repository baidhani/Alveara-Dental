# Admin-Grantable Permissions — Story Proposal for Independent Review (revision R02)

**Proposed work:** two new high-risk engineering stories on the authorization layer, placed after `ALV-N015` and before `STORY-009`, as the reviewer approved in `docs/design/ADMIN_GRANTABLE_PERMISSIONS_REVIEW_RESPONSE.md`. Story identifiers and the exact Execution Plan wording remain the reviewer's.
**Type:** New production stories (no course parent). Each is **high-risk** (authentication/authorization primitive; migrations/schema) and takes the two-stage review with a full formal Stage 2.
**Document status:** REVISED FOR REVIEW (R02). **Do not implement. The Execution Plan is not amended and no story control record exists.** R01 (committed as `3a9b6ce`) received a "design direction approved with required changes" response; this revision incorporates every required change. The proposal stops at `AWAITING_REVIEW`.
**Prepared:** 2026-10-22, session CC-20261008-k4q9. **Repository state when written:** `main` at `3a9b6ce` (local; `origin/main` is `4147fa6`). `ALV-N015` (item 42) is next and not started.
**Raised by:** the project owner, 2026-10-20 (`docs/DOCUMENTS.md`, "Deferred: admin-grantable permissions", open point 8).

**How to respond.** Section 10 lists only the points that are still open after the reviewer's response. Reply APPROVE, APPROVE WITH CHANGE (say what), or REJECT (say what to do instead). Anything not answered is treated as "recommendation not yet accepted".

---

## 0. What changed in this revision

| Reviewer requirement | Where it is addressed |
|---|---|
| Two stories: (1) policy engine, (2) policy administration | Section 7 (the split) and Appendix A |
| Protected security permissions: permanently Admin-only, never granted to another role, never delegated; new `ManagePermissionGrants`; resolve the contradiction in R01 3.3 | Section 3.3 (rewritten). The R01 text that let an Admin grant a protected permission after confirmation is **withdrawn** |
| Last-enabled-Admin invariant: one transactional, concurrency-safe rule over every path; direct concurrency tests | Section 3.4 (new) |
| Exact race-safe cache protocol; two-instance proof; 60 s limit; never fall back to defaults while revocations may exist | Section 3.5 (new, replaces R01 3.4) |
| Invalid persisted policy data: safe behaviour, status, PHI-safe logging, repair/removal | Section 3.6 (new) |
| A measurable predeclared p95 budget with baseline, dataset, warm/miss results and database-read behaviour | Section 3.7 (new, with a measured baseline). The R01 "5% of median" rule is **withdrawn** |
| Session behaviour: ordinary changes need no stamp bump; protected permissions cannot leave Admin | Section 3.9; R01 decision D8 is **not applicable** and removed |
| Delegation policy (targets, exclusions, visibility) | Section 3.8 |
| Mandatory evidence per story; story 2 only after story 1 is `COMPLETE` | Section 4 and Appendix A |

## 1. What the project owner asked for

1. **An Admin can configure any rule for any permission for any role.** *Deliberate security exception, approved by the reviewer:* seven security-administration permissions are never configurable (section 3.3). An Admin keeps broad authority over every other permission but cannot create an indirect escalation path by giving another role security-administration powers.
2. **The Office manager can, by default, grant only the right to add (import) documents** (`ImportDocuments`) to other roles, and nothing else.
3. Everything else about who may do what stays as it is today until an Admin changes it.

Not asked for, and not proposed: per-user permissions, new roles, time-limited grants, approval workflows.

## 2. Verified current state (what exists today)

Checked in the code on 2026-10-22.

- **Permissions are fixed in one table in code.** `PermissionMatrix` maps each of the **8** roles (`Dentist`, `Hygienist`, `Assistant`, `FrontDesk`, `Billing`, `OfficeManager`, `Admin`, `Unassigned`) to a set of the **35** permissions in the `Permission` enum. `Admin` is defined as every permission; `Unassigned` holds none.
- **One seam.** Every check goes through `PermissionMatrix.RoleHas`: **274** `[RequirePermission(...)]` uses, **1** `[RequireAnyPermission(...)]` use, and **3** controllers that call it directly (`ClinicalNotesController`, `FormsController`, `VisitsController`). `AuthController` reads `PermissionsFor` for `GET /api/auth/permissions` and `GET /api/auth/permission-matrix`. Nothing else reads the static table.
- **Permissions are not in the session cookie.** The cookie carries the role; the matrix is consulted on every request.
- **A per-request lookup already exists for revocation.** `OnValidatePrincipal` in `Program.cs` compares the cookie's `security_stamp` with the account's current value on every request; changing a role or disabling an account bumps the stamp and ends that person's sessions.
- **The client already refreshes permissions** every 30 seconds from `GET /api/auth/permissions` (`ALV-N009` R02).
- **The matrix screen is read-only**, gated by `ViewPermissionMatrix` (Admin and Office manager).
- **The paths that can demote or disable an account are exactly two:** `AccountService.ChangeRoleAsync` and `AccountService.SetAccountEnabledAsync` (called by `AuthController`). Registration creates `Unassigned`, disabled accounts; bootstrap creates the first Admin. **There is no account deletion** today. **There is no protection against demoting or disabling the last enabled Admin.**
- **Bulk operations bypass the application guards.** `AlveraDbContext` notes that its `SaveChanges` guard does not cover `ExecuteUpdate`/`ExecuteDelete` or raw SQL, so an invariant that must hold absolutely needs a database-level backstop (section 3.4).
- **Audit exists and is protected** (`AuditService.Record`, named event types, read-only through the application).
- **Tests that pin today's behaviour:** `PermissionMatrixTests` and `AuthControllerPermissionMatrixTests`.

## 3. Proposed design

### 3.1 Principle: code keeps the defaults, the database holds the changes
The `PermissionMatrix` table in code stays as the **default**. Stored rows are **changes to the default**: a *grant* or a *revoke*. The **effective** permissions of a role are `defaults + grants − revokes`, computed by one service. With no stored rows the result is exactly today's matrix. A new permission added in code arrives with its default and needs no data change.

### 3.2 Data
Role and permission are stored as stable **names** (never ordinals), validated in code against a committed **permission registry** (3.6).
- `RolePermissionOverride` — `Role`, `Permission`, `Effect` (`Grant`/`Revoke`, with a `CHECK` constraint), `Reason`, `ChangedBy`, `ChangedAtUtc`, `RowVersion`; unique on (`Role`, `Permission`). A row exists only while it differs from the default.
- `RolePermissionHistory` — immutable (database triggers, as the ledger tables) record of every change: role, permission, old and new effective value, reason, who, when, whether delegated.
- `PermissionDelegation` — (`DelegateRole`, `Permission`, `TargetRole`). **Default (in code):** (`OfficeManager`, `ImportDocuments`, each of Dentist, Hygienist, Assistant, FrontDesk, Billing). Only an Admin can change delegation rows.
- `PermissionPolicyState` — one row holding a `Version` that is incremented in the same transaction as any policy change.

### 3.3 Protected security permissions and other invariants
**Protected permissions (permanently Admin-only):** `ManageUsers`, `ManageRoles`, `ManageAccountStatus`, `ManageMfaPolicy`, `IssuePasswordResets`, `RevokeSessions`, and the new `ManagePermissionGrants`. They:
- are held by Admin and **can never be granted to any other role**, by anyone, including an Admin;
- **can never be delegated**;
- cannot be the subject of an override row or a delegation row (such a row is *invalid persisted data*, 3.6);
- `ManagePermissionGrants` is new, code-defined and held by Admin by default; it is the only permission that authorizes editing grants, and it is itself protected.

**Other invariants (enforced in the service, checked by tests, and where possible by the database):**
1. Admin always holds every permission; Admin has no editable rows.
2. `Unassigned` never holds a permission.
3. Nobody can grant or revoke a permission they do not themselves hold; a delegate cannot change the grants of their own role.
4. **At least one enabled Admin always exists** (3.4).

### 3.4 The last-enabled-Admin invariant
**Definition.** An Admin is *available* when `Role = Admin` and the account is not disabled. (Temporary sign-in lockout after failed attempts is time-limited and is **not** counted as removal; it is listed here so the choice is explicit.) The invariant is: after any committed change, at least one Admin is available.

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

**Two-instance proof (mandatory):** two application hosts on one database; a revocation (and a grant, and a revert) made through host A applies to the very next authorization on host B with no waiting; repeated 200 times with interleaved requests; a fault-injected store failure on B is served from the snapshot for no more than 60 s and then denies; a writer racing a reloader never yields a request that sees a half-applied change (checked with a command interceptor and a barrier).

### 3.6 Invalid persisted policy data
**The committed permission registry.** A file lists every permission name ever shipped with its status (`Active` or `Retired`). A test compares it with `Enum.GetNames<Permission>()` and fails if a permission disappears from code without being marked `Retired` in the same change; retiring a permission ships with a data migration that deletes its rows. A rename is a retirement plus a new permission.

**Row classes.**
- *Valid.*
- *Retired permission* (named in the registry as Retired): ignored, reported at information level, removable. It is not invalid.
- *Invalid:* a role that is not a known role; a permission name absent from the registry; an effect other than Grant/Revoke; an override for Admin or Unassigned; an override or delegation naming a protected permission; a delegation to Admin, Unassigned or OfficeManager.

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
(The R01 rule "under 5% of the median" would have allowed 0.03 ms and is withdrawn.)

**Acceptance thresholds, frozen before implementation** (same harness and machine, n = 2000 after 300 warm-up, quiet machine, three repeats, the worst of the three is judged):
- **Warm path** (snapshot current): the added cost of authorization is **at most 1.0 ms at p95**, with **at most one additional database round trip** (the scalar version read; zero if it is folded into the existing security-stamp query).
- **Cache miss / reload** (the request that finds a changed version): **p95 ≤ 25 ms** with the standard dataset (8 roles × 35 permissions, 60 overrides, 5 delegations) and **≤ 50 ms** with a stress dataset of 500 override rows.
- **Policy write transaction:** p95 ≤ 100 ms.
- **Database-read behaviour** is verified with a command interceptor: a warm evaluation issues exactly the version read (and nothing else); a reload issues the bracketed reads (version, rows, version) and nothing else; a concurrent burst of 50 requests on a changed version issues one reload, not 50.
A breach is `CHANGES_REQUIRED` unless the reviewer re-approves a different budget with the measured numbers.

### 3.8 Delegation policy (approved for design)
By default `OfficeManager` may grant or revoke only `ImportDocuments` for `Dentist`, `Hygienist`, `Assistant`, `FrontDesk` and `Billing`. It may **not** target `Admin`, `Unassigned` or `OfficeManager`; it may not edit delegation rules; it may not alter any protected permission. The Office manager card and related matrix information are visible only to the existing `ViewPermissionMatrix` holders (Admin and Office manager).

### 3.9 API, screens, audit, sessions
**Story 1 (read-only plus repair):** `GET /api/permissions/effective` (`ViewPermissionMatrix`: effective yes/no, source default/granted/revoked, who/when/why, delegable flag, health), `GET /api/permissions/history` (`ViewAuditLog`), `GET /api/permissions/health` and the invalid-row removal above (`ManagePermissionGrants`); `AuthController`'s two endpoints read the service. **Story 2 (administration):** `PUT /api/permissions/overrides` (Admin), `POST /api/permissions/delegated-grants` (a delegate), `GET/PUT /api/permissions/delegations` (Admin). All writes: CSRF, row versions or idempotency keys, stable error codes, closed sets of role and permission names, bounded reasons; malformed input is 400.
**Screens (story 2):** the matrix gains an Admin edit mode (each cell shows its source; a change asks for a reason, shows how many enabled users hold the role, asks for confirmation for the high-impact list (section 10, R4), and offers "revert to default"; protected rows are shown locked); the Office manager's "Who may import documents" card; the invalid-row repair panel. WCAG 2.1 AA, light and dark, keyboard operable.
**Audit:** new event types `PermissionGranted`, `PermissionRevoked`, `PermissionReverted`, `DelegationChanged`, `PermissionPolicyInvalidRowDetected`, `InvalidPolicyRowRemoved`, each with role, permission, old → new, reason and actor, written in the same transaction as the change; **if the audit write fails the whole change is rolled back**.
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
| A permission removed from code? | the registry test forces a Retired entry and a data migration |
| Handled / not handled | handled: concurrency, duplicates, store outage, invalid data, escalation attempts, lock-out. Not handled: a compromised Admin account (the account-security layer's job, unchanged) |

## 4. Mandatory evidence and test plan
Both stories are high-risk and use the two-stage process; each requires a **full formal Stage 2 after its Stage 1 design/code review is approved**. Mandatory types per feature: happy path, failure path, boundary, idempotency.

**Story 1 (policy engine) requires at minimum:**
1. The **280-cell no-overrides equivalence proof** (8 roles × 35 permissions), plus the existing backend and client suites passing unchanged.
2. An **architecture inventory guard** proving all permission resolution uses the effective-permissions seam (and that `UserAccount.Role`/`IsDisabled` are written only by the guarded service).
3. **Real API-pipeline grant, revoke and revert checks** covering every module sample, **preserving the existing 403 contracts** (body and code).
4. **Invariant** tests (3.3), **last-Admin concurrency** tests (3.4), **idempotency**, **audit-rollback**, **immutable-history** (direct update/delete rejected), **outage** (within and past 60 s), **corrupt-row** (3.6) and **two-instance** (3.5) tests.
5. A **backup/restore drill** proving every new policy, history, delegation and state table survives.
6. **p95 performance evidence** against section 3.7 (baseline, dataset, warm, reload, database-read behaviour).
7. A **real-backend walkthrough** and the **full Stage 2**.
8. **Negative controls** for the core mutations: defaults-only, ignore revokes, skip the escalation check, fail open on outage, skip the audit write, skip the Admin guard, ignore an invalid row.

**Story 2 (administration)** may begin only after story 1 is `COMPLETE`, and requires: Admin editing with reason and confirmation; Office manager limits (only `ImportDocuments`, only the five target roles, no escalation, no self-edit); stale-write handling (409); accessibility in light and dark; read-only behaviour for unauthorized users; audit history; the 30 s refresh picking up a change; the real-backend walkthrough; and the full Stage 2.

## 5. What does not change
Roles stay a closed enum of job titles; a user still has exactly one role; the cookie still carries only the role; the 274 + 1 + 3 enforcement sites keep their attributes (their implementation changes); no endpoint's required permission changes in story 1; story 1 changes no ordinary access (the only behaviour change is the new last-Admin protection and the new read-only endpoints).

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

## 7. Delivery as two stories, risks and open questions
**Story 1 — policy engine:** tables and triggers, the effective-permissions service with the section 3.5 protocol, the seam replacement at all sites, the architecture guard, the last-Admin guard (service plus trigger), the permission registry, invalid-data behaviour and repair, immutable history and audit, the read-only endpoints, no ordinary access change on deployment.
**Story 2 — policy administration:** override and delegation write APIs, the editable Admin matrix, the Office manager card, confirmation flows, stale-write handling, accessibility and error states.
**Risks.** Blast radius (every endpoint is authorized through the seam) — mitigated by one seam, the 280-cell proof, the guard, and a first story that changes no ordinary access. Lock-out — the Admin invariant plus the last-Admin protection (a behaviour change to account administration with its own tests and a release note). Escalation — protected permissions, no-escalation and no-self-edit rules, the closed delegation table. A corrupt row takes a role offline by design (3.6): the cost of failing closed, visible and repairable. Multi-instance deployment — covered by the version protocol and the two-instance test. Backup coverage — proved by the drill. The software makes access changes auditable; the practice's access policy itself is a business decision.

## 8. Decisions already settled by the reviewer's response
Two stories after `ALV-N015` and before `STORY-009` (identifiers reviewer-owned); code defaults plus stored role overrides; per role, not per user; the seven protected permissions; the delegation policy and its visibility; the 60 s limit and no default fallback; session behaviour; the mandatory evidence; story 2 only after story 1 is `COMPLETE`.

## 9. Work plan and packaging
Small, reversible steps per story, each a focused Stage 1 package with a frozen Stage 2 (full serial backend, client and repository gates, a real-backend walkthrough, a restore drill including the new tables) per `HANDOFF_SCHEMA.md` v3. Story 1 is medium (mostly mechanical, heavy on proof); story 2 is medium-large (UI-heavy). Nothing starts before the reviewer approves this revision, amends the plan and assigns identifiers.

## 10. Open points for the reviewer (response template)

| Point | Proposal | Reply (APPROVE / APPROVE WITH CHANGE / REJECT) | Notes |
|---|---|---|---|
| **R1** Invalid persisted data (3.6) | known non-Admin role: that role denied (`permission_policy_repair_required`); unattributable role: store degraded, all non-Admin denied (503); invalid delegation: that delegation disabled; Admin-only repair by removing the row | | |
| **R2** Numeric p95 budget (3.7) | warm added p95 ≤ 1.0 ms with at most one extra round trip; reload p95 ≤ 25 ms (standard) and ≤ 50 ms (500-row stress); write p95 ≤ 100 ms; measured against the recorded baseline (p95 1.20 ms) | | |
| **R3** Last-Admin protocol (3.4) | transaction-scoped application lock plus latest-committed count in the service, and a database trigger as a backstop for raw SQL | | |
| **R4** Confirmation list in story 2 (non-protected, high-impact) | typed confirmation for `ViewPatientRecords`, `ViewAuditLog`, `ManageBackups`, `ManageBilling`, `ManagePrescriptions`, `ManagePracticeConfiguration`, `ManageDocumentSettings`, `ManageDocumentStorage`; ordinary confirmation (with the affected-user count) for the rest | | |
| **R5** Permission registry (3.6) | a committed registry of permission names with Active/Retired status, a test against the enum, and a migration requirement for retirement | | |
| **R6** Repair endpoint in story 1 | the health and invalid-row-removal APIs ship in story 1 (the screen panel in story 2) | | |
| **R7** Version read folded into the stamp query | allowed as an optimization if measured to meet 3.7, otherwise a separate scalar read | | |

## Appendix A — Draft acceptance criteria (for the reviewer to reshape)

**Story 1 (engine):** (1) With no stored rows the effective permissions equal today's matrix in all 280 cells. (2) No code resolves permissions except the effective-permissions service, and `UserAccount.Role`/`IsDisabled` are written only by the guarded account service (architecture guards). (3) The 274 + 1 + 3 sites and the two `AuthController` endpoints read the service, and existing 403 bodies and codes are unchanged. (4) A stored grant, revoke or revert changes the result on the next authorization, on one instance and on a second instance. (5) The seven protected permissions can never be granted, delegated or overridden, and such rows are invalid data. (6) At least one available Admin always exists under concurrency (service lock and database trigger). (7) The reader and writer protocol of 3.5 holds (no partly updated policy; 60 s limit; deny afterwards; never defaults while rows may exist). (8) Invalid rows behave as 3.6 and are repairable by an Admin with an audit trail and no PHI in logs. (9) Every change is audited in the same transaction (rollback on audit failure) and recorded in an immutable history. (10) The p95 budget of 3.7 is met and evidenced. (11) A restore drill carries every new table. (12) No ordinary access changes on deployment. (13) Full Stage 2.

**Story 2 (administration):** (1) An Admin can set, revert and annotate any non-protected cell from the matrix screen with a reason and the required confirmation. (2) An Office manager can grant and revoke only `ImportDocuments`, only for Dentist, Hygienist, Assistant, FrontDesk and Billing, cannot target Admin, Unassigned or OfficeManager, cannot edit delegation rules and cannot escalate. (3) Delegation rules are editable only by an Admin. (4) Protected rows are shown locked and cannot be changed. (5) Stale writes return 409 and store nothing. (6) History is visible to `ViewAuditLog` holders; the matrix, the card and the health status are visible only to Admin and Office manager. (7) The UI reflects a change within about 30 seconds. (8) Accessibility, keyboard and theme checks pass. (9) The real-backend walkthrough proves a revoke blocks the next request and a revert restores it. (10) Full Stage 2.
