# Alveara Test-Speed Parallelism Gate 2 Final Session v1 — Independent Review

**Decision:** NOT APPROVED FOR GATE 2 CLOSURE — TESTSPEED-P2 PARALLEL CONFIGURATION IS NOT QUALIFIED

**Reviewed package:** `C:\tmp\Alveara_TestSpeed_Parallelism_Gate2_FinalSession_v1.zip`  
**SHA-256:** `B6EC0D84153DB0BD1120ABF5A267798AC26BD0A3F3DA900F6C0DCA2005306EC1`  
**Review date:** 2026-10-07  
**Tested repository commit:** `1b93508`; local and unpushed. The checked repository configuration remains serial.

## Evidence accepted

- The controlled final-session tooling implements the approved Option C protocol. The final tool hashes match `session.json`; the accepted baseline has 28 samples, 18.34% median foreign CPU capacity, and the correctly fixed 28.34% session threshold.
- The valid opening serial run passes 2,297 / 2,297 tests in 41.74 minutes. Each of P1, P2, and P3 passes 2,297 / 2,297 tests with equivalent Phase 1a identifiers/outcomes, no skips, and stable four-worker wall times of 23.88, 23.43, and 23.62 minutes.
- Those three parallel runs meet the resource controls: `AlveraTest_` remains 54, `AlveraRestore_` 59, migration-failure databases 1, test logins 273, all databases 542, and no orphan process or listener on port 5193 is present throughout the session.
- The attempt history is complete and candid. The first session had a configuration defect, was aborted after its first misconfigured opening run, and is not used as acceptance evidence. The corrected session preserves every later attempt and applies the invalid/replacement rules without selecting a favorable rerun.
- The closing serial S1 attempt is invalid under the declared CPU rule (43 of 408 foreign-loaded samples). Its one permitted replacement has a real test failure and the driver stops as required.

## Why closure is not approved

The approved Gate 2 rule requires a same-session serial bracket: S0, P1–P3, S1, followed by the declared median-and-floor evaluation. There is no valid S1. Therefore the formal performance criterion cannot be evaluated.

The valid parallel evidence is encouraging but insufficient to substitute an unapproved baseline. Against S0 alone, the parallel median is 1.767x, below the 1.8x target. The invalid S1 attempt would produce a passing-looking median if selectively used, which is precisely why the protocol excludes it. The report correctly does not make that selection.

I do **not** authorize the proposed late S1 completion step. It would follow a stopped session, and correcting the discovered test would create a different commit from the one used for S0/P1/P2/P3. Running a single replacement serial measurement after that change would break the same-commit, bracketed-comparison control and create a new opportunity to select a favorable result.

## Separate test-quality finding

`BackupCryptoTests.A_file_that_is_not_a_backup_is_rejected_with_its_own_reason` is flaky by construction. Its `"ALVBK" + RandomBytes(500)` input can legitimately reach the decryptor's `corrupt_or_tampered` path, while the assertion requires `not_a_backup`. The supplied 20,000-iteration probe observes that outcome 305 times (1.5%), consistent with the closing run’s failure.

This is unrelated to parallel scheduling, but it is a valid test-quality defect. Handle it as a separate, narrowly scoped test correction: use deterministic malformed input that proves the intended `not_a_backup` path, retain an assertion that distinguishes the error reason, and validate it with an appropriate focused test and mutation/control evidence. Do not use its correction to revive or complete this P2 timing session.

## Required disposition

1. Keep `parallelizeTestCollections: false` and do not push or enable the four-worker configuration under TESTSPEED-P2.
2. Record TESTSPEED-P2 parallelism as **not qualified against its approved performance gate**. The accepted cleanup/retry/login-leak and restore-walkthrough corrections may be considered separately for integration, but they do not justify enabling parallel execution.
3. Open and review the deterministic backup-input test correction separately. A later, newly approved performance initiative may start a new full measurement protocol on a single fixed commit; it may not reuse this session as closure evidence.

The session provides useful stability and cleanup evidence, but its specified performance proof did not complete. Serial execution remains the approved configuration.
