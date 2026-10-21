# Two-Stage Handoff Review — Decision

**Decision:** APPROVED WITH RECORDED SAFEGUARDS  
**Effective:** Stories beginning after `ALV-010-C01`  
**Does not apply retroactively:** `ALV-010-C01` remains single-stage.

The two-stage process is approved when broad regression is required by the testing cadence and the individual story prompt does not expressly require broad regression before its initial handoff.

## Binding safeguards

1. Stage 1 contains all focused acceptance, parent, authorization, data-integrity, concurrency, changed-subsystem integration, and prompt-required real-backend walkthrough evidence. Broad regression is labelled `PENDING` without implying it passed.
2. `APPROVED_PENDING_REGRESSION` is a conditional Stage 1 decision only. It moves the story to `AWAITING_REGRESSION`; it never makes a story complete or authorizes the next story.
3. The Stage 1 package freezes the exact Stage 2 commands, suites, walkthroughs, data/environment requirements, and expected outcome format.
4. Stage 2 runs only from the Stage 1 review-closure commit. Compared with the implementation commit, it may differ only in the Stage 1 review artifact, `.alveara/EXECUTION_STATUS.json`, and `.alveara/BUILD_STATE.md`. Any other change requires a new Stage 1 review.
5. A passing Stage 2 produces an additive regression package and returns the story to `AWAITING_REVIEW` for final review. Only final `APPROVED` may lead to `COMPLETE`.
6. A product or test regression failure is `CHANGES_REQUIRED` and starts a new attempt. An unavailable or otherwise inconclusive environment is `BLOCKED`; retain the Stage 1 decision and rerun the unchanged Stage 2 contract after repair.
7. Future gates, releases, milestones, and prompts that require broad regression use the same two-stage timing. Single-stage is allowed only when the reviewer documents that broad regression is necessary to assess a specific acceptance criterion before implementation review.
