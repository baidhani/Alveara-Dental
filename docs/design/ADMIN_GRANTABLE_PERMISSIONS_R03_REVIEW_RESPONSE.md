# Admin-Grantable Permissions — R03 Reviewer Response

**Status:** `CHANGES_REQUIRED` — revise the proposal only. Do not amend the Execution Plan, create story control records, or implement code.

## What is accepted

The R03 improvements to last-enabled-Admin protection, cache/version consistency, invalid persisted-data handling, p95 performance evidence, immutable history, audit coupling, and the two-story delivery sequence are directionally accepted.

## Blocking security correction

The R03 proposal must not use the security-permission-only target rule:

```text
S ∩ Eff(Target) ⊆ Eff(Actor)
```

It is unsafe for `IssuePasswordResets` and `ManageRoles`.

The current password-reset endpoint returns a reusable reset token to the requester. A non-Admin holder of `IssuePasswordResets` could reset a Dentist account under the proposed rule, receive the token, take over that account, and thereby obtain clinical permissions the holder did not previously have. Restricting only security permissions does not prevent this escalation.

Likewise, `ManageRoles` restricted only by security permissions can alter roles involving clinical, patient, billing, or other non-security powers beyond the actor’s own authority.

## Required R04 policy

1. Keep these permissions permanently **Admin-only**, non-grantable, and non-delegable for the first release:
   - `ManageUsers`
   - `ManageRoles`
   - `ManageMfaPolicy`
   - `IssuePasswordResets`
   - `ManagePermissionGrants`

2. `ManageAccountStatus` and `RevokeSessions` may be Admin-assignable but never delegable only if every non-Admin action applies to an account whose **full effective permission set** is a subset of the actor’s full effective permission set:

```text
Eff(Target) ⊆ Eff(Actor)
```

No non-Admin may act on an Admin account. Self-disable, self-role-change, and equivalent self-escalation paths remain refused.

3. A future HR workflow that can reset passwords for broader staff categories requires a separate design. It must not give a non-Admin a reusable token capable of taking over a more privileged account. A target-delivery recovery design, verified recovery identity, and separate security review would be required.

4. The Office Manager delegation remains exactly as already approved: only `ImportDocuments`, only for Dentist, Hygienist, Assistant, FrontDesk, and Billing; never Admin, Unassigned, OfficeManager, protected permissions, or delegation administration.

## Required specification corrections

- `ManagePermissionGrants` increases the current permission enum from 35 to 36 values. The no-overrides proof is therefore **8 × 36 = 288 cells**. Prefer a generated count derived from the enum so future permission additions cannot stale the requirement.
- Story 1 must not change the authorization contract for `GET /api/auth/users` if its promise is “no ordinary access change.” Keep the proposed broader user-list authorization and target-filtered listing in Story 2. Story 1 may still enforce the target rules inside existing account-administration operations and test them through controlled API/test setup.

## Resubmission requirements

Return an R04 design proposal at `AWAITING_REVIEW` that:

- removes the R03 security-permission-only target rule and its security-target decision options;
- incorporates the protected and Admin-assignable sets above;
- updates every acceptance criterion, negative control, endpoint description, invariant, UI statement, test matrix, and open-decision table to match;
- updates the permission-count equivalence proof; and
- preserves the already accepted cache, last-Admin, corrupt-row, backup/restore, p95, full Stage 2, and two-story controls.

Do not proceed to implementation until the revised design is independently approved.
