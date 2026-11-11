# ALV-N019 R04 — Pre-Implementation Briefing Review

**Decision: CHANGES_REQUIRED before implementation authorization.**

The proposed test-infrastructure-only direction is appropriate: warm the clone pool before release, preserve the narrow bounded pool-acquisition retry, clean up after owner initialization fails, run the deterministic 60-iteration preflight, and defer the expensive full regression until Stage 1 approval.  No full run is authorized during implementation.

Before the owner gives implementation Go, revise the briefing to include these binding corrections.

## 1. Require the real forced pruning mechanism

The acceptance proof must force the actual SqlClient pool-pruning condition used in the documented TESTSPEED-P2 reproduction.  An injected exception may be an additional narrow-classification test, but it cannot substitute for demonstrating that the warm-up protects the real first pooled-open path.  The proof must show both:

- without the warm-up, the real forced pruning condition reaches the known pool-acquisition timeout; and
- with the warm-up and its existing bounded retry, the subsequent owner/test setup open succeeds.

## 2. Inventory and cover every relevant fixture owner

`SafetyTestBase` alone is insufficient: the initial R03 failure arose from `ImportIntakeServiceTests`, while the rerun failure arose through `SafetyTestBase`.  Before changing code, inventory every fixture owner that calls `Fixture.InitializeAsync()` and performs setup afterward, including inherited/shared bases and direct owners.

The briefing must name the complete set and state how each is covered.  A shared helper is preferred where it genuinely covers a family of owners, but no owner may be deferred to a later handoff merely because it is not in `SafetyTestBase`.  The R04 Stage 1 evidence must include the inventory and prove owner-side failure cleanup for both observed failure paths.

## 3. Make failed-initialization cleanup exact and observable

The owner wrapper must preserve the original setup exception, perform best-effort cleanup only for the fixture-owned clone, and surface cleanup failure distinctly without hiding the original cause.  Tests must demonstrate that a failure after database creation leaves no `AlveraTest_*` database and no associated fixture-owned files.

## 4. Retain the required preflight contract

The actual-pruning preflight must run **60 consecutive iterations**.  Each iteration must pass and independently verify zero `AlveraTest_*` databases/files afterward.  Include raw per-iteration output, the force mechanism, and the cleanup check in the R04 Stage 1 package.  Negative controls must prove removal of the warm-up and removal of owner cleanup are caught.

## 5. Preserve R03 scope and testing flow

- Do not alter the 9.0.21 Pkcs pin or expand into other package updates.
- Do not weaken or broaden the retry classifier beyond the specific pool-acquisition timeout.
- A revised R04 briefing must be approved before code changes.  Then Stage 1 focused evidence, including the preflight, is reviewed.  Only after Stage 1 approval may one new frozen full Stage 2 regression run occur.  No additional owner approval is required beyond the normal Stage 1 authorization.
