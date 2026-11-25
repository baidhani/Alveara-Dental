# ALV-N030-C01 / ACCT — R10 Addendum Review

**Decision:** APPROVED_FOR_IMPLEMENTATION

**Reviewed:** 2026-11-25

**Package:** `.alveara/dist/Alveara_Briefing_ALV-N030-C01_0a1d3c6_ADDENDUM-R10.zip`

**Package SHA-256:** `619A7F98BAAC2CBF9374F68545EC8C96E0ACCB7948E31D1F704B17BF314BB2CE`

## Scope reviewed

Section 18 only of `docs/design/ACCT_PRE_IMPLEMENTATION_BRIEF.md`, including the R10 resolution of `BRIEFING-R09.md`. All 24 manifest-listed member hashes were independently recomputed successfully. The two secret-scan matches are code identifiers assigning generated temporary passwords, not embedded credentials.

## Ruling

Approved. R10 closes the two remaining concurrency defects without reopening the owner decisions or the four corrections already closed at R09.

1. **Initial-designation actor eligibility is transactionally authoritative.** Section 18.3a establishes one lock order for every owner-affecting operation: administrator guard, designation range/row, account rows by id, then staff rows by id. E1a rechecks the actor's stored role, enabled state and account kind under those locks. S11b includes forced-gap and live race proofs, so a downgrade or disable cannot leave an ineligible actor designating an owner.
2. **Transfer rejects stale intent.** The request and idempotency fingerprint now carry `expectedDesignationId`. After the active designation is locked, a mismatch returns 409 `owner_changed` with no mutation. S35 requires exactly one of two different-key requests based on the same designation to commit, while readers never observe zero or two active owners.

The rest of Section 18 remains approved as revised: Administrator-role Practice Owners, atomic creation, Deployment-Administrator-only transfer and recovery, protected owner account state, narrowly safe provider eligibility, deterministic migration-audit idempotency, ids-only stored audit text, and automated C01 acceptance.

## Execution requirements

- Implement ALV-N030-C01 in the six reviewed slices. Show each slice before running its focused proof.
- Focused checks reasonably expected within five minutes may run under the standing rule. Obtain the owner's approval before any longer, broad, full-regression or chain run.
- C01 requires no owner manual walkthrough. Its acceptance uses the focused service/API/concurrency tests, updated S14 browser automation and the targeted transfer/recovery browser spec.
- C01 must not add mandatory MFA enrolment or a `MustEnrollMfa` gate.
- Immediately after C01 closes, execute ALV-N030-C02 before ALV-N027. C02 Stage 1 implements local QR provisioning and automated proof. A short targeted walkthrough using a real authenticator app to scan the QR and complete enrolment is mandatory before C02 Stage 2 may enable Deployment Administrator MFA enforcement.
- Update the brief's top-level status after this approval so Section 18 names R10 as the approved revision.

No broad regression is required here; `fullRegression` remains `DEFERRED_TO_ALV-N028`.
