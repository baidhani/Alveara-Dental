# ALV-N019 R04 — Pre-Implementation Briefing Approval

**Decision: APPROVED FOR STAGE 1 IMPLEMENTATION**

The revised R04 briefing and owner inventory resolve the changes required by the prior briefing review.  Implementation may begin for the narrowly scoped shared test-fixture hardening only.  Do not run a full Stage 2 regression during this implementation phase.

## Approved scope

- Warm the real clone connection pool before the fixture is released, using only the existing bounded classification for the known pool-acquisition timeout.
- Provide owner-side initialization cleanup that preserves the original exception and removes only the fixture-owned clone/files.
- Extract retry helpers as needed to keep the fixture maintainable.
- Convert and account for the inventoried owner families, including both observed R03 failure paths: `ImportIntakeServiceTests` and `SafetyTestBase` / `PrescriptionWorkflowSchemaTests`.
- Add the repository guard, narrow-classification tests, real forced-pruning proof, failure-cleanup tests, negative controls, and the 60-iteration preflight.

## Stage 1 evidence requirements

1. The real SqlClient pool-pruning mechanism is exercised.  The warm-up-disabled control arm must show at least one authentic first-open pool-acquisition timeout in at least 800 forced cycles.
2. The warm-up-enabled treatment arm completes 60 iterations of 15 forced cycles each, with every first owner/test setup open succeeding and zero `AlveraTest_*` databases/files after every iteration.
3. Owner-initialization failure tests cover both direct and `SafetyTestBase` paths, preserve the original exception, and leave no owned clone/files.  Cleanup failure is separately observable without concealing the original error.
4. The retry classifier rejects non-pool product, SQL, invalid-operation, and assertion exceptions without retrying them.
5. The full inventory/conversion report, per-iteration raw data, negative-control evidence, and exact changed-file list are included in the R04 Stage 1 handoff.
6. The 9.0.21 Pkcs pin and its scanner-clean proof remain intact; no unrelated package upgrade is permitted.

## Next transition

Submit R04 Stage 1 evidence for independent review.  Only after `APPROVED_PENDING_REGRESSION` may one new frozen full Stage 2 run be performed.  ALV-N020 remains unauthorized until final approval of ALV-N019.
