# ALV-CONTROL R01 — Control Inventory

## Required `.alveara/` files/directories

| Path | Present |
|---|---|
| `.alveara/EXECUTION_STATUS.json` | Yes |
| `.alveara/HANDOFF_SCHEMA.md` | Yes |
| `.alveara/BUILD_STATE.md` | Yes |
| `.alveara/QUALITY_GATES.md` | Yes |
| `.alveara/handoffs/` | Yes |
| `.alveara/reviews/` | Yes |

## Ledger counts

- Total active first-release records: **53**
- Course (`STORY-*`) records: **19**
- First-release ALV records: **34**

## Story-ID uniqueness

All 53 `storyId` values are unique (verified via `new Set(ids).size === ids.length`).

## Dependency checks

- `ALV-009-C02` dependencies: `["STORY-009", "ALV-009-C01"]` — includes required `STORY-009`. **PASS**
- `STORY-014` dependencies include `ALV-N005` (built before the course story per the Execution Plan's explicit sequencing note) in addition to its linear predecessor `ALV-015-C01`.
- `ALV-008-C01` dependencies include `ALV-007-C01` (financial ledger/charge foundation must exist before procedure completion creates billable charges) in addition to its parent `STORY-008` and linear predecessor `STORY-008`.
- `ALV-N012` dependencies include `ALV-N007` (must never execute when `ALV-N007` is deferred).
- Every other companion's dependencies are `[parentCourseStory, immediateLinearPredecessor]` (deduplicated).

## Initial READY state

Only `ALV-N001` is `READY` (its sole dependency, `STORY-000`, is `COMPLETE`). All other 51 non-`STORY-000` records are `PLANNED`. `STORY-000` itself is `COMPLETE`.

## Status distribution

| Status | Count |
|---|---|
| COMPLETE | 1 (`STORY-000`) |
| READY | 1 (`ALV-N001`) |
| PLANNED | 51 |
| IN_PROGRESS / AWAITING_REVIEW / BLOCKED / CHANGES_REQUIRED / DEFERRED / REOPENED / REVALIDATION_REQUIRED | 0 |
