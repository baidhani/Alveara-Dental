# ALV-001-C01 R06 — Next Story Impact

Supersedes R05's `NEXT_STORY_IMPACT.md` with one addition; everything else there still applies unchanged.

## New for future stories to build on

- **A concurrency/race test must prove branch entry, not just a compatible final state.** Twice in this story's review history (R04's threshold-math bug, R05's premature-write and no-coordination bugs), a concurrency test passed while never actually exercising the branch it claimed to prove, because the final state it asserted was also reachable via a completely different, non-racing path. The fix each time was the same shape: force the exact interleaving deterministically (via priming to an exact boundary, via a coordination seam, or via purely sequential ordering when that alone proves the claim) rather than trusting uncontrolled scheduling or a plausible-looking end state.
- **`AccountService`'s three `internal Func<Task>?` test-only coordination seams** (`TestHook_BeforeStepUpConditionalReset`, `TestHook_AfterRecoveryCodeVerifiedBeforeConsumption`, `TestHook_AfterRecoveryCodeConsumedBeforeCommit`) are now an established, reusable pattern in this codebase for deterministically testing a race window inside a service method without restructuring the method itself: a null-by-default `internal Func<Task>?` property, awaited at the exact point a test needs to pause, visible to the test assembly via the existing `InternalsVisibleTo`. Future stories needing to deterministically prove a similar race/rollback branch should follow this same pattern rather than relying on `Task.WhenAll` over uncontrolled tasks.

## Prompt changes relevant to the next scheduled item

None identified that require the Execution Plan document itself to change before `ALV-N009` begins.
