# ALV-016-C01 R02 - Next-story impact

**Next scheduled item: GATE D (Treatment Completion and Financial Core Ready)**, then item 39 `STORY-010` (a portal course story) and item 40 `ALV-010-C01`. This attempt stops at `AWAITING_REVIEW`; the review closure must be recorded before Gate D starts. R01's next-story notes (`R01-evidence/NEXT_STORY_IMPACT.md`) stand; what R02 adds or changes is below.

## For Gate D
- **The required full backend regression now passes** (3686 of 3686), including the completion race test that failed in R01. Gate D's "financial and completed-procedure histories survive duplicate submissions, corrections and stale edits without destructive overwrite" evidence now has a stronger basis for the completion race specifically: a deadlock between two overlapping completions is retried, replayed or refused with the documented conflict, and a victim on every attempt leaves nothing (`CompletionDeadlockTests`, real deadlocks).
- **D4 (prescription workflow, shared safety context, no unsupported decision support)** is unchanged and ready, as in R01.
- **The "last successful broad regression" baseline.** This attempt's full regression is the first complete green one since item 37; `BUILD_STATE.md` records it as the baseline only after approval and closure, as the cadence response requires, so it does not yet (the closure commit should record: item 38, implementation `486e44c`, backend 3686 of 3686, vitest 1453 of 1453, mocked Playwright 136, repository checks 8).
- **Known remaining exposure to weigh at the gate:** the amendment of a completed procedure and the plan edits can still surface a raw provider exception if chosen as a deadlock victim (see `HANDOFF.md` "Findings in shared components"). The same `DeadlockRetryPolicy` can wrap them without change if the gate wants them covered.

## Interfaces and conventions now available
- **`DeadlockVictim.Is(exception)`** and **`DeadlockRetryPolicy.RunAsync(operation, ct, observe)`** (`Architecture/Concurrency/DeadlockRetry.cs`): a reusable, bounded, idempotent-by-construction way to run a whole operation again when SQL Server chooses it as a deadlock victim. Use it only around an operation that re-reads and re-checks everything from the start; pass a fixed pause or observer in tests (the constructor takes a delay function and an observer). A new operation adopting it must add its own engineered-deadlock test; the method in `CompletionDeadlockTests` (an exclusive table lock held by a second session, released into a cycle at a moment the test controls) is a ready pattern.
- **`TreatmentPlanException` `save_failed` 503** is now also the outcome of an exhausted deadlock retry for completion and adoption; the message is "The completion could not be saved because the database was busy, so nothing was completed" (adoption: "... adopted").
- **Structured log events:** `completion_deadlock_retry` and `completion_deadlock_retries_exhausted` (operation, attempt, max attempts, error class only).
- **Triggers:** unchanged, 51175 to 51181 used, **51182 and up are free**. No migration in R02; the chain tip is still `20261014024801_AddPrescriptionWorkflow`. **Permissions, routes, screens:** unchanged.
- **Prompt text and conventions unchanged:** no change to the next items' prompts is needed.

## Assumptions the reviewer may revisit
- 3 attempts in all and a 40 ms-per-failure pause with up to 40 ms of jitter are judgement values, not measured thresholds; they are constants of the policy.
- Only deadlock victims are retried; other transient errors are not (after them it cannot be known whether the work committed).
- R01's assumptions stand (a second Prescription composer tab; typing-error ceilings; STORY-016's review-note rule as the allergy-conflict policy).

## Unresolved decisions
- Whether to extend the policy to the amendment of a completed procedure and the plan edits, and whether to retire STORY-016's quick entry form (see `HANDOFF.md` "Unresolved questions").
