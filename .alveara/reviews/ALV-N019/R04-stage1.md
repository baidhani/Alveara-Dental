# ALV-N019 — Independent Stage 1 Review, R04

**Decision: APPROVED_PENDING_REGRESSION**

R04 satisfactorily hardens the test-fixture failure mode that made the two
unchanged R03 Stage 2 attempts inconclusive. The implementation is confined to
test infrastructure and its controls. It preserves the R03 `System.Security.Cryptography.Pkcs`
9.0.21 remediation and contains no production-application, client, project,
solution, or package-reference change.

## Reviewed material

- Stage 1 package: `.alveara/dist/Alveara_Handoff_ALV-N019_115821d_R04.zip`
- Package SHA-256: `2B2BAC9C0ED1042F5837CD4BFC40C0867FE96481FB5B2F3A4790F5FA619B0716`
- Implementation commit: `115821d9d9c1632a82e4131602472e46a0e215c2`
- Evidence commit: `f4268b88d0126618009ca19368e9939d7daa0d71`
- Status-record commit: `289eaad58bcbd9d9623dbf9db790f5bd7abd9997`
- Prior decisions: `R03-stage2-inconclusive.md` and `R03-stage2-rerun.md` —
  both identify a backend test-fixture database-pool timeout; the unchanged
  rerun failed on a different test with the same failure signature.

## Independent findings

- The actual R04 commit has 108 changed files: the fixture/retry implementation,
  preflight script, 18 focused tests, and the expected mechanical conversion of
  fixture owners. Repository comparison found no application, client, project,
  solution, or package-reference changes.
- The control arm recorded 154 authentic first-open pool-acquisition timeouts
  across 800 forced-pruning cycles. This demonstrates that the test can
  reproduce the condition; it is not merely a no-failure smoke test.
- The treatment arm recorded 60 iterations × 15 cycles = 900 successful
  first-open operations, with zero first-open failures. The narrow warm-up retry
  recovered 39 warm-up races. Every treatment iteration recorded zero fixture
  database and file leftovers.
- The retry is confined to the recognized pool-acquisition condition, is bounded,
  and has negative controls. Removing warm-up failed the treatment proof;
  removing owner cleanup failed its controls; widening the classifier failed its
  guard controls. The restored implementation passed its check.
- Owner-side failure cleanup now removes only the fixture clone and rethrows the
  original owner-setup exception. Guard coverage includes the shared safety base
  and the inventory's direct fixture owners, including the owner previously
  discovered by the inventory guard.
- The focused infrastructure set, owner samples, build, and repository/client
  checks pass as recorded. No broad regression run was performed at Stage 1,
  which is correct under the two-stage policy.

## Frozen Stage 2 authorization

Run the prepared R04 Stage 2 contract exactly once on the unchanged R04
implementation snapshot `115821d9d9c1632a82e4131602472e46a0e215c2`.

This review is the authorization to begin that run. No additional product-owner
approval is required. Do not change code, tests, scripts, configuration,
dependencies, solution/project files, catalog/control records, or the frozen
contract before the run. Preserve the two prior R03 inconclusive attempts and
the R04 preflight evidence as immutable history.

The fresh Stage 2 contract must use the prepared **5,715 backend-test** identity
baseline (the accepted R02 baseline of 5,697 plus R04's 18 new test identities),
and must complete all required backend, client, repository, walkthrough,
injected-chain, equivalence, and leak-accounting criteria. A product or test
failure requires a correction attempt. An environmental-only failure may be
classified only with a recorded diagnosis, cleanup evidence, and the unchanged
rerun process defined by the execution-control policy.

After independent approval of that Stage 2 package, ALV-N019 may be marked
`COMPLETE` and ALV-N020 may begin. Do not begin ALV-N020 beforehand.
