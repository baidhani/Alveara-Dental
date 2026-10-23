# Change-Impact Test Selection Backtest — Independent Review

**Decision:** `APPROVED — NOT_READY_FOR_SHADOW_PILOT`  
**Reviewed:** 2026-10-22  
**Package:** `Alveara_Backtest_ChangeImpactSelection.zip`  
**Package SHA-256:** `155A662D22669CB49E03CF1F4C12F1F3A774C232BECC2BCF5B48578559ED0BCD`

## Decision

The report is accepted as a read-only analysis. Its recommendation is correct: do not start a shadow pilot, do not apply change-impact selection to any Stage 2 run, and do not amend the Execution Plan, Engineering/Product Reference, or `HANDOFF_SCHEMA.md`.

## Basis

- The strict fail-closed rule forced a full run in all **9 of 9** evaluated story-sized historical changes and **102 of 109** backend-touching commits. Therefore its apparent zero-miss result is vacuous: it made no historical selected-run decision to validate.
- The graph-only diagnostic found that the latent financial-history race would have fallen outside its selected set. Although that defect was not caused by the template-infrastructure change under test, it demonstrates why a non-semantic graph cannot justify selection.
- The evidence set is too small and too heterogeneous to prove estimated savings. The claimed 20–25 minute saving per qualifying story is not supported by this history.
- The analysis appropriately identifies runtime dependencies that its static approach cannot prove: DI registration, reflection, assembly scanning, EF/migration behavior, test fixtures, and discovery/filter configuration.

## Direction

Keep the current approved approaches:

1. Two-stage review, so a broad gate runs once after focused approval.
2. Reuse by exact equivalence when the approved evaluator proves no relevant backend or backend-test change.
3. Whole frontend gates for frontend changes.
4. Full backend runs at existing quality gates, releases, milestones, shared-core changes, and other already-required high-risk boundaries.

Do not create an implementation story or shadow-pilot story for change-impact selection at this time. Reconsider only after enough future preserved full-run history exists and a semantic, closure-commit-specific analyzer can demonstrate at least several meaningful candidate selections without weakening the fail-closed boundary.

The report's attribution distinction may be useful in a future design, but it is not adopted as policy now. Any future proposal must separately define and obtain approval for how latent defects, flakes, and selection misses are handled.
