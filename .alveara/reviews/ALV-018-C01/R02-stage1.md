# ALV-018-C01 — Independent Stage 1 Review R02

**Decision:** `APPROVED_PENDING_REGRESSION`  
**Reviewed:** 2026-10-27  
**Story:** ALV-018-C01 — Versioned Data Export and Portable Record Package  
**Package:** `.alveara/dist/Alveara_Handoff_ALV-018-C01_94e1347_R02.zip`  
**Package SHA-256:** `6D4871C213D7E923FB300631BCEF1CD493E3D8F88CCE4FE4273C55A297176881`  
**Implementation commit:** `94e1347e97582b3f89e8a359aaa49618328ce8b3`  
**Evidence commit:** `da0f8b12aac7e7c5a81fda393b1692544ead4252`

## Decision basis

- R01's failed Stage 2 is preserved as failed evidence, with a source-location record and hash inventory. It is not represented as a qualifying result.
- R02 changes only the shared test-database fixture and its tests; export-package product code, migrations, client code, scripts, and build configuration are unchanged from the previously approved Stage 1 implementation.
- The correction addresses the actual defect without weakening the safety property. The fixture writes exactly one numbered holder-check marker for each SQL 3702 failed drop attempt; holder-session rows remain individually logged. Tests require exactly four ordered markers, preserve the original SQL 3702 and bounded-attempt behavior, require the database to remain until explicit cleanup, and require every reported session to be named.
- A new deterministic two-real-holder test proves that several holder sessions during one failed attempt do not become several attempts. The two targeted tests passed their initial run and **10 sequential fresh-fixture repeats**; the affected `TestDatabase*` suite passed **59/59**.
- The new frozen contract requires the repaired pre-existing test to pass, the ALV-017-C01 R03 5,136-ID baseline to retain its outcomes, and exactly 198 listed new IDs for a total of **5,334 passed, 0 failed, 0 skipped**. It retains clean/equivalent-source, template, leak-count, client/repository, and seven real-backend walkthrough checks. Elapsed time remains operational evidence only.

## Required next action — Stage 2 only

1. Make the closure-only commit `ALV-018-C01: record stage 1 review`. It may modify only this review artifact, `.alveara/EXECUTION_STATUS.json`, and `.alveara/BUILD_STATE.md`; it must set this attempt to `AWAITING_REGRESSION` and record broad regression as required.
2. On the resulting clean, code-equivalent closure commit, run exactly once:

   ```sh
   sh .alveara/handoffs/ALV-018-C01/R02-evidence/stage2/run_stage2.sh 94e1347e97582b3f89e8a359aaa49618328ce8b3 <outside-repository-output-folder>
   ```

3. Do not change source, tests, migrations, scripts, build/package configuration, or general documentation before the run. Any such change voids this approval and requires another Stage 1 review.
4. If every frozen criterion passes, commit the regression evidence, package it as the R02 regression ZIP, and return it for final review. Do not mark the story complete.

No change is required before Stage 2.
