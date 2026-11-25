# ALV-N030-C01 / ACCT — R08 Addendum Review

**Decision:** CHANGES_REQUIRED

**Reviewed:** 2026-11-25

**Package:** `.alveara/dist/Alveara_Briefing_ALV-N030-C01_ffadbd2_ADDENDUM-R08.zip`

**Package SHA-256:** `66EF5B5E02EECFAE953A74BBA4F2216A95F85ABB159663B4524B4B133D3BE0F7`

## Scope reviewed

Section 18 only of `docs/design/ACCT_PRE_IMPLEMENTATION_BRIEF.md`, against the source context and prior R04, R06, R07 and ALV-N030 R02 decisions supplied in the package. All 22 manifest-listed member hashes were independently recomputed successfully. No code is authorized from this review.

## Ruling

The principal architecture is accepted: a Practice Owner is a protected designation attached to an active linked staff user whose account role is Administrator; the designation is not a role or a second permission source. Atomic owner creation, Deployment-Administrator-only transfer, atomic transfer-and-disable, console recovery, and the C01 -> C02 -> N027 execution order are directionally correct.

The addendum is not ready for implementation because four previously required corrections remain unresolved.

## Required corrections

1. **Restrict the initial-designation actor to Administrator.** `POST /api/practice-owner/initial` still relies on `ManageUsers`, while section 18.3 E1 and S11 constrain only the target. The one-time protected designation must require the acting account itself to have role `Admin` (including the Deployment Administrator), not merely an effective `ManageUsers` grant. Add a negative test proving that a non-Administrator holding `ManageUsers` cannot designate an existing Administrator staff member.

2. **Do not broaden the checkout clinical action to every provider-linked login role.** Section 18.9 allows any login role linked to an active provider profile. The current provider record does not yet carry a controlled professional classification: `ProviderProfile.Specialty` is free text, and current creation rules can attach a provider profile without proving Dentist/Hygienist qualification. For C01, narrowly admit `Admin` alongside the existing Dentist/Hygienist roles, always with the active booked-provider link. Front Desk, Assistant, Billing and Office Manager must remain refused even if incorrectly linked to a provider profile. N027 may replace the role portion only after it supplies and proves the controlled professional classification.

3. **Define an enforceable idempotency mechanism for the migration audit row.** Section 18.7 says a second execution writes no second `PracticeOwnerInvariantViolation`, but it gives no deterministic identifier, unique key, or exact `NOT EXISTS` predicate. Specify the stored key/predicate and test it directly. The mechanism must distinguish a repeated execution over the same detected state from a genuinely new violation state without rewriting immutable audit history.

4. **Remove the required general owner walkthrough.** Lines 770–772 and slice 6 require the owner to rerun general N030 steps B3–C3 plus transfer/recovery steps. Those walkthroughs are not mandatory. C01 acceptance must rely on focused API/service/concurrency tests, updated S14 browser automation, and targeted recovery/transfer automation; any general manual inspection remains at the owner's discretion. This does not remove the separate C02 gate: after the QR implementation and automated tests pass, a short targeted walkthrough using a real authenticator application to scan the QR code and successfully complete MFA enrollment is **required before mandatory Deployment Administrator MFA enforcement may be enabled**.

## Decisions confirmed

- Active-owner role change, profile inactivation and login disabling are refused until ownership is transferred.
- Transfer is available only to the Deployment Administrator and requires password plus TOTP when MFA is enabled.
- Transfer changes only the designation; the former owner keeps the Administrator role unless the atomic disable option is selected.
- C01 resets MFA during Deployment Administrator recovery but does not enforce re-enrolment.
- ALV-N030-C02 is the immediate next item: Stage 1 delivers and proves local QR provisioning with automated checks and the required targeted real-authenticator walkthrough; only after that proof passes may Stage 2 enable mandatory MFA for created/recovered Deployment Administrators. C02 completes before ALV-N027.
- The U-046 dashboard panel remains outside C01.
- Server transaction/lock enforcement is used without a new cross-table trigger.

## Next review

Submit a revised section 18 as R09. No implementation code or test run is authorized before that revision is independently approved.
