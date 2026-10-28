# ALV-018-C02 — Independent Stage 1 Review, R01

**Decision: APPROVED_PENDING_REGRESSION**

This is a Stage 1 approval only. It approves the implementation and its focused evidence for the one frozen Stage 2 regression run; it is not final completion and it does not authorize any source change before that run.

## Package identity

- Package: `.alveara/dist/Alveara_Handoff_ALV-018-C02_a13d5b4_R01.zip`
- SHA-256: `2273645075F547C46948169D8904B4A7C4EC7E99729EC1164321B1E3F3AA43D2`
- Implementation commit: `a13d5b44b98a6c5fc72cd967e5415a4990027b73`
- Evidence commit: `7d501ace2328137e8aca140f84761faf12ea5aef`
- Status/packaging commit: `663034e3b4be8aed5f09a4125eeba966341a0c06`

## Independent findings

The repository state, package manifest, handoff, focused artifacts, and critical implementation paths support the claimed scope.

- The import controller is permission-gated by `BulkImportData`; opening-balance commit and retry also require `ManageBilling` before changing anything.
- The dry run persists only import-batch/row results. It validates current data without creating a patient, appointment, or ledger entry.
- Commit revalidates current state. Patient, appointment, and balance rows are replay-safe through stable per-row idempotency keys; a retry is refused while a current-round job remains pending or in progress.
- Opening balances are appended as immutable adjustment entries, accept only an empty account, retain their import-batch correlation id, and reject a changed account rather than overwrite its history.
- Database constraints and triggers preserve batch provenance, prevent deletion, prevent completed-batch/row mutation, and protect the immutable identity of a row.
- The R01 evidence records all eight acceptance criteria passing, 147/147 new focused backend tests, 928/928 API tests, 62/62 STORY-018 import/export tests, six caught negative controls, client/repository gates, and eight real-backend walkthroughs. No Stage 2 full run was performed prematurely.

The deliberately disclosed operational limits (for example, production rollback with import records and real-disk sweep timing) are documented rather than hidden. They do not invalidate the present acceptance scope.

## Required Stage 2 procedure

1. Make only the normal review-closure/control-record commit. Do **not** alter `src/`, tests, frozen Stage 2 files, or the implementation commit.
2. On that clean closure commit, run the frozen script exactly once, with no other workload using the test environment:

   ```sh
   sh .alveara/handoffs/ALV-018-C02/R01-evidence/stage2/run_stage2.sh a13d5b44b98a6c5fc72cd967e5415a4990027b73 C:/tmp/stage2-ALV-018-C02-R01
   ```

3. Supply the resulting regression package for final review. It must qualify under its own frozen evaluator: exactly 5,481 passed backend tests with no failures/skips; unchanged prior test identities/outcomes plus exactly 147 listed new identities; clean tree before/after; template and cleanup checks; and all specified client, repository, and real-backend gates.

Any source/test/evidence-contract change after this approval invalidates this approval and requires a new Stage 1 handoff. Do not start `ALV-N006` until final review marks ALV-018-C02 complete.
