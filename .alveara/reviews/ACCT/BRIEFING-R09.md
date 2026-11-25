# ALV-N030-C01 / ACCT — R09 Addendum Review

**Decision:** CHANGES_REQUIRED

**Reviewed:** 2026-11-25

**Package:** `.alveara/dist/Alveara_Briefing_ALV-N030-C01_e7dc3c0_ADDENDUM-R09.zip`

**Package SHA-256:** `80C7C478E324636CE36A02DC262FE4B5415D53098051B3CBC3ABE81867AD1506`

## Scope reviewed

Section 18 only of `docs/design/ACCT_PRE_IMPLEMENTATION_BRIEF.md`, including its resolution of `BRIEFING-R08.md`. All 23 manifest-listed member hashes were independently recomputed successfully. No implementation code is authorized by this review.

## R08 corrections

All four R08 corrections are closed:

1. E1a and S11b now require and prove an Administrator actor for initial designation.
2. Section 18.9 narrowly adds Admin to Dentist/Hygienist and preserves the active booked-provider link; the other roles remain refused.
3. Section 18.7 defines a deterministic state fingerprint, primary-key derivation and exact locked `NOT EXISTS` insert, with repeat and changed-state proof in S30.
4. C01 has automated acceptance only; the short real-authenticator walkthrough remains a mandatory C02 Stage 1 gate before C02 Stage 2 enforcement.

The owner decisions confirmed at R08 otherwise stand.

## Required corrections

1. **Close the initial-designation actor TOCTOU.** E1a reads the actor's stored role and enabled state before the transaction opens. A concurrent role downgrade or disable can therefore commit after that read but before the designation, allowing an account that is no longer an enabled Administrator to perform the protected action. Move the authoritative actor check into the serializable designation transaction, lock the actor account row together with the target account in a documented fixed order, and re-check role, enabled state and account kind under that lock. A preliminary check may remain for fast refusal, but it is not the control. Extend S11b with designation racing an actor downgrade/disable: either the designation commits while the actor remains eligible and the competing change is serialized, or designation is refused; an ineligible committed actor must never designate.

2. **Make concurrent transfers reject stale intent rather than both succeed.** The transfer body contains only the destination, while section 18.5 explicitly allows the second different-key request to proceed from the newly committed owner. Two simultaneous requests from separate tabs could therefore transfer A→B and then B→C, even though the second dialog was confirmed while A was the displayed owner. Add the expected current designation identity (preferably `expectedDesignationId`, alternatively both expected owner id and designation version) to the request and idempotency fingerprint. After locking the active designation, require it to match; otherwise return 409 `owner_changed` with no mutation. Revise S35 so two different-key transfers based on the same expected designation produce exactly one committed transfer and one conflict, while readers still never observe zero or two active owners.

## Next review

Submit the two narrow concurrency corrections as R10. Do not reopen the resolved provider, migration-audit, manual-walkthrough or C02 MFA decisions. No code or test run is authorized before R10 approval.
