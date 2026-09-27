# ALV-CONTROL R01 — Changed Files

## Bootstrap implementation commit (`c88f6ff7112f0f607a6468f3e5865874e2565b7e`)

| File | Why |
|---|---|
| `.alveara/EXECUTION_STATUS.json` | The 53-record production execution ledger (19 course + 34 ALV), required by the control bootstrap. |
| `.alveara/HANDOFF_SCHEMA.md` | Durable attempt/review/ZIP contract plus the persisted Standard Production Prompt Rules and Gate Evidence Rules. |
| `.alveara/BUILD_STATE.md` | Truthful current-state record: no production implementation exists yet. |
| `.alveara/QUALITY_GATES.md` | Gates A–F predeclared criteria, all `NOT YET EVALUABLE`. |
| `.alveara/reviews/.gitkeep` | Placeholder so Git tracks the otherwise-empty `reviews/` directory. |

## Bootstrap evidence commit (this commit)

| File | Why |
|---|---|
| `.alveara/handoffs/ALV-CONTROL/R01.md` | The attempt-level handoff record for this bootstrap. |
| `.alveara/handoffs/ALV-CONTROL/R01-evidence/VALIDATION_RESULTS.md` | Every control-bootstrap validation check with method and PASS/FAIL result. |
| `.alveara/handoffs/ALV-CONTROL/R01-evidence/CHANGED_FILES.md` | This file. |
| `.alveara/handoffs/ALV-CONTROL/R01-evidence/GIT_STATE.md` | Branch, implementation SHA, evidence SHA, clean/dirty state. |
| `.alveara/handoffs/ALV-CONTROL/R01-evidence/CONTROL_INVENTORY.md` | Required files/directories, 53/19/34 counts, uniqueness/dependency checks, initial READY state. |
| `.alveara/handoffs/ALV-CONTROL/R01-evidence/COURSE_SEPARATION.md` | Observed post-STORY-000 course baseline and evidence `.colaberry` was not touched. |

No files outside `.alveara/` were changed by either commit.
