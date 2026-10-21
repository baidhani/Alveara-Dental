# Proposal: review the handoff before the full regression run (two-stage review)

**Status:** APPROVED WITH RECORDED SAFEGUARDS. The binding protocol is now in `.alveara/HANDOFF_SCHEMA.md` v3, the Execution Plan, and the Engineering/Product Reference.
**Raised by:** the product owner, 2026-10-20, during `ALV-010-C01`.
**Applies from:** the first story after `ALV-010-C01`. `ALV-010-C01` itself keeps the current single-package flow.

## 1. The problem

When a story needs a full regression run (Section 4 says when), the current flow is: implement, run everything including the full backend suite, package, review. If the reviewer returns `CHANGES_REQUIRED`, the next attempt needs a new full run. The full backend suite is now the slowest step by far.

| Measurement | Value |
|---|---|
| Full serial backend run, 2026-10-08 (2405 tests) | 48 min 58 s |
| Full serial backend run, `ALV-010-C01`, 2026-10-20 | started 19:12; not yet finished when this was written, expected about 2 h 20 min (an estimate, not a measurement) |

A story that is revised once therefore costs two full runs, and the first run is spent on code the reviewer may change. The suite also grows with every story. A separate, reviewed test-speed initiative will shorten the run itself; this proposal removes wasted runs whatever the run time is.

## 2. How this relates to what already exists

The Binding Testing Cadence Notice (2026-10-13) and Standard Production Prompt Rule 16 already limit full regression to: high-risk stories, the third consecutive low/medium-risk story, quality gates, releases, milestones, and prompts that require it. **This proposal does not change when a full regression is required.** It changes only *when in the review sequence* the required full regression is run and reviewed, and only for stories where it is required. Stories that do not need a full regression keep the current single-package flow unchanged.

## 3. Proposed flow

**Stage 1: package and review without the full backend run.**
The Stage 1 package contains everything the cadence already requires on every story: the story's acceptance tests, parent-course regression, material failure, authorization, data-integrity and concurrency tests, targeted integration tests of every changed subsystem, `tsc`, vitest, build, lint, the real-backend walkthrough(s) the prompt requires, negative controls and leak counts. `TEST_RESULTS.md` states that the full backend regression is **pending by design**, names the story's risk class, and names the reason a full run is required. The story's status is `AWAITING_REVIEW` and its record says `fullRegression: "PENDING"`. The reviewer may return the existing decisions (`CHANGES_REQUIRED`, `BLOCKED`, `REOPENED`) with their existing meaning, or the new decision below.

**Stage 1 decision: `APPROVED_PENDING_REGRESSION`.**
The reviewer accepts the implementation subject to the full regression passing on the approved code. The story is **not** `COMPLETE`. It moves to a new status, `AWAITING_REGRESSION`. A closure-style commit records the decision (see Section 5).

**Stage 2: full regression on the approved code, then final review.**
The coding agent runs the full regression (backend, frontend, repository checks, mocked browser, and any real-backend walkthroughs the cadence requires) on a commit that is *code-equivalent* to the approved implementation (Section 6, rule 2). It records the result as an additive, immutable regression addendum and sends a small regression package to the reviewer. The reviewer returns `APPROVED` (then the normal closure commit moves the story to `COMPLETE`) or `CHANGES_REQUIRED` (normal new attempt, new Stage 1, as today).

## 4. When two-stage applies

Whenever the cadence requires expensive broad regression for a future story, milestone, release, or gate after `ALV-010-C01`. The risk classification declares the need (for example "high-risk: schema change"). Future prompts use two-stage timing even when they require the full suite; the suite remains mandatory at Stage 2. Single-stage is allowed only where the reviewer records that broad regression itself is necessary to assess a specific acceptance criterion before implementation review. Current work already running broad regression keeps its existing flow.

## 5. Proposed edits to `HANDOFF_SCHEMA.md`

| Section | Change |
|---|---|
| 1 Status Lifecycle | Add `AWAITING_REGRESSION` (after a Stage 1 `APPROVED_PENDING_REGRESSION`, before `COMPLETE`). State that only a final `APPROVED` review closure may produce `COMPLETE`, so the "never the coding agent alone" rule is unchanged. |
| 2 Immutable Attempts | Add: a regression addendum belongs to one attempt, is additive, and never overwrites that attempt's handoff, evidence, ZIP or review. |
| 3 Commit Order | Add an optional step 4 for two-stage stories: **regression evidence commit** (`<ALV-ID>: record regression evidence`), touching only the addendum folder, the status record and `BUILD_STATE.md` lines. It records the commit the regression ran on and the implementation SHA, never its own SHA (Section 9 unchanged). |
| 4 Review Decisions | Add `APPROVED_PENDING_REGRESSION` as a Stage 1 decision. Store it unchanged at `.alveara/reviews/<ALV-ID>/<attempt>-stage1.md`. Its recording commit (`<ALV-ID>: record stage 1 review`) sets `AWAITING_REGRESSION` and `fullRegression: "REQUIRED"` and changes nothing else. The final review is stored at `<attempt>.md` as today. |
| 5 ZIP | Stage 1 uses the existing package, with `MANIFEST.json` `fullRegression: "PENDING"` and `TEST_RESULTS.md` stating it. Stage 2 adds a regression package `Alveara_Regression_<ALV-ID>_<implementation-short-sha>_<attempt>.zip` containing `REGRESSION_RESULTS.md` (exact commands and pass, fail and skip counts), the results files, leak counts before and after, `GIT_STATE.md` (implementation SHA, the SHA the run executed on, the code-equivalence check, evidence SHA) and a `MANIFEST.json`. Package Safety (Section 8) applies unchanged. |
| Prompt Rule 16 | Add: for a story that requires full regression, the agent may package for Stage 1 before running it, provided the package declares the full regression pending and the story record says so. The cadence classification, batching and gate rules are unchanged. |
| Prompt Rule 21 | Replace "leave the story `AWAITING_REVIEW`, and stop" with: leave it `AWAITING_REVIEW` (single package) or `AWAITING_REVIEW` with `fullRegression: "PENDING"` (two-stage), and stop. |
| `EXECUTION_STATUS.json` (field list in the schema or plan) | Add `fullRegression`: `NOT_REQUIRED`, `PENDING`, `REQUIRED`, `PASSED` or `FAILED`, and `regressionEvidencePath` and `regressionRunCommit` (null until stage 2). |

