# Change-Impact Test Selection — Reviewer Response and Authorized Backtest

**Status:** Approved for a read-only backtest only. This is not approval to change Stage 2 policy, alter `HANDOFF_SCHEMA.md`, modify the Execution Plan, select tests for a production story, or omit any currently required regression run.

## Decision

The proposed direction is promising. It can reduce future Stage 2 duration when a deterministic rule proves that a test cannot be affected, while retaining qualifying evidence for the unselected portion of the suite. The existing ALV-N016 / ALV-007-C01 equivalence decision is a useful precedent, but it does not yet prove a general selection rule.

The next authorized work is a historical, read-only backtest. Its purpose is to answer one question: would the proposed rule have selected every test that later exposed a real regression?

## Authorized scope

You may:

- Inspect repository history, preserved handoffs, regression artifacts, test-result files, and current source.
- Create read-only analysis tooling and an evidence report under a clearly named `.alveara/` or `docs/testing/` backtest location.
- Compute hypothetical selections for completed stories that have preserved full-run evidence.
- Produce a proposed forced-full trigger list and a proposed selection manifest format.
- Measure the historical test counts and estimated duration of selected versus reused classes from preserved evidence.

You may not:

- Change application code, tests, database infrastructure, test parallelism, test configuration, test cadence, review rules, `HANDOFF_SCHEMA.md`, the Execution Plan, or the Engineering/Product Reference.
- Use selected testing as Stage 2 evidence for any active story.
- Run a new full backend suite merely for this backtest.
- Treat a heuristic mapping as evidence that a test is safe to omit.

## Required design corrections

The backtest must incorporate these rules. They are mandatory for any later pilot proposal.

### Fail closed on runtime and cross-cutting surfaces

Namespace and `using` analysis alone is insufficient. A hypothetical selection must force a full run when the change touches, or cannot conclusively exclude, any of the following:

- dependency-injection/service registration, interfaces, DTOs, serialization, validation, endpoint/controller wiring, middleware, authorization, audit, concurrency, idempotency, money, time, storage, backup, migrations, EF configuration, data models, SQL/seed scripts;
- shared test fixtures, base classes, template-database machinery, test discovery/filter configuration, test projects, build/SDK/package/solution files, environment/tool-version files, or dynamic/reflection-based loading;
- any new, renamed, deleted, unmapped, or ambiguously mapped production or test file.

The rule may select too much. It may never select too little because of uncertainty.

### Historical analysis must use historical state

For each historical change, derive the production-module map, dependency graph, test inventory, and changed-file set from the relevant historical commits. Do not use current source code to decide what a prior change could have affected.

Use an actual test-discovery inventory for the candidate closure state. A future implementation must fail if discovery, class mapping, filter construction, test identity matching, or preserved-result matching is incomplete.

### Preserve the existing full-run floor

Do not propose a full run before every remote push; normal story closure pushes would make that equivalent to a full run per story.

The later pilot proposal may retain full runs at quality gates, releases, milestones, and all shared-core changes. If selection is eventually adopted, propose an audit full run after every five selected Stage 2s that were not already covered by one of those events.

### Define a selection miss

A `SELECTION_MISS` is a later qualifying full run that finds a failure in a test class the selector reused, or exposes a regression attributable to a reused module. Any miss suspends selection immediately until the rule is corrected, backtested again, and re-approved.

## Required backtest report

Submit one review package or report containing:

1. The exact historical commits and full-run artifacts examined, with evidence that each run was qualifying and comparable.
2. For every evaluated historical change: changed files, forced-full decision or hypothetical selected and reused classes, reasons for each classification, and whether a known regression/failure would have been selected.
3. A test-inventory reconciliation: selected plus reused equals the discovered full test list; no test is missing or duplicated.
4. A table of every detected or potential miss. Any actual miss means the backtest does not pass.
5. The full proposed forced-full trigger list, including all categories above.
6. Estimated savings based only on preserved timing evidence, clearly labeled as estimates.
7. A proposed frozen `selection.json` schema and evaluator contract for a later shadow pilot.
8. A recommendation: either `NOT_READY_FOR_SHADOW_PILOT` with exact gaps, or `READY_FOR_REVIEW_OF_SHADOW_PILOT` with evidence of zero misses.

## Completion condition

Stop at `AWAITING_REVIEW` after delivering the report. Do not begin a shadow pilot, change any policy, or apply selected testing to a story without a separate reviewer decision.
