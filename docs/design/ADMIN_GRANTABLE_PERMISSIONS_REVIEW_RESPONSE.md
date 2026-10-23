# Admin-Grantable Permissions — Reviewer Response

**Status:** Design direction approved with required changes. Do not implement yet. Update the proposal to incorporate this response, then submit the revised design for review before the Execution Plan is amended or either story begins.

## Approved direction

Create **two new high-risk engineering stories** after `ALV-N015` and before `STORY-009`:

1. **Permission-policy engine:** code defaults plus stored role overrides, effective-permission seam, immutable history, audit coupling, protected invariants, cache/version behavior, read-only APIs, architecture guard, and the last-enabled-Admin protection. This story must make no ordinary access-policy change on deployment.
2. **Permission-policy administration:** override and delegation write APIs, editable Admin matrix, Office Manager document-import delegation card, confirmation flows, and related accessibility/error-state behavior.

Story identifiers and exact Execution Plan wording remain reviewer-owned. Do not create or modify those plan entries yet.

## Required design changes

### 1. Protected security permissions

Resolve the contradiction in proposal section 3.3. The following permissions are permanently **Admin-only**, cannot be granted to another role, and cannot be delegated:

- `ManageUsers`
- `ManageRoles`
- `ManageAccountStatus`
- `ManageMfaPolicy`
- `IssuePasswordResets`
- `RevokeSessions`
- `ManagePermissionGrants`

`ManagePermissionGrants` is a new code-defined permission, held by Admin by default. It must itself be non-grantable and non-delegable.

This is the deliberate security exception to the owner’s general instruction that an Admin can configure role-permission rules. An Admin retains broad configuration authority over all non-protected permissions, but may not create an indirect privilege-escalation path by giving another role security-administration powers.

### 2. Last-enabled-Admin invariant

Implement the invariant as one transactional, concurrency-safe rule across every account-administration path that can demote, disable, delete, or otherwise make an Admin unavailable. Two concurrent actions must not both remove the last enabled Admin. Add direct concurrency tests.

### 3. Policy cache and multi-instance consistency

Specify the exact race-safe protocol for reading `PermissionPolicyState`, loading the snapshot, atomically publishing it, and bumping the version in the same transaction as a policy change. No request may observe a partly updated policy.

The two-instance test must prove that a revocation made through one instance applies to a request served by the other instance on its next authorization evaluation. The last-good snapshot may serve for at most 60 seconds on a store-read failure; after that the authorization result is deny/503. Never fall back to code defaults while stored revocations may exist.

### 4. Invalid persisted policy data

Unknown or corrupt stored role/permission values must never silently restore access. Define and test safe behavior, Admin-visible operational status, PHI-safe logging, audit/measurement as appropriate, and a repair/removal workflow. State whether an invalid row blocks only its affected decision or causes policy-store degradation; choose the conservative behavior and make it explicit.

### 5. Performance evidence

Replace “under 5% of median authorized request” with a measurable predeclared p95 budget. Record the baseline, dataset, warm-cache result, cache-miss/reload result, and database-read behavior. The proposal must give the numeric acceptance threshold before implementation.

### 6. Session behavior

Ordinary grant/revoke changes apply on the next server authorization check and do not require a session-stamp bump. Revoking a protected permission is not applicable because protected permissions cannot leave Admin. User-role or account-status changes retain their existing stamp/session invalidation behavior.

## Delegation policy approved for design

By default, `OfficeManager` may grant or revoke only `ImportDocuments` for these target roles:

- `Dentist`
- `Hygienist`
- `Assistant`
- `FrontDesk`
- `Billing`

It may not target `Admin`, `Unassigned`, or `OfficeManager`; it may not edit delegation rules; and it may not alter any protected permission. The Office Manager card and related matrix information are visible only to the existing `ViewPermissionMatrix` holders: Admin and Office Manager.

## Mandatory evidence

Both stories are high-risk and use the two-stage process. Each requires a full formal Stage 2 only after its Stage 1 design/code review is approved.

For story 1, require at minimum:

- 280-cell no-overrides equivalence proof;
- architecture inventory guard proving all permission resolution uses the effective-permissions seam;
- real API-pipeline grant/revoke/revert checks while preserving existing 403 contracts;
- invariant, concurrency, idempotency, audit-rollback, immutable-history, outage, corrupt-row, and two-instance tests;
- backup/restore drill proving every new policy/history/delegation table survives;
- p95 performance evidence;
- real-backend walkthrough and full Stage 2.

Story 2 may begin only after story 1 is `COMPLETE`. It requires Admin editing, reason/confirmation, Office Manager limits, stale-write handling, accessibility in light/dark themes, read-only unauthorized behavior, audit history, and full Stage 2 evidence.

## Stop condition

Return the revised design proposal at `AWAITING_REVIEW`. Do not implement, amend the Execution Plan, or create story control records until the revised proposal is independently approved.