## 6. Safeguards (what keeps this honest)

1. **No `COMPLETE` without the full regression.** `AWAITING_REGRESSION` is not complete, and Stage 1 approval is conditional by name. The final `APPROVED` still goes through the existing closure commit.
2. **The regression must run on the approved code.** The run starts only from the Stage 1 review-closure commit. Its diff from the implementation commit may contain only the exact Stage 1 review artifact, `.alveara/EXECUTION_STATUS.json`, and `.alveara/BUILD_STATE.md`. Any other change—including source, tests, migrations, scripts, build/package configuration, or a general documentation edit—voids the Stage 1 approval for that changed tree; the new code goes back to Stage 1 review.
3. **Fixing a Stage 2 failure is a new attempt.** If the full regression fails, the story becomes `CHANGES_REQUIRED` and a new attempt (`R02`) starts with a new Stage 1, exactly like a rejected review today. The failed run is kept as evidence, not overwritten.
4. **Honest labelling.** Stage 1 never claims regression evidence it does not have. Every Stage 1 document that mentions regression says "pending".
5. **A reviewer can always choose single-package.** Two-stage is an option the reviewer may refuse per story.
6. **Stage 1 still carries the evidence that finds most defects.** On `ALV-010-C01`, for example, the real-backend walkthrough found three defects (an empty encounter picker, a stale tooth carried to the next import, and a dark-mode contrast failure) that no unit test had caught.

## 7. What this saves, and what it does not

- **Saves:** the full run for any attempt the reviewer rejects, which is currently a complete wasted run. If, say, one story in three is revised, about a third of full runs disappear.
- **Does not save:** the full run for a story that passes first time. That cost is addressed by the separate test-speed initiative.
- **Adds:** one small regression package and one status-recording commit per two-stage story; a second review touch. The second review is short: a results file, not a code review.
- **Risk:** a failure in Stage 2 forces a new attempt after approval, which is a sharper experience than a failure found before packaging. In practice the targeted evidence in Stage 1 is meant to make that rare, and the alternative is to find the same failure only after a 2-hour wait.

## 8. Worked timeline (illustrative)

| Step | Single package (today) | Two-stage |
|---|---|---|
| Implementation and targeted evidence | 3 h | 3 h |
| Full backend run | 2 h 20 min | not yet |
| Review 1 | reviewer returns `CHANGES_REQUIRED` | reviewer returns `CHANGES_REQUIRED` (no full run wasted) |
| Fix and targeted evidence | 1 h | 1 h |
| Full backend run | 2 h 20 min | not yet |
| Review 2 | `APPROVED` | `APPROVED_PENDING_REGRESSION` |
| Full backend run | already done | 2 h 20 min |
| Review 3 | n/a | `APPROVED` (results only) |
| Full runs spent | 2 | 1 |

## 9. Resolved reviewer decisions

1. `APPROVED_PENDING_REGRESSION` is approved as the Stage 1 decision.
2. The equivalence check is stricter than the proposed general path whitelist: Stage 2 runs only from the Stage 1 review-closure commit, whose diff from implementation may contain only the exact Stage 1 review artifact, `EXECUTION_STATUS.json`, and `BUILD_STATE.md`.
3. Required real-backend walkthroughs remain in Stage 1 and repeat in Stage 2 when included in the frozen broad-regression contract.
4. `ALV-010-C01` remains single-stage.
5. A product/test Stage 2 failure is `CHANGES_REQUIRED` and starts a new attempt. An environment-only inconclusive result is `BLOCKED` and may rerun the unchanged Stage 2 contract after repair.
6. The exact Stage 2 suite list is frozen in the approved Stage 1 package.

## 10. Rollout if accepted

1. The reviewer amends `.alveara/HANDOFF_SCHEMA.md` (a version bump to v3 is suggested, since the status list changes) and, if needed, the Execution Plan's control text.
2. `BUILD_STATE.md` and the `EXECUTION_STATUS.json` field list are updated to match.
3. The next story that needs a full regression is packaged in two stages.
