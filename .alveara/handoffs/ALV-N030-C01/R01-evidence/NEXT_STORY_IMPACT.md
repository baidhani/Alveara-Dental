# ALV-N030-C01 R01: next-story impact

**ALV-N030-C02** (next, owner order) is MFA QR enrolment (U-050), then mandatory MFA for newly created or recovered Deployment Administrators.
- Recovery's reset (`AccountService.ResetForRecoveryInTransactionAsync`) is the single place to add "and require enrolment". It already runs in the recovery transaction (18.6a).
- The transfer step-up (`ReauthenticateMfaAsync`) already requires a code when MFA is on, so an enforced Deployment Administrator MFA needs no transfer change.
- The QR walkthrough with a real authenticator is a required C02 Stage 1 gate (R10).

**ALV-N027** (after C02):
- **The checkout rule** admits Admin, Dentist and Hygienist only, with the active booked-provider link. ALV-N027 may replace the role part only once it supplies and proves a controlled professional classification (R08-2).
- **The guided setup wizard** must use the normal permission checks and be offered only to the designated owner. `isPracticeOwner` and `practiceOwnerStatus` are on `/api/auth/permissions`.
- **Owner-related writes** must take the locks in the 18.3a order: the administrator guard, then the designation, then accounts by id, then staff profiles by id.

**ALV-N013** (installer):
- It should offer `Alveara.Api recovery issue-token` next to `setup issue-token`.
- The two migrations `AddSetupTokenPurpose` and `RecordPracticeOwnerInvariantViolations` run through the normal upgrade. The second records and never repairs.

**ALV-N028** (regression) gains these files and specs:
- `PracticeOwnerInvariantTests`, `PracticeOwnerCreationTests`, `PracticeOwnerTransferTests`, `DeploymentRecoveryTests`, `OwnerInvariantAuditTests`, `CheckoutTests` S29;
- the client `OwnerAdministration.test.tsx`;
- the `owner-transfer-recovery-real-backend` walkthrough.

It should also add `account-bootstrap-real-backend.spec.ts` and `server-states-real-backend.spec.ts` to the mocked configuration's ignore list (R01.md disclosure 6).

**U-046:** the slide-in dashboard panel stays with the UI improvements (Q2). Its single server call already exists (C01 slice 2), so the panel is client-only.

**U-049:** resolved by C01, pending review approval.
