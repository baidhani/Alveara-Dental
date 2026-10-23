# Admin-Grantable Permissions — Story Proposal for Independent Review

**Proposed work:** one new production story, or two (decision D2), on the authorization layer. The identifiers and their position in the plan are the reviewer's to set (decision D1).
**Type:** New production story (no course parent). It touches the authorization primitive every endpoint uses, so under the risk-based cadence it is **high-risk** (authentication/authorization primitive; migrations/schema) and takes the two-stage review with a full Stage 2.
**Document status:** PROPOSED. No code has been written for it. Nothing in this document is in force.
**Prepared:** 2026-10-22, session CC-20261008-k4q9. **Repository state when written:** `main` at `86b4655` (three documentation commits ahead of `origin/main` `4147fa6`). Items 1–41 and the control items `ALV-CONTROL-C01` and `ALV-003-C01` R03 are `COMPLETE`; `ALV-N015` (item 42) is next and not started.
**Raised by:** the project owner, 2026-10-20 (recorded in `docs/DOCUMENTS.md`, "Deferred: admin-grantable permissions", and open point 8). Held back until `ALV-010-C01` was finished, as the owner asked.

**How to respond.** Section 10 is a response template. For each decision D1–D14 reply APPROVE, APPROVE WITH CHANGE (say what), or REJECT (say what to do instead). Anything not answered is treated as "recommendation not yet accepted".

---

## 1. What the project owner asked for

1. **An Admin can configure any rule for any permission for any role.**
2. **The Office manager can, by default, grant only the right to add (import) documents** (`ImportDocuments`) to other roles, and nothing else.
3. Everything else about who may do what stays as it is today until an Admin changes it.

Not asked for, and not proposed: per-user permissions, new roles, time-limited grants, approval workflows. (They are listed as alternatives in section 6 so they are visibly out of scope.)

## 2. Verified current state (what exists today)

Checked in the code on 2026-10-22.

- **Permissions are fixed in one table in code.** `PermissionMatrix` maps each of the **8** roles (`Dentist`, `Hygienist`, `Assistant`, `FrontDesk`, `Billing`, `OfficeManager`, `Admin`, `Unassigned`) to a set of the **35** permissions in the `Permission` enum. `Admin` is defined as every permission (`Enum.GetValues`); `Unassigned` holds none.
- **One seam.** Every check goes through `PermissionMatrix.RoleHas`: **274** `[RequirePermission(...)]` uses, **1** `[RequireAnyPermission(...)]` use, and **3** controllers that call it directly (`ClinicalNotesController`, `FormsController`, `VisitsController`). `AuthController` reads `PermissionsFor` for `GET /api/auth/permissions` and `GET /api/auth/permission-matrix`. Nothing else reads the static table.
- **Permissions are not in the session cookie.** The cookie carries the role; the matrix is consulted on every request. So a changed rule would apply to the next request without anyone signing in again.
- **There is already a per-request lookup for revocation.** `OnValidatePrincipal` in `Program.cs` compares the cookie's `security_stamp` with the account's current value on **every request**; changing a user's role bumps the stamp and ends their sessions.
- **The client already refreshes permissions.** `AuthContext` re-derives the signed-in person's permissions from `GET /api/auth/permissions` every **30 seconds** (`ALV-N009` R02), so a change reaches an open browser within about 30 s with no client change to that mechanism.
- **The matrix screen is read-only** and gated by `ViewPermissionMatrix` (held by Admin and Office manager). Only the role of a user can be changed today (`AccountService.ChangeRoleAsync`, audited as `RoleChanged`).
- **Audit exists and is protected.** `AuditService.Record` writes `AuditLogEntry` rows with named event types; the log is read-only through the application.
- **Tests that pin today's behaviour:** `PermissionMatrixTests` (Admin holds every permission; Unassigned none; every role has an entry; role-specific expectations) and `AuthControllerPermissionMatrixTests` (the attribute really gates API access).
- **I did not find** any protection against demoting or disabling the last Admin. It is not a defect of this proposal, but changing rules at run time makes lock-out a more realistic risk, so section 3.3 adds invariants and section 7 lists it.
- `ALV-N015`'s proposal (decision D11) already says its new permissions can move into this system later without changing its contracts.

