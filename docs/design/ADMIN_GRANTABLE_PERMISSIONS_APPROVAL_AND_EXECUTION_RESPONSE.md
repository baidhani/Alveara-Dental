# Admin-Grantable Permissions — Design Approval and Execution Response

**Decision:** `APPROVED`  
**Reviewed:** 2026-10-22

The R04 proposal is approved. The authoritative Execution Plan now contains the two assigned high-risk stories:

1. **ALV-N017 — Effective Permission Policy Engine and Authorization Safety** (item 43).
2. **ALV-N018 — Permission Policy Administration and Controlled Delegation** (item 44).

They are placed after `ALV-N015` and before `STORY-009`. The following planned items are renumbered by two in the Execution Plan and repository execution ledger.

## Execution authorization

- Do not start ALV-N017 until `ALV-N015` is independently approved and durably `COMPLETE`.
- Before implementation, read the updated Execution Plan, the Engineering/Product Reference, `docs/design/ADMIN_GRANTABLE_PERMISSIONS_PROPOSAL.md` R04, the two earlier reviewer responses, `.alveara/BUILD_STATE.md`, `.alveara/EXECUTION_STATUS.json`, `.alveara/HANDOFF_SCHEMA.md`, and current authorization code/tests.
- ALV-N017 is high risk and uses the two-stage protocol. Submit focused Stage 1 evidence first. Do not run its formal full Stage 2 before Stage 1 approval.
- Do not begin ALV-N018 until ALV-N017 is `COMPLETE`.

## Non-negotiable security boundaries

- `ManageUsers`, `ManageRoles`, `ManageMfaPolicy`, `IssuePasswordResets`, and `ManagePermissionGrants` remain Admin-only, non-grantable, and non-delegable.
- `ManageAccountStatus` and `RevokeSessions` are Admin-assignable but never delegable. A non-Admin holder may act only on a non-Admin target whose complete effective permission set is a subset of the actor’s.
- Delegated password reset and delegated role assignment are outside this release.
- Story 1 does not change the authorization or response contract of `GET /api/auth/users`; its broader target-filtered access belongs only to ALV-N018.
- Generate the no-overrides equivalence proof from the enums. After adding `ManagePermissionGrants`, the current expected matrix is 8 × 36 = 288 cells, but the test must not hard-code that value.

Follow the exact prompts now recorded in `GPT Docs/Alveara_Dental_Execution_Plan_v4.md`. Stop at `AWAITING_REVIEW` for every attempt; do not self-certify completion.
