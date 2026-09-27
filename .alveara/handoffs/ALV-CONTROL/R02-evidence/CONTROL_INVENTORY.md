# ALV-CONTROL R02 — Control Inventory

## Required `.alveara/` files/directories

Unchanged from R01 — all present: `EXECUTION_STATUS.json`, `HANDOFF_SCHEMA.md`, `BUILD_STATE.md`, `QUALITY_GATES.md`, `handoffs/`, `reviews/`.

## Ledger counts (unchanged)

- Total active first-release records: **53**
- Course (`STORY-*`) records: **19**
- First-release ALV records: **34**

## Story-ID uniqueness

Unchanged — all 53 `storyId` values unique.

## Dependency checks (corrected in R02)

- Every one of the 34 ALV records' `dependencies` now matches its explicit `**Dependencies:**` declaration in the Execution Plan (full transcription in `DEPENDENCY_CORRECTIONS.md`).
- `ALV-009-C02` dependencies: `["STORY-009", "ALV-009-C01", "ALV-010-C01"]` — includes required `STORY-009`. **PASS**
- `ALV-N013` dependencies: `["ALV-N004", "ALV-018-C01", "ALV-N002"]` — does **not** include `ALV-N007`/`ALV-N012`. **PASS** (this was the R01-01 defect)
- All 53 records' dependency references resolve to an existing record ID. **PASS** (0 unknown references)
- Dependency graph contains no cycle (DFS check). **PASS**

## Gate coverage (corrected in R02)

Gate F now carries 9 criteria (F1–F9), matching all 9 bullets in the Execution Plan's `GATE F EVIDENCE REQUIREMENTS` block (verified line-by-line against plan lines 3670–3682).

Actual per-gate row counts in `QUALITY_GATES.md` (via `awk` count of `| <Letter><N> |` rows, not a raw grep of the phrase "NOT YET EVALUABLE" — that phrase also appears twice in prose outside the tables):

| Gate | Rows |
|---|---|
| A | 9 |
| B | 7 |
| C | 8 |
| D | 5 |
| E | 8 |
| F | 9 (was 8) |
| **Total** | **46** (was 45 per the R01 review's independent count of 9/7/8/5/8/8) |

## Initial READY state (unchanged)

Only `ALV-N001` is `READY`. All other 51 non-`STORY-000` records are `PLANNED`. `STORY-000` is `COMPLETE`.

## Status distribution (unchanged)

| Status | Count |
|---|---|
| COMPLETE | 1 (`STORY-000`) |
| READY | 1 (`ALV-N001`) |
| PLANNED | 51 |
| Other (IN_PROGRESS/AWAITING_REVIEW/BLOCKED/CHANGES_REQUIRED/DEFERRED/REOPENED/REVALIDATION_REQUIRED) | 0 |