## 3. Proposed design

### 3.1 Principle: code keeps the defaults, the database holds the changes
The `PermissionMatrix` table in code stays as the **default** (it is reviewed, versioned and tested). Stored rows are **changes to the default**: a *grant* (the role also gets this permission) or a *revoke* (the role loses a permission it has by default). The **effective** permissions of a role are `defaults + grants − revokes`, computed by one service. With no stored rows the result is exactly today's matrix. A new permission added in code later arrives with its default row in the table and needs no data change.

### 3.2 Data (new tables, append-only where they are history)
- `RolePermissionOverride` — `Role`, `Permission`, `Effect` (`Grant`/`Revoke`), `Reason`, `ChangedBy`, `ChangedAtUtc`, `RowVersion`; unique on (`Role`, `Permission`). A row exists only while it differs from the default; "revert to default" removes it.
- `RolePermissionHistory` — immutable (database triggers, as the ledger tables) record of every change: role, permission, old effective value, new effective value, reason, who, when, whether it was a delegated grant.
- `PermissionDelegation` — (`DelegateRole`, `Permission`, `TargetRole`): the delegate role may grant or revoke that permission for that target role. **Default (in code, shown in the table):** (`OfficeManager`, `ImportDocuments`, each of Dentist, Hygienist, Assistant, FrontDesk, Billing). An Admin can add or remove delegation rows.
- `PermissionPolicyState` — one row with a version number bumped in the same transaction as any change; it is the cache key.

### 3.3 Invariants (cannot be changed by anyone, enforced in the service and checked by tests)
1. **Admin always holds every permission.** Admin rows are not editable.
2. **`Unassigned` never holds a permission** (the self-registration safety of `ALV-001-C01`).
3. **Security-administration permissions are Admin-only forever and cannot be delegated:** `ManageUsers`, `ManageRoles`, `ManageAccountStatus`, `ManageMfaPolicy`, `IssuePasswordResets`, `RevokeSessions`, and the new permission that edits grants (`ManagePermissionGrants`). They can be granted to another role only by an Admin and only after a confirmation (D6 decides the exact list).
4. **No escalation:** nobody can grant or revoke a permission they do not themselves hold, and a delegate cannot change the grants of their own role.
5. **At least one enabled Admin must remain** (new guard in `ChangeRoleAsync`/`SetAccountEnabledAsync`; today there is none, section 2). This is what protects against lock-out, since grants cannot remove Admin's access.

### 3.4 Resolution, cache and failure behaviour
A service `IEffectivePermissions` replaces every read of the static table. `RequirePermission`, `RequireAnyPermission`, the three direct call sites and `AuthController` all call it; **an architecture guard test fails if any other code reads the static matrix** (the same technique as the existing inventory guards).
- **Per request:** read the policy version (one scalar query; the pipeline already makes one query per request for the security stamp, and the two can share a round trip); if it matches the in-memory snapshot use the snapshot, otherwise reload the rows (a few hundred at most) and swap the snapshot atomically.
- **If the store cannot be read:** serve the last good snapshot for at most **60 seconds** (so a short database blip does not lock out the practice), and after that **deny** (HTTP 503 with a stable error code, logged with the error class, never the rule contents). Failing open to the code defaults is **rejected**: it would silently re-grant a permission an Admin had revoked.
- **Revocation latency:** the next request on the server; about 30 s for a UI already open.

### 3.5 Zero behaviour change on deployment
With no override rows, the service returns exactly `PermissionMatrix`'s defaults. A test compares the service with the static table for **all 8 roles × 35 permissions = 280 cells**, and the existing 4,324 backend tests and the 1,545 client tests must pass unchanged. The first story therefore changes no one's access.

