# ALV-001-C01 R05 — Next Story Impact

Supersedes R04's `NEXT_STORY_IMPACT.md` with one addition; everything else there still applies unchanged.

## New for future stories to build on

- **A concurrency test that claims to prove a threshold/race outcome must independently assert the threshold is actually reachable within the test's own setup**, not just assert the two permitted final outcomes. R04's test asserted valid outcome shapes but primed a count that could never reach the real threshold, so it passed regardless of whether the underlying fix worked. Future concurrency tests in this codebase should assert their own precondition (e.g. `Assert.Equal(4, primed.FailedLoginAttempts)`) before racing, not just the post-race outcome.
- **When a conditional "consume once" update depends on a second conditional update elsewhere in the same request (here: challenge consumption gated on a recovery-code consumption that might itself lose a race), a failure of the second update must roll back the first, not just fail to record an audit for it.** The R04 fix made challenge-consumption-plus-audit atomic for the single-recipient case (same challenge, same code, replayed) but missed the case where the *other* conditional update in the same transaction (the recovery code) is what actually loses - rollback, not selective committing, is the general-purpose fix for "this multi-step conditional transition either all happens or none of it does."

## Prompt changes relevant to the next scheduled item

None identified that require the Execution Plan document itself to change before `ALV-N009` begins.
