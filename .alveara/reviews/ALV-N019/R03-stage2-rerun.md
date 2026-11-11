# ALV-N019 — Independent Stage 2 Review, R03 Rerun

**Decision: CHANGES_REQUIRED**

The sole permitted unchanged rerun also does not qualify: **5,696 passed and 1 failed**.  It cannot be combined with the initial run to establish a passing full regression.

## Evidence reviewed

- Rerun package: `.alveara/dist/Alveara_Regression_ALV-N019_56b05c0_R03_rerun.zip`
- Package SHA-256: `D2EDBE42643D6F1A0540E185F940A766FD3228F230932F8FB2CBB68BFC500885`
- Approved implementation snapshot: `56b05c02d5e447a8286e1dcd2f3750560f34f3cb`
- Rerun commit: `faa651b34b9eb03164fb4ac1f566606d9e51e266`
- Rerun evidence commit: `114dfecc17a9e3e9b428dfe6c40f0620eebf3958`

## Ruling

The authorized cleanup was properly scoped: it removed only the named first-run `AlveraTest_...` database and its two verified files, leaving `AlveraDev` intact.  The rerun preserved source/contract equivalence and all non-backend gates passed.

However, the rerun failed in a **different** class, `PrescriptionWorkflowSchemaTests`, at the same unhandled first pooled-open path after fixture initialization.  It left a second `AlveraTest_...` database and its two files behind.  Together, the two failures demonstrate a shared test-infrastructure gap:

- the fixture's pool-acquisition retry protects creation/drop but not the first pooled open used by test setup after the fixture returns; and
- failed initialization does not reliably clean up the fixture-owned database.

This is no longer an isolated environmental-inconclusive event.  The Pkcs remediation evidence remains valid—R03 demonstrates the vulnerable dependency is no longer resolved—but ALV-N019 cannot be marked complete until the test infrastructure produces a clean full regression on the remediated graph.

## Required R04 scope

Submit a new R04 Stage 1 pre-implementation briefing and handoff before any third full run.  Its narrowly bounded purpose is to harden the shared test fixture against this known pool-pruning race and failed-initialization leakage.

The briefing must specify and then prove:

1. A bounded, narrowly classified recovery at the first pooled open after clone initialization, or an equivalent deterministic warm-up before the fixture is released.  It must not retry arbitrary SQL/product exceptions.
2. Guaranteed cleanup of a fixture-owned database and files when initialization fails after the database was created.
3. Deterministic focused tests that force the pruning-tick condition, prove recovery, and prove no leftover `AlveraTest_*` database/files on the failure path.
4. No rollback or expansion of the PD-26 pin; retain the scanner-clean proof and the R03 evidence unchanged.
5. A fresh Stage 1 review and, only after approval, one new frozen full Stage 2 run.  No further unchanged rerun of R03 is authorized.

Before R04 work, clean the current second leftover only after verifying its exact `AlveraTest_<32 lower-case hex>` name and its two fixture-owned files.  Preserve both failed-run packages and cleanup records.  ALV-N020 remains unauthorized.