### 3.6 API (existing conventions: CSRF on writes, row versions, idempotency keys, stable `error` codes)
- `GET /api/permissions/effective` (`ViewPermissionMatrix`): for every role and permission: effective yes/no, source (`default`/`granted`/`revoked`), who/when/why, whether it is delegable, and for the caller which cells they may change.
- `PUT /api/permissions/overrides` (`ManagePermissionGrants`, Admin): set a cell to granted, revoked or default, with a required reason and the row version; 409 on a stale version; 422 on an invariant violation.
- `POST /api/permissions/delegated-grants` (a delegate): grant or revoke within a delegation rule; refused with a stable code otherwise.
- `GET/PUT /api/permissions/delegations` (Admin): read and edit the delegation rules.
- `GET /api/permissions/history` (`ViewAuditLog`): paged history.
- Inputs are validated at the route (a closed set of role and permission names, bounded reason length); malformed input is 400 and never reaches business logic.

### 3.7 Screens
- The **permission matrix** screen gains an edit mode for Admin: each cell shows its source; changing one asks for a reason, shows who is affected (how many enabled users hold that role), asks for confirmation for any permission on the protected list, and offers "revert to default". Read-only for everyone else with `ViewPermissionMatrix`.
- A small **"Who may import documents"** card for the Office manager (one switch per role, the history underneath), shown only to a delegate. Plain language, keyboard operable, WCAG 2.1 AA, light and dark (the project's design skills apply).

### 3.8 Audit
Every change writes an audit entry (new event types `PermissionGranted`, `PermissionRevoked`, `PermissionReverted`, `DelegationChanged`) with role, permission, old → new, reason and actor, in the same transaction as the change: **if the audit write fails the whole change is rolled back** (the pattern used by billing). Delegated changes are marked as such.

### 3.9 Interaction with existing flows
- `ChangeRoleAsync` is unchanged (it still bumps the security stamp). A **grant or revoke does not bump stamps**: sessions stay valid and the new rule applies on the next request. Whether revoking a *protected* permission should also end that role's sessions is decision D8.
- The backup and restore drill must include the new tables. I have not verified that the encrypted backup covers new tables automatically; the story's evidence must show a restore carrying the rules (to confirm, not assumed).

### 3.10 Failure-first answers
| Question | Answer |
|---|---|
| Store unreadable? | last good snapshot up to 60 s, then 503 deny; never fail open to defaults |
| Two admins edit the same cell? | row version; the loser gets 409 and nothing is stored |
| Same request submitted twice? | idempotency key; the second is a quiet replay |
| Audit write fails? | the whole change is rolled back |
| A stored rule names a permission or role that no longer exists in code? | ignored for decisions, reported on the screen and in the log, removable by an Admin |
| A new permission is added in code? | it exists with its default; no data change |
| A rule would break an invariant? | refused (422) with a stable code; nothing stored |
| Handled / not handled | handled: concurrency, duplicates, store outage, invalid input, escalation attempts. Not handled: a compromised Admin account (that is the account-security layer's job, unchanged) |

## 4. Acceptance mapping and test plan
Mandatory types per feature: happy path, failure path, boundary, idempotency.
- **Equivalence:** the service equals the static table in all 280 cells with no rows (the zero-change proof).
- **Guard:** an architecture test fails if anything but the service reads the matrix.
- **Behaviour through the real pipeline** (API tests, not unit tests): for a sample covering every module, a grant lets a role in, a revoke keeps it out, a revert restores the default, each effective on the next request; and the 403 body is unchanged.
- **Delegation limits:** Office manager can grant `ImportDocuments` to each allowed role; cannot grant anything else; cannot grant to Admin or Unassigned; cannot grant what they do not hold; cannot edit their own role's rules; a non-delegate cannot call the delegated endpoint.
- **Invariants:** each of section 3.3 has a failing-then-passing test; the last-Admin guard is tested at the account service.
- **Concurrency / idempotency / audit:** two simultaneous edits; a replayed request; audit failure rolls back; history rows are immutable (direct update/delete attempts are rejected).
- **Failure paths:** store outage inside and past the 60 s window; corrupt or unknown stored value; cache invalidation across two server instances.
- **Frontend:** edit mode, confirmation, reason required, delegate card, read-only users, error states, axe scans in both themes, and the 30-second refresh picking up a change.
- **Negative controls:** mutate the service (return defaults only; ignore revokes; skip the escalation check; fail open on outage; skip the audit write) and show a test fails for each.
- **Performance:** measure the added per-request cost and record it (the story must state a limit; D10).
- **Route scan (Gate A style):** every endpoint still carries its permission attribute.

## 5. What does not change
Roles stay a closed enum of job titles; a user still has exactly one role; the cookie still carries only the role; `RequirePermission` attributes at the 274 sites are not edited (their implementation changes); no endpoint's required permission changes in the first story.

## 6. Alternatives considered and rejected
1. **Make the database the whole matrix** (seed it from code and drop the table in code). Rejected: new permissions would need data migrations, drift between code and data becomes possible, and "what is the default?" loses its single reviewed answer.
2. **Put permissions in the cookie (claims).** Rejected: a revoke would not apply until each person signs in again, which is the opposite of what an Admin removing a permission expects.
3. **Per-user grants.** Not requested; it multiplies the audit surface and the screens. Could follow later on the same service.
4. **A general policy engine (attribute- or relationship-based rules).** Over-built for eight job-title roles.
5. **Leave permissions fixed in code.** Status quo: any practice-specific change is a code change and a release, which is what the owner wants to avoid.
6. **Fail open to code defaults when the store is unreadable.** Rejected (section 3.4).

## 7. Risks and open questions
- **Blast radius.** It changes how every endpoint is authorized. Mitigations: one seam, the 280-cell equivalence proof, the architecture guard, a first story that changes no one's access, and the full Stage 2.
- **Lock-out.** Mitigated by the Admin invariant and the new last-Admin guard (which is itself a behaviour change to the account service that needs its own tests and a mention in the release notes).
- **Escalation via delegation.** Mitigated by the no-escalation and no-self-edit rules and the closed delegation table; reviewed by a security-focused review of the story.
- **Operational mistakes.** An Admin can revoke `ViewPatientRecords` from the Dentist. The confirmation shows the affected user count; a protected list covers the dangerous cases; the history makes it recoverable; "revert to default" is one action. D6 asks which permissions need the confirmation.
- **Staleness window.** About 30 s for an open UI, one request on the server.
- **Multi-instance deployment.** The production stack is one VPS today. The version check makes several instances correct; the story must include a two-instance test.
- **Backup/restore coverage** of the new tables (section 3.9).
- **Clinical safety and records law** (who may see or edit patient data) are the practice's policy decisions; the software makes them auditable, it does not decide them.

## 8. Decisions requested from the reviewer and owner

- **D1.** Story identifier(s) and position. Recommended: after `ALV-N015`, before `STORY-009`; or independently, since `ALV-N015` already says its permissions can move in later.
- **D2.** One story or two. **Recommended: two.** (B1) the engine: tables, service, seam replacement, equivalence proof, guard, audit, the read-only effective/history endpoints, and the last-Admin guard, with no visible or access change; (B2) editing: the override and delegation APIs, the editable matrix screen and the Office manager card. Each is high-risk and takes its own two-stage review, so the first can be proven safe before any rule can be changed.
- **D3.** The model: defaults in code plus stored changes (section 3.1). Recommended: yes.
- **D4.** Fail-closed behaviour (60 s last-good snapshot, then deny). Recommended: yes; or a different window.
- **D5.** Delegation default: Office manager may grant/revoke `ImportDocuments` to Dentist, Hygienist, Assistant, FrontDesk and Billing, and nothing else. Recommended: yes. (Not to Admin, which already holds it, nor Unassigned.)
- **D6.** The protected list (Admin-only, never delegable; needs confirmation to grant to a non-Admin role). Proposed: `ManageUsers`, `ManageRoles`, `ManageAccountStatus`, `ManageMfaPolicy`, `IssuePasswordResets`, `RevokeSessions`, `ManagePermissionGrants`; the reviewer may add `ViewAuditLog`, `ManageBackups`, `ManagePrescriptions`, `ManageBilling`.
- **D7.** Whether the Office manager's delegated grants are visible to all roles in the matrix or only to Admin and Office manager. Recommended: Admin and Office manager (the existing `ViewPermissionMatrix` holders).
- **D8.** Whether revoking a *protected* permission also ends the affected role's sessions (a stamp bump). Recommended: no for ordinary permissions; yes only for protected ones.
- **D9.** The last-Admin guard (section 3.3) as part of B1. Recommended: yes.
- **D10.** A performance limit for the added per-request cost. Recommended: state the measured value in the story and require it to be under 5% of the median authorized request.
- **D11.** Whether a grant is expressed per role only (this proposal) or may later be per user. Recommended: per role now.
- **D12.** Reason required on every change (min length). Recommended: yes, 10 characters.
- **D13.** Whether the story must also add the new permissions of `ALV-N015` to the delegable set or leave them fixed. Recommended: leave them fixed; they can be re-registered as defaults later.
- **D14.** Evidence required before B2: B1 `COMPLETE` and an approved two-instance cache test. Recommended: yes.

## 9. Work plan and packaging
Small, reversible steps per story: (B1) schema and triggers with tests; the service and cache with the 280-cell equivalence test; replace the seam at the 274 + 1 + 3 sites and `AuthController`; the architecture guard; the last-Admin guard; the read-only endpoints; audit; (B2) the override and delegation services and endpoints; the screens; the end-to-end walkthrough. Each story: focused Stage 1 package with a frozen Stage 2 (full serial backend, client and repository gates, a real-backend walkthrough, a restore drill including the new tables), per `HANDOFF_SCHEMA.md` v3. Estimated shape: B1 is medium (mostly mechanical, heavy on proof), B2 is medium-large (UI-heavy).

## 10. Reviewer response template

| Decision | Reply (APPROVE / APPROVE WITH CHANGE / REJECT) | Notes |
|---|---|---|
| D1 Identifier and position | | |
| D2 One story or two | | |
| D3 Defaults in code plus stored changes | | |
| D4 Fail-closed window | | |
| D5 Delegation default | | |
| D6 Protected list | | |
| D7 Visibility of delegated grants | | |
| D8 Session end on protected revoke | | |
| D9 Last-Admin guard | | |
| D10 Performance limit | | |
| D11 Per role (not per user) | | |
| D12 Reason required | | |
| D13 ALV-N015's permissions | | |
| D14 Gate before B2 | | |

## Appendix A — Draft acceptance criteria (for the reviewer to reshape)

**B1 (engine):** (1) With no stored rows the effective permissions equal today's matrix in all 280 cells. (2) No code reads the static matrix except the service (guard test). (3) The 274 + 1 + 3 enforcement sites and the two `AuthController` endpoints read the service. (4) A stored grant or revoke changes the effective result on the next request. (5) The invariants of section 3.3 hold and are tested. (6) Every change is audited in the same transaction and recorded in an immutable history. (7) If the store is unreadable the behaviour is exactly section 3.4. (8) At least one enabled Admin always remains. (9) A restore carries the rules. (10) No access changes on deployment.

**B2 (editing):** (1) An Admin can set, revert and reason-annotate any non-invariant cell from the matrix screen. (2) An Office manager can grant and revoke only `ImportDocuments`, only to the allowed roles, and cannot escalate. (3) Delegation rules are editable only by an Admin. (4) Protected permissions need confirmation and are never delegable. (5) History is visible to `ViewAuditLog` holders. (6) The UI reflects a change within about 30 seconds. (7) Accessibility, keyboard and theme checks pass. (8) The real-backend walkthrough proves a revoke blocks the next request and a revert restores it.
