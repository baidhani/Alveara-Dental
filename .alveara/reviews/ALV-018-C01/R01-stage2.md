# ALV-018-C01 — Independent Stage 2 Ruling R01

**Decision:** `CHANGES_REQUIRED`  
**Reviewed:** 2026-10-27  
**Stage 1 implementation:** `1de9de5f08b903fe6b3d1776ffd3b22cddc282d6`  
**Stage 1 review-closure/run commit:** `b2870675a5e84913fbcbfc6340b931e042c44d0f`  
**Stage 2 evidence folder:** `C:/tmp/stage2-ALV-018-C01`

## Ruling

The frozen Stage 2 contract does **not** qualify. It correctly reports 5,332 passed and 1 failed of 5,333 expected backend tests. The source tree was clean and equivalent to the Stage 1 implementation; template, artifact-leak, client/repository, and every real-backend walkthrough criterion passed.

The failure is not caused by the ALV-018-C01 export-package implementation and it is not an environment-only interruption. It is a real defect in the previously completed shared test-infrastructure test `TestDatabaseInUseTests.A_holder_that_never_leaves_is_a_failure_after_the_bounded_attempts_with_the_first_3702_unchanged`.

Its test establishes four bounded drop attempts, then assumes exactly one SQL session-holder diagnostic line per attempt:

```csharp
Assert.Equal(TestDatabaseFixture.MaxDropAttempts, text.Split("holds it;").Length - 1);
```

The fixture correctly made four bounded retry attempts. SQL Server validly reported five held sessions across those attempts, so the assertion failed `Expected: 4; Actual: 5`. A holder diagnostic may report more than one session for a single failed drop; exact equality is therefore not the stated safety property and makes the test flaky/incorrect.

## Required correction

1. Preserve this failed Stage 2 evidence; do not rerun its frozen contract unchanged.
2. Correct the responsible shared teardown-test infrastructure. The proof must assert the actual contract: four attempts, original SQL 3702 preserved, the database remains until explicit cleanup, and a non-sensitive holder diagnostic is emitted for **each failed attempt**. It must not assume one holder session per attempt. Prefer an explicit per-failure diagnostic marker/counter over inferring attempts from session-row count.
3. Add focused proof that covers both one holder and multiple holders (or an equivalent deterministic seam), and demonstrate repeated stability of the corrected focused test plus the affected teardown test suite.
4. Record the correction as a new immutable attempt. Because shared test infrastructure is changed, provide Stage 1 focused evidence and a fresh frozen full-regression contract before another expensive full run.
5. ALV-018-C01 R01 cannot become complete. Its next qualifying full run must be on the corrected final tree under a new Stage 1-approved attempt; do not reuse this failed Stage 2 as a pass.

The run otherwise provides useful evidence: run identity/equivalence and clean tree passed; 4,023 template clones with zero fallback; artifacts unchanged; tsc, Vitest, build, lint, mocked Playwright, repository checks, and all seven walkthroughs passed.
